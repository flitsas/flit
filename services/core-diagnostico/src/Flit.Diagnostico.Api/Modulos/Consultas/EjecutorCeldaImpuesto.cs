using System.Text.Json;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Celda IMPUESTO_DEPARTAMENTAL (HU #13862, F09-C3, F09-C5, F09-C6; desde ADR-0075 «construir solicitud / aplicar respuesta»):
/// una celda por vehículo, consultada con un trabajo <c>impuesto</c> a nombre de la empresa de la celda (respuesta aplicada con
/// <see cref="Mapeos.MapeoImpuesto"/>) con la placa definitiva del RUNT (<c>vehiculos.placa</c>) y
/// el departamento derivado de su organismo (<c>vehiculos.departamento_codigo</c>). Solo se toma cuando el RUNT la liberó en
/// PENDIENTE (<see cref="OrquestadorVehiculo"/>: servicio público o departamento no soportado ya quedan N/A ahí). Reglas:
/// <list type="bullet">
/// <item>Sin placa: ERROR <see cref="SinPlaca"/>, definitivo, sin consultar.</item>
/// <item>Departamento nulo o que dejó de estar soportado en la réplica (<c>departamentos_impuesto</c>) desde que se liberó:
/// N/A <c>DEPARTAMENTO_NO_SOPORTADO</c>, sin consultar ni dejar intento (el constructor no arma la consulta).</item>
/// <item>Con respuesta: COMPLETADO con <see cref="ResultadoImpuesto"/> en snake_case, sin proveedor ni transacción (R-20); el
/// intento lleva la placa, el servicio del proveedor y la transacción si la hay.</item>
/// <item>Error de la fuente: su código tal cual; el consumidor de resultados reintenta los transitorios (D-12) y, agotados, la celda queda en
/// ERROR reprocesable. No bloquea el vehículo (solo RUNT bloquea).</item>
/// </list>
/// Idempotencia por (vehículo, fuente) y reutilización del mismo día (D-10) son del publicador. Sin llenado manual (R-9,
/// <see cref="ValidadorResultadoManual.AdmiteManual"/>). Los logs llevan ids y tipo de error; nunca placa, departamento ni la
/// respuesta del proveedor.
/// </summary>
/// <example>
/// <code>
/// services.AddScoped&lt;IEjecutorCelda, EjecutorCeldaImpuesto&gt;();
/// </code>
/// </example>
internal sealed partial class EjecutorCeldaImpuesto(ILogger<EjecutorCeldaImpuesto> log) : IEjecutorCelda
{
    /// <summary><c>tipo_error</c> si el vehículo no tiene placa definitiva tras el RUNT (definitivo).</summary>
    public const string SinPlaca = "SIN_PLACA";

    public Fuente Fuente => Fuente.ImpuestoDepartamental;

    /// <summary>
    /// El constructor no arma la consulta: sin placa, ERROR <see cref="SinPlaca"/> (definitivo); con el departamento nulo o que ya no
    /// está soportado en la réplica, N/A <c>DEPARTAMENTO_NO_SOPORTADO</c> sin intento.
    /// </summary>
    public async Task<ResultadoCelda> SinConsultaAsync(ContextoCelda contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var celda = contexto.Celda;
        var vehiculo = await LeerAsync(contexto, ct).ConfigureAwait(false);
        if (Limpio(vehiculo?.Placa) is null)
        {
            ImpuestoEnError(log, celda.Id, SinPlaca);
            return ResultadoCelda.Error(SinPlaca) with { Intento = new DatosIntento(null, null, null, "El vehículo no tiene placa definitiva tras el RUNT.") };
        }

        ImpuestoNoSoportado(log, celda.Id);
        return ResultadoCelda.Na(MotivoNa.DepartamentoNoSoportado);
    }

    public async Task<ResultadoCelda> AplicarAsync(ContextoCelda contexto, RespuestaConsulta respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(respuesta);
        var celda = contexto.Celda;
        var vehiculo = await LeerAsync(contexto, ct).ConfigureAwait(false);
        var placa = Limpio(vehiculo?.Placa);
        var departamento = Limpio(vehiculo?.DepartamentoCodigo);
        var r = respuesta.Mapear<ConsultarImpuestoVehicularResponse, ResultadoImpuesto>(
            ok => departamento is null ? RespuestaFuente<ResultadoImpuesto>.Fallo(RespuestaConsulta.Invalida) : MapeoImpuesto.Mapear(ok, departamento),
            MapeoImpuesto.Error);
        var error = r.Error ?? (r.Resultado is null ? new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "El impuesto respondió sin resultado.") : null);
        if (error is not null)
        {
            ImpuestoEnError(log, celda.Id, error.TipoError);
            return ResultadoCelda.Error(error.TipoError) with
            {
                Intento = new DatosIntento(placa, r.Servicio, r.TransaccionProveedorId, error.Mensaje),
            };
        }

        ImpuestoCompletado(log, celda.Id);
        return ResultadoCelda.Completado(JsonSerializer.Serialize(r.Resultado, EjecutorPorPersonas.Json), respuesta.Fin) with
        {
            Intento = new DatosIntento(placa, r.Servicio, r.TransaccionProveedorId, null),
        };
    }

    private static Task<VehiculoImpuesto?> LeerAsync(ContextoCelda contexto, CancellationToken ct) =>
        contexto.Db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.TenantId == contexto.Celda.TenantId && v.Id == contexto.Celda.VehiculoId)
            .Select(v => new VehiculoImpuesto(v.Placa, v.DepartamentoCodigo))
            .SingleOrDefaultAsync(ct);

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private sealed record VehiculoImpuesto(string? Placa, string? DepartamentoCodigo);

    [LoggerMessage(EventId = 138620, Level = LogLevel.Information, Message = "La celda IMPUESTO_DEPARTAMENTAL {CeldaId} completó")]
    private static partial void ImpuestoCompletado(ILogger logger, Guid celdaId);

    [LoggerMessage(EventId = 138621, Level = LogLevel.Warning, Message = "La celda IMPUESTO_DEPARTAMENTAL {CeldaId} terminó en error ({TipoError})")]
    private static partial void ImpuestoEnError(ILogger logger, Guid celdaId, string tipoError);

    [LoggerMessage(EventId = 138622, Level = LogLevel.Information, Message = "La celda IMPUESTO_DEPARTAMENTAL {CeldaId} queda N/A: el departamento ya no está soportado")]
    private static partial void ImpuestoNoSoportado(ILogger logger, Guid celdaId);
}
