using Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances.BulkImport;

public sealed class ProcedureImportCsvParserTests
{
    [Fact]
    public void Parse_EmptyContent_ReturnsFatalError()
    {
        var outcome = ProcedureImportCsvParser.Parse("   ");

        outcome.Rows.Should().BeEmpty();
        outcome.FatalError.Should().NotBeNull();
    }

    [Fact]
    public void Parse_HeaderWithoutModalidadOrTipo_ReturnsFatalError()
    {
        var outcome = ProcedureImportCsvParser.Parse("placa,vin\nABC123,");

        outcome.FatalError.Should().Contain("modalidad");
    }

    [Fact]
    public void Parse_HeaderButNoDataRows_ReturnsFatalError()
    {
        var outcome = ProcedureImportCsvParser.Parse("modalidad,placa\n");

        outcome.Rows.Should().BeEmpty();
        outcome.FatalError.Should().NotBeNull();
    }

    [Fact]
    public void Parse_ValidRows_MapsColumnsCaseInsensitiveAnyOrder()
    {
        const string csv =
            "Placa,Modalidad,Tipo_Codigo,Oficina_Transito_Codigo,Vin\n" +
            "ABC123,traspaso,,05001,\n" +
            ",matricula_inicial,,,9BWZZZ377VT004251";

        var outcome = ProcedureImportCsvParser.Parse(csv);

        outcome.FatalError.Should().BeNull();
        outcome.Rows.Should().HaveCount(2);

        var r1 = outcome.Rows[0];
        r1.RowNumber.Should().Be(1);
        r1.Modalidad.Should().Be("traspaso");
        r1.Placa.Should().Be("ABC123");
        r1.TransitOfficeCode.Should().Be("05001");
        r1.Vin.Should().BeNull();
        r1.TipoCodigo.Should().BeNull();

        var r2 = outcome.Rows[1];
        r2.RowNumber.Should().Be(2);
        r2.Modalidad.Should().Be("matricula_inicial");
        r2.Vin.Should().Be("9BWZZZ377VT004251");
        r2.Placa.Should().BeNull();
    }

    [Fact]
    public void Parse_SkipsBlankLines_AndTrimsCells()
    {
        const string csv = "modalidad, placa \r\n\r\ntraspaso , ABC123 \r\n";

        var outcome = ProcedureImportCsvParser.Parse(csv);

        outcome.Rows.Should().HaveCount(1);
        outcome.Rows[0].Modalidad.Should().Be("traspaso");
        outcome.Rows[0].Placa.Should().Be("ABC123");
    }

    [Fact]
    public void Parse_ExceedingMaxRows_ReturnsFatalError()
    {
        var lines = new List<string> { "modalidad" };
        for (var i = 0; i < 4; i++)
            lines.Add("traspaso");

        var outcome = ProcedureImportCsvParser.Parse(string.Join('\n', lines), maxRows: 3);

        outcome.Rows.Should().BeEmpty();
        outcome.FatalError.Should().Contain("máximo");
    }

    [Fact]
    public void Parse_AtMaxRows_Succeeds()
    {
        var outcome = ProcedureImportCsvParser.Parse("modalidad\ntraspaso\ntraspaso\ntraspaso", maxRows: 3);

        outcome.FatalError.Should().BeNull();
        outcome.Rows.Should().HaveCount(3);
    }

    [Fact]
    public void Parse_RespectsQuotedFieldsWithCommasAndEscapedQuotes()
    {
        const string csv = "modalidad,tipo_codigo\n\"traspaso\",\"TRASPASO_\"\"STD\"\"\"";

        var outcome = ProcedureImportCsvParser.Parse(csv);

        outcome.Rows.Should().HaveCount(1);
        outcome.Rows[0].Modalidad.Should().Be("traspaso");
        outcome.Rows[0].TipoCodigo.Should().Be("TRASPASO_\"STD\"");
    }
}
