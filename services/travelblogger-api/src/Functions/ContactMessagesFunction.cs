using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using TravelBlogger.Common;
using TravelBlogger.Contracts.Requests;
using TravelBlogger.Contracts.Responses;
using TravelBlogger.Domain.Entities;
using TravelBlogger.Infrastructure.Notifications;
using TravelBlogger.Infrastructure.Repositories;

namespace TravelBlogger.Functions;

public sealed class ContactMessagesFunction
{
    private const int NameMaxLength = 200;
    private const int EmailMaxLength = 256;
    private const int PhoneMaxLength = 32;
    private const int MessageMaxLength = 4000;
    private static readonly Regex E164PhoneRegex = new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);

    private readonly IContactMessageRepository _messages;
    private readonly IContactNotificationService _notifications;
    private readonly ILogger<ContactMessagesFunction> _logger;

    public ContactMessagesFunction(
        IContactMessageRepository messages,
        IContactNotificationService notifications,
        ILogger<ContactMessagesFunction> logger)
    {
        _messages = messages;
        _notifications = notifications;
        _logger = logger;
    }

    [Function("GetContactMessages")]
    [OpenApiOperation(operationId: "GetContactMessages", tags: new[] { "ContactMessages" }, Summary = "Get all contact messages (admin)")]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<List<ContactMessageResponse>>))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> GetContactMessages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "contact-messages")] HttpRequestData req,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - GetContactMessages started", correlationId);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - GetContactMessages unauthorized", correlationId);
                return authResult;
            }

            var items = await _messages.GetAllAsync(ct);
            var response = items.Select(ToResponse).ToList();

            _logger.LogInformation("CorrelationId {CorrelationId} - GetContactMessages completed", correlationId);
            return await ResponseFactory.OkAsync(req, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - GetContactMessages failed", correlationId);
            throw;
        }
    }

    [Function("CreateContactMessage")]
    [OpenApiOperation(operationId: "CreateContactMessage", tags: new[] { "ContactMessages" }, Summary = "Create contact message")]
    [OpenApiRequestBody("application/json", typeof(ContactMessageCreateRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.Created, "application/json", typeof(ApiResponse<ContactMessageResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> CreateContactMessage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "contact-messages")] HttpRequestData req,
        CancellationToken ct)
    {
        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return await ResponseFactory.NoContentAsync(req);
        }

        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - CreateContactMessage started", correlationId);

        try
        {
            var body = await req.ReadFromJsonAsync<ContactMessageCreateRequest>(cancellationToken: ct);
            if (body is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateContactMessage invalid request body", correlationId);
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");
            }

            var validationErrors = ValidateCreatePayload(body.Name, body.Email, body.PhoneNumber, body.Message);
            if (validationErrors.Length > 0)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - CreateContactMessage validation failed", correlationId);
                return await ResponseFactory.BadRequestAsync(req, "Validation failed.", validationErrors);
            }

            var entity = new ContactMessage
            {
                Id = Guid.NewGuid(),
                Name = body.Name.Trim(),
                Email = body.Email.Trim(),
                PhoneNumber = body.PhoneNumber.Trim(),
                Message = body.Message.Trim(),
                SubmittedAt = DateTime.UtcNow,
                ReplyMessage = string.Empty,
                RepliedAt = null
            };

            await _messages.AddAsync(entity, ct);

            _logger.LogInformation("CorrelationId {CorrelationId} - CreateContactMessage completed. MessageId {MessageId}", correlationId, entity.Id);
            return await ResponseFactory.CreatedAsync(req, ToResponse(entity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - CreateContactMessage failed", correlationId);
            throw;
        }
    }

    [Function("ReplyContactMessage")]
    [OpenApiOperation(operationId: "ReplyContactMessage", tags: new[] { "ContactMessages" }, Summary = "Reply to contact message (admin)")]
    [OpenApiParameter("id", In = ParameterLocation.Path, Required = true, Type = typeof(Guid))]
    [OpenApiRequestBody("application/json", typeof(ContactMessageReplyRequest), Required = true)]
    [OpenApiResponseWithBody(HttpStatusCode.OK, "application/json", typeof(ApiResponse<ContactMessageResponse>))]
    [OpenApiResponseWithBody(HttpStatusCode.BadRequest, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.NotFound, "application/json", typeof(ApiResponse))]
    [OpenApiResponseWithBody(HttpStatusCode.Unauthorized, "application/json", typeof(ApiResponse))]
    public async Task<HttpResponseData> ReplyContactMessage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "contact-messages/{id:guid}/reply")] HttpRequestData req,
        Guid id,
        CancellationToken ct)
    {
        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return await ResponseFactory.NoContentAsync(req);
        }

        var correlationId = Guid.NewGuid();
        _logger.LogInformation("CorrelationId {CorrelationId} - ReplyContactMessage started. MessageId {MessageId}", correlationId, id);

        try
        {
            var authResult = await ValidateAdminAsync(req, correlationId);
            if (authResult is not null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - ReplyContactMessage unauthorized. MessageId {MessageId}", correlationId, id);
                return authResult;
            }

            var body = await req.ReadFromJsonAsync<ContactMessageReplyRequest>(cancellationToken: ct);
            if (body is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - ReplyContactMessage invalid request body. MessageId {MessageId}", correlationId, id);
                return await ResponseFactory.BadRequestAsync(req, "Invalid request body.");
            }

            var replyMessage = body.ReplyMessage?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(replyMessage))
            {
                return await ResponseFactory.BadRequestAsync(req, "Reply message is required.");
            }

            if (replyMessage.Length > MessageMaxLength)
            {
                return await ResponseFactory.BadRequestAsync(req, $"Reply message cannot exceed {MessageMaxLength} characters.");
            }

            var entity = await _messages.GetByIdAsync(id, ct);
            if (entity is null)
            {
                _logger.LogWarning("CorrelationId {CorrelationId} - ReplyContactMessage not found. MessageId {MessageId}", correlationId, id);
                return await ResponseFactory.NotFoundAsync(req, "Contact message not found.");
            }

            await _notifications.SendReplyAsync(entity, replyMessage, ct);

            entity.ReplyMessage = replyMessage;
            entity.RepliedAt = DateTime.UtcNow;

            await _messages.UpdateAsync(entity, ct);

            _logger.LogInformation("CorrelationId {CorrelationId} - ReplyContactMessage completed. MessageId {MessageId}", correlationId, id);
            return await ResponseFactory.OkAsync(req, ToResponse(entity), "Reply sent");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Contact reply notification failed. MessageId {MessageId}", id);
            return await ResponseFactory.BadRequestAsync(req, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CorrelationId {CorrelationId} - ReplyContactMessage failed. MessageId {MessageId}", correlationId, id);
            throw;
        }
    }

    private Task<HttpResponseData?> ValidateAdminAsync(HttpRequestData req, Guid correlationId)
    {
        return Task.FromResult<HttpResponseData?>(null);
    }

    private static ContactMessageResponse ToResponse(ContactMessage entity)
    {
        return new ContactMessageResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Email = entity.Email,
            PhoneNumber = entity.PhoneNumber,
            Message = entity.Message,
            SubmittedAt = entity.SubmittedAt,
            ReplyMessage = entity.ReplyMessage,
            RepliedAt = entity.RepliedAt
        };
    }

    private static string[] ValidateCreatePayload(string? name, string? email, string? phoneNumber, string? message)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Name is required.");
        }
        else if (name.Trim().Length > NameMaxLength)
        {
            errors.Add($"Name cannot exceed {NameMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors.Add("Email is required.");
        }
        else if (email.Trim().Length > EmailMaxLength)
        {
            errors.Add($"Email cannot exceed {EmailMaxLength} characters.");
        }
        else if (!IsValidEmail(email.Trim()))
        {
            errors.Add("Email is not valid.");
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            errors.Add("Phone number is required.");
        }
        else if (phoneNumber.Trim().Length > PhoneMaxLength)
        {
            errors.Add($"Phone number cannot exceed {PhoneMaxLength} characters.");
        }
        else if (!E164PhoneRegex.IsMatch(phoneNumber.Trim()))
        {
            errors.Add("Phone number must be in E.164 format (for example, +14155552671).");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            errors.Add("Message is required.");
        }
        else if (message.Trim().Length > MessageMaxLength)
        {
            errors.Add($"Message cannot exceed {MessageMaxLength} characters.");
        }

        return errors.ToArray();
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var parsed = new MailAddress(email);
            return string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
