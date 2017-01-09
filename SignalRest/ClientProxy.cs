using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

namespace SignalRest
{
    public class ClientProxy : DynamicObject, IClientProxy
    {
        public ClientProxy(string hubName, IList<string> exclude)
        {
            HubName = hubName;
            Exclude = exclude;
        }

        private string HubName { get; }
        private IList<string> Exclude { get; }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            result = Invoke(binder.Name, args);
            return true;
        }

        public async Task Invoke(string method, params object[] args)
        {
            ApiController.ClientProxyInvoke(Exclude, HubName, method, args);
            await ((IClientProxy)GlobalHost.ConnectionManager.GetHubContext(HubName).Clients.AllExcept(Exclude.ToArray())).Invoke(method, args);
        }
    }
}
