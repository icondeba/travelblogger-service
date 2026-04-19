namespace TravelBlogger.Contracts.Requests;

public sealed class EventCreateRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
}
