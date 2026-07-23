using Flit.Ict.Infrastructure.Logging;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Observability;

public sealed class MaskerTests
{
    [Fact]
    public void Sensitive_headers_are_redacted_on_capture()
    {
        var headers = new[]
        {
            new KeyValuePair<string, string>("Authorization", "Bearer super-secret-token"),
            new KeyValuePair<string, string>("Content-Type", "application/json"),
        };

        var json = IctSensitiveDataMasker.RedactHeaders(headers);

        json.Should().NotContain("super-secret-token");
        json.Should().Contain(IctSensitiveDataMasker.Redacted);
        json.Should().Contain("application/json");
    }

    [Fact]
    public void Pii_values_are_masked_on_serve_leaving_last_four()
    {
        var json = """{"documentNumber":"11207262","city":"Manizales"}""";

        var masked = IctSensitiveDataMasker.MaskJson(json);

        masked.Should().NotContain("11207262");
        masked.Should().Contain("7262"); // últimos 4
        masked.Should().Contain("Manizales"); // no PII sensible
    }
}
