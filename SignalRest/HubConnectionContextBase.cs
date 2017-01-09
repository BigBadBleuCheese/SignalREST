using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using System;
using System.Collections.Generic;

namespace SignalRest
{
    public class HubConnectionContextBase : IHubConnectionContext<object>
    {
        public HubConnectionContextBase()
        {
            All = AllExcept();
        }

        public HubConnectionContextBase(string hubName)
        {
            HubName = hubName;

            All = AllExcept();
        }

        protected string HubName { get; private set; }

        public dynamic All { get; set; }

        public dynamic AllExcept(params string[] excludeConnectionIds)
        {
            return new ClientProxy(HubName, excludeConnectionIds);
        }

        public dynamic Client(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                throw new ArgumentException("Argument cannot be null or empty");
            return new ConnectionIdProxy(connectionId, HubName);
        }

        public dynamic Clients(IList<string> connectionIds)
        {
            if (connectionIds == null)
                throw new ArgumentNullException(nameof(connectionIds));
            return new ConnectionIdsProxy(connectionIds, HubName);
        }

        public dynamic Group(string groupName, params string[] excludeConnectionIds)
        {
            if (string.IsNullOrEmpty(groupName))
                throw new ArgumentException("Argument cannot be null or empty");
            return new GroupProxy(groupName, HubName, excludeConnectionIds);
        }

        public dynamic Groups(IList<string> groupNames, params string[] excludeConnectionIds)
        {
            if (groupNames == null)
                throw new ArgumentNullException(nameof(groupNames));
            return new GroupsProxy(groupNames, HubName, excludeConnectionIds);
        }

        public dynamic User(string userId)
        {
            return GlobalHost.ConnectionManager.GetHubContext(HubName).Clients.User(userId);
        }

        public dynamic Users(IList<string> userIds)
        {
            return GlobalHost.ConnectionManager.GetHubContext(HubName).Clients.Users(userIds);
        }
    }
}
