using Microsoft.AspNet.SignalR;
using System.Threading.Tasks;

namespace SignalRest
{
    public class GroupManager : IGroupManager
    {
        public GroupManager(string hubName)
        {
            HubName = hubName;
        }

        private string HubName { get; }

        public async Task Add(string connectionId, string groupName)
        {
            ApiController.GroupAdd(HubName, connectionId, groupName);
            await GlobalHost.ConnectionManager.GetHubContext(HubName).Groups.Add(connectionId, groupName);
        }

        public async Task Remove(string connectionId, string groupName)
        {
            ApiController.GroupRemove(HubName, connectionId, groupName);
            await GlobalHost.ConnectionManager.GetHubContext(HubName).Groups.Remove(connectionId, groupName);
        }
    }
}
