using System.Text.Json;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #12798 (Épica #12760) — bitácora de fallos de regeneración del consolidado con aviso en cascada.
/// Los generadores son los REALES (<see cref="GenerarConsolidadoHandler"/> /
/// <see cref="GenerarConsolidadoMaestroHandler"/>) con repositorio sustituto y storage en memoria; el
/// fallo se provoca quitando el binario de un adjunto (<c>adjunto_no_disponible</c>) o haciendo que el
/// merger lance.
/// <para>Uso de ejemplo:
/// <code>
/// var bitacora = new ConsolidadoFalloBitacora(trazaWriter, logger);
/// var salida = await bitacora.GenerarConRespaldoAsync(tenantId, id, "consolidado",
///     ConsolidadoFalloBitacora.Origenes.EntregaConsolidado, anterior, c => generar(c), ct);
/// // fallo + anterior ⇒ salida.SirvioAnterior, salida.Result.AvisosCascada = ["consolidado: adjunto_no_disponible"]
/// </code></para>
/// </summary>
public sealed class ConsolidadoFalloBitacoraTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly FakeStorage _storage = new();
    private readonly FakeMerger _merger = new();
    private readonly TrazaEspia _traza = new();
    private readonly ConsolidadoFalloBitacora _bitacora;
    private readonly GenerarConsolidadoHandler _wizard;
    private readonly GenerarConsolidadoMaestroHandler _maestro;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ConsolidadoFalloBitacoraTests()
    {
        _bitacora = new ConsolidadoFalloBitacora(_traza);
        _wizard = new GenerarConsolidadoHandler(_repo, _merger, _storage);
        _maestro = new GenerarConsolidadoMaestroHandler(_repo, _merger, _storage);
    }

    public static TheoryData<ConsolidadoEntregaTipo> AmbosPdf() =>
        new() { ConsolidadoEntregaTipo.Wizard, ConsolidadoEntregaTipo.Maestro };

    // ── AC1 — evento de fallo ───────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosPdf))]
    public async Task AC1_RegeneracionQueFalla_RegistraEventoConCausaDocumentoEInstante(ConsolidadoEntregaTipo tipo)
    {
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, TipoAdjunto(tipo), "previo.pdf", "system");
        SetBandera(instance, tipo, false);
        RomperFactura(instance);
        Wire(instance);
        var antes = DateTimeOffset.UtcNow;

        await Entrega().HandleAsync(new EntregarConsolidadoRequest(instance.Id, instance.TenantId, tipo), Ct);

        var evento = _traza.Eventos.Should().ContainSingle().Subject;
        evento.TenantId.Should().Be(instance.TenantId);
        evento.InstanceId.Should().Be(instance.Id);
        evento.Tipo.Should().Be(ConsolidadoFalloBitacora.EventoFallo).And.Be("consolidado_regeneracion_fallida");
        var payload = JsonDocument.Parse(evento.Payload).RootElement;
        payload.GetProperty("error").GetString().Should().Be("adjunto_no_disponible");
        payload.GetProperty("documento").GetString().Should().Be(TipoAdjunto(tipo));
        payload.GetProperty("origen").GetString().Should().Be(ConsolidadoFalloBitacora.Origenes.EntregaConsolidado);
        payload.GetProperty("fallido_at").GetDateTimeOffset().Should().BeOnOrAfter(antes).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        payload.GetProperty("detalle").ValueKind.Should().Be(JsonValueKind.Null, "fue un código de negocio, no una excepción");
    }

    [Fact]
    public async Task AC1_ExcepcionDelGenerador_RegistraCausaExcepcionYTipo()
    {
        var instance = Instancia(TramiteEstado.Borrador);
        AddAttachment(instance, "consolidado", "previo.pdf", "system");
        Wire(instance);

        await _bitacora.GenerarConRespaldoAsync(
            instance.TenantId, instance.Id, "consolidado", ConsolidadoFalloBitacora.Origenes.GeneracionWizard,
            instance.Attachments.Single(a => a.Tipo == "consolidado"),
            _ => throw new IOException("disco lleno"), Ct);

        var payload = JsonDocument.Parse(_traza.Eventos.Single().Payload).RootElement;
        payload.GetProperty("error").GetString().Should().Be(ConsolidadoFalloBitacora.CausaExcepcion);
        payload.GetProperty("detalle").GetString().Should().Be(nameof(IOException));
        payload.GetProperty("mensaje").GetString().Should().Be("disco lleno");
    }

    [Fact]
    public async Task AC1_Contrato_SiLaEscrituraDeLaBitacoraFalla_NoTumbaLaEntrega()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = AddAttachment(instance, "consolidado", "previo.pdf", "system");
        instance.ConsolidadoWizardVigente = false;
        RomperFactura(instance);
        Wire(instance);
        _traza.Lanza = new InvalidOperationException("bd caída");

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Wizard), Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(previo.Id);
    }

    // ── AC2 — productor de AvisosCascada sin cambio de contrato ─────────────────────────────────

    [Fact]
    public async Task AC2_ElFalloRegistrado_EmiteElAvisoEnCascada_FormatoDocumentoMotivo()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, "consolidado", "previo.pdf", "system");
        instance.ConsolidadoWizardVigente = false;
        RomperFactura(instance);
        Wire(instance);

        var (result, _) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Wizard), Ct);

        var aviso = result!.AvisosCascada.Should().ContainSingle().Subject;
        aviso.Should().Be("consolidado: adjunto_no_disponible");
        aviso.Should().Be(ConsolidadoFalloBitacora.Aviso("consolidado", JsonDocument
            .Parse(_traza.Eventos.Single().Payload).RootElement.GetProperty("error").GetString()!),
            "el aviso sale del mismo fallo que se registró");
    }

    [Fact]
    public void AC2_Contrato_ElResultadoSerializaLasMismasPropiedades_AvisosCascadaEsListaDeStrings()
    {
        var result = new GenerarConsolidadoResult(
            new ConsolidadoDocumentDto(Guid.NewGuid(), "consolidado", "c.pdf", "sha"),
            Regenerado: false,
            AvisosCascada: [ConsolidadoFalloBitacora.Aviso("consolidado", "storage_unavailable")]);

        var json = JsonSerializer.SerializeToElement(result, WebJson);

        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["document", "regenerado", "incompleto", "documentosFaltantes", "avisosCascada", "definitivoPorEstadoFinal", "modo"],
            "la HU no añade campos al contrato: el aviso viaja en avisosCascada");
        json.GetProperty("avisosCascada")[0].GetString().Should().Be("consolidado: storage_unavailable");
    }

    [Fact]
    public async Task AC2_RegeneracionCorrecta_NoEmiteAvisoNiEvento()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, "consolidado", "previo.pdf", "system");
        instance.ConsolidadoWizardVigente = false;
        Wire(instance);

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Wizard), Ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        result.AvisosCascada.Should().BeNull();
        _traza.Eventos.Should().BeEmpty();
    }

    // ── AC3 — fallo en segundo plano (handler de la cola) ───────────────────────────────────────

    [Theory]
    [InlineData(TipoConsolidado.Wizard)]
    [InlineData(TipoConsolidado.Maestro)]
    public async Task AC3_RegeneracionAnticipadaQueFalla_TerminaFallido_SinPropagar_YQuedaEnBitacora(TipoConsolidado documento)
    {
        var tipo = documento == TipoConsolidado.Wizard ? "consolidado" : "consolidado_maestro";
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = AddAttachment(instance, tipo, "previo.pdf", "system");
        RomperFactura(instance);
        Wire(instance);

        var resultado = await Anticipado().HandleAsync(instance.TenantId, instance.Id, documento, Ct);

        resultado.Should().Be(ResultadoRegeneracionAnticipada.Fallido);
        var payload = JsonDocument.Parse(_traza.Eventos.Should().ContainSingle().Subject.Payload).RootElement;
        payload.GetProperty("origen").GetString().Should().Be(ConsolidadoFalloBitacora.Origenes.RegeneracionAnticipada);
        payload.GetProperty("documento").GetString().Should().Be(tipo);
        _storage.Files.Should().ContainKey(previo.StoragePath, "el PDF anterior sigue disponible");
        _storage.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_ExcepcionEnLaRegeneracionAnticipada_NoSePropaga_YSeRegistra()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, "consolidado_maestro", "previo.pdf", "system");
        Wire(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("timeout"));

        var act = () => Anticipado().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct);

        (await act.Should().NotThrowAsync()).Subject.Should().Be(ResultadoRegeneracionAnticipada.Fallido);
        var payload = JsonDocument.Parse(_traza.Eventos.Single().Payload).RootElement;
        payload.GetProperty("error").GetString().Should().Be(ConsolidadoFalloBitacora.CausaExcepcion);
        payload.GetProperty("detalle").GetString().Should().Be(nameof(InvalidOperationException));
    }

    // ── AC4 — fallo en el acceso del usuario: se entrega el anterior ────────────────────────────

    [Theory]
    [MemberData(nameof(AmbosPdf))]
    public async Task AC4_Entrega_FalloConAnterior_DevuelveElAnteriorConAviso(ConsolidadoEntregaTipo tipo)
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = AddAttachment(instance, TipoAdjunto(tipo), "previo.pdf", "system");
        SetBandera(instance, tipo, false);
        RomperFactura(instance);
        Wire(instance);

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, tipo, Force: true), Ct);

        error.Should().BeNull("el usuario recibe documento, no un error");
        result!.Document.AttachmentId.Should().Be(previo.Id);
        result.Document.Sha256.Should().Be(previo.Sha256);
        result.Regenerado.Should().BeFalse();
        result.Modo.Should().BeNull("el enum de modo del contrato no se amplía en esta HU");
        result.DefinitivoPorEstadoFinal.Should().BeFalse();
        result.AvisosCascada.Should().ContainSingle().Which.Should().Be($"{TipoAdjunto(tipo)}: adjunto_no_disponible");
        instance.Attachments.Should().Contain(previo);
        _storage.Files.Should().ContainKey(previo.StoragePath);
    }

    [Fact]
    public async Task AC4_Entrega_ExcepcionConAnterior_DevuelveElAnterior()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        var previo = AddAttachment(instance, "consolidado", "previo.pdf", "system");
        instance.ConsolidadoWizardVigente = false;
        Wire(instance);
        _merger.Lanza = new InvalidOperationException("pdf corrupto");

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Wizard), Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(previo.Id);
        result.AvisosCascada.Should().Equal("consolidado: excepcion");
    }

    [Fact]
    public async Task AC4_Entrega_FalloSinAnterior_ErrorComoHoy()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        RomperFactura(instance);
        Wire(instance);

        var (result, error) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro), Ct);

        result.Should().BeNull();
        error.Should().Be("adjunto_no_disponible");
        _traza.Eventos.Should().ContainSingle("el fallo se registra aunque no haya anterior que servir");
    }

    [Fact]
    public async Task AC4_PostWizard_ForceQueFalla_ConAnterior_DevuelveElAnteriorConAviso()
    {
        var instance = Instancia(TramiteEstado.Borrador);
        var previo = AddAttachment(instance, "consolidado", "previo.pdf", "system");
        instance.ConsolidadoWizardVigente = true;
        RomperFactura(instance);
        Wire(instance);

        var (result, error) = await PostWizard().HandleAsync(instance.Id, instance.TenantId, userId: null, force: true, Ct);

        error.Should().BeNull();
        result!.Document.AttachmentId.Should().Be(previo.Id);
        result.Regenerado.Should().BeFalse();
        result.AvisosCascada.Should().Equal("consolidado: adjunto_no_disponible");
        var payload = JsonDocument.Parse(_traza.Eventos.Single().Payload).RootElement;
        payload.GetProperty("origen").GetString().Should().Be(ConsolidadoFalloBitacora.Origenes.GeneracionWizard);
    }

    [Fact]
    public async Task AC4_PostWizard_FalloSinAnterior_ErrorComoHoy()
    {
        var instance = Instancia(TramiteEstado.Borrador);
        RomperFactura(instance);
        Wire(instance);

        var (result, error) = await PostWizard().HandleAsync(instance.Id, instance.TenantId, userId: null, force: false, Ct);

        result.Should().BeNull();
        error.Should().Be("adjunto_no_disponible");
    }

    [Fact]
    public async Task AC4_PostWizard_ExcepcionSinAnterior_SeRelanzaComoHoy()
    {
        var instance = Instancia(TramiteEstado.Borrador);
        Wire(instance);
        _merger.Lanza = new InvalidOperationException("pdf corrupto");

        var act = () => PostWizard().HandleAsync(instance.Id, instance.TenantId, userId: null, force: false, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _traza.Eventos.Should().ContainSingle();
    }

    [Fact]
    public async Task AC4_PostWizard_Exito_ContratoSinCambios()
    {
        var instance = Instancia(TramiteEstado.Borrador);
        AddAttachment(instance, "consolidado", "previo.pdf", "system");
        Wire(instance);

        var (result, error) = await PostWizard().HandleAsync(instance.Id, instance.TenantId, userId: null, force: true, Ct);

        error.Should().BeNull();
        result!.Regenerado.Should().BeTrue();
        result.AvisosCascada.Should().BeNull();
        result.Modo.Should().BeNull("el POST no pasa por la entrega");
        _traza.Eventos.Should().BeEmpty();
    }

    // ── AC5 — sin ruido por omisión ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    public async Task AC5_Entrega_EstadoFinal_NoRegistraNiAvisa(string estado)
    {
        var instance = Instancia(estado);
        AddAttachment(instance, "consolidado_maestro", "definitivo.pdf", "system");
        RomperFactura(instance);
        Wire(instance);

        var (result, _) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Maestro, Force: true), Ct);

        result!.AvisosCascada.Should().BeNull();
        _traza.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC5_Entrega_OrigenManual_NoRegistraNiAvisa()
    {
        var instance = Instancia(TramiteEstado.Entregado);
        AddAttachment(instance, "consolidado", "cargado.pdf", "user");
        instance.ConsolidadoWizardVigente = false;
        RomperFactura(instance);
        Wire(instance);

        var (result, _) = await Entrega().HandleAsync(
            new EntregarConsolidadoRequest(instance.Id, instance.TenantId, ConsolidadoEntregaTipo.Wizard, Force: true), Ct);

        result!.AvisosCascada.Should().BeNull();
        _traza.Eventos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado, false, "system")]
    [InlineData(TramiteEstado.Borrador, true, "migration")]
    [InlineData(TramiteEstado.Entregado, false, "user")]
    public async Task AC5_Anticipado_Omitido_NoRegistra(string estado, bool migrado, string source)
    {
        var instance = Instancia(estado);
        instance.IsMigrated = migrado;
        AddAttachment(instance, "consolidado_maestro", "previo.pdf", source);
        RomperFactura(instance);
        Wire(instance);

        var resultado = await Anticipado().HandleAsync(instance.TenantId, instance.Id, TipoConsolidado.Maestro, Ct);

        resultado.Should().NotBe(ResultadoRegeneracionAnticipada.Fallido);
        _traza.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC5_MigradoSoloLecturaDelGenerador_NoEsFallo()
    {
        var instance = Instancia(TramiteEstado.Aprobado);
        var previo = AddAttachment(instance, "consolidado", "v1.pdf", "migration");

        var salida = await _bitacora.GenerarConRespaldoAsync(
            instance.TenantId, instance.Id, "consolidado", ConsolidadoFalloBitacora.Origenes.GeneracionWizard, previo,
            _ => Task.FromResult<(GenerarConsolidadoResult?, string?)>((null, "migrado_solo_lectura")), Ct);

        salida.Error.Should().Be("migrado_solo_lectura", "la omisión viaja como siempre");
        salida.SirvioAnterior.Should().BeFalse();
        _traza.Eventos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("not_found", false)]
    [InlineData("migrado_solo_lectura", false)]
    [InlineData("storage_unavailable", true)]
    [InlineData("adjunto_no_disponible", true)]
    [InlineData("organismo_requerido", true)]
    public void AC5_EsFallo_ClasificaOmisionesYFallos(string? error, bool esperado) =>
        ConsolidadoFalloBitacora.EsFallo(error).Should().Be(esperado);

    // ── AC6 — sin datos sensibles ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AC6_ElPayloadNoLlevaPiiNiUrlsNiTrazas()
    {
        var instance = Instancia(TramiteEstado.Borrador);
        var previo = AddAttachment(instance, "consolidado", "previo.pdf", "system");
        var ex = new InvalidOperationException(
            "Titular 'Juan Pérez' CC 1023456789 (juan.perez@correo.com) falló en "
            + "https://s3.amazonaws.com/bucket/obj?X-Amz-Credential=AKIAXYZ&X-Amz-Signature=abc123 "
            + @"leyendo C:\secretos\clave.pem");

        await _bitacora.GenerarConRespaldoAsync(
            instance.TenantId, instance.Id, "consolidado", ConsolidadoFalloBitacora.Origenes.GeneracionWizard, previo,
            _ => throw ex, Ct);

        var payload = _traza.Eventos.Single().Payload;
        payload.Should().NotContain("Juan").And.NotContain("1023456789").And.NotContain("juan.perez")
            .And.NotContain("amazonaws").And.NotContain("X-Amz").And.NotContain("AKIA")
            .And.NotContain("secretos").And.NotContain("clave.pem")
            .And.NotContain(" at ", "nunca se persiste ex.ToString() con la traza de pila")
            .And.NotContain(instance.ReferenceNumber);
        JsonDocument.Parse(payload).RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["origen", "documento", "error", "detalle", "mensaje", "fallido_at", "tenant_id"]);
    }

    [Theory]
    [InlineData("ver https://bucket.s3.amazonaws.com/a.pdf?X-Amz-Signature=zz ahora", "ver [url] ahora")]
    [InlineData("correo ana@dominio.co invalido", "correo [email] invalido")]
    [InlineData("cedula 1023456789 duplicada", "cedula # duplicada")]
    [InlineData("Key (placa)=(ABC123) already exists", "Key (***)=(***) already exists")]
    [InlineData("nombre 'Ana Gómez' rechazado", "nombre '***' rechazado")]
    [InlineData(@"ruta D:\datos\x.pdf y /var/lib/flit/y.pdf", "ruta [ruta] y [ruta]")]
    [InlineData("linea1\r\nlinea2", "linea1 linea2")]
    public void AC6_Sanear_QuitaDatosSensibles(string entrada, string esperado) =>
        ConsolidadoFalloBitacora.Sanear(entrada).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AC6_Sanear_VacioDevuelveNull(string? entrada) =>
        ConsolidadoFalloBitacora.Sanear(entrada).Should().BeNull();

    [Fact]
    public void AC6_Sanear_AcotaLaLongitud() =>
        ConsolidadoFalloBitacora.Sanear(new string('x', 500))!.Length.Should().Be(ConsolidadoFalloBitacora.MensajeMaximo);

    // ── Infraestructura del test ────────────────────────────────────────────────────────────────

    private EntregarConsolidadoHandler Entrega() => new(_repo, _wizard, _maestro, _bitacora);

    private RegenerarConsolidadoAnticipadoHandler Anticipado() => new(_repo, _wizard, _maestro, logger: null, _bitacora);

    private GenerarConsolidadoConRespaldoHandler PostWizard() => new(_repo, _wizard, _bitacora);

    private static string TipoAdjunto(ConsolidadoEntregaTipo tipo) =>
        tipo == ConsolidadoEntregaTipo.Maestro ? "consolidado_maestro" : "consolidado";

    private static void SetBandera(ProcedureInstance instance, ConsolidadoEntregaTipo tipo, bool valor)
    {
        if (tipo == ConsolidadoEntregaTipo.Maestro)
            instance.ConsolidadoMaestroVigente = valor;
        else
            instance.ConsolidadoWizardVigente = valor;
    }

    /// <summary>Quita el binario de la factura: el generador responde <c>adjunto_no_disponible</c>.</summary>
    private void RomperFactura(ProcedureInstance instance) =>
        _storage.Files.Remove(instance.Attachments.Single(a => a.Tipo == "factura").StoragePath);

    private void Wire(ProcedureInstance instance)
    {
        _repo.GetByIdWithAttachmentsAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        _repo.GetByIdWithChecklistGraphAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
    }

    private ProcedureInstance Instancia(string estado)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-012798",
            Status = estado,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        AddAttachment(instance, "fur", "fur.pdf", "system");
        AddAttachment(instance, "factura", "factura.pdf", "user");
        return instance;
    }

    private ProcedureInstanceAttachment AddAttachment(ProcedureInstance instance, string tipo, string filename, string source)
    {
        var path = $"{instance.Id:D}/{tipo}-{Guid.NewGuid():N}";
        var content = System.Text.Encoding.UTF8.GetBytes($"%PDF-{filename}");
        _storage.Files[path] = content;
        var attachment = new ProcedureInstanceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = instance.TenantId,
            ProcedureInstanceId = instance.Id,
            Tipo = tipo,
            Filename = filename,
            Mimetype = "application/pdf",
            SizeBytes = content.Length,
            Sha256 = $"sha-{tipo}-{filename}",
            StoragePath = path,
            Source = source,
            UploadedAt = DateTimeOffset.UtcNow,
        };
        instance.Attachments.Add(attachment);
        return attachment;
    }

    private sealed record Evento(Guid TenantId, Guid InstanceId, string Tipo, string Payload);

    private sealed class TrazaEspia : IRegeneracionDocumentalTrazaWriter
    {
        public List<Evento> Eventos { get; } = [];
        public Exception? Lanza { get; set; }

        public Task<bool> EscribirFalloAsync(
            Guid tenantId, Guid procedureInstanceId, string origen, string codigoError, string? detalle,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> EscribirFalloAsync(
            Guid tenantId, Guid procedureInstanceId, string origen, string codigoError, string? detalle,
            string tipoEvento, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> EscribirEventoAsync(
            Guid tenantId, Guid procedureInstanceId, string tipoEvento, string payloadJson,
            CancellationToken cancellationToken = default)
        {
            if (Lanza is not null)
                throw Lanza;
            Eventos.Add(new Evento(tenantId, procedureInstanceId, tipoEvento, payloadJson));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeMerger : IExpedienteConsolidadoMerger
    {
        public Exception? Lanza { get; set; }

        public byte[] NormalizeToPdf(byte[] content, string mimetype) => content;

        public byte[] Merge(IReadOnlyList<byte[]> pdfParts) => pdfParts.SelectMany(x => x).ToArray();

        public byte[] Compose(MergeRequest request) =>
            Lanza is not null ? throw Lanza : Merge(request.Parts.Select(p => p.Pdf).ToList());
    }

    private sealed class FakeStorage : IAttachmentStorage
    {
        public List<string> Saved { get; } = [];
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<StoredFile> SaveAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            var path = $"{procedureInstanceId:D}/{tipo}_saved_{Saved.Count}";
            Files[path] = ms.ToArray();
            Saved.Add(path);
            return new StoredFile(path, $"sha-{tipo}-nuevo", ms.Length);
        }

        public Task<PresignedUpload> CreatePresignedUploadAsync(
            Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void Delete(string storagePath) => Files.Remove(storagePath);

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes) : null);

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(
            string storagePath, CancellationToken ct = default) =>
            Task.FromResult<(string Url, DateTimeOffset ExpiresAt)?>(null);
    }
}
