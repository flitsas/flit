namespace Flit.Diagnostico.Domain.Diagnosticos;

/// <summary>Una transición del lote ya validada: lo que se escribe en el historial de estados (R-8).</summary>
/// <param name="Anterior">Estado de partida.</param>
/// <param name="Nuevo">Estado de llegada.</param>
/// <param name="Disparador">Qué la provocó.</param>
public sealed record TransicionLote(EstadoDiagnostico Anterior, EstadoDiagnostico Nuevo, DisparadorEstado Disparador);

/// <summary>
/// Máquina de estados del lote (plan §3.5, R-8, HU #13727): qué transiciones existen y con qué disparador. Esta HU implementa
/// <c>CREADO → EN_PROCESO</c> con <see cref="DisparadorEstado.Auto"/> (al encolar los vehículos); HU #13743 suma
/// <c>EN_PROCESO → COMPLETADO</c> (AUTO) y <c>EN_PROCESO → INCOMPLETO</c> (CIERRE_PANEL); el reproceso lo suma su HU a <see cref="Tabla"/>. Una transición que no está en la tabla lanza.
/// </summary>
public static class TransicionesLote
{
    private static readonly HashSet<TransicionLote> Tabla =
    [
        new(EstadoDiagnostico.Creado, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto),
        // HU #13743: todas las celdas aplicables en COMPLETADO o NA (EstadoGlobalLote) y el cierre del panel con ERROR.
        new(EstadoDiagnostico.EnProceso, EstadoDiagnostico.Completado, DisparadorEstado.Auto),
        new(EstadoDiagnostico.EnProceso, EstadoDiagnostico.Incompleto, DisparadorEstado.CierrePanel),
    ];

    /// <summary>true si el lote puede pasar de <paramref name="de"/> a <paramref name="a"/> con <paramref name="por"/>.</summary>
    public static bool Permitida(EstadoDiagnostico de, EstadoDiagnostico a, DisparadorEstado por) => Tabla.Contains(new(de, a, por));

    /// <summary>La transición validada; lanza <see cref="InvalidOperationException"/> si no está permitida.</summary>
    public static TransicionLote Transicionar(EstadoDiagnostico de, EstadoDiagnostico a, DisparadorEstado por) =>
        Permitida(de, a, por)
            ? new TransicionLote(de, a, por)
            : throw new InvalidOperationException($"El lote no puede pasar de {de} a {a} con disparador {por}.");
}
