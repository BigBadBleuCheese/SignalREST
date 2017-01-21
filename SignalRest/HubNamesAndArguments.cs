using Newtonsoft.Json.Linq;

namespace SignalRest
{
    public class HubNamesAndArguments
    {
        public string[] HubNames { get; set; }
        public JArray Arguments { get; set; }
    }
}
