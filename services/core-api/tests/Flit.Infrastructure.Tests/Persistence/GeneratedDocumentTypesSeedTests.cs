using System.Text;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #11181 (Feature #11174) — los documentos que genera el sistema entran al catálogo
/// (46-HU11181-document-types-generados.sql) para poder reordenarse igual que los adjuntos.
/// <para>
/// Las pruebas leen el DDL embebido —igual que <see cref="DocumentCatalogParityTests"/>— porque el
/// esquema de <c>document_types</c> lo lleva SQL crudo, no el scaffolding de EF.
/// </para>
/// </summary>
public sealed class GeneratedDocumentTypesSeedTests
{
    private const string ResourceName =
        "Flit.Infrastructure.Persistence.Sql.Ddl.46-HU11181-document-types-generados.sql";

    /// <summary>Tipos generados que NO existían en el catálogo y esta HU da de alta (AC1).</summary>
    private static readonly string[] NuevosGenerados =
    [
        "certificado_identidad", "certificado_identidad_vendedor",
        "certificado_rues", "certificado_rnmc",
        "escritura", "escritura_comprador",
    ];

    /// <summary>
    /// Todos los documentos que produce FLIT, incluidos los que ya existían en el catálogo (AC2).
    /// Es el mismo conjunto que ordenan hoy <c>TraspasoConsolidadoOrdering</c> y
    /// <c>MatriculaConsolidadoOrdering</c>.
    /// </summary>
    private static readonly string[] TodosLosGenerados =
    [
        "fur", "licencia_transito", "mandato", "tramite_virtual",
        "certificado_identidad", "certificado_identidad_vendedor",
        "certificado_rues", "certificado_rnmc",
        "compraventa", "escritura", "escritura_comprador", "impronta",
    ];

    private static string LoadDdl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        stream.Should().NotBeNull($"el DDL embebido {ResourceName} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void AC1_DaDeAltaLosTiposGeneradosQueFaltaban()
    {
        var sql = LoadDdl();

        var missing = NuevosGenerados
            .Where(code => !sql.Contains($"('{code}',", StringComparison.Ordinal))
            .ToList();

        missing.Should().BeEmpty("los tipos generados que no existían deben insertarse en el catálogo");
    }

    [Fact]
    public void AC1_ElAltaEsIdempotente()
    {
        LoadDdl().Should().Contain("ON CONFLICT (code) DO NOTHING");
    }

    [Fact]
    public void AC2_MarcaComoGeneradosTambienLosQueYaExistian()
    {
        var sql = LoadDdl();

        sql.Should().Contain("is_system_generated = true");

        var sinMarcar = TodosLosGenerados
            .Where(code => !sql.Contains($"('{code}',", StringComparison.Ordinal))
            .ToList();

        sinMarcar.Should().BeEmpty("todo documento que produce el sistema debe quedar marcado");
    }

    [Fact]
    public void AC2_CadaGeneradoTieneOrdenPorDefecto()
    {
        // El orden por defecto reproduce el del expediente de traspaso vigente, para que la
        // pantalla de prelación arranque mostrando el PDF tal como sale hoy.
        var sql = LoadDdl();

        sql.Should().Contain("generated_sort_order");
        foreach (var (code, orden) in TodosLosGenerados.Select((c, i) => (c, i + 1)))
        {
            sql.Should().MatchRegex(
                $@"\('{code}',\s*{orden}::smallint\)",
                $"'{code}' debe llevar su orden por defecto ({orden})");
        }
    }

    [Fact]
    public void AC3_AC4_NoTocaLaMatrizDeRequisitosNiLaObligatoriedad()
    {
        // La marca de "generado" es ADITIVA: el checklist del gestor sigue saliendo de
        // procedure_document_requirements, así que este DDL no puede escribir en esa tabla.
        // Si lo hiciera, compraventa (obligatoria en traspaso) o impronta cambiarían de estado.
        var sql = LoadDdl().ToUpperInvariant();

        sql.Should().NotContain("INTO TRAMITES.PROCEDURE_DOCUMENT_REQUIREMENTS");
        sql.Should().NotContain("UPDATE TRAMITES.PROCEDURE_DOCUMENT_REQUIREMENTS");
        sql.Should().NotContain("DELETE FROM TRAMITES.PROCEDURE_DOCUMENT_REQUIREMENTS");
        sql.Should().NotContain("IS_MANDATORY");
    }

    [Fact]
    public void AC5_ElDdlEsIdempotenteYReaplicable()
    {
        var sql = LoadDdl();

        sql.Should().Contain("ADD COLUMN IF NOT EXISTS is_system_generated");
        sql.Should().Contain("ADD COLUMN IF NOT EXISTS generated_sort_order");
    }
}
