using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Dictamen;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// Arma las lecturas de las celdas de un vehículo para <see cref="LectorHechosDictamen"/> (brief C5, HU #14576): extraído sin cambio
/// de comportamiento de <see cref="EmisorDictamen"/>, que lo usa, para que el saneamiento lea los hechos de un vehículo en cualquier
/// modo (Simple incluido) sin pasar por el emisor. Descifra con el mismo protector. No lee la base: recibe las celdas ya leídas en el
/// contexto de la empresa de quien llama.
/// </summary>
/// <example>
/// <code>
/// var lecturas = new LecturasVehiculo(protector).Armar(celdasDelVehiculo);
/// var hechos = LectorHechosDictamen.Leer(LecturasVehiculo.Pedido("TRASPASO_STANDARD", ahora, lecturas));
/// </code>
/// </example>
internal sealed class LecturasVehiculo(IProtectorRespuestaFuente protector)
{
    /// <summary>El payload solo guarda cuántos locatarios había. El lector pregunta si el número viene con texto.</summary>
    private const string DocumentoSoloParaElLector = "1";

    private static readonly JsonSerializerOptions Json = new();

    /// <summary>
    /// Pedido del lector para cualquier modo: <paramref name="tipoTramiteCodigo"/> no se valida contra el catálogo (lo hace quien
    /// evalúa); el día es el de Colombia en <paramref name="ahora"/>.
    /// </summary>
    public static PedidoLectura Pedido(string tipoTramiteCodigo, DateTimeOffset ahora, LecturasParaDictamen lecturas)
    {
        ArgumentNullException.ThrowIfNull(tipoTramiteCodigo);
        ArgumentNullException.ThrowIfNull(lecturas);
        var hoy = DateOnly.FromDateTime(ahora.ToOffset(VigenciaDelDia.OffsetColombia).DateTime);
        return new PedidoLectura(
            tipoTramiteCodigo,
            hoy,
            lecturas.RuntDetalle,
            lecturas.RuntNormalizado,
            lecturas.SimitVehiculo,
            lecturas.RuntPropietario,
            lecturas.SimitPropietario,
            lecturas.SimitLocatario,
            lecturas.RuntLocatario,
            lecturas.Impuesto,
            lecturas.AdvertenciasImpuesto);
    }

    /// <summary>Las lecturas de las celdas de UN vehículo (todas sus fuentes); una fuente sin celda queda en null.</summary>
    public LecturasParaDictamen Armar(IReadOnlyList<CeldaConsulta> celdas)
    {
        ArgumentNullException.ThrowIfNull(celdas);
        CeldaConsulta? De(Fuente fuente) => celdas.FirstOrDefault(c => c.Fuente == fuente);
        var runt = De(Fuente.Runt);
        var impuesto = De(Fuente.ImpuestoDepartamental);
        return new LecturasParaDictamen(
            Detalle(runt),
            Normalizado(runt),
            SimitVehiculo(De(Fuente.SimitVehiculo)),
            PersonasRunt(De(Fuente.RuntPropietario)),
            PersonasSimit(De(Fuente.SimitPropietario)),
            PersonasSimit(De(Fuente.SimitLocatario)),
            PersonasRunt(De(Fuente.RuntLocatario)),
            Impuesto(impuesto),
            Advertencias(impuesto));
    }

    private static IReadOnlyList<AdvertenciaImpuestoLeida>? Advertencias(CeldaConsulta? celda)
    {
        if (celda is not { Estado: EstadoCelda.Completado } || string.IsNullOrWhiteSpace(celda.Payload))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(celda.Payload);
            return ResultadoImpuestoDetalle.LeerAdvertencias(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private LecturaCelda<DetalleRuntReporte>? Detalle(CeldaConsulta? celda) =>
        celda is null ? null : new LecturaCelda<DetalleRuntReporte>(celda.Estado, LectorDetalleRunt.Leer(protector, celda.RespuestaFuenteCifrada));

    private static LecturaCelda<VehiculoRunt>? Normalizado(CeldaConsulta? celda)
    {
        if (celda is null)
            return null;
        VehiculoRunt? vehiculo = null;
        if (celda.Estado == EstadoCelda.Completado && ProyectorDetalleCelda.ResultadoVehiculo(Fuente.Runt, celda.Payload) is ResultadoRuntDetalle detalle)
        {
            var propietarios = (detalle.Propietarios ?? []).Select(p => new PropietarioRunt(
                p.TipoDocumento ?? string.Empty,
                string.Empty,
                CodigosDeEnum.TryLeer<TipoPropiedad>(p.TipoPropiedad, out var tipo) ? tipo : TipoPropiedad.Desconocido,
                [])).ToArray();
            var locatarios = detalle.Locatarios > 0 ? new[] { new LocatarioRunt(string.Empty, DocumentoSoloParaElLector) } : Array.Empty<LocatarioRunt>();
            vehiculo = new VehiculoRunt(detalle.Placa, detalle.Vin, detalle.Servicio, detalle.Clase, detalle.Organismo, propietarios, locatarios, string.Empty, null);
        }

        return new LecturaCelda<VehiculoRunt>(celda.Estado, vehiculo);
    }

    private LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? SimitVehiculo(CeldaConsulta? celda)
    {
        if (celda is null)
            return null;
        var resultado = ProyectorDetalleCelda.ResultadoVehiculo(Fuente.SimitVehiculo, celda.Payload) as ResultadoSimitDetalle;
        if (resultado is not null
            && RespuestaFuenteCelda.TryLeerVehiculo(protector, celda.RespuestaFuenteCifrada, out var cruda)
            && cruda is { } elemento
            && ProyectorDetalleCelda.RespuestaProveedor(elemento, out _) is { } cuerpo)
            resultado = SimitVisible.Completar(resultado, cuerpo);
        return new LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>(celda.Estado, resultado is null ? null : [resultado]);
    }

    private LecturaCelda<IReadOnlyList<ResultadoRuntPersonaDetalle>>? PersonasRunt(CeldaConsulta? celda) =>
        Personas<ResultadoRuntPersonaDetalle>(celda, EjecutorCeldaRuntPersona.ConsultaRuntPersona, static (detalle, crudo) => LicenciasConductor.Completar(detalle, crudo));

    private LecturaCelda<IReadOnlyList<ResultadoSimitDetalle>>? PersonasSimit(CeldaConsulta? celda) =>
        Personas<ResultadoSimitDetalle>(celda, EjecutorCeldaSimitPersona.ConsultaSimit, static (detalle, crudo) => SimitVisible.Completar(detalle, crudo));

    private LecturaCelda<IReadOnlyList<T>>? Personas<T>(CeldaConsulta? celda, string consulta, Func<T, JsonElement, T> completar)
        where T : class
    {
        if (celda is null)
            return null;
        var payload = EjecutorPorPersonas.Leer(celda.Payload);
        if (payload is null)
            return new LecturaCelda<IReadOnlyList<T>>(celda.Estado, null);
        if (!RespuestaFuenteCelda.TryLeerPersonas(protector, celda.RespuestaFuenteCifrada, out var respuestas))
            respuestas = new Dictionary<int, JsonElement>();

        var lista = new List<T>();
        foreach (var persona in payload.Personas)
        {
            if (persona.Consulta != consulta || persona.Estado != EjecutorPorPersonas.EstadoOk || persona.Resultado is not { } json)
                continue;
            T? detalle;
            try
            {
                detalle = json.Deserialize<T>(Json);
            }
            catch (JsonException)
            {
                detalle = null;
            }

            if (detalle is null)
                continue;
            if (respuestas.TryGetValue(persona.Orden, out var cruda) && ProyectorDetalleCelda.RespuestaProveedor(cruda, out _) is { } cuerpo)
                detalle = completar(detalle, cuerpo);
            lista.Add(detalle);
        }

        return new LecturaCelda<IReadOnlyList<T>>(celda.Estado, lista);
    }

    private static LecturaCelda<ResultadoImpuestoDetalle>? Impuesto(CeldaConsulta? celda) =>
        celda is null
            ? null
            : new LecturaCelda<ResultadoImpuestoDetalle>(
                celda.Estado, ProyectorDetalleCelda.ResultadoVehiculo(Fuente.ImpuestoDepartamental, celda.Payload) as ResultadoImpuestoDetalle);
}
