using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using IplFranchise.Api.Auth;
using IplFranchise.Api.Http;
using IplFranchise.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IplFranchise.Api.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<CartViewBuilder>();
        services.AddScoped<CatalogueService>();
        services.AddScoped<CartService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<CheckoutRecovery>();
        services.AddScoped<OrderService>();
        services.AddScoped<ProductAdminService>();
        return services;
    }

    public static IMvcBuilder AddApiControllers(this IServiceCollection services) =>
        services.AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)))
            .ConfigureApiBehaviorOptions(options =>
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value is { Errors.Count: > 0 })
                        .ToDictionary(
                            entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                            entry => entry.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());

                    return new BadRequestObjectResult(ProblemFactory.CreateValidation(errors));
                });

    public static IServiceCollection AddProblemHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                if (context.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
                {
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
                }
            });
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }

    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>>((jwt, auth) =>
            {
                // Keep the claim names as issued ("sub", "role") instead of the legacy long Microsoft names.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = auth.Value.Issuer,
                    ValidAudience = auth.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.Value.SigningKey)),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, policy => policy.RequireRole(TokenIssuer.AdminRole));

        return services;
    }

    /// <summary>Search is cheap but public, checkout is expensive. Both are limited per signed-in user, or per address when anonymous.</summary>
    public static IServiceCollection AddRequestLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Limits are read per request from options, so per-environment (and test) settings always apply.
            options.AddPolicy("search", context => PerCaller(context, Limits(context).SearchPerMinute));
            options.AddPolicy("checkout", context => PerCaller(context, Limits(context).CheckoutPerMinute));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                var problem = ProblemFactory.Create(
                    StatusCodes.Status429TooManyRequests, ProblemCodes.RateLimited, "Too many requests. Please slow down.");
                await context.HttpContext.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
            };
        });

        return services;
    }

    private static RateLimitingOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static RateLimitPartition<string> PerCaller(HttpContext context, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [System.ComponentModel.DataAnnotations.Range(1, 100_000)]
    public int SearchPerMinute { get; set; } = 120;

    [System.ComponentModel.DataAnnotations.Range(1, 100_000)]
    public int CheckoutPerMinute { get; set; } = 10;
}
