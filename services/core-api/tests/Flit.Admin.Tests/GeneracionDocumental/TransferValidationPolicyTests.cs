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
        // HU #12208: el escenario viaja declarado. Las VB propias de un escenario solo se evalúan en
        // su escenario —exigirle a un payload de B el título jurídico del art. 5.3.2.1 sería inventar
        // un requisito—, así que sin escenario único no habría VB-A-* que inspeccionar aquí.
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            escenarios: [TransferScenario.TraspasoOrdinario],
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

        // Los cuatro bloqueantes del escenario A conviven en una sola respuesta: corregir de a un
        // error por viaje es inaceptable en un formulario de 35 campos.
        Codigos(outcome.Blocking).Should().Contain(
        [
            TransferValidationCodes.PlacaFormato,
            TransferValidationCodes.PartesDistintas,
            TransferValidationCodes.TituloJuridicoDeclarado,
            TransferValidationCodes.GravamenConLevantamiento,
        ]);
    }

    // ── VB-07 — régimen aplicable (§4.0). Este bloque REEMPLAZA a propósito al de HU #12207, que
    //    fijaba el comportamiento anterior («todavía no bloquea»). Ahora sí bloquea. ─────────────

    /// <summary>
    /// Las <b>once</b> condiciones de los arts. 5.3.2.3 a 5.3.2.13, una por una. No es un test
    /// parametrizado de muestra: el criterio de aceptación pide verificarlas todas, porque cada una
    /// tiene su artículo y su juego de soportes, y basta que una quede fuera de la matriz para que
    /// el módulo emita un documento normativamente insuficiente con la marca de FLIT.
    /// </summary>
    [Theory]
    [InlineData(TransferSpecialRegime.ServicioPublicoPasajerosOMixto, "art. 5.3.2.3")]
    [InlineData(TransferSpecialRegime.AseguradoraPorHurto, "art. 5.3.2.4")]
    [InlineData(TransferSpecialRegime.AseguradoraPorPerdidaParcial, "art. 5.3.2.5")]
    [InlineData(TransferSpecialRegime.VehiculoBlindado, "art. 5.3.2.6")]
    [InlineData(TransferSpecialRegime.DecisionJudicialOAdministrativa, "art. 5.3.2.7")]
    [InlineData(TransferSpecialRegime.Sucesion, "art. 5.3.2.8")]
    [InlineData(TransferSpecialRegime.ImportacionTemporalSustitucionImportador, "art. 5.3.2.9")]
    [InlineData(TransferSpecialRegime.DecomisoDianOAdjudicacionNacion, "art. 5.3.2.10")]
    [InlineData(TransferSpecialRegime.ComisoFiscalia, "art. 5.3.2.11")]
    [InlineData(TransferSpecialRegime.DeclaratoriaDeAbandono, "art. 5.3.2.12")]
    [InlineData(TransferSpecialRegime.CargaPbvSuperior10500Kg, "art. 5.3.2.13")]
    public void CadaCondicionEspecialDeclarada_BloqueaConVb07YCitaSuArticulo(
        string condicion,
        string articulo)
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            regimen: TransferTestData.Regimen(ningunaAplica: false, condiciones: [condicion])));

        var issue = outcome.Blocking.Single(i => i.Code == TransferValidationCodes.RegimenAplicable);

        issue.Field.Should().Be("regimenAplicable");
        issue.Message.Should().Contain(articulo);
        issue.Message.Should().Contain("no produce ni acredita");
    }

    /// <summary>
    /// Declarar «ninguna aplica» Y a la vez una condición es una contradicción, y se resuelve del
    /// lado seguro: manda la condición declarada.
    /// </summary>
    [Fact]
    public void NingunaAplicaJuntoAUnaCondicion_BloqueaIgual()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            regimen: TransferTestData.Regimen(
                ningunaAplica: true, condiciones: [TransferSpecialRegime.Sucesion])));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.RegimenAplicable);
    }

    /// <summary>
    /// El silencio no es un «no aplica»: el gate es previo a elegir escenario (§4.0) y el checklist
    /// §13.1 exige que la declaración «fue respondida».
    /// </summary>
    [Fact]
    public void SinDeclaracionDeRegimen_BloqueaConVb07()
    {
        var comando = TransferTestData.Comando() with { RegimenAplicable = null };

        var outcome = TransferValidationPolicy.Evaluate(comando);

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.RegimenAplicable);
    }

    [Fact]
    public void DeclaracionSinResponder_BloqueaConVb07()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            regimen: TransferTestData.Regimen(ningunaAplica: null)));

        Codigos(outcome.Blocking).Should().Contain(TransferValidationCodes.RegimenAplicable);
    }

    [Fact]
    public void DeclararQueNingunaAplica_NoBloquea()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            regimen: TransferTestData.Regimen(ningunaAplica: true)));

        Codigos(outcome.Blocking).Should().NotContain(TransferValidationCodes.RegimenAplicable);
    }

    /// <summary>
    /// El art. 5.3.2.14 (expedición de la nueva licencia de tránsito) <b>no</b> es una condición
    /// especial: es el paso final común a todo traspaso. No está en la matriz de bloqueo y por eso
    /// un código que lo nombre no impide generar.
    /// </summary>
    [Fact]
    public void ElArticulo5_3_2_14_NoEsCondicionEspecialYNoBloquea()
    {
        TransferSpecialRegime.IsKnown("ART_5_3_2_14").Should().BeFalse();

        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.Comando(
            regimen: TransferTestData.Regimen(
                ningunaAplica: true, condiciones: ["ART_5_3_2_14"])));

        Codigos(outcome.Blocking).Should().NotContain(TransferValidationCodes.RegimenAplicable);
        outcome.IsBlocked.Should().BeFalse();
    }

    // ── VB-B-01..05 — escenario B (art. 5.3.2.2) ─────────────────────────────────────

    [Fact]
    public void PayloadValidoDeEscenarioB_NoBloquea()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB());

        outcome.IsBlocked.Should().BeFalse();
        outcome.Blocking.Should().BeEmpty();
    }

    [Fact]
    public void EscenarioBSinDeclararEntidadFinanciera_BloqueaConVbB01()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB(
            leasing: TransferTestData.Leasing(esEntidadFinanciera: false)));

        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.TransferenteEsEntidadFinanciera)
            .Field.Should().Be("leasing.transferenteEsEntidadFinanciera");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EscenarioBSinContratoDeLeasing_BloqueaConVbB02(string? contrato)
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB(
            leasing: TransferTestData.Leasing(contrato: contrato)));

        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.ContratoDeLeasingDeclarado)
            .Field.Should().Be("leasing.noContratoLeasing");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PACTADA")]
    [InlineData("OPCION_DE_COMPRA")]
    public void EscenarioBConOpcionDeCompraFueraDelCatalogo_BloqueaConVbB03(string? opcion)
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB(
            leasing: TransferTestData.Leasing(tipoOpcion: opcion)));

        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.TipoOpcionDeCompraDeclarado)
            .Field.Should().Be("leasing.tipoOpcionCompra");
    }

    [Theory]
    [InlineData(TransferPurchaseOption.Ejercida)]
    [InlineData(TransferPurchaseOption.Automatica)]
    [InlineData(TransferPurchaseOption.TerminacionContrato)]
    public void LasTresCausalesDelCatalogo_NoBloquean(string opcion)
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB(
            leasing: TransferTestData.Leasing(tipoOpcion: opcion)));

        outcome.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void EscenarioBSinNombreDelLocatario_BloqueaConVbB04()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB(
            leasing: TransferTestData.Leasing(locatarioNombre: null)));

        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.LocatarioDestinatarioDeclarado)
            .Field.Should().Be("leasing.locatarioNombre");
    }

    [Fact]
    public void EscenarioBSinDocumentoDelLocatario_BloqueaConVbB04()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB(
            leasing: TransferTestData.Leasing(locatarioNoDoc: null)));

        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.LocatarioDestinatarioDeclarado)
            .Field.Should().Be("leasing.locatarioNoDoc");
    }

    /// <summary>§10 regla #1 — el locatario recibe el dominio; no puede ser el transferente.</summary>
    [Fact]
    public void EscenarioBConElLocatarioComoTransferente_BloqueaConVbB04()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB(
            leasing: TransferTestData.Leasing(locatarioNoDoc: "900.123.456")));

        Codigos(outcome.Blocking).Should()
            .Contain(TransferValidationCodes.LocatarioDestinatarioDeclarado);
    }

    /// <summary>
    /// VB-B-05 — el formulario del escenario B no tiene campo de precio y el backend no lo acepta
    /// aunque el cliente lo mande: un precio descartado en silencio dejaría al usuario creyendo que
    /// quedó en el documento.
    /// </summary>
    [Theory]
    [InlineData("negocio.precioLetras")]
    [InlineData("negocio.precioNumeros")]
    [InlineData("negocio.contraprestacionDescripcion")]
    public void EscenarioBConPrecioEnElPayload_BloqueaConVbB05(string campo)
    {
        var negocio = TransferTestData.NegocioSinPrecio();
        negocio = campo switch
        {
            "negocio.precioLetras" => negocio with { PrecioLetras = "VEINTE MILLONES DE PESOS" },
            "negocio.precioNumeros" => negocio with { PrecioNumeros = "20.000.000" },
            _ => negocio with { ContraprestacionDescripcion = "Un inmueble" },
        };

        var outcome = TransferValidationPolicy.Evaluate(
            TransferTestData.ComandoEscenarioB(negocio: negocio));

        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.SinPrecioEnEscenarioB)
            .Field.Should().Be(campo);
    }

    /// <summary>
    /// El escenario B no exige título jurídico ni catálogos fiscales: son requisitos del negocio del
    /// art. 5.3.2.1 y el acto unilateral no los tiene. Exigírselos sería inventar una regla.
    /// </summary>
    [Fact]
    public void EscenarioBNoExigeTituloJuridicoNiPrecioDeCompraventa()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB());

        Codigos(outcome.Blocking).Should().NotContain(TransferValidationCodes.TituloJuridicoDeclarado);
        Codigos(outcome.Blocking).Should().NotContain(TransferValidationCodes.PrecioCompraventa);
    }

    /// <summary>
    /// VB-B-06 — el art. 5.3.2.2 NO exime lo fiscal (hallazgo del dictamen, §6.3). Y no aparecen los
    /// avisos de RTM ni de QR/improntas, que sí están exentos.
    /// </summary>
    [Fact]
    public void EscenarioB_EmiteElAvisoFiscalYNoLosDeRtmNiImprontas()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioB());

        var aviso = outcome.Advisories.Single(
            i => i.Code == TransferValidationCodes.CargasFiscalesEnLeasing);

        aviso.Message.Should().Contain("no exime el numeral 5");
        Codigos(outcome.Advisories).Should().NotContain(TransferValidationCodes.RtmVigenteC);
        Codigos(outcome.Advisories).Should().NotContain(TransferValidationCodes.QrGuarismosImprontas);
        Codigos(outcome.Advisories).Should().NotContain(TransferValidationCodes.AdquirenteEnRunt);
    }

    // ── Escenario C — sin exenciones (art. 5.3.2.1 íntegro) ────────────────────────────

    [Fact]
    public void PayloadValidoDeEscenarioC_NoBloquea()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioC());

        outcome.IsBlocked.Should().BeFalse();
    }

    /// <summary>VB-C-01 — si el adquirente es el locatario histórico, la operación es la del B.</summary>
    [Fact]
    public void EscenarioCConElLocatarioHistoricoComoAdquirente_BloqueaConVbC01()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioC(
            adquirente: TransferTestData.Adquirente("901555444"),
            leasing: TransferTestData.Leasing()));

        outcome.Blocking.Single(i => i.Code == TransferValidationCodes.AdquirenteNoEsElLocatario)
            .Field.Should().Be("adquirente.numeroDoc");
    }

    /// <summary>
    /// El escenario C no hereda exenciones: emite los avisos de RTM y de QR/improntas, que el
    /// escenario B no tiene.
    /// </summary>
    [Fact]
    public void EscenarioC_EmiteLosAvisosPlenosDelArticulo5321()
    {
        var outcome = TransferValidationPolicy.Evaluate(TransferTestData.ComandoEscenarioC());

        Codigos(outcome.Advisories).Should().Contain(
        [
            TransferValidationCodes.AdquirenteEnRuntC,
            TransferValidationCodes.SoatVigenteC,
            TransferValidationCodes.RtmVigenteC,
            TransferValidationCodes.SinMedidasJudicialesC,
            TransferValidationCodes.QrGuarismosImprontas,
            TransferValidationCodes.RetencionEnLaFuenteC,
            TransferValidationCodes.DerechosDeTramiteC,
            TransferValidationCodes.ImpuestoVehiculoC,
        ]);

        Codigos(outcome.Advisories).Should().NotContain(TransferValidationCodes.CargasFiscalesEnLeasing);
    }

    /// <summary>
    /// Ningún mensaje de las VB nuevas refleja el valor capturado (CF-09), tampoco los del régimen ni
    /// los del leasing.
    /// </summary>
    [Fact]
    public void LosMensajesDeLasVbNuevas_NoReflejanElValorCapturado()
    {
        var comando = TransferTestData.ComandoEscenarioB(
            leasing: TransferTestData.Leasing(
                esEntidadFinanciera: false,
                contrato: null,
                tipoOpcion: "PACTADA-SECRETA",
                locatarioNombre: null,
                locatarioNoDoc: "9995551111"),
            negocio: TransferTestData.NegocioSinPrecio() with { PrecioNumeros = "77.777.777" });

        var mensajes = TransferValidationPolicy.Evaluate(comando).Blocking
            .Select(i => i.Message)
            .ToList();

        foreach (var mensaje in mensajes)
        {
            mensaje.Should().NotContain("PACTADA-SECRETA");
            mensaje.Should().NotContain("9995551111");
            mensaje.Should().NotContain("77.777.777");
        }
    }
}
