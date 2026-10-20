using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Stub del proveedor "flit_integrations" (gateway propio). HU#10201 lo deja como
/// placeholder hasta que se configure (ver ADR-0020). Devuelve un check unknown.
/// </summary>
internal sealed class FlitIntegrationsGatewayProvider : IConsultationProvider
{
    public string Key => "flit_integrations";

    public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) =>
        Task.FromResult(new ConsultationResult(
            "flit_integrations",
            "yellow",
            [
                new ConsultationCheck(
                    "gateway",
                    "Gateway Flit",
                    "unknown",
                    "flit_integrations",
                    "Proveedor no configurado (stub HU#10201, ver ADR-0020)")
            ],
            []));
}
