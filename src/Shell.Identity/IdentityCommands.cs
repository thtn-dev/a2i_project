using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shell.Identity.Access;

namespace Shell.Identity;

/// <summary>Account commands of the host: <c>dotnet Host.dll disable-user|enable-user &lt;email&gt;</c>. Exit code 0 on success.</summary>
public static class IdentityCommands
{
    public static async Task<int> RunAsync(IServiceProvider services, string command, string argument, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentityCommands));

        switch (command)
        {
            case "disable-user" or "enable-user":
                var disable = command == "disable-user";
                if (!await accounts.SetDisabledAsync(argument, disable, cancellationToken))
                {
                    logger.LogError("No account with email {Email}", argument);
                    return 1;
                }

                logger.LogInformation("Account {Email} {State}", argument, disable ? "disabled, all grants revoked" : "enabled");
                return 0;

            default:
                logger.LogError("Unknown command {Command}", command);
                return 1;
        }
    }
}
