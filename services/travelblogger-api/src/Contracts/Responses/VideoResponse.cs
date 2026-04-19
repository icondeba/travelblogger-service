namespace TravelBlogger.Contracts.Responses;

public sealed class VideoResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string YouTubeUrl { get; set; } = string.Empty;
    public string VideoId { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
}
