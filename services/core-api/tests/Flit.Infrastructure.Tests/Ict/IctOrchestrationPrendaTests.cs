using Flit.Api.Grpc;
using Flit.Ict.Grpc.Contracts;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Ict;

/// <summary>
/// Bug #13445 — pasos de <c>IctOrchestrationService.CreateDraftFromIct</c> que no necesitan base de datos:
/// aplicar la resolución de prenda (Auto → handler con metadata <c>ict_auto</c>; pendiente/discrepancia → aviso sin PII),
/// la siembra de <c>es_leasing</c>/<c>cambio_carroceria</c> que no pisa lo que vino de ICT y el aviso de
/// transformación sin subtipo.
/// <para>Uso de ejemplo: <c>await IctOrchestrationService.AplicarResolucionPrendaAsync(reply,
/// IctPrendaResolucion.Auto("levantar", n, d), registrar, _ =&gt; { }, ct)</c>.</para>
/// </summary>
public sealed class IctOrchestrationPrendaTests
{
    // Datos ficticios (nunca de producción).
    private const string Acreedor = "BANCO DE PRUEBA SA";
    private const string AcreedorDoc = "900000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Prenda ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AutoLevantar_LlamaAlHandlerConLaDecisionYElAcreedor_YNoAvisa()
    {
        var reply = new DraftReply();
        RegistrarPrendaInput? recibido = null;

        await IctOrchestrationService.AplicarResolucionPrendaAsync(
            reply,
            IctPrendaResolucion.Auto(PrendaDecision.Levantar, Acreedor, AcreedorDoc),
            input =>
            {
                recibido = input;
                return Task.FromResult<(PrendaDto?, string?)>((null, null));
            },
            _ => { },
            Ct);

        recibido.Should().NotBeNull();
        recibido!.Decision.Should().Be(PrendaDecision.Levantar);
        recibido.AcreedorNombre.Should().Be(Acreedor);
        recibido.AcreedorDocumento.Should().Be(AcreedorDoc);
        recibido.MetadataJson.Should().Be("""{"origen":"ict_auto","resolucion":"levantar"}""");
        reply.ErrorCode.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Pendiente_AvisaElMotivoSinLlamarAlHandlerNiExponerElAcreedor()
    {
        var reply = new DraftReply { ErrorCode = "preflight_warning:x" };
        var llamado = false;

        await IctOrchestrationService.AplicarResolucionPrendaAsync(
            reply,
            IctPrendaResolucion.Pendiente(IctPrendaResolver.MotivoAcreedorDistinto, IctPrendaResolver.MotivoAcreedorDistinto),
            _ =>
            {
                llamado = true;
                return Task.FromResult<(PrendaDto?, string?)>((null, null));
            },
            _ => { },
            Ct);

        llamado.Should().BeFalse();
        reply.ErrorCode.Should().Be(
            "preflight_warning:x;prenda_pendiente_gestor:acreedor_distinto;prenda_discrepancia_runt:acreedor_distinto");
        reply.ErrorCode.Should().NotContain(Acreedor).And.NotContain(AcreedorDoc);
    }

    [Fact]
    public async Task LevantarSinPrendaRunt_RegistraSinPrendaYAvisaLaDiscrepancia()
    {
        // Tras T2 el único Auto con discrepancia es «levantar» con RUNT sin prenda → sin_prenda + aviso.
        var reply = new DraftReply();
        RegistrarPrendaInput? recibido = null;

        await IctOrchestrationService.AplicarResolucionPrendaAsync(
            reply,
            IctPrendaResolucion.Auto(PrendaDecision.SinPrenda, null, null, IctPrendaResolver.MotivoLevantarSinPrendaRunt),
            input =>
            {
                recibido = input;
                return Task.FromResult<(PrendaDto?, string?)>((null, null));
            },
            _ => { },
            Ct);

        recibido!.Decision.Should().Be(PrendaDecision.SinPrenda);
        recibido.MetadataJson.Should().Be("""{"origen":"ict_auto","resolucion":"levantar_sin_prenda_runt"}""");
        reply.ErrorCode.Should().Be("prenda_discrepancia_runt:levantar_sin_prenda_runt");
    }

    [Fact]
    public void MetadataPrendaAuto_NoLlevaElAcreedor()
    {
        var json = IctOrchestrationService.MetadataPrendaAuto(
            IctPrendaResolucion.Auto(PrendaDecision.Registrar, Acreedor, AcreedorDoc));

        json.Should().Be("""{"origen":"ict_auto","resolucion":"registrar"}""");
        json.Should().NotContain(Acreedor).And.NotContain(AcreedorDoc);
    }

    [Fact]
    public async Task ErrorDelHandler_SeAvisaConSuCodigo()
    {
        var reply = new DraftReply();

        await IctOrchestrationService.AplicarResolucionPrendaAsync(
            reply,
            IctPrendaResolucion.Auto(PrendaDecision.Omitir, null, null),
            _ => Task.FromResult<(PrendaDto?, string?)>((null, RegistrarPrendaHandler.OmitirNoAdmitidoError)),
            _ => { },
            Ct);

        reply.ErrorCode.Should().Be("prenda_auto_error:prenda_omitir_no_admitido");
    }

    [Fact]
    public async Task ExcepcionDelHandler_NoTumbaLaMaterializacion_YSeAvisaSinElMensaje()
    {
        var reply = new DraftReply();
        Exception? reportada = null;

        await IctOrchestrationService.AplicarResolucionPrendaAsync(
            reply,
            IctPrendaResolucion.Auto(PrendaDecision.Registrar, Acreedor, AcreedorDoc),
            _ => throw new DbUpdateException("valor " + AcreedorDoc + " demasiado largo"),
            ex => reportada = ex,
            Ct);

        reportada.Should().BeOfType<DbUpdateException>();
        reply.ErrorCode.Should().Be("prenda_auto_error:persist_failed");
        reply.ErrorCode.Should().NotContain(AcreedorDoc);
    }

    [Fact]
    public async Task Nada_NoLlamaNiAvisa()
    {
        var reply = new DraftReply();
        var llamado = false;

        await IctOrchestrationService.AplicarResolucionPrendaAsync(
            reply,
            IctPrendaResolucion.Nada(),
            _ =>
            {
                llamado = true;
                return Task.FromResult<(PrendaDto?, string?)>((null, null));
            },
            _ => { },
            Ct);

        llamado.Should().BeFalse();
        reply.ErrorCode.Should().BeNullOrEmpty();
    }

    // ── Siembra del traspaso ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Siembra_NoPisaCambioCarroceriaQueVinoDeIct()
    {
        var request = new CreateDraftFromIctRequest();
        request.FieldValues.Add(new FieldValue { FieldKey = "cambio_carroceria", ValueText = "true" });
        var items = request.FieldValues.Select(f => new FieldValueInput(null, f.FieldKey, f.ValueText, null)).ToList();

        IctOrchestrationService.SembrarAtributosManualesTraspaso(items, request);

        items.Where(i => i.FieldKey == "cambio_carroceria").Should().ContainSingle()
            .Which.ValueText.Should().Be("true");
        items.Should().ContainSingle(i => i.FieldKey == "es_leasing" && i.ValueText == "false");
    }

    [Fact]
    public void Siembra_SinClavesDeIct_PoneLosDosDefaults()
    {
        var request = new CreateDraftFromIctRequest();
        var items = new List<FieldValueInput>();

        IctOrchestrationService.SembrarAtributosManualesTraspaso(items, request);

        items.Select(i => (i.FieldKey, i.ValueText)).Should().BeEquivalentTo(
            new[] { ("es_leasing", "false"), ("cambio_carroceria", "false") });
    }

    [Fact]
    public void Siembra_NoPisaEsLeasingQueVinoDeIct()
    {
        var request = new CreateDraftFromIctRequest();
        request.FieldValues.Add(new FieldValue { FieldKey = "ES_LEASING", ValueText = "true" });
        var items = new List<FieldValueInput>();

        IctOrchestrationService.SembrarAtributosManualesTraspaso(items, request);

        items.Should().ContainSingle().Which.FieldKey.Should().Be("cambio_carroceria");
    }
}
