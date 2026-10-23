using FluentValidation;
using LedgerCore.Application.Common;
using LedgerCore.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LedgerCore.Api.ErrorHandling;

/// <summary>
/// Turns every exception into an RFC 7807 ProblemDetails response. Known failures keep their
/// message; anything else becomes a generic 500 with no internal detail, and is logged instead.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => ValidationProblem(validation),
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Resource not found", "not_found", exception.Message),
            ConflictException => Problem(StatusCodes.Status409Conflict, "Conflict", "conflict", exception.Message),
            AuthenticationFailedException => Problem(StatusCodes.Status401Unauthorized, "Authentication failed", "authentication_failed", exception.Message),
            // A well-formed request that breaks a business rule, e.g. insufficient funds.
            DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", domain.Code, domain.Message),
            _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred", "internal_error", detail: null),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception");
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string code, string? detail) =>
        new()
        {
            Status = status,
            Title = title,
            Detail = detail,
            Extensions = { ["code"] = code },
        };

    private static ValidationProblemDetails ValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Extensions = { ["code"] = "validation_failed" },
        };
    }
}
