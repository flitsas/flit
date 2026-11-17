using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Celda SIMIT_VEHICULO (HU #13793, F05-C1, F05-C6, F05-C9, F05-C10; desde ADR-0075 «construir solicitud / aplicar respuesta»):
/// una celda por vehículo, consultada por la placa definitiva que dejó el RUNT (<c>vehiculos.placa</c>) con un trabajo
/// <c>simit_vehiculo</c> a nombre de la empresa de la celda; la respuesta se aplica con <see cref="Mapeos.MapeoSimit"/>.
/// Solo se toma cuando el RUNT la liberó (bloqueo de #13730, <see cref="OrquestadorVehiculo"/>). Sin placa (vehículo consultado
/// por VIN sin placa asignada) queda en ERROR <see cref="SinPlaca"/>, definitivo, sin llamar ni consumir. El payload es
/// <see cref="ResultadoSimit"/> en snake_case (sin datos del infractor). Un error de la fuente devuelve su código tal cual
/// (<see cref="ErrorConsulta.TipoError"/>): el consumidor de resultados reintenta los transitorios (#13737); el reproceso (#13746) y la
/// reutilización del mismo día de la misma compañía (#13739, por placa y fuente) son los del motor, sin código propio. El
/// intento auditable lleva la placa, el servicio y la transacción; los logs, ids y tipo de error.
/// </summary>
/// <example>
/// <code>
/// services.AddScoped&lt;IEjecutorCelda, EjecutorCeldaSimitVehiculo&gt;();
/// </code>
/// </example>
internal sealed partial class EjecutorCeldaSimitVehiculo(ILogger<EjecutorCeldaSimitVehiculo> log) : IEjecutorCelda
{
    /// <summary><c>tipo_error</c> si el vehículo no tiene placa tras el RUNT (definitivo).</summary>
    public const string SinPlaca = "SIN_PLACA";

    public Fuente Fuente => Fuente.SimitVehiculo;

    /// <summary>Sin placa tras el RUNT el constructor no arma la consulta: ERROR <see cref="SinPlaca"/>, definitivo, sin consultar.</summary>
    public Task<ResultadoCelda> SinConsultaAsync(ContextoCelda contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        SimitEnError(log, contexto.Celda.Id, SinPlaca);
        return Task.FromResult(ResultadoCelda.Error(SinPlaca) with { Intento = new DatosIntento(null, null, null, "El vehículo no tiene placa tras el RUNT.") });
    }

    public async Task<ResultadoCelda> AplicarAsync(ContextoCelda contexto, RespuestaConsulta respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(respuesta);
        var celda = contexto.Celda;
        var placa = await contexto.Db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.TenantId == celda.TenantId && v.Id == celda.VehiculoId)
            .Select(v => v.Placa)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var consultada = string.IsNullOrWhiteSpace(placa) ? null : placa.Trim().ToUpperInvariant();
        var r = respuesta.Mapear<ConsultarMultasResponse, ResultadoSimit>(MapeoSimit.Mapear, MapeoSimit.Error);
        var error = r.Error ?? (r.Resultado is null ? new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "SIMIT respondió sin resultado.") : null);
        if (error is not null)
        {
            SimitEnError(log, celda.Id, error.TipoError);
            return ResultadoCelda.Error(error.TipoError) with
            {
                Intento = new DatosIntento(consultada, r.Servicio, r.TransaccionProveedorId, error.Mensaje),
            };
        }

        SimitCompletado(log, celda.Id, r.Resultado!.CantidadComparendos);
        return ResultadoCelda.Completado(JsonSerializer.Serialize(r.Resultado, EjecutorPorPersonas.Json), respuesta.Fin) with
        {
            Intento = new DatosIntento(consultada, r.Servicio, r.TransaccionProveedorId, null),
        };
    }

    [LoggerMessage(EventId = 137930, Level = LogLevel.Information, Message = "La celda SIMIT_VEHICULO {CeldaId} completó con {Comparendos} comparendos")]
    private static partial void SimitCompletado(ILogger logger, Guid celdaId, int comparendos);

    [LoggerMessage(EventId = 137931, Level = LogLevel.Warning, Message = "La celda SIMIT_VEHICULO {CeldaId} terminó en error ({TipoError})")]
    private static partial void SimitEnError(ILogger logger, Guid celdaId, string tipoError);
}
