namespace TravelBlogger.Domain.Entities;

public enum ArticleStatus
{
    Draft = 0,
    Published = 1
}

public sealed class Article
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string ImageBlobName { get; set; } = string.Empty;
    public ArticleStatus Status { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
