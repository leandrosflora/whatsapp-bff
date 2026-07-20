using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using JsonWebToken = Microsoft.IdentityModel.JsonWebTokens.JsonWebToken;
using whatsapp_bff.Configuration;

namespace whatsapp_bff.Platform;

public sealed class InternalAuthOptions
{
    public const string SectionName = "InternalAuth";
    public string Issuer { get; init; } = "conversational-ai-platform";
    public string ServiceName { get; init; } = "whatsapp-bff";
    public Dictionary<string, string> OutboundSecrets { get; init; } = new();
    public Dictionary<string, string> InboundSecrets { get; init; } = new();
    public int TokenTtlSeconds { get; init; } = 300;

    public static bool HasValidSecret(string? secret) =>
        !string.IsNullOrEmpty(secret) && Encoding.UTF8.GetByteCount(secret) >= 32;
}

public static class TenantClaims
{
    public const string ClaimType = "tenant_id";

    public static bool TryNormalize(string? value, out string tenantId)
    {
        tenantId = string.Empty;
        if (!Guid.TryParse(value?.Trim(), out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }
        tenantId = parsed.ToString("D");
        return true;
    }

    public static bool Matches(ClaimsPrincipal principal, string? headerTenant, out string tenantId)
    {
        tenantId = string.Empty;
        return TryNormalize(headerTenant, out var header)
            && TryNormalize(principal.FindFirstValue(ClaimType), out var claim)
            && string.Equals(header, claim, StringComparison.Ordinal)
            && ((tenantId = claim) is not null);
    }
}

public sealed class InternalTokenService(IOptions<InternalAuthOptions> options)
{
    public string CreateToken(string audience, string tenantId)
    {
        var value = options.Value;
        if (!value.OutboundSecrets.TryGetValue(audience, out var secret) || !InternalAuthOptions.HasValidSecret(secret))
        {
            throw new InvalidOperationException(
                $"InternalAuth:OutboundSecrets:{audience} must be configured with at least 32 UTF-8 bytes.");
        }
        if (!TenantClaims.TryNormalize(tenantId, out var canonicalTenant))
        {
            throw new ArgumentException("Tenant ID must be a non-empty UUID.", nameof(tenantId));
        }

        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);
        var header = new JwtHeader(credentials);
        header["kid"] = value.ServiceName;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, value.ServiceName),
            new(TenantClaims.ClaimType, canonicalTenant),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n"))
        };
        var payload = new JwtPayload(
            issuer: value.Issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(Math.Clamp(value.TokenTtlSeconds, 30, 900)),
            issuedAt: null);
        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class InternalAuthHandler(
    InternalTokenService tokenService,
    IOptions<OrchestratorOptions> orchestratorOptions,
    string audience) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!TenantClaims.TryNormalize(orchestratorOptions.Value.TenantId, out var tenantId))
        {
            throw new InvalidOperationException("Orchestrator:TenantId must be a non-empty UUID.");
        }
        request.Headers.Authorization = new("Bearer", tokenService.CreateToken(audience, tenantId));
        request.Headers.Remove("X-Tenant-Id");
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId);
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class PlatformMetrics
{
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _durationSums = new();
    private readonly ConcurrentDictionary<string, long> _durationCounts = new();

    public void RecordRequest(string method, string path, int statusCode, double durationSeconds)
    {
        var normalizedPath = NormalizePath(path);
        Increment("platform_http_requests_total",
            ("method", method), ("path", normalizedPath), ("status", statusCode.ToString()));
        var durationKey = MetricKey("platform_http_request_duration_seconds", ("method", method), ("path", normalizedPath));
        _durationSums.AddOrUpdate(durationKey, durationSeconds, (_, current) => current + durationSeconds);
        _durationCounts.AddOrUpdate(durationKey, 1, (_, current) => current + 1);
    }

    public void Increment(string metricName, params (string Name, string Value)[] labels) =>
        _counters.AddOrUpdate(MetricKey(metricName, labels), 1, (_, current) => current + 1);

    public string RenderPrometheus()
    {
        var builder = new StringBuilder();
        foreach (var item in _counters.OrderBy(item => item.Key))
        {
            builder.Append(item.Key).Append(' ').Append(item.Value).AppendLine();
        }
        foreach (var item in _durationCounts.OrderBy(item => item.Key))
        {
            _durationSums.TryGetValue(item.Key, out var sum);
            builder.Append(item.Key.Replace("_seconds", "_seconds_count", StringComparison.Ordinal))
                .Append(' ').Append(item.Value).AppendLine();
            builder.Append(item.Key.Replace("_seconds", "_seconds_sum", StringComparison.Ordinal))
                .Append(' ').Append(sum.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        }
        return builder.ToString();
    }

    private static string MetricKey(string metricName, params (string Name, string Value)[] labels)
    {
        if (labels.Length == 0) return metricName;
        var rendered = string.Join(",", labels.Select(label => $"{Sanitize(label.Name)}=\"{Escape(label.Value)}\""));
        return $"{Sanitize(metricName)}{{{rendered}}}";
    }

    private static string Sanitize(string value) => Regex.Replace(value, "[^a-zA-Z0-9_:]", "_");
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string NormalizePath(string path)
    {
        path = Regex.Replace(path, "/[0-9a-fA-F]{8}-[0-9a-fA-F-]{27,}", "/{id}");
        path = Regex.Replace(path, "/\\d+", "/{id}");
        path = Regex.Replace(path, "/[A-Za-z0-9_-]{24,}", "/{id}");
        return path;
    }
}

public sealed class PlatformMetricsMiddleware(RequestDelegate next, PlatformMetrics metrics)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            metrics.RecordRequest(
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalSeconds);
        }
    }
}

public static class PlatformServiceExtensions
{
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var auth = configuration.GetSection(InternalAuthOptions.SectionName).Get<InternalAuthOptions>()
            ?? new InternalAuthOptions();
        services.Configure<InternalAuthOptions>(configuration.GetSection(InternalAuthOptions.SectionName));
        services.AddSingleton<InternalTokenService>();
        services.AddSingleton<PlatformMetrics>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth.Issuer,
                    ValidateAudience = true,
                    ValidAudience = auth.ServiceName,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                    {
                        if (kid is null
                            || !auth.InboundSecrets.TryGetValue(kid, out var secret)
                            || !InternalAuthOptions.HasValidSecret(secret))
                        {
                            return Array.Empty<SecurityKey>();
                        }
                        return new SecurityKey[] { new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) };
                    },
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var metrics = context.HttpContext.RequestServices.GetRequiredService<PlatformMetrics>();
                        var kid = context.SecurityToken switch
                        {
                            JsonWebToken jsonWebToken => jsonWebToken.Kid,
                            JwtSecurityToken legacyToken => legacyToken.Header.Kid,
                            _ => null
                        };
                        var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                        if (kid is null || !string.Equals(kid, sub, StringComparison.Ordinal))
                        {
                            metrics.Increment("internal_auth_validation_failures_total", ("reason", "kid_sub_mismatch"));
                            context.Fail("kid/sub mismatch");
                        }
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        var metrics = context.HttpContext.RequestServices.GetRequiredService<PlatformMetrics>();
                        metrics.Increment("internal_auth_validation_failures_total", ("reason", "token_invalid"));
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(JwtRegisteredClaimNames.Sub)
                .RequireClaim(TenantClaims.ClaimType)
                .Build();
        });
        return services;
    }

    public static WebApplication UsePlatformServices(this WebApplication app)
    {
        app.UseMiddleware<PlatformMetricsMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static WebApplication MapPlatformEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/metrics", (PlatformMetrics metrics) =>
            Results.Text(metrics.RenderPrometheus(), "text/plain; version=0.0.4")).AllowAnonymous();
        return app;
    }
}
