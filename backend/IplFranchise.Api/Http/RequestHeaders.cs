using IplFranchise.Application;

namespace IplFranchise.Api.Http;

internal static class RequestHeaders
{
    public const string IdempotencyKey = "Idempotency-Key";
    public const string CorrelationId = "X-Correlation-Id";

    public static string RequireIdempotencyKey(this HttpRequest request)
    {
        var raw = request.Headers[IdempotencyKey].ToString();
        if (string.IsNullOrEmpty(raw))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest, ProblemCodes.IdempotencyKeyRequired, $"The {IdempotencyKey} header is required.");
        }

        return Guid.TryParse(raw, out var key)
            ? key.ToString("N")
            : throw new ApiProblemException(
                StatusCodes.Status400BadRequest, ProblemCodes.ValidationFailed, $"The {IdempotencyKey} header must be a GUID.");
    }

    /// <summary>The ETag exactly as the client last received it, or null when the header is missing.</summary>
    public static string? IfMatch(this HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString().Trim();
        return raw.Length == 0 ? null : raw;
    }
}
