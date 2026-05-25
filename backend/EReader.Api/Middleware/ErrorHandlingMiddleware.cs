using System.Text.Json;
using EReader.Api.Dtos;
using EReader.Core.Exceptions;

namespace EReader.Api.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await WriteAsync(context, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", ex.Message, ex.Details);
        }
        catch (AuthenticationException ex)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, ex.Code, ex.Message);
        }
        catch (AuthorizationException ex)
        {
            await WriteAsync(context, StatusCodes.Status403Forbidden, ex.Code, ex.Message);
        }
        catch (ConflictException ex)
        {
            await WriteAsync(context, StatusCodes.Status409Conflict, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "An unexpected error occurred.");
        }
    }

    private static Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string message,
        object? details = null)
    {
        if (context.Response.HasStarted)
        {
            // Can't rewrite a response that's mid-flight; just let it die.
            return Task.CompletedTask;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";

        var payload = new ErrorResponse(new ErrorBody(code, message, details));
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
