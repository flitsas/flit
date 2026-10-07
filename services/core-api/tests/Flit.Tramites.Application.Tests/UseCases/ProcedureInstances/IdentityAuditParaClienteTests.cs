using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>Épica #13202 — la compañía y el cliente ven la bitácora de una validación biométrica normal.</summary>
public sealed class IdentityAuditParaClienteTests
{
    private static IdentityAuditEventDto Evento(string stage, string outcome = "ok", string? mensaje = "usuario 019f… manual") =>
        new(DateTimeOffset.Parse("2026-10-06T15:30:00Z"), stage, outcome, null, null, null, null, "manual_approved", null, mensaje);

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

    [Theory]
    [InlineData(IdentityValidationAuditStages.ManualConsentimiento)]
    [InlineData(IdentityValidationAuditStages.ManualImagenesConsultadas)]
    [InlineData(IdentityValidationAuditStages.ManualCorreoFallido)]
    [InlineData(IdentityValidationAuditStages.KyverumCanceladoPorManual)]
    [InlineData(IdentityValidationAuditStages.WebhookIgnoradoManual)]
    public void Los_eventos_internos_sin_equivalente_no_se_muestran(string stage) =>
        IdentityAuditParaCliente.Aplicar(new[] { Evento(stage) }).Should().BeEmpty();

    [Fact]
    public void Los_eventos_normales_pasan_intactos_y_en_orden()
    {
        var normal = new IdentityAuditEventDto(DateTimeOffset.Parse("2026-10-06T15:29:00Z"), IdentityValidationAuditStages.Send, "ok", 200, null, null, null, "sent", null, "enviado");
        var res = IdentityAuditParaCliente.Aplicar(new[] { normal, Evento(IdentityValidationAuditStages.ManualActivado) });
        res[0].Should().Be(normal);
        res.Should().HaveCount(2);
        JsonText(res).Should().NotContainEquivalentOf("manual");
    }

    private static string JsonText(IEnumerable<IdentityAuditEventDto> e) => System.Text.Json.JsonSerializer.Serialize(e);
}
