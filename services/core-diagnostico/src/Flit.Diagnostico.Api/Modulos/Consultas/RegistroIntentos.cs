using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Intento auditable de cada ejecución de una celda (HU #13737, F03-H6, F03-C11, R-1): una fila de solo inserción en
/// <c>diagnostico.intentos_consulta</c> con lote, vehículo, celda, identificador consultado (placa o VIN; nunca un documento),
/// fuente, servicio que respondió, inicio y fin, estado SUCCESS|ERROR, tipo y mensaje de error, transacción del proveedor,
/// <c>disparador=AUTO</c> y <c>usuario_id</c> nulo, o <c>disparador=REPROCESO</c> con el usuario si la ejecución es un reproceso
/// (<see cref="CeldaEnCurso.ReprocesoUsuarioId"/>, HU #13746, R-1). Desde ADR-0075 lo inserta quien cierra la celda en la MISMA
/// transacción (<see cref="Bus.PublicadorCeldas.CerrarAsync"/>), o el reintento en su scope propio (<see cref="Bus.ProgramadorReintentos"/>):
/// la consulta se hizo y queda auditada aunque el mensaje se revierta. Una celda de persona (HU #13792) deja un intento por persona consultada, con
/// <c>persona_id</c> y sin identificador (<see cref="CrearTodos"/>). Una ejecución que devuelve N/A no llamó a ninguna fuente y no deja intento. Escribe con
/// el contexto de la empresa de la celda (<c>TenantFilter.ValidarEscritura</c>). La reutilización del mismo día (HU #13739, D-10)
/// también deja su intento, con <c>desde_cache=true</c> y sin servicio ni transacción: no se llamó a la fuente ni hubo consumo.
/// </summary>
/// <example>
/// <code>
/// var intentos = RegistroIntentos.CrearTodos(CeldaEnCurso.De(celda), respuesta.Inicio, respuesta.Fin, resultado);
/// await publicador.CerrarAsync(celda, resultado, intentos, versionTablero: null, ct);
/// </code>
/// </example>
internal static class RegistroIntentos
{
    /// <summary>Largo máximo del mensaje de error que se guarda.</summary>
    public const int LargoMensaje = 1000;

    private const int LargoIdentificador = 17;
    private const int LargoTransaccion = 128;

    /// <summary>El intento de la ejecución; null si el resultado es N/A (no hubo llamada).</summary>
    public static IntentoConsulta? Crear(CeldaEnCurso celda, DateTimeOffset inicio, DateTimeOffset fin, ResultadoCelda resultado)
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
            Disparador = celda.ReprocesoUsuarioId is null ? DisparadorIntento.Auto : DisparadorIntento.Reproceso,
            Inicio = inicio,
            Fin = fin < inicio ? inicio : fin,
            Estado = exito ? ResultadoIntento.Success : ResultadoIntento.Error,
            TipoError = exito ? null : resultado.TipoError,
            MensajeError = exito ? null : Recortar(datos?.MensajeError, LargoMensaje),
            TransaccionProveedorId = Recortar(datos?.TransaccionProveedorId, LargoTransaccion),
            DesdeCache = exito && resultado.ReutilizadaDeId is not null,
            UsuarioId = celda.ReprocesoUsuarioId,
        };
    }

    /// <summary>
    /// Los intentos de la ejecución (HU #13792): en una celda de persona, uno por cada persona consultada
    /// (<see cref="ResultadoCelda.IntentosPorPersona"/>) con su <c>persona_id</c>, su inicio y fin, y su propio estado, también si la
    /// celda sigue EN_CURSO esperando a otras personas; en las demás, el intento único de <see cref="Crear"/>. Vacío si el resultado
    /// es N/A.
    /// </summary>
    public static IReadOnlyList<IntentoConsulta> CrearTodos(CeldaEnCurso celda, DateTimeOffset inicio, DateTimeOffset fin, ResultadoCelda resultado)
    {
        ArgumentNullException.ThrowIfNull(celda);
        ArgumentNullException.ThrowIfNull(resultado);
        // HU #13792: también la celda de persona que aún espera a otras (EN_CURSO) deja el intento de la persona que respondió.
        if (resultado.IntentosPorPersona is not { } porPersona || resultado.Estado is not (EstadoCelda.Completado or EstadoCelda.Error or EstadoCelda.EnCurso))
            return Crear(celda, inicio, fin, resultado) is { } unico ? [unico] : [];

        var desdeCache = resultado.ReutilizadaDeId is not null;
        return [.. porPersona.Select(p =>
        {
            var exito = p.TipoError is null;
            return new IntentoConsulta
            {
                TenantId = celda.TenantId,
                DiagnosticoId = celda.DiagnosticoId,
                VehiculoId = celda.VehiculoId,
                ConsultaId = celda.Id,
                Fuente = celda.Fuente,
                IdentificadorConsultado = null,
                Servicio = p.Datos.Servicio,
                Disparador = celda.ReprocesoUsuarioId is null ? DisparadorIntento.Auto : DisparadorIntento.Reproceso,
                Inicio = p.Inicio,
                Fin = p.Fin < p.Inicio ? p.Inicio : p.Fin,
                Estado = exito ? ResultadoIntento.Success : ResultadoIntento.Error,
                TipoError = p.TipoError,
                MensajeError = exito ? null : Recortar(p.Datos.MensajeError, LargoMensaje),
                TransaccionProveedorId = Recortar(p.Datos.TransaccionProveedorId, LargoTransaccion),
                DesdeCache = exito && desdeCache,
                UsuarioId = celda.ReprocesoUsuarioId,
                PersonaId = p.PersonaId,
            };
        })];
    }

    private static string? Recortar(string? valor, int largo) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Length <= largo ? valor : valor[..largo];
}
