using System;
using System.Dynamic;
using System.Globalization;

namespace SignalRest
{
    internal class NullClientProxy : DynamicObject
    {
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Using a Hub instance not created by the HubPipeline is unsupported."));
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "Using a Hub instance not created by the HubPipeline is unsupported."));
        }
    }
}
