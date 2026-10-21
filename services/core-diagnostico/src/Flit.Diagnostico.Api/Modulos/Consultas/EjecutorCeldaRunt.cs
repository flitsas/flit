using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Ejecutor de la celda RUNT (HU #13730, F03-C4/F03-C5, MOD03 paso 1): consulta <see cref="IFuenteRunt"/> con el identificador
/// normalizado del vehículo (por placa, sin documento, Intempo; por VIN, la cadena normal) y, con el cierre de la celda y en su
/// misma transacción, guarda en <c>vehiculos</c> la placa definitiva, el VIN, el servicio, el organismo,
/// <c>runt_consultado_en</c>, <c>datos_consolidados</c> y los documentos cifrados (<see cref="DocumentosVehiculo"/>). La celda
/// guarda el resultado normalizado SIN documentos ni nombres (<c>payload</c> es <c>@pii:medium</c>) y
/// <c>dato_obtenido_en</c>. Un error de la fuente deja la celda en ERROR con el código de <see cref="TipoErrorFuente"/> (los
/// reintentos de transitorios son de F03-H6). Lee y escribe solo con <see cref="ContextoCelda.Db"/>, que tiene la empresa
/// de la celda. Los logs llevan ids, servicio y tipo de error; nunca documentos.
/// </summary>
/// <example>
/// <code>
/// services.AddScoped&lt;IEjecutorCelda, EjecutorCeldaRunt&gt;();
/// </code>
/// </example>
internal sealed partial class EjecutorCeldaRunt(
    IFuenteRunt fuente, IProtectorDocumentoPersona protector, TimeProvider reloj, ILogger<EjecutorCeldaRunt> log) : IEjecutorCelda
{
    private const int LargoPlaca = 10;
    private const int LargoVin = 17;
    private const int LargoServicio = 32;
    private const int LargoOrganismo = 200;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public Fuente Fuente => Fuente.Runt;

    public async Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var celda = contexto.Celda;
        var vehiculo = await contexto.Db.Set<VehiculoDiagnostico>().AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == celda.VehiculoId, ct).ConfigureAwait(false);
        if (vehiculo is null)
        {
            RuntEnError(log, celda.Id, "VEHICULO_NO_ENCONTRADO");
            return ResultadoCelda.Error();
        }

        var identificador = new IdentificadorVehiculo(vehiculo.TipoIdentificador, vehiculo.IdentificadorNorm);
        var respuesta = await fuente.ConsultarVehiculoAsync(celda.TenantId, identificador, ct).ConfigureAwait(false);
        if (respuesta.Vehiculo is not { } runt)
        {
            var tipo = CodigosDeEnum.Codigo(respuesta.Error ?? TipoErrorFuente.RespuestaInvalida);
            RuntEnError(log, celda.Id, tipo);
            return ResultadoCelda.Error(tipo);
        }

        var placa = runt.Placa ?? (identificador.EsVin ? null : identificador.Valor);
        var vin = runt.Vin ?? (identificador.EsVin ? identificador.Valor : null);
        if (!Cabe(placa, LargoPlaca) || !Cabe(vin, LargoVin) || !Cabe(runt.Servicio, LargoServicio) || !Cabe(runt.Organismo, LargoOrganismo)
            || !DocumentosVehiculo.TiposValidos(runt))
        {
            var tipo = CodigosDeEnum.Codigo(TipoErrorFuente.RespuestaInvalida);
            RuntEnError(log, celda.Id, tipo);
            return ResultadoCelda.Error(tipo);
        }

        var ahora = reloj.GetUtcNow();
        RuntCompletado(log, celda.Id, runt.ServicioQueResolvio);
        return ResultadoCelda.Completado(Payload(runt, placa, vin), ahora, async (db, _, token) =>
        {
            var guardado = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.Id == celda.VehiculoId, token).ConfigureAwait(false);
            guardado.Placa = placa;
            guardado.Vin = vin;
            guardado.Servicio = runt.Servicio;
            guardado.OrganismoNombre = runt.Organismo;
            guardado.RuntConsultadoEn = ahora;
            guardado.DatosConsolidados = Consolidar(guardado.DatosConsolidados, runt, placa, vin);
            DocumentosVehiculo.Aplicar(guardado, runt, protector);
        });
    }

    private static bool Cabe(string? valor, int largo) => valor is null || valor.Length <= largo;

    /// <summary>Resultado normalizado de la celda: datos del vehículo y tipos de documento, sin números ni nombres.</summary>
    private static string Payload(VehiculoRunt runt, string? placa, string? vin) => JsonSerializer.Serialize(new
    {
        placa,
        vin,
        servicio = runt.Servicio,
        clase = runt.Clase,
        organismo = runt.Organismo,
        servicioQueResolvio = runt.ServicioQueResolvio,
        idTransaccion = runt.IdTransaccion,
        propietarios = runt.Propietarios.Select(p => new { tipoDocumento = p.TipoDocumento, tipoPropiedad = CodigosDeEnum.Codigo(p.TipoPropiedad) }),
        locatarios = runt.Locatarios.Count,
    }, Json);

    /// <summary><c>datos_consolidados</c> con la clave <c>vehiculo</c> del RUNT; conserva lo que hayan dejado otras fuentes.</summary>
    private static string Consolidar(string actual, VehiculoRunt runt, string? placa, string? vin)
    {
        var raiz = (string.IsNullOrWhiteSpace(actual) ? null : JsonNode.Parse(actual) as JsonObject) ?? [];
        raiz["vehiculo"] = new JsonObject
        {
            ["placa"] = placa,
            ["vin"] = vin,
            ["servicio"] = runt.Servicio,
            ["clase"] = runt.Clase,
            ["organismo"] = runt.Organismo,
            ["tipo_propiedad"] = runt.Propietarios.Count > 0 ? CodigosDeEnum.Codigo(runt.Propietarios[0].TipoPropiedad) : null,
        };
        return raiz.ToJsonString();
    }

    [LoggerMessage(EventId = 137300, Level = LogLevel.Information, Message = "La celda RUNT {CeldaId} completó (servicio {Servicio})")]
    private static partial void RuntCompletado(ILogger logger, Guid celdaId, string servicio);

    [LoggerMessage(EventId = 137301, Level = LogLevel.Warning, Message = "La celda RUNT {CeldaId} queda en ERROR ({TipoError}); las demás celdas del vehículo quedan bloqueadas")]
    private static partial void RuntEnError(ILogger logger, Guid celdaId, string tipoError);
}
