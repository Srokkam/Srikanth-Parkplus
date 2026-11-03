namespace IplFranchise.Api.Http;

/// <summary>A request problem found by the API layer itself (headers, route values), mapped to a problem+json response.</summary>
public sealed class ApiProblemException(int status, string code, string detail) : Exception(detail)
{
    public int Status { get; } = status;

    public string Code { get; } = code;
}
