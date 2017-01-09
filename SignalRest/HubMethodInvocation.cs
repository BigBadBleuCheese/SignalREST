using Newtonsoft.Json.Linq;

namespace SignalRest
{
    public class HubMethodInvocation
    {
        public string Hub { get; set; }
        public string Method { get; set; }
        public JArray Arguments { get; set; }
    }
}
