using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>Épica #13202 — la compañía y el cliente ven la bitácora de una validación biométrica normal.</summary>
public sealed class IdentityAuditParaClienteTests
{
    private static IdentityAuditEventDto Evento(
        string stage, string outcome = "ok", string? mensaje = "usuario 019f… manual", string? rejectionReasonLabel = null) =>
        new(DateTimeOffset.Parse("2026-10-06T15:30:00Z"), stage, outcome, null, null, null, null, "manual_approved", null, mensaje,
            RejectionReasonLabel: rejectionReasonLabel);

    [Fact]
    public void Los_eventos_manuales_se_traducen_a_sus_equivalentes_normales_sin_mensajes()
    {
        var res = IdentityAuditParaCliente.Aplicar(new[]
        {
            Evento(IdentityValidationAuditStages.ManualActivado),
            Evento(IdentityValidationAuditStages.ManualCapturaRecibida),
            Evento(IdentityValidationAuditStages.ManualAprobado, IdentityValidationAuditOutcomes.Approved),
        });

        res.Select(e => (e.Stage, e.Outcome)).Should().Equal(
            (IdentityValidationAuditStages.Send, IdentityValidationAuditOutcomes.Ok),
            (IdentityValidationAuditStages.WebhookReceived, IdentityValidationAuditOutcomes.Received),
            (IdentityValidationAuditStages.WebhookApplied, IdentityValidationAuditOutcomes.Approved));
        res.Should().OnlyContain(e => e.Message == null && e.ProviderStatus == null && e.ErrorType == null);
    }

    [Fact]
    public void Rechazo_y_reenvio_tambien_tienen_equivalente()
    {
        var res = IdentityAuditParaCliente.Aplicar(new[]
        {
            Evento(IdentityValidationAuditStages.ManualRechazado, IdentityValidationAuditOutcomes.Rejected),
            Evento(IdentityValidationAuditStages.ManualEnlaceRegenerado),
        });
        res.Select(e => (e.Stage, e.Outcome)).Should().Equal(
            (IdentityValidationAuditStages.WebhookApplied, IdentityValidationAuditOutcomes.Rejected),
            (IdentityValidationAuditStages.Resend, IdentityValidationAuditOutcomes.Ok));
    }

    [Fact]
    public void Bug14634_El_motivo_homologado_del_rechazo_viaja_aunque_se_disfrace_lo_manual()
    {
        var res = IdentityAuditParaCliente.Aplicar(new[]
        {
            Evento(IdentityValidationAuditStages.ManualRechazado, IdentityValidationAuditOutcomes.Rejected,
                rejectionReasonLabel: "Documento ilegible o incompleto"),
        });

        res.Should().ContainSingle();
        res[0].Stage.Should().Be(IdentityValidationAuditStages.WebhookApplied);
        res[0].RejectionReasonLabel.Should().Be("Documento ilegible o incompleto");
        res[0].Message.Should().BeNull();
    }

    [Theory]
    [InlineData(IdentityValidationAuditStages.ManualConsentimiento)]
    [InlineData(IdentityValidationAuditStages.ManualImagenesConsultadas)]
    [InlineData(IdentityValidationAuditStages.ManualCorreoFallido)]
    [InlineData(IdentityValidationAuditStages.KyverumCanceladoPorManual)]
    [InlineData(IdentityValidationAuditStages.WebhookIgnoradoManual)]
    public void Los_eventos_internos_sin_equivalente_no_se_muestran(string stage) =>
        IdentityAuditParaCliente.Aplicar(new[] { Evento(stage) }).Should().BeEmpty();

    [Fact]
    public void Los_eventos_normales_conservan_etapa_y_resultado_pero_no_revelan_el_proveedor_ni_el_error_tecnico()
    {
        var normal = new IdentityAuditEventDto(DateTimeOffset.Parse("2026-10-06T15:29:00Z"), IdentityValidationAuditStages.Send, "ok", 200, null, null, null,
            "kyverum_status", null, "Enviando validación a Kyverum (create).");
        var conError = new IdentityAuditEventDto(DateTimeOffset.Parse("2026-10-06T15:30:00Z"), IdentityValidationAuditStages.Send, "error", 503, null, null, null,
            null, "KyverumVerifyException", "Kyverum no respondió");
        var res = IdentityAuditParaCliente.Aplicar(new[] { normal, conError, Evento(IdentityValidationAuditStages.ManualActivado) });

        res.Should().HaveCount(3);
        res[0].Stage.Should().Be(IdentityValidationAuditStages.Send);
        res[0].Outcome.Should().Be("ok");
        res[0].HttpStatus.Should().Be(200);
        res[0].Message.Should().BeNull();
        res[0].ProviderStatus.Should().BeNull();
        res[1].ErrorType.Should().Be(IdentityAuditParaCliente.ErrorProveedorGenerico);
        res[1].Message.Should().BeNull();
        JsonText(res).Should().NotContainEquivalentOf("kyverum");
        JsonText(res).Should().NotContainEquivalentOf("exception");
        JsonText(res).Should().NotContainEquivalentOf("manual");
    }

    private static string JsonText(IEnumerable<IdentityAuditEventDto> e) => System.Text.Json.JsonSerializer.Serialize(e);
}
