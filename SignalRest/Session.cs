using System;
using System.Collections.Concurrent;
using System.Linq;

namespace SignalRest
{
    public class Session
    {
        public Session(string[] hubNames)
        {
            ClientInvocations = new ConcurrentQueue<ClientInvocation>();
            Hubs = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(hubNames.ToDictionary(hubName => hubName, hubName => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        }

        public ConcurrentQueue<ClientInvocation> ClientInvocations { get; }
        public string ConnectionId { get; set; }
        public ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> Hubs { get; set; }
        public DateTime LastKeepAlive { get; set; }
    }
}
