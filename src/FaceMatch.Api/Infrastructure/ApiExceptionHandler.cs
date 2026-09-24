using FaceMatch.Core.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FaceMatch.Api.Infrastructure;

/// <summary>Maps domain exceptions to RFC 7807 problem responses.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            InvalidImageException => (StatusCodes.Status400BadRequest, "Invalid image"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            BadHttpRequestException bad => (bad.StatusCode, "Bad request"),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "Client closed request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status >= 500 ? "An unexpected error occurred." : exception.Message,
            },
        });
    }
}
