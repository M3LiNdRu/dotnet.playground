using ApplicationServices.Modules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace WebApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseKestrel();
                })
                .ConfigureServices((hostContext, services) =>
                        {
                    //overiding Microsoft.Extensions.Hosting.Internal.ConsoleLifetime
                    //services.AddSingleton<IHostLifetime, ExampleHostLifetime>();
                    services.AddHostedService<ApplicationServices.HostedService>();
                    services.AddHostedService<ApplicationServices.BackgroundService>();

                    services.AddSingleton<ICommonModule, CommonModule>();
                });
    }
}
