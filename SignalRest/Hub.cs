using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hosting;
using Microsoft.AspNet.SignalR.Hubs;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;

namespace SignalRest
{
    /// <summary>
    /// Extends a <see cref="Microsoft.AspNet.SignalR.Hub"/> with the necessary methods to permit REST API consumers to invoke its methods and subscribe to its events.
    /// </summary>
    public abstract class Hub : Microsoft.AspNet.SignalR.Hub
    {
        internal static Hub GetHub(IDictionary<string, object> environment, string hubName, Session session)
        {
            Hub hub = null;
            ReflectedHub reflectedHub;
            if (ApiController.Hubs.TryGetValue(hubName, out reflectedHub))
            {
                var hubType = reflectedHub.Type;
                hubName = hubType.GetCustomAttribute<HubNameAttribute>()?.HubName ?? hubType.Name;
                hub = (Hub)Activator.CreateInstance(hubType);
                hub.signalRestClients = new HubConnectionContext(hubName, session.ConnectionId);
                hub.signalRestContext = new HubCallerContext(new HostContext(environment).Request, session.ConnectionId);
                hub.signalRestEnvironment = environment;
                hub.signalRestGroups = new GroupManager(hubName);
                hub.isSignalRestInitialized = true;
            }
            return hub;
        }

        internal static Hub GetHub(HttpRequestMessage requestMessage, string hubName, Session session)
        {
            var hub = GetHub(requestMessage.GetOwinEnvironment(), hubName, session);
            hub.request = requestMessage;
            return hub;
        }

        /// <summary>
        /// Returns a <see cref="IHubContext"/> for the specified SignalRest hub.
        /// </summary>
        /// <param name="hubName">The name of the hub.</param>
        public static IHubContext GetHubContext(string hubName)
        {
            ReflectedHub reflectedHub;
            if (ApiController.Hubs.TryGetValue(hubName, out reflectedHub))
            {
                var hubType = reflectedHub.Type;
                hubName = hubType.GetCustomAttribute<HubNameAttribute>()?.HubName ?? hubType.Name;
                return new HubContext(hubName);
            }
            return null;
        }

        /// <summary>
        /// Returns a <see cref="IHubContext"/> for the specified <see cref="Hub"/>. 
        /// </summary>
        public static IHubContext GetHubContext<T>()
            where T : Hub
        {
            var hubType = typeof(T);
            return new HubContext(hubType.GetCustomAttribute<HubNameAttribute>()?.HubName ?? hubType.Name);
        }

        static void InitializeWithoutCaller(Hub hub)
        {
            var hubType = hub.GetType();
            var hubName = hubType.GetCustomAttribute<HubNameAttribute>()?.HubName ?? hubType.Name;
            hub.signalRestClients = new HubConnectionContext(hubName);
            hub.signalRestGroups = new GroupManager(hubName);
            hub.isSignalRestInitialized = true;
        }

        bool isSignalRestInitialized;
        HttpRequestMessage request;
        IHubCallerConnectionContext<dynamic> signalRestClients;
        HubCallerContext signalRestContext;
        IDictionary<string, object> signalRestEnvironment;
        IGroupManager signalRestGroups;

        void InitializeFromSignalRIfNecessary()
        {
            if (!isSignalRestInitialized)
            {
                var hubType = GetType();
                var hubName = hubType.GetCustomAttribute<HubNameAttribute>()?.HubName ?? hubType.Name;
                signalRestClients = new HubConnectionContext(hubName, base.Context.ConnectionId);
                signalRestGroups = new GroupManager(hubName);
                isSignalRestInitialized = true;
            }
        }

        new public IHubCallerConnectionContext<dynamic> Clients
        {
            get
            {
                InitializeFromSignalRIfNecessary();
                return signalRestClients;
            }
        }

        new public HubCallerContext Context
        {
            get { return signalRestContext ?? base.Context; }
            set { base.Context = value; }
        }

        public IDictionary<string, object> Environment
        {
            get { return signalRestEnvironment ?? base.Context.Request.Environment; }
        }

        new public IGroupManager Groups
        {
            get
            {
                InitializeFromSignalRIfNecessary();
                return signalRestGroups;
            }
        }

        /// <summary>
        /// Gets the current <see cref="HttpRequestMessage"/> if the request is being processed by SignalREST and Web API; otherwise, null.
        /// </summary>
        public HttpRequestMessage Request
        {
            get { return request; }
        }
    }
}
