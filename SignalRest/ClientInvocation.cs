namespace SignalRest
{
    public class ClientInvocation
    {
        public ClientInvocation(string hub, string method, params object[] arguments)
        {
            Hub = hub;
            Method = method;
            Arguments = arguments;
        }

        public string Hub { get; }
        public string Method { get; }
        public object[] Arguments { get; }
    }
}
