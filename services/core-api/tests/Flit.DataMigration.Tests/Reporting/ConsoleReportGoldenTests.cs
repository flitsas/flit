using System.Globalization;
using FluentAssertions;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Reporting;
using Xunit;

namespace Flit.DataMigration.Tests.Reporting;

/// <summary>
/// Congela la salida de consola del migrador, carácter por carácter.
/// <para>
/// El migrador se opera leyendo su reporte: los avisos, la cuarentena y la reconciliación son el
/// producto, no un adorno. Estos golden se generaron con el código ANTERIOR a la extracción del
/// motor a <c>Flit.DataMigration.Core</c>, así que son la prueba de que el refactor no cambió lo
/// que ve el operador.
/// </para>
/// <para>
/// <b>Si un cambio obliga a editar un <c>.golden.txt</c>, el cambio está mal.</b> Para regenerarlos
/// —solo si de verdad se decide cambiar el reporte— correr con <c>GOLDEN_UPDATE=1</c>.
/// </para>
/// </summary>
public sealed class ConsoleReportGoldenTests
{
    // Salto de línea fijo: el contenedor corre Linux y el desarrollo puede ser Windows.
    // Sin esto el golden fallaría por \r\n en una máquina y \n en otra.
    private static StringWriter NewWriter() => new(CultureInfo.InvariantCulture) { NewLine = "\n" };

    private static void AssertGolden(StringWriter writer, string nombre)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{nombre}.golden.txt");
        var producido = writer.ToString();

        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta, producido);
            // También al árbol de fuentes, porque BaseDirectory es bin/.
            var fuente = Path.Combine(SourceFixturesDirectory(), $"{nombre}.golden.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(fuente)!);
            File.WriteAllText(fuente, producido);
            return;
        }

        File.Exists(ruta).Should().BeTrue(
            $"el golden '{nombre}' debe existir; genéralo una sola vez con GOLDEN_UPDATE=1");
        producido.Should().Be(File.ReadAllText(ruta));
    }

    private static string SourceFixturesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Flit.DataMigration.Tests.csproj")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "Fixtures");
    }

    // ---------------------------------------------------------------- cabecera

    [Fact]
    public void Header_ConMasDeDiezIds_TruncaYNoFiltraLaContraseña()
    {
        var writer = NewWriter();

        ConsoleReport.Header(writer, ReportFixtures.OrigenAdjuntosConIdsDeSobra());

        AssertGolden(writer, "header-registration-attachments");
    }

    [Fact]
    public void Header_TraspasoEjecucionReal_MuestraLaTablaDeV1()
    {
        var writer = NewWriter();

        ConsoleReport.Header(writer, ReportFixtures.OrigenTraspasoReal());

        AssertGolden(writer, "header-transfer-real");
    }

    [Fact]
    public void Header_NuncaImprimeLaContraseñaDeLaCadenaDeConexion()
    {
        var writer = NewWriter();

        ConsoleReport.Header(writer, ReportFixtures.OrigenTraspasoReal());

        writer.ToString().Should().NotContain("NO-DEBE-APARECER");
    }

    // ------------------------------------------------ instancia 1: data plana

    [Fact]
    public void Data_ConLosCuatroEstadosYPrerrequisitos_ProduceLaSalidaEsperada()
    {
        var writer = NewWriter();

        ConsoleReport.Data(writer, ReportFixtures.DataReport(dryRun: true, ReportFixtures.Provisioned()));

        AssertGolden(writer, "report-data-dryrun");
    }

    [Fact]
    public void Data_SinPrerrequisitosYEjecucionReal_OmiteElBloqueDeCabecera()
    {
        var writer = NewWriter();

        ConsoleReport.Data(writer, ReportFixtures.DataReport(dryRun: false, []));

        AssertGolden(writer, "report-data-real");
    }

    // -------------------------------------------------- instancia 2: adjuntos

    [Fact]
    public void Attachments_ModoCopyConRedundantesYColumnasSinDeclarar_ProduceLaSalidaEsperada()
    {
        var writer = NewWriter();

        ConsoleReport.Attachments(writer, ReportFixtures.AttachmentsReport(
            CopyMode.Copy, dryRun: true, keepIdentityImages: false, ReportFixtures.UndeclaredColumns()));

        AssertGolden(writer, "report-attachments-copy");
    }

    [Fact]
    public void Attachments_ModoReference_OmiteElDestinoYLasColumnas()
    {
        var writer = NewWriter();

        ConsoleReport.Attachments(writer, ReportFixtures.AttachmentsReport(
            CopyMode.Reference, dryRun: false, keepIdentityImages: true, []));

        AssertGolden(writer, "report-attachments-reference");
    }

    // ------------------------------------------------ instancia 3: documentos

    [Fact]
    public void Documents_ConIssuesYTramiteVacio_ProduceLaSalidaEsperada()
    {
        var writer = NewWriter();

        ConsoleReport.Documents(writer, ReportFixtures.DocumentsReport(
            dryRun: true, ReportFixtures.DocumentResults()));

        AssertGolden(writer, "report-documents-dryrun");
    }

    [Fact]
    public void Documents_EjecucionRealSinVacios_OmiteElAvisoFinal()
    {
        var writer = NewWriter();
        var limpios = ReportFixtures.DocumentResults().Where(r => r.V1Id != 303).ToList();

        ConsoleReport.Documents(writer, ReportFixtures.DocumentsReport(dryRun: false, limpios));

        AssertGolden(writer, "report-documents-real");
    }

    // ------------------------------------------------------------- centinelas

    [Theory]
    [InlineData("TOKEN-CENTINELA-NO-DEBE-APARECER")]
    public void LosReportes_NuncaImprimenLosTokensDeLosEndpoints(string centinela)
    {
        var adjuntos = NewWriter();
        ConsoleReport.Attachments(adjuntos, ReportFixtures.AttachmentsReport(
            CopyMode.Copy, dryRun: true, keepIdentityImages: false, ReportFixtures.UndeclaredColumns()));

        var documentos = NewWriter();
        ConsoleReport.Documents(documentos, ReportFixtures.DocumentsReport(
            dryRun: true, ReportFixtures.DocumentResults()));

        adjuntos.ToString().Should().NotContain(centinela);
        documentos.ToString().Should().NotContain(centinela);
    }
}
