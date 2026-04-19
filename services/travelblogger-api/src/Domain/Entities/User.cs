namespace TravelBlogger.Domain.Entities;

public enum UserRole
{
    Admin = 0
}

public sealed class User
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Admin;
    public DateTime? LastLoginDate { get; set; }
    public bool IsActive { get; set; } = true;
}
