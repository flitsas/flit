using System.Globalization;
using Flit.Consultas.Grpc.V1;
using Google.Protobuf;
using TipoErrorIntempo = Flit.Consultas.Grpc.V1.TipoErrorFuente;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Regla única del producto para el RUNT por Intempo (HU #13877, F10-H5; antes HU #13725 y #13795): convierte la respuesta
/// tipada de core-consultas (<see cref="ConsultarVehiculoIntempoResponse"/>, <c>runt_intempo.proto</c>) en
/// <see cref="ResultadoRunt"/>. La fuente real (F10-H6) llama a <see cref="Mapear(ConsultarVehiculoIntempoResponse, string)"/>
/// con el mensaje recibido; la simulada pasa su fixture proto-JSON por <see cref="MapearJson"/>, que lo lee con el
/// <see cref="JsonParser"/> estricto y llama a la misma regla (G5=a).
/// <list type="bullet">
/// <item><c>ENCONTRADO</c>: placa, VIN, servicio, clase, organismo, marca, línea, modelo, <c>cilidraje</c> (errata de la
/// especificación), combustible y <c>datosTecnicos.pasajerosSentados</c>; propietarios con el tipo de
/// <c>idTipoDocumento</c> (C/N…) o, si falta, del texto <c>tipoDocumento</c>; locatarios como unión sin repetir de los de cada
/// propietario y de <c>lessee[]</c> (tipo en texto), por documento normalizado. <c>IdTransaccion</c> es nulo: Intempo no
/// informa transacción.</item>
/// <item><c>NO_ENCONTRADO</c>: <see cref="TipoErrorFuente.EstadoNoOk"/> definitivo con <see cref="MensajeNoEncontrado"/> (G3=a).</item>
/// <item><c>ERROR</c>: <c>TIMEOUT</c>→Timeout, <c>RED</c>→Red, <c>HTTP_NO_EXITOSO</c> ≥ 500 o sin status→Http5xx (transitorio) y
/// el resto→EstadoNoOk, <c>AUTENTICACION</c>→Autenticacion, <c>RESPUESTA_INVALIDA</c>→RespuestaInvalida, otro→EstadoNoOk.</item>
/// <item>JSON inválido, clave desconocida (la forma vieja con <c>codigoResultado</c>), resultado sin especificar o
/// <c>ENCONTRADO</c> sin vehículo: <see cref="TipoErrorFuente.RespuestaInvalida"/>.</item>
/// </list>
/// Datos personales (R-20, Ley 1581): solo lee tipo y número de documento. Nombres, direcciones, teléfonos, correos,
/// <c>owner[]</c> y el histórico nunca salen de aquí. Los mensajes de error son fijos y técnicos: nunca copia
/// <c>mensaje_error</c> ni <c>errores</c>, que pueden traer un recorte del cuerpo. Puro y sin logs.
/// </summary>
/// <example>
/// <code>
/// var resultado = MapperIntempo.Mapear(await cliente.ConsultarPorPlacaAsync(new() { Placa = "TST003" }), "INTEMPO");
/// // resultado.Vehiculo.Propietarios[0] → ("NIT", "999990003", Leasing, [("CC", "9999900013")])
/// </code>
/// </example>
public static class MapperIntempo
{
    /// <summary>Mensaje del vehículo que el RUNT no registra (G3=a).</summary>
    public const string MensajeNoEncontrado = "El RUNT no registra el vehículo consultado.";

    private const string Prefijo = "RUNT (Intempo): ";

    private static readonly JsonParser Lector = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(false));

    /// <summary>Compatibilidad: la firma anterior lee proto-JSON por <see cref="MapearJson"/>.</summary>
    public static ResultadoRunt Mapear(string json, string servicioQueResolvio) => MapearJson(json, servicioQueResolvio);

    /// <summary>
    /// Lee <paramref name="protoJson"/> como <see cref="ConsultarVehiculoIntempoResponse"/> (claves de §5 por el
    /// <c>json_name</c> del contrato, sin admitir claves desconocidas) y lo mapea con la misma regla de la fuente real.
    /// </summary>
    public static ResultadoRunt MapearJson(string protoJson, string servicioQueResolvio)
    {
        ArgumentNullException.ThrowIfNull(protoJson);
        ConsultarVehiculoIntempoResponse respuesta;
        try
        {
            respuesta = Lector.Parse<ConsultarVehiculoIntempoResponse>(protoJson);
        }
        catch (InvalidJsonException)
        {
            return Invalida("la respuesta no es JSON válido");
        }
        catch (InvalidProtocolBufferException)
        {
            return Invalida("la respuesta no tiene la forma del contrato");
        }

        return Mapear(respuesta, servicioQueResolvio);
    }

    /// <summary>Ruta única: la respuesta tipada de <c>RuntIntempoService</c> a <see cref="ResultadoRunt"/>.</summary>
    public static ResultadoRunt Mapear(ConsultarVehiculoIntempoResponse respuesta, string servicioQueResolvio)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentNullException.ThrowIfNull(servicioQueResolvio);
        return respuesta.Resultado switch
        {
            ResultadoRuntIntempo.Encontrado when respuesta.Vehiculo is { } vehiculo => ResultadoRunt.Exito(Vehiculo(vehiculo, servicioQueResolvio)),
            ResultadoRuntIntempo.Encontrado => Invalida("la respuesta encontrada no trae el vehículo"),
            ResultadoRuntIntempo.NoEncontrado => ResultadoRunt.Fallo(TipoErrorFuente.EstadoNoOk, MensajeNoEncontrado),
            ResultadoRuntIntempo.Error => Error(respuesta),
            _ => Invalida("la respuesta no informa el resultado"),
        };
    }

    /// <summary>Código del tipo de documento del producto (CC, CE, NIT, PAS o TI) para el código de Intempo (C, N, …).</summary>
    public static string TipoDocumento(string codigo)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        return codigo.Trim().ToUpperInvariant() switch
        {
            "C" or "CC" => "CC",
            "N" or "NIT" => "NIT",
            "E" or "CE" => "CE",
            "P" or "PA" or "PAS" => "PAS",
            "T" or "TI" => "TI",
            var otro => otro,
        };
    }

    /// <summary>
    /// Código del producto para el tipo de documento en texto de Intempo (§6.1: «Cédula de ciudadanía», «Cédula Ciudadanía»,
    /// «NIT», «Cédula de extranjería», «Pasaporte», «Tarjeta de identidad»), sin distinguir tildes, mayúsculas ni el «de»; un
    /// código (C, N…) también vale. Lo desconocido vuelve tal cual, recortado.
    /// </summary>
    public static string TipoDocumentoDeTexto(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var clave = string.Join(' ', SinTildes(texto).ToUpperInvariant()
            .Split([' ', '\t', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "DE"));
        return clave switch
        {
            "CEDULA CIUDADANIA" => "CC",
            "CEDULA EXTRANJERIA" => "CE",
            "PASAPORTE" => "PAS",
            "TARJETA IDENTIDAD" => "TI",
            _ => TipoDocumento(texto),
        };
    }

    private static VehiculoRunt Vehiculo(VehiculoIntempo v, string servicio)
    {
        var general = v.InformacionGeneral ?? new InformacionGeneralIntempo();
        var propietarios = v.PropietariosActuales.Select(Propietario).OfType<PropietarioRunt>().ToList();
        var locatarios = v.Lessee
            .Select(l => Locatario(l.DocumentType, l.DocumentNumber))
            .Concat(propietarios.SelectMany(p => p.Locatarios))
            .OfType<LocatarioRunt>()
            .Distinct()
            .ToList();

        return new VehiculoRunt(
            Placa: Texto(v.Plate) ?? Texto(general.NoPlaca),
            Vin: Texto(v.Vin) ?? Texto(general.NoVin),
            Servicio: Texto(general.TipoServicio),
            Clase: Texto(general.ClaseVehiculo),
            Organismo: Texto(general.OrganismoTransito),
            Propietarios: propietarios,
            Locatarios: locatarios,
            ServicioQueResolvio: servicio,
            IdTransaccion: null)
        {
            // HU #13795: datos técnicos del bien para el avalúo comercial.
            Marca = Texto(general.Marca),
            Linea = Texto(general.Linea),
            Modelo = Entero(general.Modelo),
            Cilindraje = Entero(general.Cilidraje),
            Combustible = Texto(general.TipoCombustible),
            Pasajeros = Entero(v.DatosTecnicos?.PasajerosSentados),
        };
    }

    private static PropietarioRunt? Propietario(PropietarioActualIntempo p)
    {
        var numero = Numero(p.NoDocumento);
        var tipo = Texto(p.IdTipoDocumento) is { } id ? TipoDocumento(id) : Texto(p.TipoDocumento) is { } texto ? TipoDocumentoDeTexto(texto) : null;
        if (tipo is null || numero is null)
            return null;
        var locatarios = p.Locatarios
            .Select(l => Locatario(l.Tdoc, l.Ndoc))
            .OfType<LocatarioRunt>()
            .Distinct()
            .ToList();
        return new PropietarioRunt(tipo, numero, Propiedad(p), locatarios);
    }

    private static LocatarioRunt? Locatario(string tipo, string numero)
    {
        var t = Texto(tipo);
        var n = Numero(numero);
        return t is null || n is null ? null : new LocatarioRunt(TipoDocumentoDeTexto(t), n);
    }

    private static TipoPropiedad Propiedad(PropietarioActualIntempo p) => Texto(p.DetallePropiedad)?.ToUpperInvariant() switch
    {
        "PROPIO" => TipoPropiedad.Propio,
        "LEASING" => TipoPropiedad.Leasing,
        _ => p.TipoPropiedad switch
        {
            1 => TipoPropiedad.Propio,
            3 => TipoPropiedad.Leasing,
            _ => TipoPropiedad.Desconocido,
        },
    };

    private static ResultadoRunt Error(ConsultarVehiculoIntempoResponse r)
    {
        int? status = r.HasHttpStatus ? r.HttpStatus : null;
        return r.TipoError switch
        {
            TipoErrorIntempo.Timeout => ResultadoRunt.Fallo(TipoErrorFuente.Timeout, Prefijo + "el proveedor no respondió a tiempo."),
            TipoErrorIntempo.Red => ResultadoRunt.Fallo(TipoErrorFuente.Red, Prefijo + "falla de red hacia el proveedor."),
            TipoErrorIntempo.HttpNoExitoso when status is null or >= 500 =>
                ResultadoRunt.Fallo(TipoErrorFuente.Http5xx, Prefijo + $"el proveedor respondió con error HTTP{Status(status)}."),
            TipoErrorIntempo.HttpNoExitoso =>
                ResultadoRunt.Fallo(TipoErrorFuente.EstadoNoOk, Prefijo + $"el proveedor rechazó la consulta (HTTP{Status(status)})."),
            TipoErrorIntempo.Autenticacion => ResultadoRunt.Fallo(TipoErrorFuente.Autenticacion, Prefijo + "el proveedor rechazó la autenticación."),
            TipoErrorIntempo.RespuestaInvalida => Invalida("la respuesta del proveedor no tiene la forma esperada"),
            _ => ResultadoRunt.Fallo(TipoErrorFuente.EstadoNoOk, Prefijo + "el proveedor devolvió un error."),
        };
    }

    private static string Status(int? status) => status is { } s ? " " + s.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    /// <summary>Número de documento normalizado para comparar: sin espacios en ningún lugar; null si vacío.</summary>
    private static string? Numero(string? valor) =>
        Texto(valor) is { } t ? string.Concat(t.Where(c => !char.IsWhiteSpace(c))) : null;

    /// <summary>Entero positivo de un texto (admite «1598.0»); null si no viene o no es un número.</summary>
    private static int? Entero(string? valor)
    {
        var texto = Texto(valor);
        if (texto is null || !decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var numero))
            return null;
        return numero is > 0 and <= int.MaxValue ? (int)numero : null;
    }

    /// <summary>
    /// Vocales sin tilde ni diéresis. Tabla propia y no <c>string.Normalize</c>: con globalización invariante la normalización no
    /// descompone los caracteres no ASCII.
    /// </summary>
    private static string SinTildes(string texto) => string.Create(texto.Length, texto, static (destino, origen) =>
    {
        for (var i = 0; i < origen.Length; i++)
        {
            destino[i] = origen[i] switch
            {
                'á' or 'à' or 'ä' => 'a',
                'é' or 'è' or 'ë' => 'e',
                'í' or 'ì' or 'ï' => 'i',
                'ó' or 'ò' or 'ö' => 'o',
                'ú' or 'ù' or 'ü' => 'u',
                'Á' or 'À' or 'Ä' => 'A',
                'É' or 'È' or 'Ë' => 'E',
                'Í' or 'Ì' or 'Ï' => 'I',
                'Ó' or 'Ò' or 'Ö' => 'O',
                'Ú' or 'Ù' or 'Ü' => 'U',
                var c => c,
            };
        }
    });

    private static ResultadoRunt Invalida(string motivo) => ResultadoRunt.Fallo(TipoErrorFuente.RespuestaInvalida, $"{Prefijo}{motivo}.");
}
