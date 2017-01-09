using Microsoft.AspNet.SignalR.Hubs;
using System.Collections.Generic;

namespace SignalRest
{
    public class HubConnectionContext : HubConnectionContextBase, IHubCallerConnectionContext<object>
    {
        public HubConnectionContext(string hubName)
            : base(hubName)
        {
            Caller = new NullClientProxy();
            Others = new NullClientProxy();
        }

        public HubConnectionContext(string hubName, string connectionId)
            : base(hubName)
        {
            ConnectionId = connectionId;

            Caller = new ConnectionIdProxy(connectionId, hubName);
            Others = AllExcept(connectionId);
        }

        private string ConnectionId { get; }

        public dynamic Caller { get; set; }
        public dynamic CallerState { get; set; }
        public dynamic Others { get; set; }

        public dynamic OthersInGroup(string groupName)
        {
            return Group(groupName, ConnectionId);
        }

        public dynamic OthersInGroups(IList<string> groupNames)
        {
            return Groups(groupNames, ConnectionId);
        }
    }
}
