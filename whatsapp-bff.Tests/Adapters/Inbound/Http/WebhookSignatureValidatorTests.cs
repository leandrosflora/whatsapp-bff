using System.Security.Cryptography;
using System.Text;
using whatsapp_bff.Adapters.Inbound.Http;
using Xunit;

namespace whatsapp_bff.Tests.Adapters.Inbound.Http;

public class WebhookSignatureValidatorTests
{
    [Fact]
    public void IsValid_MatchingSignature_ReturnsTrue()
    {
        const string secret = "shh";
        var body = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(body));

        Assert.True(WebhookSignatureValidator.IsValid(body, signature, secret));
    }

    [Fact]
    public void IsValid_WrongSecret_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("correct-secret"));
        var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(body));

        Assert.False(WebhookSignatureValidator.IsValid(body, signature, "wrong-secret"));
    }

    [Fact]
    public void IsValid_MissingHeader_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        Assert.False(WebhookSignatureValidator.IsValid(body, null, "secret"));
    }

    [Fact]
    public void IsValid_MalformedHeader_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("{}");

        Assert.False(WebhookSignatureValidator.IsValid(body, "not-a-signature", "secret"));
    }
}
