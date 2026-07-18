using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace whatsapp_bff.Platform;

public sealed class InternalAuthOptions
{
    public const string SectionName = "InternalAuth";
    public string Issuer { get; init; } = "conversational-ai-platform";
    public string ServiceName { get; init; } = "whatsapp-bff";
    public string SigningKey { get; init; } = string.Empty;
    public int TokenTtlSeconds { get; init; } = 300;
}

public sealed class InternalTokenService(IOptions<InternalAuthOptions> options)
{
    public string CreateToken(string audience)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.SigningKey))
        {
            throw new InvalidOperationException("InternalAuth:SigningKey is required.");
        }

        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: value.Issuer,
            audience: audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, value.ServiceName)],
            notBefore: now,
            expires: now.AddSeconds(Math.Max(30, value.TokenTtlSeconds)),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class InternalAuthHandler(
    InternalTokenService tokenService,
    string audience) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new("Bearer", tokenService.CreateToken(audience));
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

    public void Increment(string metricName, params (string Name, string Value)[] labels)
    {
        _counters.AddOrUpdate(MetricKey(metricName, labels), 1, (_, current) => current + 1);
    }

    public string RenderPrometheus()
    {
        var builder = new StringBuilder();
        foreach (var item in _counters.OrderBy(item => item.Key))
        {
            builder.Append(item.Key).Append(' ').Append(item.Value).AppendLine();
        }
        foreach (var item in _durationCounts.OrderBy(item => item.Key))
        {
            var baseKey = item.Key;
            _durationSums.TryGetValue(baseKey, out var sum);
            builder.Append(baseKey.Replace("_seconds", "_seconds_count", StringComparison.Ordinal))
                .Append(' ').Append(item.Value).AppendLine();
            builder.Append(baseKey.Replace("_seconds", "_seconds_sum", StringComparison.Ordinal))
                .Append(' ').Append(sum.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        }
        return builder.ToString();
    }

    private static string MetricKey(string metricName, params (string Name, string Value)[] labels)
    {
        if (labels.Length == 0) return metricName;
        var rendered = string.Join(',', labels.Select(label =>
            $"{Sanitize(label.Name)}=\"{Escape(label.Value)}\""));
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

public sealed class PlatformMetricsMiddleware(
    RequestDelegate next,
    PlatformMetrics metrics)
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

        var validationKey = string.IsNullOrWhiteSpace(auth.SigningKey)
            ? "invalid-missing-internal-auth-signing-key"
            : auth.SigningKey;
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth.Issuer,
                    ValidateAudience = true,
                    ValidAudience = auth.ServiceName,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(validationKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });
        services.AddAuthorization();
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
