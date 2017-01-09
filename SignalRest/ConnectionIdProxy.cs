using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using System.Dynamic;
using System.Threading.Tasks;

namespace SignalRest
{
    public class ConnectionIdProxy : DynamicObject, IClientProxy
    {
        public ConnectionIdProxy(string connectionId, string hubName)
        {
            ConnectionId = connectionId;
            HubName = hubName;
        }

        private string ConnectionId { get; }
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
            ApiController.ConnectionIdsInvoke(new string[] { ConnectionId }, HubName, method, args);
            await ((IClientProxy)GlobalHost.ConnectionManager.GetHubContext(HubName).Clients.Client(ConnectionId)).Invoke(method, args);
        }
    }
}
