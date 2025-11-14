using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectOpsTestData_MCP.Mcp;
using ProjectOpsTestData_MCP.Services;

namespace ProjectOpsTestData_MCP;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();
            
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Starting Project Operations Test Data MCP Server");

            var mcpServer = host.Services.GetRequiredService<McpServer>();
            await mcpServer.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Application startup failed: {ex.Message}");
            return 1;
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // Get the directory where the executable is located
                var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
                
                if (!string.IsNullOrEmpty(assemblyDirectory))
                {
                    var appSettingsPath = Path.Combine(assemblyDirectory, "appsettings.json");
                    config.AddJsonFile(appSettingsPath, optional: true, reloadOnChange: true);
                }
                
                // Also try the current directory
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                
                // Environment variables take precedence
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddDebug();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                services.AddTransient<IProjectOpsTestDataService, ProjectOpsTestDataService>();
                services.AddTransient<McpServer>();
            })
            .UseConsoleLifetime(options =>
            {
                options.SuppressStatusMessages = true;
            });
}