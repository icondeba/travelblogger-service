namespace TravelBlogger.Contracts.Requests;

public sealed class LoginRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
