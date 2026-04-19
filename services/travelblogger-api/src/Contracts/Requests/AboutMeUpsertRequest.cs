namespace TravelBlogger.Contracts.Requests;

public sealed class AboutMeUpsertRequest
{
    public string Heading { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
}
