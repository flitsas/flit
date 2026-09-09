using System.Text.Json;
using Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12207 — emisión del Documento de Transferencia de Dominio, escenario A.
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var result = await new GenerateTransferenciaHandler(repo, generador, storage)
///     .HandleAsync(TransferTestData.Comando());
/// </code>
/// </summary>
public sealed class GenerateTransferenciaHandlerTests
{
    private readonly FakeStandaloneDocumentRepository _repository = new();
    private readonly FakeStandaloneTransferGenerator _generator = new();
    private readonly FakeStandaloneDocumentStorage _storage = new();

    private GenerateTransferenciaHandler Handler() => new(_repository, _generator, _storage);

    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task PayloadValido_GeneraYPersisteConEscenarioA()
    {
        var result = await Handler().HandleAsync(TransferTestData.Comando(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateTransferenciaOutcome.Generated);
        result.Status.Should().Be(StandaloneDocumentStatus.Generated);
        result.Errors.Should().BeEmpty();

        var fila = _repository.Rows.Single();
        fila.DocumentType.Should().Be(StandaloneDocumentType.TransferenciaDominioGenerada);
        fila.Scenario.Should().Be(TransferScenario.TraspasoOrdinario);
        fila.TenantId.Should().Be(Tenant);
        fila.Status.Should().Be(StandaloneDocumentStatus.Generated);

        _generator.Calls.Should().Be(1);
        _storage.Saved.Should().ContainSingle();
        _storage.Saved[0].Tipo.Should().Be("generacion_documental");
    }

    /// <summary>Las prevalidaciones advisory viajan en la respuesta 200 (CF-09).</summary>
    [Fact]
    public async Task LaRespuestaExitosaLlevaLasPrevalidacionesAdvisory()
    {
        var result = await Handler().HandleAsync(TransferTestData.Comando(), TestContext.Current.CancellationToken);

        result.Advisories.Select(a => a.Code).Should().Contain(
        [
            TransferValidationCodes.MatriculaVigente,
            TransferValidationCodes.RetencionEnLaFuente,
            TransferValidationCodes.DerechosDeTramite,
            TransferValidationCodes.ImpuestoVehiculo,
        ]);
    }

    /// <summary>
    /// VB-05 — sin escenario no puede escribirse la fila: el CHECK
    /// <c>ck_standalone_documents_scenario_por_tipo</c> exige escenario en este tipo de documento, y
    /// el handler lo rechaza antes de intentarlo.
    /// </summary>
    [Fact]
    public async Task SinEscenario_RechazaConVb05YNoEscribeFila()
    {
        var result = await Handler().HandleAsync(
            TransferTestData.Comando(escenarios: []), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateTransferenciaOutcome.ValidationFailed);
        result.Errors.Select(e => e.Code).Should().Contain(TransferValidationCodes.EscenarioUnico);

        _repository.Rows.Should().BeEmpty();
        _storage.Saved.Should().BeEmpty();
        _generator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ValidacionBloqueante_NoRenderizaNiEscribeArchivo()
    {
        var result = await Handler().HandleAsync(
            TransferTestData.Comando(vehiculo: TransferTestData.Vehiculo("XX")),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateTransferenciaOutcome.ValidationFailed);
        result.Errors.Select(e => e.Code).Should().Contain(TransferValidationCodes.PlacaFormato);
        _generator.Calls.Should().Be(0);
        _storage.Saved.Should().BeEmpty();
    }

    /// <summary>Escenarios B y C: reconocidos por VB-05, pero su generador llega en HU-06.</summary>
    [Theory]
    [InlineData(TransferScenario.UnilateralLeasing)]
    [InlineData(TransferScenario.FinancieraATercero)]
    public async Task EscenariosBYC_SeRechazanSinPersistirNada(string escenario)
    {
        var result = await Handler().HandleAsync(
            TransferTestData.Comando(escenarios: [escenario]), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateTransferenciaOutcome.ScenarioNotImplemented);
        _repository.Rows.Should().BeEmpty();
        _generator.Calls.Should().Be(0);
    }

    /// <summary>
    /// CF-26 — <c>document_snapshot</c> guarda TODO lo que se usó para renderizar: partes, vehículo,
    /// negocio, documentos de identidad y domicilios.
    /// </summary>
    [Fact]
    public async Task DocumentSnapshot_ContieneTodoLoUsadoParaRenderizar()
    {
        await Handler().HandleAsync(TransferTestData.Comando(), TestContext.Current.CancellationToken);

        var snapshot = JsonDocument.Parse(_repository.DocumentSnapshots.Single().Snapshot).RootElement;

        snapshot.GetProperty("scenario").GetString().Should().Be(TransferScenario.TraspasoOrdinario);
        snapshot.GetProperty("signatureMode").GetString().Should().Be(TransferSignatureMode.Manuscrita);
        snapshot.GetProperty("ciudadFirma").GetString().Should().Be("CIUDAD DE PRUEBA");

        var vehiculo = snapshot.GetProperty("vehiculo");
        vehiculo.GetProperty("placa").GetString().Should().Be("ABC123");
        vehiculo.GetProperty("noChasis").GetString().Should().Be("CHA0000001");
        vehiculo.GetProperty("organismoTransito").GetString().Should().NotBeNullOrEmpty();

        var partes = snapshot.GetProperty("partes").EnumerateArray().ToList();
        partes.Should().HaveCount(2);
        partes[0].GetProperty("rol").GetString().Should().Be(TransferPartyRole.Transferente);
        partes[0].GetProperty("nombreRazonSocial").GetString().Should().Be("EMPRESA TRANSFERENTE SAS");
        partes[0].GetProperty("domicilio").GetString().Should().Be("CIUDAD DE PRUEBA");
        partes[0].GetProperty("digitoVerificacion").GetString().Should().Be("8");
        partes[1].GetProperty("rol").GetString().Should().Be(TransferPartyRole.Adquirente);
        partes[1].GetProperty("numeroDoc").GetString().Should().Be("10000002");

        var negocio = snapshot.GetProperty("negocio");
        negocio.GetProperty("tituloJuridico").GetString().Should().Be("COMPRAVENTA");
        negocio.GetProperty("precioNumeros").GetString().Should().Be("20.000.000");
        negocio.GetProperty("asumeRetencionFuente").GetString().Should().Be("TRANSFERENTE");
        negocio.GetProperty("asumeDerechosTramite").GetString().Should().Be("COMPARTIDOS");
        negocio.GetProperty("asumeImpuestoVehiculo").GetString().Should().Be("ADQUIRENTE");
    }

    /// <summary>
    /// CF-26 — <c>input_summary</c> es POBRE en PII: placa, NIT, escenario, título jurídico y la
    /// declaración de régimen. Sin domicilios, sin correos y sin nombres completos. Es el JSON que
    /// alimenta el listado y que la auditoría copia en cada UPDATE de la fila.
    /// </summary>
    [Fact]
    public async Task InputSummary_NoLlevaDomiciliosNiNombresNiCedulas()
    {
        await Handler().HandleAsync(
            TransferTestData.Comando(regimen: new RegimenDeclarationInput(
                NingunaAplica: true,
                CondicionesDeclaradas: [],
                DeclaredAt: DateTimeOffset.Parse(
                    "2026-09-09T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture))),
            TestContext.Current.CancellationToken);

        var crudo = _repository.Rows.Single().InputSummary;
        var resumen = JsonDocument.Parse(crudo).RootElement;

        resumen.GetProperty("placa").GetString().Should().Be("ABC123");
        resumen.GetProperty("nit").GetString().Should().Be("900123456");
        resumen.GetProperty("escenario").GetString().Should().Be("A");
        resumen.GetProperty("tituloJuridico").GetString().Should().Be("COMPRAVENTA");
        resumen.GetProperty("regimenAplicable").GetProperty("ningunaAplica").GetBoolean().Should().BeTrue();
        resumen.GetProperty("regimenAplicable").GetProperty("declaredAt").GetString().Should().NotBeNull();

        // Lo que NO puede estar.
        crudo.Should().NotContain("EMPRESA TRANSFERENTE SAS");
        crudo.Should().NotContain("PERSONA ADQUIRENTE DE PRUEBA");
        crudo.Should().NotContain("CIUDAD DE PRUEBA");
        crudo.Should().NotContain("OTRA CIUDAD DE PRUEBA");
        crudo.Should().NotContain("REPRESENTANTE DE PRUEBA");
        crudo.Should().NotContain("10000001");
        crudo.Should().NotContain("10000002");
        crudo.Should().NotContain("@");
    }

    /// <summary>
    /// El documento de una persona natural NO es un «NIT»: si ninguna parte es jurídica, el resumen
    /// no inventa un identificador.
    /// </summary>
    [Fact]
    public async Task SinParteJuridica_ElResumenNoLlevaNit()
    {
        await Handler().HandleAsync(
            TransferTestData.Comando(transferente: new TransferPartyInput(
                "PN", "PERSONA TRANSFERENTE DE PRUEBA", "CC", "10000003", null, "CIUDAD DE PRUEBA")),
            TestContext.Current.CancellationToken);

        var resumen = JsonDocument.Parse(_repository.Rows.Single().InputSummary).RootElement;

        resumen.GetProperty("nit").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// El historial nunca ve el snapshot: la proyección del repositorio no tiene dónde ponerlo
    /// (CF-17/CF-26).
    /// </summary>
    [Fact]
    public async Task ElListadoNoExponeElDocumentSnapshot()
    {
        await Handler().HandleAsync(TransferTestData.Comando(), TestContext.Current.CancellationToken);

        var page = await _repository.ListAsync(
            new StandaloneDocumentFilter { TenantId = Tenant, Page = 1, PageSize = 20 },
            TestContext.Current.CancellationToken);

        var item = page.Items.Single();
        typeof(StandaloneDocumentListItem).GetProperty("DocumentSnapshot").Should().BeNull();
        item.Scenario.Should().Be("A");
        item.DocumentType.Should().Be(StandaloneDocumentType.TransferenciaDominioGenerada);
    }

    /// <summary>CF-16 — repetir la clave devuelve el documento existente sin renderizar de nuevo.</summary>
    [Fact]
    public async Task IdempotencyKeyRepetida_DevuelveElMismoDocumentoSinRegenerar()
    {
        var primero = await Handler().HandleAsync(
            TransferTestData.Comando(idempotencyKey: "K-1"), TestContext.Current.CancellationToken);

        var segundo = await Handler().HandleAsync(
            TransferTestData.Comando(idempotencyKey: "K-1"), TestContext.Current.CancellationToken);

        segundo.Id.Should().Be(primero.Id);
        _repository.Rows.Should().ContainSingle();
        _generator.Calls.Should().Be(1);
        _storage.Saved.Should().ContainSingle();
    }

    /// <summary>Cuerpo incompleto: 400 de contrato, no un 422 normativo.</summary>
    [Theory]
    [InlineData("nombre")]
    [InlineData("ciudad")]
    [InlineData("retencion")]
    [InlineData("derechos")]
    public async Task CuerpoIncompleto_RespondeInvalidRequestConElCampo(string caso)
    {
        var comando = caso switch
        {
            "nombre" => TransferTestData.Comando(adquirente: new TransferPartyInput(
                "PN", null, "CC", "10000002")),
            "ciudad" => TransferTestData.Comando(
                negocio: TransferTestData.Negocio() with { CiudadFirma = null }),
            "retencion" => TransferTestData.Comando(
                negocio: TransferTestData.Negocio() with { AsumeRetencionFuente = null }),
            _ => TransferTestData.Comando(
                negocio: TransferTestData.Negocio() with { AsumeDerechosTramite = "SEGUN_LEY" }),
        };

        var result = await Handler().HandleAsync(comando, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateTransferenciaOutcome.InvalidRequest);
        result.ErrorField.Should().NotBeNullOrWhiteSpace();
        _repository.Rows.Should().BeEmpty();
    }

    /// <summary>
    /// En remolques y semirremolques no se pide quién asume el impuesto sobre vehículos: están
    /// exentos (Ley 488/1998, art. 5.3.2.1 num. 5.º inciso final).
    /// </summary>
    [Fact]
    public async Task Remolque_NoExigeQuienAsumeElImpuesto()
    {
        var result = await Handler().HandleAsync(
            TransferTestData.Comando(
                vehiculo: TransferTestData.Vehiculo("R12345", "SEMIRREMOLQUE"),
                negocio: TransferTestData.Negocio() with { AsumeImpuestoVehiculo = null }),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(GenerateTransferenciaOutcome.Generated);
        _generator.LastModel!.Negocio!.AsumeImpuestoVehiculo.Should().BeNull();
    }

    /// <summary>
    /// Adenda 15.4 — el modelo que llega al generador declara MANUSCRITA y trae DOS firmantes con su
    /// rol. El generador no infiere quién firma: se lo dice el escenario.
    /// </summary>
    [Fact]
    public async Task ElModeloEnviadoAlGeneradorDeclaraFirmaManuscritaYDosFirmantes()
    {
        await Handler().HandleAsync(TransferTestData.Comando(), TestContext.Current.CancellationToken);

        var model = _generator.LastModel!;
        model.SignatureMode.Should().Be(TransferSignatureMode.Manuscrita);
        model.Partes.Should().HaveCount(2);
        model.Partes.Select(p => p.Rol).Should().Equal(
            TransferPartyRole.Transferente, TransferPartyRole.Adquirente);
    }

    /// <summary>El DV se recalcula: un NIT y su dígito no pueden discrepar dentro del mismo PDF.</summary>
    [Fact]
    public async Task ElDigitoDeVerificacionSeRecalculaAunqueElClienteEnvieOtro()
    {
        await Handler().HandleAsync(
            TransferTestData.Comando(
                transferente: TransferTestData.Transferente() with { DigitoVerificacion = "0" }),
            TestContext.Current.CancellationToken);

        _generator.LastModel!.ParteConRol(TransferPartyRole.Transferente)!
            .DigitoVerificacion.Should().Be("8");
    }
}

/// <summary>
/// Doble del puerto del generador. Cuenta invocaciones y conserva el último modelo: varios AC se
/// juegan en QUÉ se le pidió renderizar, no solo en que se le pidiera.
/// </summary>
internal sealed class FakeStandaloneTransferGenerator : IStandaloneTransferGenerator
{
    public int Calls { get; private set; }

    public TransferDocumentModel? LastModel { get; private set; }

    public RenderedStandaloneDocument Render(TransferDocumentModel model)
    {
        Calls++;
        LastModel = model;
        return new RenderedStandaloneDocument(
            $"transferencia_dominio_{model.Vehiculo.Placa}.pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46]);
    }
}
