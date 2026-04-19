using System.Net;
using BCrypt.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using TravelBlogger.Common;
using TravelBlogger.Contracts.Requests;
using TravelBlogger.Contracts.Responses;
using TravelBlogger.Infrastructure.Repositories;

namespace TravelBlogger.Functions;

public sealed class AuthFunction
{
    private readonly IUserRepository _users;
    private readonly ILogger<AuthFunction> _logger;

    public AuthFunction(IUserRepository users, ILogger<AuthFunction> logger)
    {
        _users = users;
        _logger = logger;
    }

    [Function("Login")]
    [OpenApiOperation(operationId: "Login", tags: new[] { "Auth" }, Summary = "Authenticate admin user")]
    [OpenApiRequestBody("application/json", typeof(LoginRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<AuthResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - Login started", correlationId);

        try
        {
            var body = await req.ReadFromJsonAsync<LoginRequest>(cancellationToken: ct);
            if (body is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - Login invalid request body", correlationId);
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");
            }

            var loginId = ReadLoginId(body);
            if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(body.Password))
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - Login missing user id or password", correlationId);
                return await ResponseFactory.BadRequestAsync(req, "User id and password are required.");
            }

            var user = await _users.GetByLoginIdAsync(loginId, ct);
            if (user is null || !BCrypt.Net.BCrypt.Verify(body.Password, user.PasswordHash))
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - Login invalid credentials for {LoginId}", correlationId, loginId);
                return await ResponseFactory.UnauthorizedAsync(req, "Invalid credentials.");
            }

            user.LastLoginDate = DateTime.UtcNow;
            await _users.UpdateAsync(user, ct);

            var response = new AuthResponse
            {
                UserId = user.UserId
            };

            _logger.LogInformation("CorrelationId {CorrelationId} - Login completed for {LoginId}", correlationId, loginId);
            return await ResponseFactory.OkAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - Login failed", correlationId);
            throw;
        }
    }

    private static string ReadLoginId(LoginRequest body) =>
        body.UserId.Trim();
}
