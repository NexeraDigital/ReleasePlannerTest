using GitHubProjectConnection.Hosting;
using Microsoft.Extensions.Hosting;

// Pin the content root to the binary's folder so appsettings.json (copied to the output
// directory) loads no matter which directory `dotnet run` is invoked from.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddSampleApp(builder.Configuration);

using IHost host = builder.Build();
return await host.RunSampleAsync(args);
