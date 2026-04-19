using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace TravelBlogger.Api;

public sealed class OpenApiConfigurationOptions : IOpenApiConfigurationOptions
{
    public OpenApiInfo Info { get; set; } = new()
    {
        Version = "1.0.0",
        Title = "TravelBlogger API",
        Description = "Azure Functions API for the TravelBlogger backend."
    };

    public List<OpenApiServer> Servers { get; set; } = new();

    public OpenApiVersionType OpenApiVersion { get; set; } = OpenApiVersionType.V3;

    public bool IncludeRequestingHostName { get; set; } = true;

    public bool ForceHttp { get; set; } = false;

    public bool ForceHttps { get; set; } = false;

    public List<IDocumentFilter> DocumentFilters { get; set; } = new();
}
