using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace whatsapp_bff.Tests.Testing;

/// <summary>
/// Mirrors whatsapp-bff's own Platform/PlatformServices.cs internal JWT validation so
/// WebApplicationFactory-based endpoint tests (e.g. POST /internal/messages, now protected)
/// can mint a token that satisfies it, instead of bypassing auth entirely.
/// </summary>
public static class TestAuth
{
    public const string InboundSecret = "test-only-internal-auth-inbound-secret-32-bytes-min";
    public const string Issuer = "conversational-ai-platform";
    public const string Audience = "whatsapp-bff";
    public const string CallerServiceName = "conversation-orchestrator";
    public const string TenantId = "00000000-0000-0000-0000-000000000001";

    public static void ConfigureSigningKey(IWebHostBuilder builder) =>
        builder.UseSetting($"InternalAuth:InboundSecrets:{CallerServiceName}", InboundSecret);

    public static string IssueToken()
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InboundSecret)),
            SecurityAlgorithms.HmacSha256);
        var header = new JwtHeader(credentials);
        header["kid"] = CallerServiceName;
        var payload = new JwtPayload(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, CallerServiceName),
                new Claim("tenant_id", TenantId)
            ],
            notBefore: now,
            expires: now.AddMinutes(5),
            issuedAt: null);
        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
