using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace SignalRest
{
    /// <summary>
    /// Allows REST API consumers to simulate a SignalR connection and invoke the methods of and subscribe to the events of SignalR hubs available in the app domain.
    /// </summary>
    [RoutePrefix("signalrest")]
    public class ApiController : System.Web.Http.ApiController
    {
        static ApiController()
        {
            var hubType = typeof(Hub);
            Hubs = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch
                {
                    return Enumerable.Empty<Type>();
                }
            }).Where(t => hubType.IsAssignableFrom(t) && !t.IsAbstract).ToDictionary(t =>
            {
                var hubNameAttribute = t.GetCustomAttribute<HubNameAttribute>();
                if (hubNameAttribute != null)
                    return hubNameAttribute.HubName.ToLowerInvariant();
                return t.Name.ToLowerInvariant();
            }, t => new ReflectedHub(t), StringComparer.OrdinalIgnoreCase);
            Sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
            SessionManagementLock = new ReaderWriterLockSlim();
            SessionManager = new Timer(new TimerCallback(state =>
            {
                SessionManagementLock.EnterWriteLock();
                try
                {
                    var now = DateTime.UtcNow;
                    var disconnectedConnectionIds = Sessions.Where(kv => kv.Value.LastKeepAlive + GlobalHost.Configuration.DisconnectTimeout < now).Select(kv => kv.Key);
                    foreach (var disconnectedConnectionId in disconnectedConnectionIds)
                    {
                        var session = Sessions[disconnectedConnectionId];
                        Sessions.Remove(disconnectedConnectionId);
                        foreach (var hubName in session.Hubs)
                            using (var hub = Hub.GetHub(null, hubName.Key, session))
                                hub.OnDisconnected(false).Wait();
                    }
                }
                finally
                {
                    SessionManagementLock.ExitWriteLock();
                    SessionManager.Change(GlobalHost.Configuration.KeepAlive ?? TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-1));
                }
            }), null, GlobalHost.Configuration.KeepAlive ?? TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-1));
            TaskValueGetters = new ConcurrentDictionary<Type, FastMethodInfo>();
        }

        internal static IReadOnlyDictionary<string, ReflectedHub> Hubs { get; }
        private static Dictionary<string, Session> Sessions { get; }
        private static Timer SessionManager { get; }
        private static ReaderWriterLockSlim SessionManagementLock { get; }
        private static ConcurrentDictionary<Type, FastMethodInfo> TaskValueGetters { get; }

        internal static void ClientProxyInvoke(IList<string> exclude, string hub, string method, params object[] args)
        {
            SessionManagementLock.EnterReadLock();
            var excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (exclude != null)
                excludeSet.UnionWith(exclude);
            try
            {
                foreach (var session in Sessions)
                {
                    if (session.Value.Hubs.ContainsKey(hub) && !excludeSet.Contains(session.Key))
                        session.Value.ClientInvocations.Enqueue(new ClientInvocation(hub, method, args));
                }
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
        }

        private static FastMethodInfo CreateTaskValueGetter(Type type)
        {
            return new FastMethodInfo(type.GetProperty(nameof(Task<object>.Result)).GetGetMethod());
        }

        internal static void ConnectionIdsInvoke(IList<string> connectionIds, string hub, string method, params object[] args)
        {
            SessionManagementLock.EnterReadLock();
            try
            {
                foreach (var connectionId in connectionIds)
                {
                    Session session;
                    if (Sessions.TryGetValue(connectionId, out session))
                        if (session.Hubs.ContainsKey(hub))
                            session.ClientInvocations.Enqueue(new ClientInvocation(hub, method, args));
                }
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
        }

        private static async Task<object> ExecuteHubMethodInvocation(Session session, IDictionary<string, object> owinEnvironment, HubMethodInvocation invocation)
        {
            object invocationResult = null;
            ReflectedHub reflectedHub;
            if (!Hubs.TryGetValue(invocation.Hub, out reflectedHub))
                invocationResult = new { Error = "Hub not found" };
            else
            {
                IReadOnlyDictionary<int, Tuple<Type, Type[], FastMethodInfo>> overloads;
                if (!reflectedHub.MethodNames.TryGetValue(invocation.Method, out overloads))
                    invocationResult = new { Error = "Hub method not found" };
                else
                {
                    Tuple<Type, Type[], FastMethodInfo> overload;
                    if (!overloads.TryGetValue(invocation.Arguments?.Count ?? 0, out overload))
                        invocationResult = new { Error = "Hub method not found" };
                    else
                    {
                        var argumentsList = new List<object>();
                        var p = -1;
                        if (invocation.Arguments != null)
                            foreach (var child in invocation.Arguments.Children())
                                argumentsList.Add(JsonConvert.DeserializeObject(child.ToString(), overload.Item2[++p]));
                        try
                        {
                            using (var hub = Hub.GetHub(owinEnvironment, invocation.Hub, session))
                                invocationResult = overload.Item3.Invoke(hub, argumentsList.ToArray());
                            if (invocationResult != null)
                            {
                                var task = invocationResult as Task;
                                if (task != null)
                                {
                                    await task.ContinueWith(t =>
                                    {
                                        if (t.IsFaulted)
                                            invocationResult = new { Error = string.Format("{0}: {1}", t.Exception.GetType().Name, t.Exception.Message) };
                                        else
                                        {
                                            var type = t.GetType();
                                            if (type.IsGenericType)
                                                invocationResult = TaskValueGetters.GetOrAdd(type, CreateTaskValueGetter).Invoke(t);
                                        }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            invocationResult = new { Error = string.Format("{0}: {1}", ex.GetType().Name, ex.Message) };
                        }
                    }
                }
            }
            return invocationResult;
        }

        private static async Task<List<object>> ExecuteMultipleHubMethodInvocations(HubMethodInvocation[] invocations, Session session, IDictionary<string, object> owinEnvironment)
        {
            var result = new List<object>();
            foreach (var invocation in invocations)
            {
                object invocationResult = await ExecuteHubMethodInvocation(session, owinEnvironment, invocation);
                result.Add(invocationResult);
            }
            return result;
        }

        private static List<ClientInvocation> GetEvents(Session session)
        {
            var events = new List<ClientInvocation>();
            ClientInvocation invocation;
            while (session.ClientInvocations.TryDequeue(out invocation))
                events.Add(invocation);
            return events;
        }

        internal static void GroupAdd(string hub, string connectionId, string groupName)
        {
            SessionManagementLock.EnterReadLock();
            try
            {
                Session session;
                if (Sessions.TryGetValue(connectionId, out session))
                {
                    ConcurrentDictionary<string, byte> groups;
                    if (session.Hubs.TryGetValue(hub, out groups))
                        groups.TryAdd(groupName, 0);
                }
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
        }

        internal static void GroupNamesInvoke(IList<string> groupNames, IList<string> exclude, string hub, string method, params object[] args)
        {
            SessionManagementLock.EnterReadLock();
            var excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (exclude != null)
                excludeSet.UnionWith(exclude);
            try
            {
                foreach (var session in Sessions)
                {
                    ConcurrentDictionary<string, byte> groups;
                    if (session.Value.Hubs.TryGetValue(hub, out groups) && !excludeSet.Contains(session.Key) && groupNames.Any(groupName => groups.ContainsKey(groupName)))
                        session.Value.ClientInvocations.Enqueue(new ClientInvocation(hub, method, args));
                }
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
        }

        internal static void GroupRemove(string hub, string connectionId, string groupName)
        {
            SessionManagementLock.EnterReadLock();
            try
            {
                Session session;
                if (Sessions.TryGetValue(connectionId, out session))
                {
                    ConcurrentDictionary<string, byte> groups;
                    if (session.Hubs.TryGetValue(hub, out groups))
                    {
                        byte junk;
                        groups.TryRemove(groupName, out junk);
                    }
                }
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
        }

        private async Task<Session> PerformConnect(string[] hubNames, string connectionId = null)
        {
            try
            {
                if (hubNames.Any(h => !Hubs.ContainsKey(h)))
                    throw new ArgumentOutOfRangeException(nameof(hubNames));
                if (connectionId == null)
                    connectionId = Guid.NewGuid().ToString().ToLowerInvariant();
                SessionManagementLock.EnterReadLock();
                Session session;
                try
                {
                    session = new Session(hubNames)
                    {
                        ConnectionId = connectionId,
                        LastKeepAlive = DateTime.UtcNow
                    };
                    Sessions.Add(connectionId, session);
                }
                finally
                {
                    SessionManagementLock.ExitReadLock();
                }
                var owinEnvironment = Request.GetOwinEnvironment();
                foreach (var hubName in hubNames)
                    using (var hub = Hub.GetHub(owinEnvironment, hubName, session))
                        await hub.OnConnected();
                return session;
            }
            catch
            {
                if (connectionId != null)
                {
                    SessionManagementLock.EnterReadLock();
                    try
                    {
                        Sessions.Remove(connectionId);
                    }
                    finally
                    {
                        SessionManagementLock.ExitReadLock();
                    }
                }
                throw;
            }
        }

        private async Task<IHttpActionResult> GetConnectResponse(string[] hubNames, string connectionId = null)
        {
            try
            {
                return Ok((await PerformConnect(hubNames, connectionId)).ConnectionId);
            }
            catch (ArgumentOutOfRangeException)
            {
                return NotFound();
            }
        }

        [HttpPost, Route("connect")]
        public async Task<IHttpActionResult> Connect([FromBody] string[] hubNames)
        {
            return await GetConnectResponse(hubNames);
        }

        [HttpPost, Route("connectAndInvoke/{hubName}/{methodName}")]
        public async Task<IHttpActionResult> ConnectAndInvoke(string hubName, string methodName, [FromBody] HubNamesAndArguments hubNamesAndArguments)
        {
            Session session;
            try
            {
                session = await PerformConnect(hubNamesAndArguments.HubNames);
            }
            catch (ArgumentOutOfRangeException)
            {
                return NotFound();
            }
            return Ok(new
            {
                ConnectionId = session.ConnectionId,
                ReturnValue = await ExecuteHubMethodInvocation(session, Request.GetOwinEnvironment(), new HubMethodInvocation
                {
                    Arguments = hubNamesAndArguments.Arguments,
                    Hub = hubName,
                    Method = methodName
                })
            });
        }

        [HttpPost, Route("connectAndInvoke")]
        public async Task<IHttpActionResult> ConnectAndInvoke([FromBody] HubNamesAndHubMethodInvocations hubNamesAndHubMethodInvocations)
        {
            Session session;
            try
            {
                session = await PerformConnect(hubNamesAndHubMethodInvocations.HubNames);
            }
            catch (ArgumentOutOfRangeException)
            {
                return NotFound();
            }
            return Ok(new
            {
                ConnectionId = session.ConnectionId,
                ReturnValues = await ExecuteMultipleHubMethodInvocations(hubNamesAndHubMethodInvocations.HubMethodInvocations, session, Request.GetOwinEnvironment())
            });
        }

        [HttpPost, Route("connections/{connectionId}/reconnect")]
        public async Task<IHttpActionResult> Reconnect(string connectionId, [FromBody] string[] hubNames)
        {
            Session session;
            bool sessionRetrieved;
            SessionManagementLock.EnterReadLock();
            try
            {
                sessionRetrieved = Sessions.TryGetValue(connectionId, out session);
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
            if (!sessionRetrieved)
                return await GetConnectResponse(hubNames, connectionId);
            if (!session.Hubs.Keys.OrderBy(n => n).SequenceEqual(hubNames.OrderBy(n => n), StringComparer.OrdinalIgnoreCase))
                return Conflict();
            return GetEventsResponse(session);
        }

        [HttpPost, Route("connections/{connectionId}/reconnectAndInvoke/{hubName}/{methodName}")]
        public async Task<IHttpActionResult> ReconnectAndInvoke(string connectionId, string hubName, string methodName, [FromBody] HubNamesAndArguments hubNamesAndArguments)
        {
            Session session;
            bool sessionRetrieved;
            SessionManagementLock.EnterReadLock();
            try
            {
                sessionRetrieved = Sessions.TryGetValue(connectionId, out session);
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
            var hubMethodInvocation = new HubMethodInvocation
            {
                Arguments = hubNamesAndArguments.Arguments,
                Hub = hubName,
                Method = methodName
            };
            if (!sessionRetrieved)
            {
                try
                {
                    session = await PerformConnect(hubNamesAndArguments.HubNames, connectionId);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return NotFound();
                }
                return Ok(new
                {
                    ConnectionId = session.ConnectionId,
                    ReturnValue = await ExecuteHubMethodInvocation(session, Request.GetOwinEnvironment(), hubMethodInvocation)
                });
            }
            if (!session.Hubs.Keys.OrderBy(n => n).SequenceEqual(hubNamesAndArguments.HubNames.OrderBy(n => n), StringComparer.OrdinalIgnoreCase))
                return Conflict();
            return Ok(new
            {
                Events = GetEvents(session),
                ReturnValue = await ExecuteHubMethodInvocation(session, Request.GetOwinEnvironment(), hubMethodInvocation)
            });
        }

        [HttpPost, Route("connections/{connectionId}/reconnectAndInvoke")]
        public async Task<IHttpActionResult> ReconnectAndInvoke(string connectionId, [FromBody] HubNamesAndHubMethodInvocations hubNamesAndHubMethodInvocations)
        {
            Session session;
            bool sessionRetrieved;
            SessionManagementLock.EnterReadLock();
            try
            {
                sessionRetrieved = Sessions.TryGetValue(connectionId, out session);
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
            if (!sessionRetrieved)
            {
                try
                {
                    session = await PerformConnect(hubNamesAndHubMethodInvocations.HubNames, connectionId);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return NotFound();
                }
                return Ok(new
                {
                    ConnectionId = session.ConnectionId,
                    ReturnValues = await ExecuteMultipleHubMethodInvocations(hubNamesAndHubMethodInvocations.HubMethodInvocations, session, Request.GetOwinEnvironment())
                });
            }
            if (!session.Hubs.Keys.OrderBy(n => n).SequenceEqual(hubNamesAndHubMethodInvocations.HubNames.OrderBy(n => n), StringComparer.OrdinalIgnoreCase))
                return Conflict();
            return Ok(new
            {
                Events = GetEvents(session),
                ReturnValues = await ExecuteMultipleHubMethodInvocations(hubNamesAndHubMethodInvocations.HubMethodInvocations, session, Request.GetOwinEnvironment())
            });
        }

        [HttpPost, Route("connections/{connectionId}/disconnect")]
        public async Task<IHttpActionResult> Disconnect(string connectionId)
        {
            Session session;
            SessionManagementLock.EnterReadLock();
            try
            {
                if (!Sessions.TryGetValue(connectionId, out session))
                    return NotFound();
                Sessions.Remove(connectionId);
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
            var owinEnvironment = Request.GetOwinEnvironment();
            foreach (var hubName in session.Hubs)
                using (var hub = Hub.GetHub(owinEnvironment, hubName.Key, session))
                    await hub.OnDisconnected(true);
            return Ok();
        }

        private IHttpActionResult GetEventsResponse(Session session)
        {
            session.LastKeepAlive = DateTime.UtcNow;
            return Ok(GetEvents(session));
        }

        [HttpPost, Route("connections/{connectionId}/events")]
        public IHttpActionResult Events(string connectionId)
        {
            SessionManagementLock.EnterReadLock();
            try
            {
                Session session;
                if (!Sessions.TryGetValue(connectionId, out session))
                    return NotFound();
                return GetEventsResponse(session);
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
        }

        [HttpPost, Route("connections/{connectionId}/invoke/{hubName}/{methodName}")]
        public async Task<IHttpActionResult> Invoke(string connectionId, string hubName, string methodName, [FromBody] JArray arguments)
        {
            Session session;
            SessionManagementLock.EnterReadLock();
            try
            {
                if (!Sessions.TryGetValue(connectionId, out session))
                    return NotFound();
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
            session.LastKeepAlive = DateTime.UtcNow;
            return Ok(await ExecuteHubMethodInvocation(session, Request.GetOwinEnvironment(), new HubMethodInvocation
            {
                Arguments = arguments,
                Hub = hubName,
                Method = methodName,
            }));
        }

        [HttpPost, Route("connections/{connectionId}/invoke")]
        public async Task<IHttpActionResult> Invoke(string connectionId, [FromBody] HubMethodInvocation[] invocations)
        {
            Session session;
            SessionManagementLock.EnterReadLock();
            try
            {
                if (!Sessions.TryGetValue(connectionId, out session))
                    return NotFound();
            }
            finally
            {
                SessionManagementLock.ExitReadLock();
            }
            session.LastKeepAlive = DateTime.UtcNow;
            return Ok(await ExecuteMultipleHubMethodInvocations(invocations, session, Request.GetOwinEnvironment()));
        }
    }
}
