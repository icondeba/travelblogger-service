namespace TravelBlogger.Domain.Entities;

public sealed class AboutMe
{
    public Guid Id { get; set; }
    public string Heading { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string ImageBlobName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
