using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuickChartMcp.Client;
using QuickChartMcp.Configuration;
using QuickChartMcp.IO;
using QuickChartMcp.Tools;

// Anchor the content root to the binary's directory so appsettings.json is found
// regardless of the working directory the MCP client launches us from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Bind and validate the QuickChart instance configuration ("QuickChart" section of
// appsettings.json or the QuickChart__* environment variables). Invalid config fails at
// startup rather than at first tool call.
builder.Services
    .AddOptions<QuickChartOptions>()
    .Bind(builder.Configuration.GetSection(QuickChartOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "QuickChart:BaseUrl must not be empty.")
    .Validate(o => o.TimeoutSeconds > 0, "QuickChart:TimeoutSeconds must be greater than 0.")
    .Validate(o => AllPatternsCompile(o.AllowedOutputPatterns),
        "QuickChart:AllowedOutputPatterns contains an invalid regular expression.")
    .ValidateOnStart();

// Compiled output-path allow-list, used by ArtifactWriter to gate every file write.
builder.Services.AddSingleton<PathPolicy>();

// Typed HttpClient pointed at the configured QuickChart instance. The optional API key is a
// request-body property ("key"), not a header, so it is applied by QuickChartClient itself.
builder.Services.AddHttpClient<QuickChartClient>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<QuickChartOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddSingleton<ArtifactWriter>();

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<QuickChartTools>();

await builder.Build().RunAsync();

static bool AllPatternsCompile(IEnumerable<string> patterns)
{
    foreach (var pattern in patterns)
    {
        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    return true;
}
