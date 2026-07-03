namespace Flit.Admin.Application.Improntas.GenerarImpronta;

/// <summary>
/// Comando de generación de impronta: el payload validado + el tenant/usuario SuperAdmin que la
/// solicita (claims <c>tenant_id</c>/<c>sub</c> del JWT), usados para la trazabilidad persistida
/// (<c>ImprontaGeneration.TenantId</c>/<c>FlitUserId</c>, HU #10466).
/// </summary>
public sealed class GenerarImprontaCommand
{
    public required GenerarImprontaRequest Request { get; init; }

    public required Guid TenantId { get; init; }

    public required Guid FlitUserId { get; init; }
}
