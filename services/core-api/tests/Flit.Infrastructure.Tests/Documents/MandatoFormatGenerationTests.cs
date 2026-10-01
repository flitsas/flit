using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Documents;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #13172 (Feature #13118, Épica #13090) — generación del mandato con la plantilla publicada de un formato: variables
/// sustituidas, datos faltantes en blanco y DDL 125 que registra la versión usada en el adjunto.
/// </summary>
public sealed class MandatoFormatGenerationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class StaticProvider(MandateFormatTemplate template) : IMandateFormatTemplateProvider
    {
        public Task<MandateFormatTemplate> ResolveAsync(string formatCode, int? pinnedVersion, CancellationToken ct = default) =>
            Task.FromResult(template);
    }

    private static string Text(MandatoData data) => string.Concat(
        MandatoPdfGenerator.ApplyPlaceholdersSegmented(data.CustomTemplateBody!, data).SelectMany(l => l).Select(s => s.Texto));

    [Fact]
    public async Task AC1_ElPdfSigueLaPlantillaDeLaVersion_ConLasVariablesSustituidas()
    {
        var sample = MandatoPreviewSample.Build("bello", datosDeMuestra: true);
        var provider = new StaticProvider(new MandateFormatTemplate("bello", 4, "Mandato de {{mandante_nombre}} sobre {{placa}}"));

        var applied = await MandatoFormatTemplateApplier.ApplyAsync(sample, provider, null, Ct);

        applied.FormatVersion.Should().Be(4);
        applied.Data.CustomTemplateKind.Should().Be(MandatoCustomTemplateKindCodes.Editor);
        var text = Text(applied.Data);
        text.Should().Contain(MandatoPreviewSample.MuestraRazonSocial).And.Contain(MandatoPreviewSample.MuestraPlaca);
        text.Should().NotContain("{{");
        Encoding.ASCII.GetString(new MandatoPdfGenerator().GenerateMandato(applied.Data).Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task AC2_SinPersonalizacion_LosDatosNoCambian_ElGeneradorUsaSuRedaccion()
    {
        var sample = MandatoPreviewSample.Build("sabaneta", datosDeMuestra: true);
        var provider = new StaticProvider(new MandateFormatTemplate("sabaneta", 0, null));

        var applied = await MandatoFormatTemplateApplier.ApplyAsync(sample, provider, null, Ct);

        applied.Data.Should().Be(sample);
        applied.FormatVersion.Should().Be(0);
    }

    [Fact]
    public async Task AC5_ElNombreVisibleDelFormatoNoEntraAlDocumento_SalvoQueLaPlantillaLoCite()
    {
        var sample = MandatoPreviewSample.Build("municipio", datosDeMuestra: true);
        // El nombre visible vive solo en admin.mandate_format_settings: ni el proveedor ni los datos del mandato lo llevan.
        var provider = new StaticProvider(new MandateFormatTemplate("municipio", 1, "Contrato sobre {{placa}}"));

        var applied = await MandatoFormatTemplateApplier.ApplyAsync(sample, provider, null, Ct);

        Text(applied.Data).Should().NotContain("Envigado, Funza y Medellín");
        typeof(MandatoData).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("DisplayName"));
    }

    [Fact]
    public void AC7_VariableSinDato_SaleEnBlanco_YNoRompeLaGeneracion()
    {
        var sample = MandatoPreviewSample.Build("generico") with
        {
            Mandatario = null,
            InstitutionalMandataryName = null,
            CustomTemplateKind = MandatoCustomTemplateKindCodes.Editor,
            CustomTemplateBody = "Mandatario {{mandatario_nombre}} ({{mandatario_documento}}) UT {{mandatario_institucional}}",
        };

        var text = Text(sample);
        var doc = new MandatoPdfGenerator().GenerateMandato(sample);

        text.Should().Be("Mandatario ___ (___) UT ___");
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task AC8_PlantillaPropiaHeredadaDelOt_ManejaElPdf_SinConsultarElFormato()
    {
        var sample = MandatoPreviewSample.Build("bello", datosDeMuestra: true) with
        {
            CustomTemplateKind = MandatoCustomTemplateKindCodes.Editor,
            CustomTemplateBody = "Heredada {{placa}}",
        };
        var provider = new StaticProvider(new MandateFormatTemplate("bello", 9, "No debe usarse"));

        var applied = await MandatoFormatTemplateApplier.ApplyAsync(sample, provider, null, Ct);

        applied.Data.CustomTemplateBody.Should().Be("Heredada {{placa}}");
        applied.FormatVersion.Should().BeNull();
        applied.FormatCode.Should().BeNull();
    }

    // ---- DDL 125: registro de la versión usada en el adjunto (verificación estática, igual que el DDL 124) ----

    private static string Ddl()
    {
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(
            "Flit.Infrastructure.Persistence.Sql.Ddl.125-HU13172-attachment-mandate-format-version.sql");
        stream.Should().NotBeNull("el DDL 125 debe estar embebido");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return Regex.Replace(reader.ReadToEnd(), @"(?m)^\s*--.*$", string.Empty);
    }

    [Fact]
    public void Ddl125_AgregaDosColumnasNullables_IdempotentesYConBackfillDeLosEmitidos()
    {
        var sql = Ddl();

        sql.Should().Contain("ADD COLUMN IF NOT EXISTS mandate_format_code varchar(30)");
        sql.Should().Contain("mandate_format_version integer");
        sql.Should().NotContain("NOT NULL", "columnas nullables: no rompe filas existentes");
        sql.Should().Contain("tipo = 'mandato'").And.Contain("source = 'system'").And.Contain("mandate_format_version IS NULL");
        sql.Should().NotContain("DROP ", "migración aditiva");
    }

    [Fact]
    public void Migracion125_SeDescubreAlArrancar_ConAtributosYDesigner()
    {
        var migration = typeof(FlitDbContext).Assembly.GetTypes().Single(t => t.Name == "HU13172_AttachmentMandateFormatVersion");

        migration.GetCustomAttribute<DbContextAttribute>()!.ContextType.Should().Be<FlitDbContext>();
        migration.GetCustomAttribute<MigrationAttribute>()!.Id.Should().EndWith("_HU13172_AttachmentMandateFormatVersion");
        typeof(Migration).IsAssignableFrom(migration).Should().BeTrue();
    }

    [Fact]
    public void ElModeloEf_MapeaLasColumnasNuevasDelAdjunto()
    {
        using var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase("mandate-format-attachment-model").Options);

        var attachment = db.Model.FindEntityType(typeof(Flit.Tramites.Domain.Entities.ProcedureInstanceAttachment))!;

        attachment.FindProperty("MandateFormatCode")!.GetColumnName().Should().Be("mandate_format_code");
        attachment.FindProperty("MandateFormatVersion")!.GetColumnName().Should().Be("mandate_format_version");
        attachment.FindProperty("MandateFormatVersion")!.IsNullable.Should().BeTrue();
    }
}
