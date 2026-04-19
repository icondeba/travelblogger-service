namespace TravelBlogger.Domain.Entities;

public sealed class VideoPost
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string YouTubeUrl { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
}
