using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Intento auditable de cada ejecución de una celda (HU #13737, F03-H6, F03-C11, R-1): una fila de solo inserción en
/// <c>diagnostico.intentos_consulta</c> con lote, vehículo, celda, identificador consultado (placa o VIN; nunca un documento),
/// fuente, servicio que respondió, inicio y fin, estado SUCCESS|ERROR, tipo y mensaje de error, transacción del proveedor,
/// <c>disparador=AUTO</c> y <c>usuario_id</c> nulo. El procesador lo inserta en la MISMA transacción que cierra la celda
/// (<see cref="EscritorCeldas"/>); si el lease se perdió y la celda no se cierra, lo guarda solo (<see cref="GuardarSoloAsync"/>):
/// la llamada se hizo y queda auditada. Una ejecución que devuelve N/A no llamó a ninguna fuente y no deja intento. Escribe con
/// el contexto de la empresa de la celda (<c>TenantFilter.ValidarEscritura</c>).
/// </summary>
/// <example>
/// <code>
/// var intento = RegistroIntentos.Crear(celdaTomada, inicio, fin, resultado);
/// await escritor.CerrarAsync(celdaTomada, resultado, ct, intento);
/// </code>
/// </example>
internal static class RegistroIntentos
{
    /// <summary>Largo máximo del mensaje de error que se guarda.</summary>
    public const int LargoMensaje = 1000;

    private const int LargoIdentificador = 17;
    private const int LargoTransaccion = 128;

    /// <summary>El intento de la ejecución; null si el resultado es N/A (no hubo llamada).</summary>
    public static IntentoConsulta? Crear(CeldaTomada celda, DateTimeOffset inicio, DateTimeOffset fin, ResultadoCelda resultado)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(resultado);
        if (resultado.Estado is not (EstadoCelda.Completado or EstadoCelda.Error))
            return null;

        var exito = resultado.Estado == EstadoCelda.Completado;
        var datos = resultado.Intento;
        return new IntentoConsulta
        {
            TenantId = celda.TenantId,
            DiagnosticoId = celda.DiagnosticoId,
            VehiculoId = celda.VehiculoId,
            ConsultaId = celda.Id,
            Fuente = celda.Fuente,
            IdentificadorConsultado = Recortar(datos?.Identificador, LargoIdentificador),
            Servicio = datos?.Servicio,
            Disparador = DisparadorIntento.Auto,
            Inicio = inicio,
            Fin = fin < inicio ? inicio : fin,
            Estado = exito ? ResultadoIntento.Success : ResultadoIntento.Error,
            TipoError = exito ? null : resultado.TipoError,
            MensajeError = exito ? null : Recortar(datos?.MensajeError, LargoMensaje),
            TransaccionProveedorId = Recortar(datos?.TransaccionProveedorId, LargoTransaccion),
            UsuarioId = null,
        };
    }

    /// <summary>Guarda el intento solo, cuando la celda no se pudo cerrar (lease perdido).</summary>
    public static async Task GuardarSoloAsync(DiagnosticoDb db, IntentoConsulta intento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(intento);
        db.ChangeTracker.Clear();
        db.Add(intento);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string? Recortar(string? valor, int largo) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Length <= largo ? valor : valor[..largo];
}
