namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// ¿La compañía permite continuar al procesar aunque el RUNT no reporte SOAT vigente?
///
/// <para>Con la opción <b>activa</b> (<see cref="IsEnabledAsync"/> = <c>true</c>) el hallazgo solo
/// se informa y el trámite avanza. Con la opción <b>apagada</b> (default) un SOAT no vigente
/// detiene el avance. La consulta al RUNT se hace siempre que haya validador inyectado.</para>
///
/// <para>Mismo patrón que el resto de políticas del wizard: puerto en Application con null-object
/// por defecto (apagado = bloquea) y binding real en Infraestructura.</para>
/// </summary>
public interface ISoatRuntValidationPolicy
{
    /// <summary>
    /// <c>true</c> = opción activa: no bloquea si el SOAT no está vigente.
    /// <c>false</c> = opción apagada: sí bloquea.
    /// </summary>
    Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Por defecto apagado: si hay validador y el RUNT reporta SOAT no vigente, el trámite no avanza.
/// Es lo que aplica en tests que no ejercitan la política (y en compañías sin fila de políticas).
/// </summary>
public sealed class NullSoatRuntValidationPolicy : ISoatRuntValidationPolicy
{
    public static NullSoatRuntValidationPolicy Instance { get; } = new();

    public Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
