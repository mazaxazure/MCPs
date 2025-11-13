using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Planning_MCP.Services.Planning;
using Planning_MCP.Mcp;

namespace Planning_MCP;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();
            
            // Get the MCP server and run it
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
                // Add logging - Configure to write to stderr only to avoid interfering with MCP stdio
                services.AddLogging(configure =>
                {
                    configure.ClearProviders();
                    configure.AddConsole(options =>
                    {
                        options.LogToStandardErrorThreshold = LogLevel.Trace;
                    });
                    configure.SetMinimumLevel(LogLevel.Information);
                });

                // Add Planning services
                services.AddSingleton<DocumentTaskParser>();
                services.AddSingleton<ScheduleInferenceService>();
                services.AddSingleton<TaskPlanner>();
                
                // Add MCP Server
                services.AddSingleton<McpServer>();
            });
}
