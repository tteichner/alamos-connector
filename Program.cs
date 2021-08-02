using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AlamosConnector;


using IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = ".NET AlamosConnector Service";
    })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                })
    .Build();

await host.RunAsync();