using Flit.Ict.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Jobs;

/// <summary>
/// HU #14600 (Feature #14601): paridad con v1 en <c>sp_processor_validation_business</c>. Como el resto de
/// pruebas del SP, leen el DDL embebido (el repo no levanta PostgreSQL en tests) y anclan la regla en el texto.
/// </summary>
/// <example>
/// var sql = EmbeddedDdl.LoadUp("05-ICT-sp-business.sql");
/// sql.Should().Contain("related_company_company_document invalido;");
/// </example>
public sealed class ParidadV1ReglasNegocioSqlTests
{
    private static readonly string Business = EmbeddedDdl.LoadUp("05-ICT-sp-business.sql");

    private static string Statement(string marker)
    {
        var text = Business.Replace("\r\n", "\n", StringComparison.Ordinal);
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        at.Should().BeGreaterThan(0, $"debe existir la regla '{marker}'");
        var start = text.LastIndexOf("UPDATE", at, StringComparison.Ordinal);
        var end = text.IndexOf("\n\n", at, StringComparison.Ordinal);
        return text[start..end];
    }

    [Fact]
    public void AC2_DocumentoDeCompaniaRelacionadaInvalidoSoloSiViene()
    {
        var s = Statement("related_company_company_document invalido;");

        s.Should().Contain("length(TRIM(COALESCE(related_company_document, ''))) > 0");
        s.Should().Contain(@"TRIM(related_company_document) !~ '^\d{6,11}$'");
        s.Should().Contain("WHERE id = rec.id_master");
    }

    [Fact]
    public void AC3_NombreDeCompaniaRelacionadaVacioConDocumentoPresente()
    {
        var s = Statement("related_company_company_name no debe estar vacio;");

        s.Should().Contain("length(TRIM(COALESCE(related_company_document, ''))) > 0");
        s.Should().Contain("length(TRIM(COALESCE(related_company_name, ''))) = 0");
    }

    [Fact]
    public void AC7_LaTransformacionFueraDeCatalogoNoSeValidaEnElSp()
    {
        // La FK fk_eimtt_type impide que un código fuera del catálogo llegue a la tabla puente: la regla vive
        // en RegisterTransformationInputValidator (ver RegisterRowValidatorTests.Ac7_*). Aquí no debe haber
        // un UPDATE muerto que nunca se dispare.
        Business.Should().NotContain("|| ' more_transaction_transaction_type/transactionType");
    }

    [Fact]
    public void AC8_SinCompaniaRelacionadaNoSeMarcaNada()
    {
        // Las dos reglas están condicionadas al documento presente: sin él el WHERE no coincide y no se
        // concatena novedad. No hay regla de obligatoriedad.
        Statement("related_company_company_document invalido;").Should().Contain("> 0");
        Statement("related_company_company_name no debe estar vacio;").Should().Contain("> 0");
        Business.Should().NotContain("related_company_company_document no debe");
    }

    [Fact]
    public void LasReglasNuevasVanEnSeccionGeneralAntesDeIntegracion()
    {
        var general = Business.IndexOf("===== Sección General =====", StringComparison.Ordinal);
        var rel = Business.IndexOf("related_company_company_document invalido;", StringComparison.Ordinal);
        var integ = Business.IndexOf("===== Sección Integración =====", StringComparison.Ordinal);

        general.Should().BeLessThan(rel);
        rel.Should().BeLessThan(integ);
    }

    [Fact]
    public void NoSePortaLaReglaV1_86DeDescripcion()
    {
        Business.Should().NotContain("transaction_description");
    }
}
