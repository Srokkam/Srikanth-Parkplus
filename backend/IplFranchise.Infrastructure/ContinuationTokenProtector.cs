using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IplFranchise.Application;
using Microsoft.Extensions.Options;

namespace IplFranchise.Infrastructure;

/// <summary>
/// Wraps the raw Cosmos DB continuation token so a client can't read it, edit it, reuse it with other filters,
/// or keep using it forever. Format: base64url(payload) "." base64url(HMAC-SHA256).
/// </summary>
internal sealed class ContinuationTokenProtector(IOptions<ContinuationTokenOptions> options, TimeProvider clock) : IContinuationTokenProtector
{
    private sealed record Payload(string T, string S, long E);

    private readonly ContinuationTokenOptions settings = options.Value;

    public string Protect(string cosmosToken, string scope)
    {
        var expires = clock.GetUtcNow().AddMinutes(settings.LifetimeMinutes).ToUnixTimeSeconds();
        var body = JsonSerializer.SerializeToUtf8Bytes(new Payload(cosmosToken, scope, expires));
        return $"{Base64Url(body)}.{Base64Url(Sign(body, settings.SigningKey))}";
    }

    public string Unprotect(string token, string scope)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2)
            {
                throw new InvalidContinuationTokenException();
            }

            var body = FromBase64Url(parts[0]);
            var signature = FromBase64Url(parts[1]);

            var genuine = CryptographicOperations.FixedTimeEquals(signature, Sign(body, settings.SigningKey))
                || (!string.IsNullOrEmpty(settings.PreviousSigningKey)
                    && CryptographicOperations.FixedTimeEquals(signature, Sign(body, settings.PreviousSigningKey)));

            var payload = genuine ? JsonSerializer.Deserialize<Payload>(body) : null;
            if (payload is null || payload.S != scope || payload.E < clock.GetUtcNow().ToUnixTimeSeconds())
            {
                throw new InvalidContinuationTokenException();
            }

            return payload.T;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new InvalidContinuationTokenException();
        }
    }

    private static byte[] Sign(byte[] body, string key) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), body);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
