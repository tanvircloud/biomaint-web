// Services/TelemetryService.cs
namespace WebApp.Services;

public sealed class TelemetryService
{
    public Task TrackAsync(string eventName, object? props = null)
    {
        // later: wire to your analytics endpoint or Consent-based logger
        return Task.CompletedTask;
    }
}
