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
/// <c>runt_consultado_en</c>, <c>datos_consolidados</c> y los documentos cifrados (<see cref="DocumentosVehiculo"/>), y en
/// <c>personas_vehiculo</c> todos los propietarios y locatarios (HU #13790). La celda
/// guarda el resultado normalizado SIN documentos ni nombres (<c>payload</c> es <c>@pii:medium</c>) y
/// <c>dato_obtenido_en</c>. Un error de la fuente devuelve ERROR con el código de <see cref="TipoErrorFuente"/>; el procesador
/// reintenta los transitorios (HU #13737). Cada salida lleva los <see cref="DatosIntento"/> del intento auditable: placa o VIN
/// consultado, servicio, transacción del proveedor y mensaje de error, nunca documentos. Lee y escribe solo con <see cref="ContextoCelda.Db"/>, que tiene la empresa
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
            return ResultadoCelda.Error() with { Intento = new DatosIntento(null, null, null, "El vehículo de la celda no existe.") };
        }

        var identificador = new IdentificadorVehiculo(vehiculo.TipoIdentificador, vehiculo.IdentificadorNorm);
        var respuesta = await fuente.ConsultarVehiculoAsync(celda.TenantId, identificador, ct).ConfigureAwait(false);
        if (respuesta.Vehiculo is not { } runt)
        {
            var tipo = CodigosDeEnum.Codigo(respuesta.Error ?? TipoErrorFuente.RespuestaInvalida);
            RuntEnError(log, celda.Id, tipo);
            return ResultadoCelda.Error(tipo) with { Intento = new DatosIntento(identificador.Valor, null, null, respuesta.Mensaje) };
        }

        var servicio = Servicio(runt.ServicioQueResolvio);

        var placa = runt.Placa ?? (identificador.EsVin ? null : identificador.Valor);
        var vin = runt.Vin ?? (identificador.EsVin ? identificador.Valor : null);
        if (!Cabe(placa, LargoPlaca) || !Cabe(vin, LargoVin) || !Cabe(runt.Servicio, LargoServicio) || !Cabe(runt.Organismo, LargoOrganismo)
            || !DocumentosVehiculo.TiposValidos(runt))
        {
            var tipo = CodigosDeEnum.Codigo(TipoErrorFuente.RespuestaInvalida);
            RuntEnError(log, celda.Id, tipo);
            return ResultadoCelda.Error(tipo) with
            {
                Intento = new DatosIntento(identificador.Valor, servicio, runt.IdTransaccion, "RUNT: un valor de la respuesta no cabe en su columna."),
            };
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
            // HU #13790: todos los propietarios y locatarios, cifrados, en la misma transacción de cierre.
            await DocumentosVehiculo.RegistrarPersonasAsync(db, guardado, DocumentosVehiculo.Personas(guardado, runt, protector, ahora), token)
                .ConfigureAwait(false);
        }) with
        { Intento = new DatosIntento(identificador.Valor, servicio, runt.IdTransaccion, null) };
    }

    /// <summary>
    /// Proveedor de <c>intentos_consulta.servicio</c> para el nombre que informa la fuente: <c>INTEMPO</c> o
    /// <c>INTEMPO_SIMULADO</c> son <see cref="ServicioConsulta.Intempo"/>; un nombre desconocido es <see cref="ServicioConsulta.Otro"/>.
    /// </summary>
    internal static ServicioConsulta Servicio(string? servicioQueResolvio)
    {
        var raiz = servicioQueResolvio?.Trim().Split('_', 2)[0] ?? string.Empty;
        return CodigosDeEnum.TryLeer<ServicioConsulta>(raiz.ToUpperInvariant(), out var servicio) ? servicio : ServicioConsulta.Otro;
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

    /// <summary>
    /// <c>datos_consolidados</c> con la clave <c>vehiculo</c> del RUNT (con los datos técnicos del bien, HU #13795); conserva lo
    /// que hayan dejado otras fuentes.
    /// </summary>
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
            // HU #13795: datos técnicos del bien (los lee el avalúo comercial); nulos si el RUNT no los trae.
            ["marca"] = runt.Marca,
            ["linea"] = runt.Linea,
            ["modelo"] = runt.Modelo,
            ["cilindraje"] = runt.Cilindraje,
            ["combustible"] = runt.Combustible,
            ["pasajeros"] = runt.Pasajeros,
        };
        return raiz.ToJsonString();
    }

    [LoggerMessage(EventId = 137300, Level = LogLevel.Information, Message = "La celda RUNT {CeldaId} completó (servicio {Servicio})")]
    private static partial void RuntCompletado(ILogger logger, Guid celdaId, string servicio);

    [LoggerMessage(EventId = 137301, Level = LogLevel.Warning, Message = "La celda RUNT {CeldaId} terminó en error ({TipoError}); el procesador decide si la reintenta o bloquea las demás celdas")]
    private static partial void RuntEnError(ILogger logger, Guid celdaId, string tipoError);
}
