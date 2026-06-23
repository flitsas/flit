using System.Text;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

public sealed class KyverumWebhookVerifierTests
{
    private const string Secret = "super-secret-hmac-key";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"verificationId":"kyv_1","status":"approved"}""");

    [Fact]
    public void IsValid_CorrectHexSignature_ReturnsTrue()
    {
        var signature = KyverumWebhookVerifier.ComputeHmac(Body, Secret);
        KyverumWebhookVerifier.IsValid(Body, signature, Secret).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithSha256Prefix_ReturnsTrue()
    {
        var signature = "sha256=" + KyverumWebhookVerifier.ComputeHmac(Body, Secret);
        KyverumWebhookVerifier.IsValid(Body, signature, Secret).Should().BeTrue();
    }

    [Fact]
    public void IsValid_Base64Signature_ReturnsTrue()
    {
        var hex = KyverumWebhookVerifier.ComputeHmac(Body, Secret);
        var bytes = Convert.FromHexString(hex);
        var base64 = Convert.ToBase64String(bytes);
        KyverumWebhookVerifier.IsValid(Body, base64, Secret).Should().BeTrue();
    }

    [Fact]
    public void IsValid_TamperedBody_ReturnsFalse()
    {
        var signature = KyverumWebhookVerifier.ComputeHmac(Body, Secret);
        var tampered = Encoding.UTF8.GetBytes("""{"verificationId":"kyv_1","status":"rejected"}""");
        KyverumWebhookVerifier.IsValid(tampered, signature, Secret).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WrongSecret_ReturnsFalse()
    {
        var signature = KyverumWebhookVerifier.ComputeHmac(Body, Secret);
        KyverumWebhookVerifier.IsValid(Body, signature, "otro-secreto").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-valid-signature!")]
    public void IsValid_MissingOrMalformedSignature_ReturnsFalse(string? signature)
    {
        KyverumWebhookVerifier.IsValid(Body, signature, Secret).Should().BeFalse();
    }

    [Fact]
    public void IsValid_NullBodyOrSecret_ReturnsFalse()
    {
        var signature = KyverumWebhookVerifier.ComputeHmac(Body, Secret);
        KyverumWebhookVerifier.IsValid(null, signature, Secret).Should().BeFalse();
        KyverumWebhookVerifier.IsValid(Body, signature, null).Should().BeFalse();
    }
}
