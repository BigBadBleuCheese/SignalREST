using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
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
            sessionManager = new Timer(SessionManagerTick, null, GlobalHost.Configuration.KeepAlive ?? TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-1));
            taskValueGetters = new ConcurrentDictionary<Type, FastMethodInfo>();
        }

        internal static readonly IReadOnlyDictionary<string, ReflectedHub> Hubs;
        static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        static readonly Timer sessionManager;
        static readonly ReaderWriterLockSlim sessionManagementLock = new ReaderWriterLockSlim();
        static readonly ConcurrentDictionary<Type, FastMethodInfo> taskValueGetters;

        public static event EventHandler<SessionManagementExceptionEventArgs> SessionManagementException;

        internal static void ClientProxyInvoke(IList<string> exclude, string hub, string method, params object[] args)
        {
            sessionManagementLock.EnterReadLock();
            var excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (exclude != null)
                excludeSet.UnionWith(exclude);
            try
            {
                foreach (var session in sessions)
                {
                    if (session.Value.Hubs.ContainsKey(hub) && !excludeSet.Contains(session.Key))
                        session.Value.ClientInvocations.Enqueue(new ClientInvocation(hub, method, args));
                }
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
            }
        }

        internal static void ConnectionIdsInvoke(IList<string> connectionIds, string hub, string method, params object[] args)
        {
            sessionManagementLock.EnterReadLock();
            try
            {
                foreach (var connectionId in connectionIds)
                {
                    Session session;
                    if (sessions.TryGetValue(connectionId, out session))
                        if (session.Hubs.ContainsKey(hub))
                            session.ClientInvocations.Enqueue(new ClientInvocation(hub, method, args));
                }
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
            }
        }

        static FastMethodInfo CreateTaskValueGetter(Type type)
        {
            return new FastMethodInfo(type.GetProperty(nameof(Task<object>.Result)).GetGetMethod());
        }

        static async Task<object> ExecuteHubMethodInvocation(Session session, HttpRequestMessage requestMessage, HubMethodInvocation invocation)
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
                                argumentsList.Add(child.ToObject(overload.Item2[++p]));
                        try
                        {
                            using (var hub = Hub.GetHub(requestMessage, invocation.Hub, session))
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
                                                invocationResult = taskValueGetters.GetOrAdd(type, CreateTaskValueGetter).Invoke(t);
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

        static async Task<List<object>> ExecuteMultipleHubMethodInvocations(HubMethodInvocation[] invocations, Session session, HttpRequestMessage requestMessage)
        {
            var result = new List<object>();
            foreach (var invocation in invocations)
            {
                object invocationResult = await ExecuteHubMethodInvocation(session, requestMessage, invocation);
                result.Add(invocationResult);
            }
            return result;
        }

        static List<ClientInvocation> GetEvents(Session session)
        {
            var events = new List<ClientInvocation>();
            ClientInvocation invocation;
            while (session.ClientInvocations.TryDequeue(out invocation))
                events.Add(invocation);
            return events;
        }

        internal static void GroupAdd(string hub, string connectionId, string groupName)
        {
            sessionManagementLock.EnterReadLock();
            try
            {
                Session session;
                if (sessions.TryGetValue(connectionId, out session))
                {
                    ConcurrentDictionary<string, byte> groups;
                    if (session.Hubs.TryGetValue(hub, out groups))
                        groups.TryAdd(groupName, 0);
                }
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
            }
        }

        internal static void GroupNamesInvoke(IList<string> groupNames, IList<string> exclude, string hub, string method, params object[] args)
        {
            sessionManagementLock.EnterReadLock();
            var excludeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (exclude != null)
                excludeSet.UnionWith(exclude);
            try
            {
                foreach (var session in sessions)
                {
                    ConcurrentDictionary<string, byte> groups;
                    if (session.Value.Hubs.TryGetValue(hub, out groups) && !excludeSet.Contains(session.Key) && groupNames.Any(groupName => groups.ContainsKey(groupName)))
                        session.Value.ClientInvocations.Enqueue(new ClientInvocation(hub, method, args));
                }
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
            }
        }

        internal static void GroupRemove(string hub, string connectionId, string groupName)
        {
            sessionManagementLock.EnterReadLock();
            try
            {
                Session session;
                if (sessions.TryGetValue(connectionId, out session))
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
                sessionManagementLock.ExitReadLock();
            }
        }

        static void OnSessionManagementException(SessionManagementExceptionEventArgs e)
        {
            SessionManagementException?.Invoke(null, e);
        }

        async static void SessionManagerTick(object state)
        {
            try
            {
                var disconnectedSessions = new Dictionary<string, Session>();
                sessionManagementLock.EnterWriteLock();
                try
                {
                    var now = DateTime.UtcNow;
                    foreach (var disconnectedConnectionId in sessions.Where(kv => kv.Value.LastKeepAlive + GlobalHost.Configuration.DisconnectTimeout < now).Select(kv => kv.Key).ToList())
                    {
                        Session session;
                        if (sessions.TryGetValue(disconnectedConnectionId, out session))
                        {
                            sessions.Remove(disconnectedConnectionId);
                            disconnectedSessions.Add(disconnectedConnectionId, session);
                        }
                    }
                }
                finally
                {
                    sessionManagementLock.ExitWriteLock();
                }
                if (disconnectedSessions.Count > 0)
                    await Task.WhenAll(disconnectedSessions.Select(ds => Task.Run(async () =>
                    {
                        foreach (var hubDescriptor in ds.Value.Hubs)
                        {
                            try
                            {
                                using (var hub = Hub.GetHub(ds.Value.LastOwinDictionary, hubDescriptor.Key, ds.Value))
                                    await hub.OnDisconnected(false).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                OnSessionManagementException(new SessionManagementExceptionEventArgs(ex, ds.Key, hubDescriptor.Key));
                            }
                        }
                    }))).ConfigureAwait(false);
            }
            finally
            {
                sessionManager.Change(GlobalHost.Configuration.KeepAlive ?? TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(-1));
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
                ReturnValue = await ExecuteHubMethodInvocation(session, Request, new HubMethodInvocation
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
                ReturnValues = await ExecuteMultipleHubMethodInvocations(hubNamesAndHubMethodInvocations.HubMethodInvocations, session, Request)
            });
        }

        [HttpPost, Route("connections/{connectionId}/disconnect")]
        public async Task<IHttpActionResult> Disconnect(string connectionId)
        {
            Session session;
            sessionManagementLock.EnterReadLock();
            try
            {
                if (!sessions.TryGetValue(connectionId, out session))
                    return NotFound();
                sessions.Remove(connectionId);
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
            }
            foreach (var hubName in session.Hubs)
                using (var hub = Hub.GetHub(Request, hubName.Key, session))
                    await hub.OnDisconnected(true);
            return Ok();
        }

        [HttpPost, Route("connections/{connectionId}/events")]
        public IHttpActionResult Events(string connectionId)
        {
            sessionManagementLock.EnterReadLock();
            try
            {
                Session session;
                if (!sessions.TryGetValue(connectionId, out session))
                    return NotFound();
                return GetEventsResponse(session);
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
            }
        }

        async Task<IHttpActionResult> GetConnectResponse(string[] hubNames, string connectionId = null)
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

        IHttpActionResult GetEventsResponse(Session session)
        {
            session.LastKeepAlive = DateTime.UtcNow;
            session.LastOwinDictionary = Request.GetOwinEnvironment();
            return Ok(GetEvents(session));
        }

        [HttpPost, Route("connections/{connectionId}/invoke/{hubName}/{methodName}")]
        public async Task<IHttpActionResult> Invoke(string connectionId, string hubName, string methodName, [FromBody] JArray arguments)
        {
            Session session;
            sessionManagementLock.EnterReadLock();
            try
            {
                if (!sessions.TryGetValue(connectionId, out session))
                    return NotFound();
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
            }
            session.LastKeepAlive = DateTime.UtcNow;
            session.LastOwinDictionary = Request.GetOwinEnvironment();
            return Ok(await ExecuteHubMethodInvocation(session, Request, new HubMethodInvocation
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
            sessionManagementLock.EnterReadLock();
            try
            {
                if (!sessions.TryGetValue(connectionId, out session))
                    return NotFound();
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
            }
            session.LastKeepAlive = DateTime.UtcNow;
            session.LastOwinDictionary = Request.GetOwinEnvironment();
            return Ok(await ExecuteMultipleHubMethodInvocations(invocations, session, Request));
        }

        async Task<Session> PerformConnect(string[] hubNames, string connectionId = null)
        {
            try
            {
                if (hubNames.Any(h => !Hubs.ContainsKey(h)))
                    throw new ArgumentOutOfRangeException(nameof(hubNames));
                if (connectionId == null)
                    connectionId = Guid.NewGuid().ToString().ToLowerInvariant();
                sessionManagementLock.EnterReadLock();
                Session session;
                try
                {
                    session = new Session(hubNames)
                    {
                        ConnectionId = connectionId,
                        LastKeepAlive = DateTime.UtcNow,
                        LastOwinDictionary = Request.GetOwinEnvironment()
                    };
                    sessions.Add(connectionId, session);
                }
                finally
                {
                    sessionManagementLock.ExitReadLock();
                }
                foreach (var hubName in hubNames)
                    using (var hub = Hub.GetHub(Request, hubName, session))
                        await hub.OnConnected();
                return session;
            }
            catch
            {
                if (connectionId != null)
                {
                    sessionManagementLock.EnterReadLock();
                    try
                    {
                        sessions.Remove(connectionId);
                    }
                    finally
                    {
                        sessionManagementLock.ExitReadLock();
                    }
                }
                throw;
            }
        }

        [HttpPost, Route("connections/{connectionId}/reconnect")]
        public async Task<IHttpActionResult> Reconnect(string connectionId, [FromBody] string[] hubNames)
        {
            Session session;
            bool sessionRetrieved;
            sessionManagementLock.EnterReadLock();
            try
            {
                sessionRetrieved = sessions.TryGetValue(connectionId, out session);
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
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
            sessionManagementLock.EnterReadLock();
            try
            {
                sessionRetrieved = sessions.TryGetValue(connectionId, out session);
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
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
                    ReturnValue = await ExecuteHubMethodInvocation(session, Request, hubMethodInvocation)
                });
            }
            if (!session.Hubs.Keys.OrderBy(n => n).SequenceEqual(hubNamesAndArguments.HubNames.OrderBy(n => n), StringComparer.OrdinalIgnoreCase))
                return Conflict();
            return Ok(new
            {
                Events = GetEvents(session),
                ReturnValue = await ExecuteHubMethodInvocation(session, Request, hubMethodInvocation)
            });
        }

        [HttpPost, Route("connections/{connectionId}/reconnectAndInvoke")]
        public async Task<IHttpActionResult> ReconnectAndInvoke(string connectionId, [FromBody] HubNamesAndHubMethodInvocations hubNamesAndHubMethodInvocations)
        {
            Session session;
            bool sessionRetrieved;
            sessionManagementLock.EnterReadLock();
            try
            {
                sessionRetrieved = sessions.TryGetValue(connectionId, out session);
            }
            finally
            {
                sessionManagementLock.ExitReadLock();
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
                    ReturnValues = await ExecuteMultipleHubMethodInvocations(hubNamesAndHubMethodInvocations.HubMethodInvocations, session, Request)
                });
            }
            if (!session.Hubs.Keys.OrderBy(n => n).SequenceEqual(hubNamesAndHubMethodInvocations.HubNames.OrderBy(n => n), StringComparer.OrdinalIgnoreCase))
                return Conflict();
            return Ok(new
            {
                Events = GetEvents(session),
                ReturnValues = await ExecuteMultipleHubMethodInvocations(hubNamesAndHubMethodInvocations.HubMethodInvocations, session, Request)
            });
        }
    }
}
