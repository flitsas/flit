using Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;
using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12207 — validaciones del anexo normativo §6 para el escenario A y las comunes a los tres
/// (<c>docs/plantilla-transferencia-dominio.md</c>).
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando());
/// outcome.IsBlocked; // false
/// </code>
/// </summary>
public sealed class TransferValidationPolicyTests
{
    private static IEnumerable<string> Codigos(IReadOnlyList<TransferValidationIssue> issues) =>
        issues.Select(i => i.Code);

    [Fact]
    public void PayloadValidoDeEscenarioA_NoBloquea()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando());

        outcome.IsBlocked.Should().BeFalse();
        outcome.Blocking.Should().BeEmpty();
    }

    // ── VB-05 — el escenario es obligatorio y único ────────────────────────────────────────────

    [Fact]
    public void SinEscenario_BloqueaConVb05()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(escenarios: []));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.EscenarioUnico);
        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.EscenarioUnico)
            .Field.Should().Be("escenario");
    }

    [Fact]
    public void ConDosEscenarios_BloqueaConVb05()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            escenarios: [TransferScenario.TraspasoOrdinario, TransferScenario.UnilateralLeasing]));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.EscenarioUnico);
    }

    [Fact]
    public void ConEscenarioDesconocido_BloqueaConVb05()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(escenarios: ["Z"]));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.EscenarioUnico);
    }

    // ── VB-02 — formato de placa ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("AB1")]
    [InlineData("ABCDEFGH")]
    [InlineData("ABC-12*")]
    public void PlacaConFormatoInvalido_BloqueaConVb02(string placa)
    {
        var outcome = TransferValidationPolicy.Evaluate(
            TransferTestData.Comando(vehiculo: TransferTestData.Vehiculo(placa)));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.PlacaFormato);
        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.PlacaFormato)
            .Field.Should().Be("vehiculo.placa");
    }

    [Theory]
    [InlineData("ABC123")]
    [InlineData("abc-123")]   // se normaliza: guion y minúsculas no son un error de formato
    [InlineData("ABC12D")]    // motocicleta
    [InlineData("R12345")]    // remolque
    public void PlacaValida_NoBloquea(string placa)
    {
        var outcome = TransferValidationPolicy.Evaluate(
            TransferTestData.Comando(vehiculo: TransferTestData.Vehiculo(placa)));

        Codigos(outcome.Blocking).Should().NotContain(TransferValidationCodes.PlacaFormato);
    }

    // ── VB-06 — transferente y adquirente distintos ────────────────────────────────────────────

    [Fact]
    public void MismoDocumentoEnAmbasPartes_BloqueaConVb06()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            transferente: TransferTestData.Transferente("900123456"),
            adquirente: TransferTestData.Adquirente("900123456")));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.PartesDistintas);
    }

    /// <summary>
    /// La auto-transferencia no se evita tecleando un punto: los documentos se comparan
    /// normalizados.
    /// </summary>
    [Fact]
    public void MismoDocumentoConSeparadores_TambienBloqueaConVb06()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            transferente: TransferTestData.Transferente("900.123.456"),
            adquirente: TransferTestData.Adquirente("900123456")));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.PartesDistintas);
    }

    // ── VB-A-07 — título jurídico declarado ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CONTRATO_RARO")]
    public void TituloJuridicoVacioOAmbiguo_BloqueaConVbA07(string? titulo)
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            negocio: TransferTestData.Negocio(titulo!)));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.TituloJuridicoDeclarado);
        outcome.Blocking.First(i => i.Code == TransferValidationCodes.TituloJuridicoDeclarado)
            .Field.Should().Be("negocio.tituloJuridico");
    }

    [Fact]
    public void TituloOtroSinDescripcion_BloqueaConVbA07()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            negocio: TransferTestData.Negocio(TransferJuridicalTitle.Otro, null, null)));

        outcome.Blocking.Should().Contain(i =>
            i.Code == TransferValidationCodes.TituloJuridicoDeclarado
            && i.Field == "negocio.descripcionTitulo");
    }

    // ── VB-A-06 — precio de la compraventa ─────────────────────────────────────────────────────

    [Fact]
    public void CompraventaSinPrecioEnLetras_BloqueaConVbA06()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            negocio: TransferTestData.Negocio(precioLetras: null)));

        outcome.Blocking.Should().Contain(i =>
            i.Code == TransferValidationCodes.PrecioCompraventa && i.Field == "negocio.precioLetras");
    }

    [Fact]
    public void CompraventaSinPrecioEnNumeros_BloqueaConVbA06()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            negocio: TransferTestData.Negocio(precioNumeros: null)));

        outcome.Blocking.Should().Contain(i =>
            i.Code == TransferValidationCodes.PrecioCompraventa && i.Field == "negocio.precioNumeros");
    }

    /// <summary>
    /// El precio NO es requisito universal (anexo §5.4 y §4.1): una donación sin precio es válida.
    /// Exigirlo siempre convertiría en inválidos títulos que el art. 5.3.2.1 num. 1.º admite.
    /// </summary>
    [Theory]
    [InlineData(TransferJuridicalTitle.Donacion)]
    [InlineData(TransferJuridicalTitle.DacionEnPago)]
    [InlineData(TransferJuridicalTitle.Permuta)]
    public void TituloNoOnerosoSinPrecio_NoBloquea(string titulo)
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            negocio: TransferTestData.Negocio(titulo, null, null)));

        Codigos(outcome.Blocking).Should().NotContain(TransferValidationCodes.PrecioCompraventa);
    }

    // ── VB-A-04 — gravamen activo ──────────────────────────────────────────────────────────────

    [Fact]
    public void GravamenActivoSinLevantamiento_BloqueaConVbA04()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            gravamen: new TransferEncumbranceInput(GravamenActivo: true)));

        outcome.Blocking.Should().Contain(i =>
            i.Code == TransferValidationCodes.GravamenConLevantamiento
            && i.Field == "gravamen.tieneLevantamientoOAutorizacion");
    }

    [Fact]
    public void GravamenActivoConAutorizacion_NoBloquea()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            gravamen: new TransferEncumbranceInput(true, TieneLevantamientoOAutorizacion: true)));

        outcome.IsBlocked.Should().BeFalse();
    }

    // ── VA advisory — nunca bloquean ───────────────────────────────────────────────────────────

    /// <summary>
    /// CF-09 — VB-01, VB-03, VB-04, VB-A-01, VB-A-02, VB-A-03, VB-A-05 y las nuevas VB-A-08..10
    /// son AVISO. Aunque ninguna pueda confirmarse, la generación procede.
    /// </summary>
    [Fact]
    public void LasPrevalidacionesAdvisorySeEmitenYNoBloquean()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando());

        outcome.IsBlocked.Should().BeFalse();

        Codigos(outcome.Advisories).Should().Contain(
        [
            TransferValidationCodes.MatriculaVigente,
            TransferValidationCodes.TransferenteEnRunt,
            TransferValidationCodes.TransferentePjEnRues,
            TransferValidationCodes.AdquirenteEnRunt,
            TransferValidationCodes.SinMedidasJudiciales,
            TransferValidationCodes.SoatVigente,
            TransferValidationCodes.RetencionEnLaFuente,
            TransferValidationCodes.DerechosDeTramite,
            TransferValidationCodes.ImpuestoVehiculo,
        ]);

        // Ningún código advisory puede aparecer también como bloqueante.
        Codigos(outcome.Blocking).Should().BeEmpty();
    }

    /// <summary>El adquirente es persona natural en el caso base: VB-A-02 solo aplica a PJ.</summary>
    [Fact]
    public void AdquirentePersonaNatural_NoEmiteElAvisoDeRues()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando());

        Codigos(outcome.Advisories).Should().NotContain(TransferValidationCodes.AdquirentePjEnRues);
    }

    /// <summary>
    /// VB-A-10 no aplica a remolques ni semirremolques: están exentos del impuesto sobre vehículos
    /// (Ley 488/1998, art. 5.3.2.1 num. 5.º inciso final).
    /// </summary>
    [Fact]
    public void Remolque_NoEmiteElAvisoDeImpuestoVehicular()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            vehiculo: TransferTestData.Vehiculo(clase: "SEMIRREMOLQUE")));

        Codigos(outcome.Advisories).Should().NotContain(TransferValidationCodes.ImpuestoVehiculo);
    }

    /// <summary>
    /// La exención de SOAT en remolques NO se atribuye a la Ley 488/1998: ese artículo exime
    /// impuesto, no SOAT (anexo §4.1 y §6.2, corrección del dictamen).
    /// </summary>
    [Fact]
    public void AvisoDeSoatEnRemolque_NoInvocaLaLey488()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            vehiculo: TransferTestData.Vehiculo(clase: "REMOLQUE")));

        var soat = outcome.Advisories.Single(i => i.Code == TransferValidationCodes.SoatVigente);

        soat.Message.Should().NotContain("488");
        soat.Message.Should().Contain("no consagra exención textual");
    }

    // ── PII — el mensaje nunca refleja lo capturado ────────────────────────────────────────────

    /// <summary>
    /// CF-09 — todo error trae código, campo y mensaje, y ninguno repite el valor tecleado: un 422
    /// termina en logs de acceso, trazas y capturas de pantalla de soporte.
    /// </summary>
    [Fact]
    public void NingunMensajeDeErrorRefleljaElValorCapturado()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            escenarios: [],
            vehiculo: TransferTestData.Vehiculo("PL@CA!"),
            transferente: TransferTestData.Transferente("80000001"),
            adquirente: TransferTestData.Adquirente("80000001"),
            negocio: TransferTestData.Negocio("TITULO_INVENTADO"),
            gravamen: new TransferEncumbranceInput(GravamenActivo: true)));

        outcome.Blocking.Should().NotBeEmpty();

        foreach (var issue in outcome.Blocking)
        {
            issue.Code.Should().NotBeNullOrWhiteSpace();
            issue.Field.Should().NotBeNullOrWhiteSpace();
            issue.Message.Should().NotBeNullOrWhiteSpace();

            issue.Message.Should().NotContain("PL@CA!");
            issue.Message.Should().NotContain("80000001");
            issue.Message.Should().NotContain("TITULO_INVENTADO");
            issue.Message.Should().NotContain("EMPRESA TRANSFERENTE SAS");
            issue.Message.Should().NotContain("PERSONA ADQUIRENTE DE PRUEBA");
        }

        // Los cinco bloqueantes del escenario A conviven en una sola respuesta.
        Codigos(outcome.Blocking).Should().Contain(
        [
            TransferValidationCodes.EscenarioUnico,
            TransferValidationCodes.PlacaFormato,
            TransferValidationCodes.PartesDistintas,
            TransferValidationCodes.TituloJuridicoDeclarado,
            TransferValidationCodes.GravamenConLevantamiento,
        ]);
    }

    /// <summary>
    /// VB-07 (régimen aplicable, arts. 5.3.2.3 a 5.3.2.13) es alcance de HU-06: esta HU no lo
    /// evalúa. El test lo deja escrito para que el día que HU-06 lo implemente, este caso cambie
    /// deliberadamente y no por accidente.
    /// </summary>
    [Fact]
    public void RegimenEspecialDeclarado_TodaviaNoBloquea()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            regimen: new RegimenDeclarationInput(
                NingunaAplica: false,
                CondicionesDeclaradas: ["art. 5.3.2.6"],
                DeclaredAt: DateTimeOffset.Parse("2026-09-09T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture))));

        Codigos(outcome.Blocking).Should().NotContain(TransferValidationCodes.RegimenAplicable);
    }
}
