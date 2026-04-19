using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;

namespace TravelBlogger.Api.Middleware;

public sealed class CorsMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IConfiguration _configuration;

    public CorsMiddleware(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var request = await context.GetHttpRequestDataAsync();
        if (request is null)
        {
            await next(context);
            return;
        }

        var allowedOrigins = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        var origin = GetHeaderValue(request.Headers, "Origin");
        var allowAll = allowedOrigins.Any(o => o == "*");
        var allowOrigin = ResolveAllowedOrigin(origin, allowedOrigins, allowAll);

        if (string.IsNullOrWhiteSpace(allowOrigin) && IsLocalhostOrigin(origin))
        {
            allowOrigin = origin;
        }

        var requestHeaders = GetHeaderValue(request.Headers, "Access-Control-Request-Headers");

        if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var preflight = request.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(preflight, allowOrigin, requestHeaders);
            context.GetInvocationResult().Value = preflight;
            return;
        }

        await next(context);

        if (context.GetInvocationResult().Value is HttpResponseData response)
        {
            AddCorsHeaders(response, allowOrigin, requestHeaders);
        }
    }

    private static void AddCorsHeaders(HttpResponseData response, string? allowOrigin, string? requestHeaders)
    {
        if (!string.IsNullOrWhiteSpace(allowOrigin))
        {
            SetHeader(response, "Access-Control-Allow-Origin", allowOrigin);

            if (allowOrigin != "*")
            {
                SetHeader(response, "Access-Control-Allow-Credentials", "true");
            }
            else
            {
                response.Headers.Remove("Access-Control-Allow-Credentials");
            }
        }

        SetHeader(response, "Vary", "Origin");
        SetHeader(response, "Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");
        SetHeader(
            response,
            "Access-Control-Allow-Headers",
            string.IsNullOrWhiteSpace(requestHeaders) ? "Authorization,Content-Type" : requestHeaders);
    }

    private static bool IsLocalhostOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetHeaderValue(HttpHeadersCollection headers, string headerName)
    {
        foreach (var (key, values) in headers)
        {
            if (string.Equals(key, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    private static string? ResolveAllowedOrigin(string? requestOrigin, string[] allowedOrigins, bool allowAll)
    {
        if (allowAll)
        {
            return "*";
        }

        if (string.IsNullOrWhiteSpace(requestOrigin))
        {
            return null;
        }

        var normalizedRequestOrigin = NormalizeOrigin(requestOrigin);
        return allowedOrigins.FirstOrDefault(allowed =>
            string.Equals(NormalizeOrigin(allowed), normalizedRequestOrigin, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeOrigin(string origin)
    {
        return origin.Trim().TrimEnd('/');
    }

    private static void SetHeader(HttpResponseData response, string headerName, string value)
    {
        response.Headers.Remove(headerName);
        response.Headers.Add(headerName, value);
    }
}
