using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Ejecutor de la celda SIMIT_VEHICULO (HU #13793, F05-C1, F05-C6, F05-C9, F05-C10): una celda por vehículo, consultada por la
/// placa definitiva que dejó el RUNT (<c>vehiculos.placa</c>) con <see cref="IFuenteSimit"/> a nombre de la empresa de la celda.
/// Solo se toma cuando el RUNT la liberó (bloqueo de #13730, <see cref="OrquestadorVehiculo"/>). Sin placa (vehículo consultado
/// por VIN sin placa asignada) queda en ERROR <see cref="SinPlaca"/>, definitivo, sin llamar ni consumir. El payload es
/// <see cref="ResultadoSimit"/> en snake_case (sin datos del infractor). Un error de la fuente devuelve su código tal cual
/// (<see cref="ErrorConsulta.TipoError"/>): el procesador reintenta los transitorios (#13737); el reproceso (#13746) y la
/// reutilización del mismo día de la misma compañía (#13739, por placa y fuente) son los del motor, sin código propio. El
/// intento auditable lleva la placa, el servicio y la transacción; los logs, ids y tipo de error.
/// </summary>
/// <example>
/// <code>
/// services.AddScoped&lt;IEjecutorCelda, EjecutorCeldaSimitVehiculo&gt;();
/// </code>
/// </example>
internal sealed partial class EjecutorCeldaSimitVehiculo(IFuenteSimit fuente, TimeProvider reloj, ILogger<EjecutorCeldaSimitVehiculo> log) : IEjecutorCelda
{
    /// <summary><c>tipo_error</c> si el vehículo no tiene placa tras el RUNT (definitivo).</summary>
    public const string SinPlaca = "SIN_PLACA";

    public Fuente Fuente => Fuente.SimitVehiculo;

    public async Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var celda = contexto.Celda;
        var placa = await contexto.Db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.TenantId == celda.TenantId && v.Id == celda.VehiculoId)
            .Select(v => v.Placa)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(placa))
        {
            SimitEnError(log, celda.Id, SinPlaca);
            return ResultadoCelda.Error(SinPlaca) with { Intento = new DatosIntento(null, null, null, "El vehículo no tiene placa tras el RUNT.") };
        }

        var respuesta = await fuente.ConsultarAsync(celda.TenantId, SolicitudSimit.PorPlaca(placa), ct).ConfigureAwait(false);
        var consultada = placa.Trim().ToUpperInvariant();
        var error = respuesta.Error ?? (respuesta.Resultado is null ? new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "SIMIT respondió sin resultado.") : null);
        if (error is not null)
        {
            SimitEnError(log, celda.Id, error.TipoError);
            return ResultadoCelda.Error(error.TipoError) with
            {
                Intento = new DatosIntento(consultada, respuesta.Servicio, respuesta.TransaccionProveedorId, error.Mensaje),
            };
        }

        SimitCompletado(log, celda.Id, respuesta.Resultado!.CantidadComparendos);
        return ResultadoCelda.Completado(JsonSerializer.Serialize(respuesta.Resultado, EjecutorPorPersonas.Json), reloj.GetUtcNow()) with
        {
            Intento = new DatosIntento(consultada, respuesta.Servicio, respuesta.TransaccionProveedorId, null),
        };
    }

    [LoggerMessage(EventId = 137930, Level = LogLevel.Information, Message = "La celda SIMIT_VEHICULO {CeldaId} completó con {Comparendos} comparendos")]
    private static partial void SimitCompletado(ILogger logger, Guid celdaId, int comparendos);

    [LoggerMessage(EventId = 137931, Level = LogLevel.Warning, Message = "La celda SIMIT_VEHICULO {CeldaId} terminó en error ({TipoError})")]
    private static partial void SimitEnError(ILogger logger, Guid celdaId, string tipoError);
}
