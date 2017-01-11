using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hosting;
using Microsoft.AspNet.SignalR.Hubs;
using System;
using System.Collections.Generic;
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
                hub.SignalRestClients = new HubConnectionContext(hubName, session.ConnectionId);
                hub.SignalRestContext = new HubCallerContext(new HostContext(environment).Request, session.ConnectionId);
                hub.SignalRestGroups = new GroupManager(hubName);
                hub.IsSignalRestInitialized = true;
            }
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

        private static void InitializeWithoutCaller(Hub hub)
        {
            var hubType = hub.GetType();
            var hubName = hubType.GetCustomAttribute<HubNameAttribute>()?.HubName ?? hubType.Name;
            hub.SignalRestClients = new HubConnectionContext(hubName);
            hub.SignalRestGroups = new GroupManager(hubName);
            hub.IsSignalRestInitialized = true;
        }

        private IHubCallerConnectionContext<dynamic> SignalRestClients { get; set; }
        private HubCallerContext SignalRestContext { get; set; }
        private IGroupManager SignalRestGroups { get; set; }
        private bool IsSignalRestInitialized { get; set; }

        public new IHubCallerConnectionContext<dynamic> Clients
        {
            get
            {
                InitializeFromSignalRIfNecessary();
                return SignalRestClients;
            }
        }

        public new HubCallerContext Context
        {
            get { return SignalRestContext ?? base.Context; }
            set { base.Context = value; }
        }

        public new IGroupManager Groups
        {
            get
            {
                InitializeFromSignalRIfNecessary();
                return SignalRestGroups;
            }
        }

        private void InitializeFromSignalRIfNecessary()
        {
            if (!IsSignalRestInitialized)
            {
                var hubType = GetType();
                var hubName = hubType.GetCustomAttribute<HubNameAttribute>()?.HubName ?? hubType.Name;
                SignalRestClients = new HubConnectionContext(hubName, base.Context.ConnectionId);
                SignalRestGroups = new GroupManager(hubName);
                IsSignalRestInitialized = true;
            }
        }
    }
}
