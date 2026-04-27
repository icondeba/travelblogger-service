using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace TravelBlogger.Common;

public static class ResponseFactory
{
    public static async Task<HttpResponseData> OkAsync<T>(HttpRequestData req, T data, string message = "OK")
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(ApiResponse<T>.Ok(data, message));
        return res;
    }

    public static async Task<HttpResponseData> OkCachedAsync<T>(HttpRequestData req, T data, int maxAgeSeconds = 300, string message = "OK")
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Cache-Control", $"public, max-age={maxAgeSeconds}");
        await res.WriteAsJsonAsync(ApiResponse<T>.Ok(data, message));
        return res;
    }

    public static async Task<HttpResponseData> CreatedAsync<T>(HttpRequestData req, T data, string message = "Created")
    {
        var res = req.CreateResponse(HttpStatusCode.Created);
        await res.WriteAsJsonAsync(ApiResponse<T>.Ok(data, message));
        return res;
    }

    public static Task<HttpResponseData> NoContentAsync(HttpRequestData req)
    {
        var res = req.CreateResponse(HttpStatusCode.NoContent);
        return Task.FromResult(res);
    }

    public static async Task<HttpResponseData> BadRequestAsync(HttpRequestData req, string message, string[]? errors = null)
    {
        var res = req.CreateResponse(HttpStatusCode.BadRequest);
        await res.WriteAsJsonAsync(ApiResponse.Fail(message, errors));
        return res;
    }

    public static async Task<HttpResponseData> UnauthorizedAsync(HttpRequestData req, string message)
    {
        var res = req.CreateResponse(HttpStatusCode.Unauthorized);
        await res.WriteAsJsonAsync(ApiResponse.Fail(message));
        return res;
    }

    public static async Task<HttpResponseData> ForbiddenAsync(HttpRequestData req, string message)
    {
        var res = req.CreateResponse(HttpStatusCode.Forbidden);
        await res.WriteAsJsonAsync(ApiResponse.Fail(message));
        return res;
    }

    public static async Task<HttpResponseData> NotFoundAsync(HttpRequestData req, string message)
    {
        var res = req.CreateResponse(HttpStatusCode.NotFound);
        await res.WriteAsJsonAsync(ApiResponse.Fail(message));
        return res;
    }
}
