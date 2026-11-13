using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Derivacion;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Notificaciones;

/// <summary>
/// Único punto que escribe en <c>diagnostico.notificaciones</c> (HU #14563 B8, ADR-0086 D-3, D8). Lo llaman los cuatro productores
/// (ZIP LISTA y ERROR definitivo en <c>EmpaquetadorZip</c>, derivación MASIVA terminada y lote COMPLETADO o INCOMPLETO en
/// <c>RecalculadorEstado</c>) DENTRO de la unidad de trabajo del hecho: agrega la fila al contexto y la guarda quien guarda el hecho,
/// así que si el hecho se revierte no queda notificación.
/// <list type="bullet">
/// <item>Sin destinatario (<see cref="Guid.Empty"/>) no inserta.</item>
/// <item>Solo los cuatro tipos de <see cref="Notificacion.Tipos"/> (D8: no hay DICTAMEN_AJUSTADO); otro lanza <see cref="ArgumentException"/>.</item>
/// <item>El <c>tenant_id</c> es explícito, el de la fila del hecho, nunca el implícito del contexto.</item>
/// <item>Idempotente: no agrega si ya existe <c>(tenant, usuario, tipo, referencia)</c> en la base ni si la misma unidad de trabajo
/// ya tiene una del mismo destinatario, tipo y lote. El índice único <c>ux_notificaciones_idempotencia</c> queda de respaldo.</item>
/// </list>
/// No usa SQL crudo: escribe por EF con el filtro de empresa (ADR-0072) y no amplía la lista blanca de
/// <c>TomaEntreEmpresasSoloEnColaTests</c>.
/// </summary>
internal static class EscritorNotificaciones
{
    /// <summary>Agrega la notificación al contexto (sin guardar); true si la agregó.</summary>
    public static async Task<bool> AgregarAsync(
        DiagnosticoDb db, Guid tenantId, Guid usuarioId, string tipo, Guid diagnosticoId, Guid referenciaId, DateTimeOffset ahora,
        CancellationToken ct, string datos = "{}")
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!Notificacion.Tipos.Contains(tipo))
            throw new ArgumentException($"Tipo de notificación no admitido: {tipo}.", nameof(tipo));
        if (usuarioId == Guid.Empty)
            return false;

        var pendiente = db.ChangeTracker.Entries<Notificacion>().Any(e => e.State == EntityState.Added
            && e.Entity.TenantId == tenantId && e.Entity.UsuarioId == usuarioId && e.Entity.Tipo == tipo
            && (e.Entity.ReferenciaId == referenciaId || e.Entity.DiagnosticoId == diagnosticoId));
        if (pendiente)
            return false;
        var existe = await db.Set<Notificacion>().AsNoTracking()
            .AnyAsync(n => n.TenantId == tenantId && n.UsuarioId == usuarioId && n.Tipo == tipo && n.ReferenciaId == referenciaId, ct)
            .ConfigureAwait(false);
        if (existe)
            return false;

        db.Add(new Notificacion
        {
            TenantId = tenantId,
            UsuarioId = usuarioId,
            Tipo = tipo,
            DiagnosticoId = diagnosticoId,
            ReferenciaId = referenciaId,
            Datos = datos,
            CreadaEn = ahora,
        });
        return true;
    }

    /// <summary>
    /// DERIVACION_TERMINADA si <paramref name="operacion"/> es MASIVA y acaba de terminar en <paramref name="ahora"/> (P2 = a: la
    /// individual no notifica). Destinatario: quien lanzó la operación; referencia: la operación.
    /// </summary>
    public static Task<bool> SiTerminoDerivacionMasivaAsync(DiagnosticoDb db, OperacionDerivacion operacion, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operacion);
        if (operacion is not { Tipo: TipoOperacionDerivacion.Masiva, Estado: EstadoOperacionDerivacion.Terminada } || operacion.TerminadaEn != ahora)
            return Task.FromResult(false);
        return AgregarAsync(db, operacion.TenantId, operacion.UsuarioId, Notificacion.DerivacionTerminada, operacion.DiagnosticoId, operacion.Id, ahora, ct,
            $$"""{"creadas":{{operacion.Creadas}},"omitidas":{{operacion.Omitidas}},"fallidas":{{operacion.Fallidas}}}""");
    }
}
