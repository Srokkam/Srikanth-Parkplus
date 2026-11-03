using System.Diagnostics;

namespace IplFranchise.Api.Http;

/// <summary>Every request gets an id the caller can quote when something goes wrong. A caller-supplied one is kept.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[RequestHeaders.CorrelationId].ToString();
        var id = supplied.Length is > 0 and <= 64 ? supplied : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = id;
        context.Response.Headers[RequestHeaders.CorrelationId] = id;
        Activity.Current?.SetTag("correlation.id", id);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }
}
