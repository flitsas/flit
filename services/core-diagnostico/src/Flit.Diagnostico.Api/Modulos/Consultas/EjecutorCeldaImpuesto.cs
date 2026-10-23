using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Ejecutor de la celda IMPUESTO_DEPARTAMENTAL (HU #13862, F09-C3, F09-C5, F09-C6): una celda por vehículo, consultada con
/// <see cref="IFuenteImpuesto"/> a nombre de la empresa de la celda con la placa definitiva del RUNT (<c>vehiculos.placa</c>) y
/// el departamento derivado de su organismo (<c>vehiculos.departamento_codigo</c>). Solo se toma cuando el RUNT la liberó en
/// PENDIENTE (<see cref="OrquestadorVehiculo"/>: servicio público o departamento no soportado ya quedan N/A ahí). Reglas:
/// <list type="bullet">
/// <item>Sin placa: ERROR <see cref="SinPlaca"/>, definitivo, sin llamar.</item>
/// <item>Departamento nulo o que dejó de estar soportado en la réplica (<c>departamentos_impuesto</c>) desde que se liberó:
/// N/A <c>DEPARTAMENTO_NO_SOPORTADO</c>, sin llamar ni dejar intento.</item>
/// <item>Con respuesta: COMPLETADO con <see cref="ResultadoImpuesto"/> en snake_case, sin proveedor ni transacción (R-20); el
/// intento lleva la placa, el servicio del proveedor y la transacción si la hay.</item>
/// <item>Error de la fuente: su código tal cual; el procesador reintenta los transitorios (D-12) y, agotados, la celda queda en
/// ERROR reprocesable. No bloquea el vehículo (solo RUNT bloquea).</item>
/// </list>
/// Idempotencia por (vehículo, fuente) y reutilización del mismo día (D-10) son del motor. Sin llenado manual (R-9,
/// <see cref="ValidadorResultadoManual.AdmiteManual"/>). Los logs llevan ids y tipo de error; nunca placa, departamento ni la
/// respuesta del proveedor.
/// </summary>
/// <example>
/// <code>
/// services.AddScoped&lt;IEjecutorCelda, EjecutorCeldaImpuesto&gt;();
/// </code>
/// </example>
internal sealed partial class EjecutorCeldaImpuesto(IFuenteImpuesto fuente, TimeProvider reloj, ILogger<EjecutorCeldaImpuesto> log) : IEjecutorCelda
{
    /// <summary><c>tipo_error</c> si el vehículo no tiene placa definitiva tras el RUNT (definitivo).</summary>
    public const string SinPlaca = "SIN_PLACA";

    public Fuente Fuente => Fuente.ImpuestoDepartamental;

    public async Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var celda = contexto.Celda;
        var vehiculo = await contexto.Db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.TenantId == celda.TenantId && v.Id == celda.VehiculoId)
            .Select(v => new { v.Placa, v.DepartamentoCodigo })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var placa = Limpio(vehiculo?.Placa);
        if (placa is null)
        {
            ImpuestoEnError(log, celda.Id, SinPlaca);
            return ResultadoCelda.Error(SinPlaca) with { Intento = new DatosIntento(null, null, null, "El vehículo no tiene placa definitiva tras el RUNT.") };
        }

        var departamento = Limpio(vehiculo!.DepartamentoCodigo);
        if (departamento is null || !await DerivacionDepartamento.SoportadoAsync(contexto.Db, departamento, ct).ConfigureAwait(false))
        {
            ImpuestoNoSoportado(log, celda.Id);
            return ResultadoCelda.Na(MotivoNa.DepartamentoNoSoportado);
        }

        var respuesta = await fuente.ConsultarAsync(celda.TenantId, new SolicitudImpuesto(placa, departamento), ct).ConfigureAwait(false);
        var error = respuesta.Error ?? (respuesta.Resultado is null ? new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "El impuesto respondió sin resultado.") : null);
        if (error is not null)
        {
            ImpuestoEnError(log, celda.Id, error.TipoError);
            return ResultadoCelda.Error(error.TipoError) with
            {
                Intento = new DatosIntento(placa, respuesta.Servicio, respuesta.TransaccionProveedorId, error.Mensaje),
            };
        }

        ImpuestoCompletado(log, celda.Id);
        return ResultadoCelda.Completado(JsonSerializer.Serialize(respuesta.Resultado, EjecutorPorPersonas.Json), reloj.GetUtcNow()) with
        {
            Intento = new DatosIntento(placa, respuesta.Servicio, respuesta.TransaccionProveedorId, null),
        };
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    [LoggerMessage(EventId = 138620, Level = LogLevel.Information, Message = "La celda IMPUESTO_DEPARTAMENTAL {CeldaId} completó")]
    private static partial void ImpuestoCompletado(ILogger logger, Guid celdaId);

    [LoggerMessage(EventId = 138621, Level = LogLevel.Warning, Message = "La celda IMPUESTO_DEPARTAMENTAL {CeldaId} terminó en error ({TipoError})")]
    private static partial void ImpuestoEnError(ILogger logger, Guid celdaId, string tipoError);

    [LoggerMessage(EventId = 138622, Level = LogLevel.Information, Message = "La celda IMPUESTO_DEPARTAMENTAL {CeldaId} queda N/A: el departamento ya no está soportado")]
    private static partial void ImpuestoNoSoportado(ILogger logger, Guid celdaId);
}
