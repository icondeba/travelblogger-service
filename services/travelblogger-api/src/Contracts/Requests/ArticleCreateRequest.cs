namespace TravelBlogger.Contracts.Requests;

public sealed class ArticleCreateRequest
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public DateTime? PublishedAt { get; set; }
}
