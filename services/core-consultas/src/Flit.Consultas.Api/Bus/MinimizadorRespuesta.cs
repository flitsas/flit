using System.Collections;
using Flit.Consultas.Grpc.V1;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Minimización de la vuelta (ADR-0075, U1 = b, Q2; corrección C3, Ley 1581): antes de publicar
/// <c>consultas.resultado.diagnostico</c> el <c>*Response</c> se PROYECTA, por rpc, a la lista de campos PERMITIDOS: solo lo que
/// leen los mapeos de core-diagnostico (<c>Modulos/Consultas/Mapeos</c> y <c>Fuentes</c>). Lo que no está en la lista no viaja:
/// nombres, direcciones, contacto, texto libre del proveedor, terceros (demandantes, acreedores, representantes), el histórico,
/// las garantías y limitaciones, los mapas clave-valor y cualquier campo que el proto agregue mañana.
/// <list type="bullet">
/// <item>RUNT por Intempo: resultado, tipo de error y status HTTP; placa, VIN, servicio, clase, organismo, marca, línea, modelo,
/// cilindraje, combustible y pasajeros sentados; tipo y número de documento y propiedad de los propietarios actuales, documentos
/// de sus locatarios y de <c>lessee[]</c>.</item>
/// <item><see cref="ResultadoConsulta"/>: el proveedor y los chequeos (clave y estado) en todos; las claves de <see cref="Campo"/>
/// de su rpc (<c>clave</c> y <c>valor_texto</c>, nunca <c>valor_json</c>); SIMIT además el semáforo y los comparendos (número,
/// fecha, valor, estado, organismo); RUES el estado y la renovación de las matrículas.</item>
/// <item>Impuesto: monto total y vigencias. Avalúos: valor sugerido, fuente principal y de cada fuente su clave, estado, valor y
/// muestras (sin el mensaje).</item>
/// </list>
/// No modifica el mensaje recibido (construye uno nuevo). Un rpc sin regla, o una respuesta que no es la de su rpc, lanza
/// <see cref="ArgumentException"/>: lo que no tiene regla no se publica.
/// </summary>
/// <example>
/// <code>
/// var publicable = MinimizadorRespuesta.Minimizar(RpcConsultas.PorPlaca, respuestaIntempo);
/// </code>
/// </example>
internal static class MinimizadorRespuesta
{
    private static readonly Proyeccion Intempo = new(
        ConsultarVehiculoIntempoResponse.Descriptor,
        [],
        (ConsultarVehiculoIntempoResponse.Descriptor, ["resultado", "vehiculo", "tipo_error", "http_status"]),
        (VehiculoIntempo.Descriptor, ["plate", "vin", "informacion_general", "datos_tecnicos", "propietarios_actuales", "lessee"]),
        (InformacionGeneralIntempo.Descriptor,
            ["no_placa", "no_vin", "tipo_servicio", "clase_vehiculo", "organismo_transito", "marca", "linea", "modelo", "cilidraje", "tipo_combustible"]),
        (DatosTecnicosIntempo.Descriptor, ["pasajeros_sentados"]),
        (PropietarioActualIntempo.Descriptor, ["id_tipo_documento", "tipo_documento", "no_documento", "tipo_propiedad", "detalle_propiedad", "locatarios"]),
        (LocatarioIntempo.Descriptor, ["tdoc", "ndoc"]),
        (PersonaIntempo.Descriptor, ["document_type", "document_number"]));

    private static readonly Proyeccion Conductor = new(
        ConsultarConductorResponse.Descriptor,
        ["person_license_status", "person_license_categories"],
        (ConsultarConductorResponse.Descriptor, ["resultado"]),
        (ResultadoConsulta.Descriptor, ["proveedor", "chequeos", "campos"]),
        (Chequeo.Descriptor, ["clave", "estado"]),
        (Campo.Descriptor, ["clave", "valor_texto"]));

    private static readonly Proyeccion Multas = new(
        ConsultarMultasResponse.Descriptor,
        [],
        (ConsultarMultasResponse.Descriptor, ["resultado"]),
        (ResultadoConsulta.Descriptor, ["proveedor", "semaforo", "chequeos"]),
        (Chequeo.Descriptor, ["clave", "estado", "comparendos"]),
        (Comparendo.Descriptor, ["numero", "fecha", "valor", "estado", "organismo"]));

    private static readonly Proyeccion Rues = new(
        ConsultarRuesResponse.Descriptor,
        ["rues_estado", "rues_fecha_renovacion", "rues_ultimo_ano_renovado"],
        (ConsultarRuesResponse.Descriptor, ["resultado"]),
        (ResultadoConsulta.Descriptor, ["proveedor", "chequeos", "campos", "certificaciones"]),
        (Chequeo.Descriptor, ["clave", "estado"]),
        (Campo.Descriptor, ["clave", "valor_texto"]),
        (Certificaciones.Descriptor, ["matriculas_mercantiles"]),
        (MatriculaMercantil.Descriptor, ["estado", "renovada_en"]),
        (EstadoVigencia.Descriptor, ["valor", "crudo"]),
        (FechaCertificada.Descriptor, ["valor"]));

    private static readonly Proyeccion Impuesto = new(
        ConsultarImpuestoVehicularResponse.Descriptor,
        [],
        (ConsultarImpuestoVehicularResponse.Descriptor, ["resultado", "monto_total", "vigencias"]),
        (ResultadoConsulta.Descriptor, ["proveedor", "chequeos"]),
        (Chequeo.Descriptor, ["clave", "estado"]),
        (VigenciaImpuesto.Descriptor, ["anio", "valor", "pagada"]));

    private static readonly Proyeccion Avaluos = new(
        ConsultarAvaluosResponse.Descriptor,
        [],
        (ConsultarAvaluosResponse.Descriptor, ["valor_sugerido", "fuente_principal", "avaluos"]),
        (Avaluo.Descriptor, ["fuente", "estado", "valor", "muestras"]));

    private static readonly Dictionary<string, Proyeccion> PorRpc = new(StringComparer.Ordinal)
    {
        [RpcConsultas.PorPlaca] = Intempo,
        [RpcConsultas.PorVin] = Intempo,
        [RpcConsultas.Conductor] = Conductor,
        [RpcConsultas.Multas] = Multas,
        [RpcConsultas.Rues] = Rues,
        [RpcConsultas.Impuesto] = Impuesto,
        [RpcConsultas.Avaluos] = Avaluos,
    };

    /// <summary>La respuesta de <paramref name="rpc"/> lista para publicar: un mensaje nuevo con solo los campos permitidos.</summary>
    public static IMessage Minimizar(string rpc, IMessage respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        if (rpc is null || !PorRpc.TryGetValue(rpc, out var proyeccion) || respuesta.Descriptor != proyeccion.Respuesta)
            throw new ArgumentException($"Sin regla de minimización para {rpc} con {respuesta.Descriptor.Name}.", nameof(rpc));

        return proyeccion.Proyectar(respuesta);
    }

    /// <summary>El <c>*Response</c> vacío de <paramref name="rpc"/> (el tipo que su regla acepta).</summary>
    public static IMessage RespuestaVacia(string rpc) => rpc switch
    {
        RpcConsultas.PorPlaca or RpcConsultas.PorVin => new ConsultarVehiculoIntempoResponse(),
        RpcConsultas.Conductor => new ConsultarConductorResponse(),
        RpcConsultas.Multas => new ConsultarMultasResponse(),
        RpcConsultas.Rues => new ConsultarRuesResponse(),
        RpcConsultas.Impuesto => new ConsultarImpuestoVehicularResponse(),
        RpcConsultas.Avaluos => new ConsultarAvaluosResponse(),
        _ => throw new ArgumentException($"Rpc desconocido: {rpc}.", nameof(rpc)),
    };

    /// <summary>
    /// Lista permitida de un rpc: por cada mensaje alcanzable, los campos que se copian; y las claves de <see cref="Campo"/> que
    /// salen de <c>ResultadoConsulta.campos</c>. Al construirse comprueba que cada nombre exista en el proto, que ningún mapa esté
    /// permitido y que cada mensaje permitido tenga su regla, así que <see cref="Proyectar"/> nunca encuentra un hueco.
    /// </summary>
    private sealed class Proyeccion
    {
        private readonly Dictionary<MessageDescriptor, FieldDescriptor[]> _campos = [];
        private readonly HashSet<string> _claves;

        public Proyeccion(MessageDescriptor respuesta, string[] claves, params (MessageDescriptor Mensaje, string[] Campos)[] reglas)
        {
            Respuesta = respuesta;
            _claves = new HashSet<string>(claves, StringComparer.Ordinal);
            foreach (var (mensaje, campos) in reglas)
            {
                _campos[mensaje] = [.. campos.Select(nombre => mensaje.FindFieldByName(nombre)
                    ?? throw new InvalidOperationException($"{mensaje.Name} no tiene el campo {nombre}."))];
            }

            foreach (var (mensaje, campos) in _campos)
            {
                foreach (var campo in campos.Where(c => c.FieldType == FieldType.Message))
                {
                    if (campo.IsMap)
                        throw new InvalidOperationException($"{mensaje.Name}.{campo.Name} es un mapa clave-valor libre: no se publica.");
                    if (!_campos.ContainsKey(campo.MessageType))
                        throw new InvalidOperationException($"{mensaje.Name}.{campo.Name} está permitido pero {campo.MessageType.Name} no tiene regla.");
                }
            }

            if (!_campos.ContainsKey(respuesta))
                throw new InvalidOperationException($"{respuesta.Name} no tiene regla.");
        }

        public MessageDescriptor Respuesta { get; }

        public IMessage Proyectar(IMessage origen)
        {
            var destino = (IMessage)Activator.CreateInstance(origen.Descriptor.ClrType)!;
            foreach (var campo in _campos[origen.Descriptor])
            {
                var acceso = campo.Accessor;
                if (campo.IsRepeated)
                {
                    var lista = (IList)acceso.GetValue(destino);
                    foreach (var elemento in (IList)acceso.GetValue(origen))
                    {
                        if (elemento is Campo { Clave: var clave } && !_claves.Contains(clave))
                            continue;
                        lista.Add(elemento is IMessage hijo ? Proyectar(hijo) : elemento);
                    }
                }
                else if (campo.FieldType == FieldType.Message)
                {
                    if (acceso.HasValue(origen))
                        acceso.SetValue(destino, Proyectar((IMessage)acceso.GetValue(origen)));
                }
                else if (!campo.HasPresence || acceso.HasValue(origen))
                {
                    acceso.SetValue(destino, acceso.GetValue(origen));
                }
            }

            return destino;
        }
    }
}
