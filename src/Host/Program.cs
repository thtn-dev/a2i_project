using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Shell.Core;
using Shell.Core.Data;
using Shell.Core.Modules;
using Shell.Host;
using Shell.Identity;

// Static logger used for start-up and fatal errors only; the host builds its own logger below.
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    .CreateLogger();

try
{
    // One-shot commands, run instead of the web host (see IdentityCommands for the account ones):
    //   migrate                   migrate (and optionally seed) the databases; never done on normal start-up
    //   disable-user <email>      block sign-in and revoke every grant;  enable-user <email>
    var (command, operand, hostArgs) = args switch
    {
        ["migrate", .. var rest] => ("migrate", null, rest),
        [var name and ("disable-user" or "enable-user"), var value, .. var rest] => (name, value, rest),
        _ => ((string?)null, (string?)null, args),
    };
    var builder = WebApplication.CreateBuilder(hostArgs);

    builder.Services.AddSerilog(
        (services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(new RenderedCompactJsonFormatter()),
        preserveStaticLogger: true);

    var catalog = ModuleLoader.LoadFromDirectory(AppContext.BaseDirectory);
    builder.Services.AddShell(builder.Configuration, catalog);
    builder.Services.AddShellIdentity(builder.Configuration);
    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.OperationFilter<TenantHostHeaderOperationFilter>();
        // Paste an access token obtained from the authorization server (tenants whose auth mode is optional also work without).
        options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer" });
        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearer", document)] = [] });
    });
    builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ShellOptions>>((forwarded, shell) =>
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        if (shell.Value.TrustForwardedHeaders)
        {
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
        }
    });
    builder.Services.AddOptions<HostOptions>()
        .Configure<IOptions<ShellOptions>>((host, shell) => host.ShutdownTimeout = TimeSpan.FromSeconds(shell.Value.ShutdownTimeoutSeconds));

    await using var app = builder.Build();

    if (command == "migrate")
    {
        await app.Services.GetRequiredService<MigrationRunner>().RunAsync(CancellationToken.None);
        return 0;
    }

    if (command is not null)
    {
        return await IdentityCommands.RunAsync(app.Services, command, operand!, CancellationToken.None);
    }

    app.UseForwardedHeaders();

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
    app.UseShellIdentity();

    app.MapShellHealthChecks();
    app.MapShellIdentity();
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
