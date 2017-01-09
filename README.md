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

Or use the NuGet Package Manager to install Epiforge.SignalREST as a NuGet package in your project.

### Step 2: Change the inheritance of your SignalR hub classes

Instead of inheriting from **Microsoft.AspNet.SignalR.Hub**, you want to inherit from **SignalRest.Hub**. SignalRest.Hub inherits from Microsoft.AspNet.SignalR.Hub itself, and SignalR will continue to work as it did prior to this change.

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

### Step 3: Get HubContexts from SignalREST instead of SignalR

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
