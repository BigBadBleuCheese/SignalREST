using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using System.Collections.Generic;
using System.Dynamic;
using System.Threading.Tasks;

namespace SignalRest
{
    public class ConnectionIdsProxy : DynamicObject, IClientProxy
    {
        public ConnectionIdsProxy(IList<string> connectionIds, string hubName)
        {
            ConnectionIds = connectionIds;
            HubName = hubName;
        }

        private IList<string> ConnectionIds { get; }
        private string HubName { get; }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = null;
            return false;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = Invoke(binder.Name, args);
            return true;
        }

        public async Task Invoke(string method, params object[] args)
        {
            ApiController.ConnectionIdsInvoke(ConnectionIds, HubName, method, args);
            await ((IClientProxy)GlobalHost.ConnectionManager.GetHubContext(HubName).Clients.Clients(ConnectionIds)).Invoke(method, args);
        }
    }
}
