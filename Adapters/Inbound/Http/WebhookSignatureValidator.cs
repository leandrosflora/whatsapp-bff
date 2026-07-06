using System.Security.Cryptography;
using System.Text;

namespace whatsapp_bff.Adapters.Inbound.Http;

public static class WebhookSignatureValidator
{
    private const string SignaturePrefix = "sha256=";

    public static bool IsValid(byte[] body, string? signatureHeader, string appSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(appSecret))
        {
            return false;
        }

        if (!signatureHeader.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedHex = signatureHeader[SignaturePrefix.Length..];
        if (!TryParseHex(providedHex, out var providedBytes))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var computedBytes = hmac.ComputeHash(body);

        return CryptographicOperations.FixedTimeEquals(computedBytes, providedBytes);
    }

    private static bool TryParseHex(string hex, out byte[] bytes)
    {
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            bytes = [];
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
