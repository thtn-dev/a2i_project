using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Shell.Core;
using Shell.Core.Data;
using Shell.Core.Modules;
using Shell.Host;

// Static logger used for start-up and fatal errors only; the host builds its own logger below.
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateLogger();

try
{
    // `dotnet Host.dll migrate` migrates (and optionally seeds) the databases and exits; never done on normal start-up.
    var migrate = args is ["migrate", ..];
    var builder = WebApplication.CreateBuilder(migrate ? args[1..] : args);

    builder.Services.AddSerilog(
        (services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(new RenderedCompactJsonFormatter()),
        preserveStaticLogger: true);

    var catalog = ModuleLoader.LoadFromDirectory(AppContext.BaseDirectory);
    builder.Services.AddShell(builder.Configuration, catalog);
    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options => options.OperationFilter<TenantHostHeaderOperationFilter>());
    builder.Services.AddOptions<HostOptions>()
        .Configure<IOptions<ShellOptions>>((host, shell) => host.ShutdownTimeout = TimeSpan.FromSeconds(shell.Value.ShutdownTimeoutSeconds));

    await using var app = builder.Build();

    if (migrate)
    {
        await app.Services.GetRequiredService<MigrationRunner>().RunAsync(CancellationToken.None);
        return 0;
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        // ProblemDetails without exception details (Development uses the developer exception page).
        app.UseExceptionHandler();
    }

    app.UseStatusCodePages();
    app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, ex) =>
        ex is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
        : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
        : LogEventLevel.Information);
    app.UseTenantResolution();

    app.MapShellHealthChecks();
    app.MapModules();

    app.Logger.LogInformation("Loaded modules in order: {Modules}", catalog.Modules.Select(m => m.Id));

    await app.RunAsync();
    return 0;
}
catch (ModuleLoadException ex)
{
    Log.Fatal("Host refused to start, invalid modules: {Reason}", ex.Message);
    return 1;
}
catch (OptionsValidationException ex)
{
    Log.Fatal("Host refused to start, invalid configuration: {Reason}", string.Join(" ", ex.Failures));
    return 1;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
