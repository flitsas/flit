using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Una celda cuyo trabajo de consulta está en vuelo por el bus (ADR-0075, HU #13727): lo justo para fijar la empresa, reconocer
/// su trabajo vigente y dejar el intento auditable. Reemplaza a la «celda tomada» de la cola de Postgres con lease.
/// </summary>
/// <param name="Id">Id de la celda.</param>
/// <param name="TenantId">Empresa de la celda; el consumidor la fija antes de leer o escribir.</param>
/// <param name="DiagnosticoId">Lote.</param>
/// <param name="VehiculoId">Vehículo.</param>
/// <param name="Fuente">Fuente consultada.</param>
/// <param name="TrabajoId">Trabajo vigente (<c>consultas.trabajo_id</c>); un resultado de otro trabajo se descarta.</param>
/// <param name="IntentosAuto">Reintentos automáticos ya programados para la celda (HU #13737, D-12).</param>
/// <param name="ReprocesoUsuarioId">
/// Usuario del reproceso en curso (HU #13746, R-1): el intento queda con <c>disparador=REPROCESO</c>; null si no es un reproceso.
/// </param>
public sealed record CeldaEnCurso(
    Guid Id, Guid TenantId, Guid DiagnosticoId, Guid VehiculoId, Fuente Fuente, Guid? TrabajoId, int IntentosAuto = 0, Guid? ReprocesoUsuarioId = null)
{
    /// <summary>La celda tal como está en el contexto.</summary>
    public static CeldaEnCurso De(CeldaConsulta celda)
    {
        ArgumentNullException.ThrowIfNull(celda);
        return new(celda.Id, celda.TenantId, celda.DiagnosticoId, celda.VehiculoId, celda.Fuente, celda.TrabajoId, celda.IntentosAuto, celda.ReprocesoUsuarioId);
    }
}
