using Microsoft.Extensions.Options;

namespace GamingStation.Api.Sessions;

// One backend process owns this sweep. The state change itself is a conditional
// database update, so examining the same session twice publishes at most once.
// Multiple servers are out of scope. A later cloud deployment can keep this
// domain model and add a single worker, because a second worker that loses the
// conditional update must not change the row or publish again.
public sealed class SessionExpiryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SessionOptions _options;
    private readonly ILogger<SessionExpiryService> _logger;

    public SessionExpiryService(
        IServiceScopeFactory scopes,
        IOptions<SessionOptions> options,
        ILogger<SessionExpiryService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.ExpiryCheckIntervalSeconds);
        _logger.LogInformation(
            "Session expiry service started. Check interval is {ExpiryCheckIntervalSeconds} seconds.",
            _options.ExpiryCheckIntervalSeconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ExpireOnceAsync(stoppingToken);
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("Session expiry service stopped.");
    }

    private async Task ExpireOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var lifecycle = scope.ServiceProvider.GetRequiredService<SessionLifecycle>();
            await lifecycle.ExpireDueSessionsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Session expiry processing failed.");
        }
    }
}
