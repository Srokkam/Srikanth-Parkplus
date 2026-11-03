using Microsoft.AspNetCore.Mvc;

namespace IplFranchise.Api.Http;

internal static class ProblemFactory
{
    private static readonly Dictionary<int, string> Titles = new()
    {
        [400] = "Bad request",
        [401] = "Unauthorized",
        [403] = "Forbidden",
        [404] = "Not found",
        [409] = "Conflict",
        [412] = "Precondition failed",
        [428] = "Precondition required",
        [429] = "Too many requests",
        [500] = "Unexpected error",
        [503] = "Service unavailable",
    };

    public static ProblemDetails Create(int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Type = $"/problems/{code}",
            Title = Titles.GetValueOrDefault(status, "Error"),
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        return problem;
    }

    public static ValidationProblemDetails CreateValidation(IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Type = "/problems/validation-failed",
            Title = "One or more fields are invalid",
        };
        problem.Extensions["code"] = "validation-failed";
        return problem;
    }
}
