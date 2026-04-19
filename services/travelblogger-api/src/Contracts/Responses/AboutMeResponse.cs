namespace TravelBlogger.Contracts.Responses;

public sealed class AboutMeResponse
{
    public Guid Id { get; set; }
    public string Heading { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
