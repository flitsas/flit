using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Bug #13194 (review PR #510, MAYOR-1) — accesor SCOPED (uno por petición) de las partes sin firma del
/// último bloqueo del gate de firma, con el estado de su notificación. Lo llena
/// <see cref="TramiteLifecycleService"/> al bloquear; lo leen los endpoints para la extensión
/// <c>partesSinFirma</c> del ProblemDetails 409. Existe para no cambiar la firma de los handlers de
/// <c>/submit</c> y <c>/enviar-al-ot</c> (unos 40 llamadores) solo para transportar este dato.
/// </summary>
public sealed class UltimoBloqueoFirma
{
    /// <summary>Partes sin firma del último bloqueo en este ámbito; null si no hubo bloqueo.</summary>
    public IReadOnlyList<ParteSinFirma>? PartesSinFirma { get; set; }
}
