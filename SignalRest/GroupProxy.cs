using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

namespace SignalRest
{
    public class GroupProxy : DynamicObject, IClientProxy
    {
        public GroupProxy(string groupName, string hubName, IList<string> excludeConnectionIds)
        {
            ExcludeConnectionIds = excludeConnectionIds;
            GroupName = groupName;
            HubName = hubName;
        }

        private IList<string> ExcludeConnectionIds { get; }
        private string GroupName { get; }
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
            ApiController.GroupNamesInvoke(new string[] { GroupName }, ExcludeConnectionIds, HubName, method, args);
            await ((IClientProxy)GlobalHost.ConnectionManager.GetHubContext(HubName).Clients.Group(GroupName, ExcludeConnectionIds?.ToArray() ?? new string[0])).Invoke(method, args);
        }
    }
}
