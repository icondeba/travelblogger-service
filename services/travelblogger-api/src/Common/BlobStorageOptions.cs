namespace TravelBlogger.Common;

public sealed class BlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public int SasMinutes { get; set; } = 60;
}
