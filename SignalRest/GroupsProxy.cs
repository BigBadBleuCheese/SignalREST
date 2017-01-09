using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

namespace SignalRest
{
    public class GroupsProxy : DynamicObject, IClientProxy
    {
        public GroupsProxy(IList<string> groupNames, string hubName, IList<string> excludeConnectionIds)
        {
            ExcludeConnectionIds = excludeConnectionIds;
            GroupNames = groupNames;
            HubName = hubName;
        }

        private IList<string> ExcludeConnectionIds { get; }
        private IList<string> GroupNames { get; }
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
            ApiController.GroupNamesInvoke(GroupNames, ExcludeConnectionIds, HubName, method, args);
            await ((IClientProxy)GlobalHost.ConnectionManager.GetHubContext(HubName).Clients.Groups(GroupNames, ExcludeConnectionIds?.ToArray() ?? new string[0])).Invoke(method, args);
        }
    }
}
