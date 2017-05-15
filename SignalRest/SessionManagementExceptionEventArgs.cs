using System;

namespace SignalRest
{
    public class SessionManagementExceptionEventArgs : EventArgs
    {
        public SessionManagementExceptionEventArgs(Exception exception, string connectionId, string hubName)
        {
            ConnectionId = connectionId;
            Exception = exception;
            HubName = hubName;
        }

        public string ConnectionId { get; }
        public Exception Exception { get; }
        public string HubName { get; }
    }
}
