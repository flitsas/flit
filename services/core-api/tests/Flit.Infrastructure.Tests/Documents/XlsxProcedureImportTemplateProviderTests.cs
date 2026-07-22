using Flit.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

public sealed class XlsxProcedureImportTemplateProviderTests
{
    [Fact]
    public void BuildXlsx_ProducesAValidTemplateReadableByTheImporter()
    {
        var bytes = new XlsxProcedureImportTemplateProvider().BuildXlsx();
        bytes.Should().NotBeEmpty();

        // La plantilla debe ser un .xlsx válido y consumible por el propio lector de importación.
        using var stream = new MemoryStream(bytes);
        var outcome = new XlsxProcedureImportReader().Read(stream);

        outcome.FatalError.Should().BeNull();
        outcome.Rows.Should().HaveCount(3);
        outcome.Rows[0].Modalidad.Should().Be("traspaso");
        outcome.Rows[0].Placa.Should().Be("ABC123");
        outcome.Rows[1].Modalidad.Should().Be("matricula_inicial");
        outcome.Rows[1].Vin.Should().Be("9BWZZZ377VT004251");
        outcome.Rows[2].TipoCodigo.Should().Be("TRASPASO_STANDARD");
    }
}
