using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

namespace SignalRest
{
    public class HubContext : IHubContext<object>, IHubContext
    {
        public HubContext(string hubName)
        {
            Clients = new HubConnectionContext(hubName);
            Groups = new GroupManager(hubName);
        }

        public IHubConnectionContext<dynamic> Clients { get; }
        public IGroupManager Groups { get; }
    }
}
