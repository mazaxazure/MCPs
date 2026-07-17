using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DataverseSchema_MCP.Services;
using DataverseSchema_MCP.Mcp;

namespace DataverseSchema_MCP;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();

            var mcpServer = host.Services.GetRequiredService<McpServer>();
            await mcpServer.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                // Logging goes to stderr only so it does not interfere with MCP stdio.
                services.AddLogging(configure =>
                {
                    configure.ClearProviders();
                    configure.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    configure.SetMinimumLevel(LogLevel.Information);
                });

                services.AddSingleton<IDataverseSchemaService, DataverseSchemaService>();
                services.AddSingleton<McpServer>();
            });
}
