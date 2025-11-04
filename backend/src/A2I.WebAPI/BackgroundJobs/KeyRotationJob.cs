using A2I.Infrastructure.Identity.Security;
using Quartz;

namespace A2I.WebAPI.BackgroundJobs;

public class KeyRotationJob : IJob
{
    private readonly KeyManagementService _keyService;
    private readonly ILogger<KeyRotationJob> _logger;

    public KeyRotationJob(KeyManagementService keyService, ILogger<KeyRotationJob> logger)
    {
        _keyService = keyService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            _logger.LogInformation("Executing scheduled key rotation...");
            await _keyService.RotateKey();
            _logger.LogInformation("Scheduled key rotation completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate keys");
            throw;
        }
    }
}