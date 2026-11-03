using IplFranchise.Api.Background;
using IplFranchise.Api.Extensions;
using IplFranchise.Api.HealthChecks;
using IplFranchise.Api.Http;
using IplFranchise.Infrastructure;

const string FrontendCorsPolicy = "Frontend";

var builder = WebApplication.CreateBuilder(args);

// Structured logs: one JSON object per line, with scopes (so every entry carries its CorrelationId), everywhere except
// a developer's console. Request bodies and tokens are never logged.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders().AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
}

builder.Services.AddApiControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemHandling();
builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddRequestLimits(builder.Configuration);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddHostedService<CheckoutSweeper>();

builder.Services.AddHealthChecks()
    .AddCheck<CosmosDbHealthCheck>("cosmosdb", tags: ["db"], timeout: TimeSpan.FromSeconds(10));

// Allow the Angular dev server (and any other configured origins) to call the API.
builder.Services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .WithExposedHeaders("ETag", "Retry-After", RequestHeaders.CorrelationId)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "IPL Franchise API v1"));
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program;
