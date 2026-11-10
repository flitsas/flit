using Flit.Tramites.Domain.Tramites.PersonaNatural;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #14469 — pago confirmado en caja del punto de atención que el portal entregó al radicar. Bloque opcional
/// <c>pagoPortal</c> del detalle del trámite. Sin datos personales ni el id del cobro del portal.
/// </summary>
public sealed record ProcedureInstancePagoPortalDto(
    long ValorCop,
    string Medio,
    string? MedioCaja,
    string? MedioDetalle,
    DateTimeOffset PagadoEn)
{
    public static ProcedureInstancePagoPortalDto Desde(PagoPortalTramite p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new ProcedureInstancePagoPortalDto(p.ValorCop, p.Medio, p.MedioCaja, p.MedioDetalle, p.PagadoEn);
    }
}
