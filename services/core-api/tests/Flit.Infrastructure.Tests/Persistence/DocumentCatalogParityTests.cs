using System.Reflection;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #10520 (RNF02) — verificación de paridad documental: el seed del catálogo
/// (23-HU10520-document-types-seed.sql) debe contener un tipo de documento por cada
/// documento de la hoja CatalogoDocumentos (Manual FLIT v8.0). Si se agrega un documento
/// a la paridad y se olvida el seed, esta prueba falla.
/// </summary>
public sealed class DocumentCatalogParityTests
{
    private const string ResourceName =
        "Flit.Infrastructure.Persistence.Sql.Ddl.23-HU10520-document-types-seed.sql";

    /// <summary>Códigos esperados = documentos de CatalogoDocumentos (paridad FLIT 1.0).</summary>
    private static readonly string[] ExpectedCatalog =
    [
        "portada", "factura", "certificado_ambiental", "aduana", "declaracion_aduana",
        "impronta", "paz_salvo", "paz_salvo_prenda", "inscripcion_prenda",
        "doc_identidad_propietario", "doc_identidad_comprador", "doc_identidad_vendedor",
        "cedulas", "poder_tramitador", "anexos_generales", "factura_carroceria", "compraventa",
        "fur", "mandato", "tramite_virtual", "tablas_certificadoras", "certificado_vigencia_soat_rtm",
        "soat", "rtm", "soat_manual", "paz_salvo_rnmc", "rues", "contrato_leasing",
        "declaracion_arrendadora", "transferencia_dominio", "impronta_validada", "firma",
        "escritura_publica", "camara_comercio", "certificado_superfinanciera", "liquidacion_impuesto",
        "licencia_transito", "tarjeta_propiedad", "reporte_rtm_soat", "consolidado", "acta_remate",
        "oficio_judicial", "comprobante_derechos", "acta_entrega", "cert_tradicion", "otro",
    ];

    private static string LoadSeedSql()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        stream.Should().NotBeNull($"el seed embebido {ResourceName} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Seed_ContainsEveryCatalogoDocumentosCode()
    {
        var sql = LoadSeedSql();

        var missing = ExpectedCatalog
            .Where(code => !sql.Contains($"('{code}',", StringComparison.Ordinal))
            .ToList();

        missing.Should().BeEmpty(
            "todo documento de CatalogoDocumentos debe tener su fila en el seed del catálogo");
    }

    [Fact]
    public void EveryConditionalObligatoriedadRuleDocumentExistsInCatalog()
    {
        // RNF02 (segunda mitad) — paridad: cada documento con REGLA DE OBLIGATORIEDAD (RF30, reglas
        // condicionales por tipología) debe tener también su tipo en el catálogo (RF32). Cierra la
        // consistencia reglas↔catálogo: ninguna regla puede apuntar a un documento no cataloged.
        var sql = LoadSeedSql();

        var docTiposConRegla = new[]
            {
                TramiteTipologiaCatalog.CodigoMatriculaInicial,
                TramiteTipologiaCatalog.CodigoTraspasoStandard,
            }
            .SelectMany(ConditionalDocumentRules.For)
            .Select(r => r.Item.DocTipo)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        docTiposConRegla.Should().NotBeEmpty(
            "las tipologías de paridad deben declarar reglas de obligatoriedad condicional (RF30)");

        var missing = docTiposConRegla
            .Where(code => !sql.Contains($"('{code}',", StringComparison.Ordinal))
            .ToList();

        missing.Should().BeEmpty(
            "cada documento con regla de obligatoriedad (RF30) debe existir en el catálogo (RNF02)");
    }

    [Fact]
    public void Seed_IsIdempotent_UsesOnConflictDoNothing()
    {
        var sql = LoadSeedSql();
        sql.Should().Contain("ON CONFLICT (code) DO NOTHING");
    }

    [Fact]
    public void Seed_KeepsCurrentGlobalLimits_NoRegression()
    {
        var sql = LoadSeedSql();
        // No-regresión: todos los tipos se siembran con los límites globales vigentes (20 MB).
        sql.Should().Contain("20971520");
        sql.Should().Contain("\"application/pdf\",\"image/jpeg\",\"image/png\",\"image/webp\"");
    }
}
