namespace TravelBlogger.Contracts.Responses;

public sealed class VideoListResponse
{
    public List<VideoResponse> Items { get; set; } = new();
    public string? NextPageToken { get; set; }
    public bool HasMore => !string.IsNullOrWhiteSpace(NextPageToken);
}
