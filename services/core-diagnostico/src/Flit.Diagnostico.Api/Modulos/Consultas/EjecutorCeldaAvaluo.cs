using System.Globalization;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Ejecutor de la celda AVALUO (HU #13795, F05-C1, F05-C7, F05-C8): una celda por vehículo, consultada con
/// <see cref="IFuenteAvaluo"/> a nombre de la empresa de la celda con la placa y el VIN que dejó el RUNT
/// (<c>vehiculos.placa</c>/<c>vin</c>) y los datos técnicos del bien de <c>datos_consolidados.vehiculo</c> (modelo, cilindraje,
/// combustible y pasajeros; los que falten van vacíos). Solo se toma cuando el RUNT la liberó (bloqueo de #13730,
/// <see cref="OrquestadorVehiculo"/>). Con valor completa con <see cref="ResultadoAvaluo"/> en snake_case (valor sugerido,
/// fuente principal y valor y fecha de cada fuente). Sin valor en ninguna fuente completa con <c>sin_valor_comercial=true</c>:
/// no es ERROR, así que no se reprocesa ni se llena a mano (decisión Q4 = b). Un error de la fuente devuelve su código tal cual:
/// el procesador reintenta los transitorios (#13737) y, agotados, la celda queda en ERROR reprocesable sin bloquear el
/// vehículo (solo RUNT bloquea). La reutilización del mismo día de la misma compañía (#13739, por placa y fuente) es la del
/// motor. Sin placa ni VIN queda en ERROR <see cref="SinIdentificador"/>, definitivo, sin llamar. Los logs llevan ids y tipo
/// de error; el avalúo no tiene datos de personas.
/// </summary>
/// <example>
/// <code>
/// services.AddScoped&lt;IEjecutorCelda, EjecutorCeldaAvaluo&gt;();
/// </code>
/// </example>
internal sealed partial class EjecutorCeldaAvaluo(IFuenteAvaluo fuente, TimeProvider reloj, ILogger<EjecutorCeldaAvaluo> log) : IEjecutorCelda
{
    /// <summary><c>tipo_error</c> si el vehículo no tiene placa ni VIN tras el RUNT (definitivo).</summary>
    public const string SinIdentificador = "SIN_IDENTIFICADOR";

    /// <summary>Colombia no tiene horario de verano: UTC-5 todo el año (fecha de cada fuente).</summary>
    private static readonly TimeSpan HoraColombia = TimeSpan.FromHours(-5);

    public Fuente Fuente => Fuente.Avaluo;

    public async Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var celda = contexto.Celda;
        var vehiculo = await contexto.Db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.TenantId == celda.TenantId && v.Id == celda.VehiculoId)
            .Select(v => new { v.Placa, v.Vin, v.DatosConsolidados })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var placa = Limpio(vehiculo?.Placa);
        var vin = Limpio(vehiculo?.Vin);
        if (placa is null && vin is null)
        {
            AvaluoEnError(log, celda.Id, SinIdentificador);
            return ResultadoCelda.Error(SinIdentificador) with { Intento = new DatosIntento(null, null, null, "El vehículo no tiene placa ni VIN tras el RUNT.") };
        }

        var solicitud = Solicitud(placa, vin, vehiculo!.DatosConsolidados);
        var respuesta = await fuente.ConsultarAsync(celda.TenantId, solicitud, ct).ConfigureAwait(false);
        var consultado = placa ?? vin;
        var error = respuesta.Error ?? (respuesta.Resultado is null ? new ErrorConsulta(ErroresFuentePersona.RespuestaInvalida, "El avalúo respondió sin resultado.") : null);
        if (error is not null)
        {
            AvaluoEnError(log, celda.Id, error.TipoError);
            return ResultadoCelda.Error(error.TipoError) with
            {
                Intento = new DatosIntento(consultado, respuesta.Servicio, respuesta.TransaccionProveedorId, error.Mensaje),
            };
        }

        var ahora = reloj.GetUtcNow();
        var resultado = respuesta.Resultado!.ConFecha(DateOnly.FromDateTime(ahora.ToOffset(HoraColombia).DateTime));
        AvaluoCompletado(log, celda.Id, resultado.SinValorComercial);
        return ResultadoCelda.Completado(JsonSerializer.Serialize(resultado, EjecutorPorPersonas.Json), ahora) with
        {
            Intento = new DatosIntento(consultado, respuesta.Servicio, respuesta.TransaccionProveedorId, null),
        };
    }

    /// <summary>Placa y VIN con los datos técnicos de <c>datos_consolidados.vehiculo</c> (los que falten, nulos).</summary>
    internal static SolicitudAvaluo Solicitud(string? placa, string? vin, string? datosConsolidados)
    {
        if (string.IsNullOrWhiteSpace(datosConsolidados))
            return new SolicitudAvaluo(placa, vin, null, null, null, null);

        using var documento = JsonDocument.Parse(datosConsolidados);
        if (documento.RootElement.ValueKind != JsonValueKind.Object
            || !documento.RootElement.TryGetProperty("vehiculo", out var v) || v.ValueKind != JsonValueKind.Object)
            return new SolicitudAvaluo(placa, vin, null, null, null, null);

        return new SolicitudAvaluo(placa, vin, Entero(v, "modelo"), Entero(v, "cilindraje"), Limpio(Texto(v, "combustible")), Entero(v, "pasajeros"));
    }

    private static string? Texto(JsonElement objeto, string clave) =>
        objeto.TryGetProperty(clave, out var valor) ? valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString(),
            JsonValueKind.Number => valor.GetRawText(),
            _ => null,
        } : null;

    private static int? Entero(JsonElement objeto, string clave) =>
        int.TryParse(Texto(objeto, clave), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    [LoggerMessage(EventId = 137950, Level = LogLevel.Information, Message = "La celda AVALUO {CeldaId} completó (sin valor comercial: {SinValor})")]
    private static partial void AvaluoCompletado(ILogger logger, Guid celdaId, bool sinValor);

    [LoggerMessage(EventId = 137951, Level = LogLevel.Warning, Message = "La celda AVALUO {CeldaId} terminó en error ({TipoError})")]
    private static partial void AvaluoEnError(ILogger logger, Guid celdaId, string tipoError);
}
