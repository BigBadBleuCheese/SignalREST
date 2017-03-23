using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Owin;
using System.Web.Http;

[assembly: OwinStartup(typeof(SignalRest.TestServer.Startup))]

namespace SignalRest.TestServer
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.UseCors(CorsOptions.AllowAll);

            var httpConfiguration = new HttpConfiguration();
            httpConfiguration.MapHttpAttributeRoutes();
            app.UseWebApi(httpConfiguration);

            app.MapSignalR(new HubConfiguration
            {
                EnableDetailedErrors = true,
                EnableJavaScriptProxies = false,
                EnableJSONP = true
            });
        }
    }
}
