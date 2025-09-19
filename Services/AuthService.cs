// Services/AuthService.cs
namespace WebApp.Services;

public sealed class AuthService
{
    private string? _token; // later: persist securely

    public ValueTask<string?> GetAccessTokenAsync() => ValueTask.FromResult(_token);

    public Task SetAccessTokenAsync(string token)
    {
        _token = token;
        return Task.CompletedTask;
    }

    public Task SignOutAsync()
    {
        _token = null;
        return Task.CompletedTask;
    }
}
