namespace TravelBlogger.Domain.Entities;

public sealed class ContactMessage
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string ReplyMessage { get; set; } = string.Empty;
    public DateTime? RepliedAt { get; set; }
}
