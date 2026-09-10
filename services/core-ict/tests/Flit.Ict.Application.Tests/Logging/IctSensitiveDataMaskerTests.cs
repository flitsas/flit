using Flit.Ict.Infrastructure.Logging;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Logging;

public sealed class IctSensitiveDataMaskerTests
{
    [Fact]
    public void MaskJsonBody_redacts_secrets_and_masks_pii_at_any_depth()
    {
        // El body del /register trae PII anidada (seller/buyer con documentos, nombres, teléfonos): el
        // enmascarado debe alcanzar cualquier nivel, no solo el objeto raíz.
        const string input = """
            {
              "password": "s3cr3t",
              "company_manager_document": "901698038",
              "seller": [
                { "document_number": "79912345", "name": "ABRAHAM CANON", "phone": "3001234567" }
              ]
            }
            """;

        var masked = IctSensitiveDataMasker.MaskJsonBody(input);

        masked.Should().NotBeNull();
        masked!.Should().Contain("***REDACTED***"); // clave sensible: password
        masked.Should().NotContain("s3cr3t");
        masked.Should().NotContain("79912345");      // documento enmascarado
        masked.Should().Contain("2345");             // conserva últimos 4
        masked.Should().NotContain("ABRAHAM CANON"); // nombre enmascarado (anidado en el array)
        masked.Should().NotContain("3001234567");    // teléfono enmascarado
    }

    [Fact]
    public void MaskJsonBody_returns_placeholder_for_non_json_never_raw()
    {
        // Un cuerpo no-JSON o truncado NUNCA debe persistirse crudo (podría llevar secretos/PII).
        var masked = IctSensitiveDataMasker.MaskJsonBody("password=s3cr3t esto-no-es-json{");

        masked.Should().Be("<cuerpo no capturable (no-JSON o truncado)>");
        masked.Should().NotContain("s3cr3t");
    }

    [Fact]
    public void MaskJsonBody_returns_null_for_empty_or_whitespace()
    {
        IctSensitiveDataMasker.MaskJsonBody(null).Should().BeNull();
        IctSensitiveDataMasker.MaskJsonBody("   ").Should().BeNull();
    }
}
