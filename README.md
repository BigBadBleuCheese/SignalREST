# SignalREST
Make SignalR hubs available to any REST client.
    
## LICENSE
[Apache 2.0 License](https://raw.githubusercontent.com/BigBadBleuCheese/SignalREST/master/LICENSE)

## What is this for?

This library is intended to be used in projects that already have a [ASP.NET SignalR](https://github.com/SignalR/SignalR) implementation, but for which you would like to quickly include a REST-style interface. Consider using an explicitly defined ASP.NET WebAPI interface for more control over interaction with REST clients.

SignalREST works with both System.Web (IIS) and OWIN self-hosted configurations of SignalR.

## How to Use in the .NET Project

### Step 1: Install the NuGet package

You can either use the Package Manager Console:

    Install-Package Epiforge.SignalREST

Or use the NuGet Package Manager to install `Epiforge.SignalREST` as a NuGet package in your project.

### Step 2: Make hubs inherit from SignalREST instead of SignalR

Instead of your hubs inheriting from **Microsoft.AspNet.SignalR.Hub**, you want them to inherit from **SignalRest.Hub**. SignalRest.Hub inherits from Microsoft.AspNet.SignalR.Hub itself, and SignalR will continue to work as it did prior to this change.

In this example hub...

```
public class ExampleHub : Microsoft.AspNet.SignalR.Hub
{
    public int Add(int a, int b)
    {
        return a + b;
    }

    public void Marco()
    {
        Clients.All.Polo(DateTime.UtcNow);
    }
}
```

Change your code to look like this:

```
public class ExampleHub : SignalRest.Hub
{
    public int Add(int a, int b)
    {
        return a + b;
    }

    public void Marco()
    {
        Clients.All.Polo(DateTime.UtcNow);
    }
}
```

### Step 3: Get hub contexts from SignalREST instead of SignalR

In any case where you are getting a HubContext, ask SignalREST for it instead of SignalR. Using the properties of the HubContext will work the same for SignalR clients as it did prior to this change.

In this example...

```
var hub = GlobalHost.ConnectionManager.GetHubContext<ExampleHub>();
hub.Clients.All.timeUpdate(DateTime.UtcNow);
```

Change your code to look like this:

```
var hub = SignalRest.Hub.GetHubContext<ExampleHub>();
hub.Clients.All.timeUpdate(DateTime.UtcNow);
```

## How REST consumers can now use your SignalR hubs

SignalREST has equivalents of every part of the SignalR hub connection lifecycle except reconnecting (reconnection logic and the OnReconnected method of your hubs will still work properly for SignalR clients).

The SignalREST base URL will always be the root of your web application, followed by the `/signalrest` path segment. So, if the default SignalR URL for your web app was `http://somecompany.com/signalr` then your SignalREST base URL will be `http://somecompany.com/signalrest`. Therefore, connecting to SignalREST in that example would involve making a request to `http://somecompany.com/signalrest/connect/`.

All SignalREST web requests should be using HTTP POST and should carry a request `Content-Type` header set to `application/json`.

SignalREST follows SignalR's conventions for resolving hubs and methods:

- Hub names are case-insensitive
- Method names are case-insensitive
- Method overloads are permitted, so long as they have varying [arity](https://en.wikipedia.org/wiki/Arity)

### `/connect/`

Starts a SignalREST connection. You should make this request every time you are starting a new session.

The request body should be a JSON array containing the names of the hubs you want to use. For example:

```
['examplehub']
```

The response body will be a JSON string containing the connection ID you will use for all subsequent requests. This connection ID is similar to a SignalR connection ID, and will be used by the server to identify your specific session. For example:

```
"61267d37-7754-4471-bde2-6b295130f67f"
```

(Note: Do not include the quotes (`"`) enclosing the connection ID when using it in the URLs below.)

### `/connections/[CONNECTION ID]/disconnect/`

This request will disconnect your SignalREST connection. You should disconnect when you are finished using SignalREST. If you neglect to do so, SignalREST will automatically terminate and clean up your session in accordance with the SignalR disconnect timeout setting, whatever that happens to be on the server. SignalREST makes note of the last time you made a request for a given connection in order to enforce this. Since SignalR was designed to deliver event broadcasts, you should be making requests to `/connections/[CONNECTION_ID]/events/` (see below) regularly enough to ensure your connection is not automatically disconnected.

### `/connections/[CONNECTION ID]/invoke/[HUB NAME]/[METHOD NAME]/`

This request will invoke the specified method of the specified hub.

The request body should be a JSON array containing the arguments of the method being invoked. It may also be `null` or empty if the method has no arguments. For example, invoking ExampleHub.Add(3, 4) would have this request body:

```
[3, 4]
```

The response body will be a JSON serialization of whatever the method returns, or empty if it the method is of type `void`. For example, our invocation of ExampleHub.Add(3, 4) would have this response body:

```
7
```

### `/connections/[CONNECTION ID]/invoke/`
This request will invoke multiple methods at once.

The request body should be a JSON array containing objects that specify the invocations to make. For example, calling ExampleHub.Add multiple times could have this request body:

```
[{
	hub: 'examplehub',
	method: 'add',
	arguments: [1, 2]
},{
	hub: 'examplehub',
	method: 'add',
	arguments: [3, 4]
},{
	hub: 'examplehub',
	method: 'add',
	arguments: [5, 6]
}]
```

The response body will be a JSON array of serialized results of the invocations you specified, in the same order as you specified them. For example, the request above would have this response body:

```
[3, 7, 11]
```

If a method invocation specified could not be found or failed, the return value at the appropriate index will be a JSON object with a single property named `error`, the value of which will be a string describing what went wrong. Possible error messages include:

- `"Hub not found"`
- `"Hub method not found"`
- `"[EXCEPTION TYPE NAME]: [EXCEPTION MESSAGE]"`

### `/connections/[CONNECTION ID]/events/`

This request will give you all the SignalR event broadcasts that have been sent to your connection since the connection was started or the last time you asked, whichever is later.

The request body should be empty.

An example response body will be a JSON array. The array will be empty if no events have been raised since the connection was started or the last time you asked. A request returning events looks like this:

```
[
  {
    "Hub": "ExampleHub",
    "Method": "Polo",
    "Arguments": [
      "2017-01-09T23:12:58.7377726Z"
    ]
  },
  {
    "Hub": "ExampleHub",
    "Method": "Polo",
    "Arguments": [
      "2017-01-09T23:13:01.2331962Z"
    ]
  },
  {
    "Hub": "ExampleHub",
    "Method": "Polo",
    "Arguments": [
      "2017-01-09T23:13:05.2539065Z"
    ]
  }
]
```

### `/connections/[CONNECTION ID]/reconnect/`

Starts a SignalREST connection if it is not already in progress, or retrieves its events if it is already in progress. This request has an overhead by comparison to the `/connections/[CONNECTION ID]/events/` request, since it must ensure the hub names it has been provided match the specified session already in progress. Since the client assigning its own connection ID for new connections is considered harmful, you should not use this request as a replacement for the `/connect/` request when the you are knowingly starting a new connection. Only use this request when you are unsure whether the connection has expired and you want to reduce the number of roundtrips to the server.

The request body should be a JSON array containing the names of the hubs you want to use. For example:

```
['examplehub']
```

If a connection with the specified connection ID was not already in progress, the response will be in the same form as the `/connect/` request (a JSON string containing the connection ID). If a connection with the specified connection was already in progress, the response will be in the same form as the `/connections/[CONNECTION ID]/events/` request (an array of SignalR event broadcasts).
