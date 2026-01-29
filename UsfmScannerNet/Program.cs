using CSnakes.Runtime;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UsfmScannerNet.Services;

namespace UsfmScannerNet;

class Program
{
    static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var configuration = builder.Configuration;
        builder.Services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.AddBlobServiceClient(configuration.GetValue<string>("BlobServiceConnectionString")).WithName("BlobClient");
            clientBuilder.AddServiceBusClient(configuration.GetValue<string>("ServiceBusConnectionString")).WithName("ServiceBusClient");
        });
        builder.Services.AddHostedService<ScannerService>();
        builder.Services.AddHttpClient("http", config =>
        {
            config.DefaultRequestHeaders.Add("User-Agent", "UsfmScannerNet");
        });
        builder.Services.WithPython()
            .WithHome(".")
            .WithVirtualEnvironment("python-env")
            .FromRedistributable()
            .WithPipInstaller();
        var app = builder.Build();
        app.Run();
    }
}