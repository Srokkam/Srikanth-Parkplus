using IplFranchise.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace IplFranchise.Api.Http;

/// <summary>The single place where domain failures become HTTP responses.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var problem = Map(exception, context);
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (problem.Status >= 500)
        {
            logger.LogError(exception, "Request failed");
        }

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }

    private static ProblemDetails Map(Exception exception, HttpContext context)
    {
        switch (exception)
        {
            case ValidationFailedException ex:
                return ProblemFactory.CreateValidation(ex.Errors.ToDictionary(e => e.Key, e => e.Value));

            case NotFoundException ex:
                return ProblemFactory.Create(StatusCodes.Status404NotFound, ProblemCodes.NotFound, ex.Message);

            case ConflictException ex:
                var conflict = ProblemFactory.Create(StatusCodes.Status409Conflict, ex.Code, ex.Message);
                foreach (var (key, value) in ex.Extensions)
                {
                    conflict.Extensions[key] = value;
                }

                return conflict;

            case PreconditionFailedException ex:
                if (ex.CurrentEtag is not null)
                {
                    context.Response.Headers.ETag = ex.CurrentEtag;
                }

                return ProblemFactory.Create(
                    StatusCodes.Status412PreconditionFailed,
                    ProblemCodes.PreconditionFailed,
                    "Someone changed this since you loaded it. Reload and try again.");

            case PreconditionRequiredException ex:
                return ProblemFactory.Create(StatusCodes.Status428PreconditionRequired, ProblemCodes.PreconditionRequired, ex.Message);

            case InvalidContinuationTokenException ex:
                return ProblemFactory.Create(StatusCodes.Status400BadRequest, ProblemCodes.InvalidContinuationToken, ex.Message);

            case ApiProblemException ex:
                return ProblemFactory.Create(ex.Status, ex.Code, ex.Message);

            case DependencyUnavailableException ex:
                return Unavailable(context, ex.RetryAfter);

            case CosmosException { StatusCode: System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.RequestTimeout } ex:
                return Unavailable(context, ex.RetryAfter);

            case BadHttpRequestException ex:
                return ProblemFactory.Create(StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, ex.Message);

            default:
                return ProblemFactory.Create(StatusCodes.Status500InternalServerError, ProblemCodes.InternalError, "Something went wrong on our side.");
        }
    }

    private static ProblemDetails Unavailable(HttpContext context, TimeSpan? retryAfter)
    {
        var seconds = (int)Math.Ceiling((retryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds);
        context.Response.Headers.RetryAfter = Math.Max(seconds, 1).ToString();
        return ProblemFactory.Create(
            StatusCodes.Status503ServiceUnavailable, ProblemCodes.DependencyUnavailable, "The service is busy. Please retry shortly.");
    }
}
