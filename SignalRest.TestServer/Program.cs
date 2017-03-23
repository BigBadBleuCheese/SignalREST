using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using System;

namespace SignalRest.TestServer
{
    class Program
    {
        static void Main(string[] args)
        {
            GlobalHost.Configuration.DisconnectTimeout = TimeSpan.FromHours(110);
            GlobalHost.Configuration.ConnectionTimeout = TimeSpan.FromHours(30);
            GlobalHost.Configuration.KeepAlive = TimeSpan.FromHours(10);

            using (var app = WebApp.Start("http://*:88"))
            {
                Console.WriteLine("Started server. Press any key to quit.");
                Console.ReadKey();
            }
        }
    }
}
