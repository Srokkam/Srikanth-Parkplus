namespace IplFranchise.Application;

/// <summary>Stable problem-type slugs. The API turns each into https://.../problems/{slug}.</summary>
public static class ProblemCodes
{
    public const string ValidationFailed = "validation-failed";
    public const string InvalidContinuationToken = "invalid-continuation-token";
    public const string IdempotencyKeyRequired = "idempotency-key-required";
    public const string IdempotencyKeyReused = "idempotency-key-reused";
    public const string NotFound = "not-found";
    public const string ProductExists = "product-exists";
    public const string InsufficientStock = "insufficient-stock";
    public const string QuantityLimitExceeded = "quantity-limit-exceeded";
    public const string OutOfStock = "out-of-stock";
    public const string PriceChanged = "price-changed";
    public const string CartEmpty = "cart-empty";
    public const string CartConflict = "cart-conflict";
    public const string PreconditionFailed = "precondition-failed";
    public const string PreconditionRequired = "precondition-required";
    public const string RateLimited = "rate-limited";
    public const string DependencyUnavailable = "dependency-unavailable";
    public const string InternalError = "internal-error";
}

public class NotFoundException(string detail) : Exception(detail);

public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more fields are invalid.")
{
    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>The request is valid but clashes with the current state (HTTP 409).</summary>
public sealed class ConflictException(string code, string detail, IReadOnlyDictionary<string, object?>? extensions = null)
    : Exception(detail)
{
    public string Code { get; } = code;

    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}

public sealed class PreconditionFailedException(string? currentEtag) : Exception("The resource was changed by someone else.")
{
    public string? CurrentEtag { get; } = currentEtag;
}

public sealed class PreconditionRequiredException() : Exception("An If-Match header with the ETag you last read is required.");

public sealed class InvalidContinuationTokenException() : Exception("The continuation token is invalid, expired, or was issued for different filters.");

/// <summary>The database throttled us or is unreachable. Safe to retry.</summary>
public sealed class DependencyUnavailableException(string detail, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(detail, inner)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
