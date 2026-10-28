using System.Globalization;
using System.Text;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Mapeos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Domain.Consultas;
using Google.Protobuf;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Detalle completo del RUNT del vehículo para el PDF (diseño v2 del reporte; ajuste 2026-10-27): sale de la respuesta COMPLETA de
/// la fuente guardada en la celda RUNT (<see cref="CeldaConsulta.RespuestaFuenteCifrada"/>, HU #14087 AC8), leída con
/// <see cref="LectorDetalleRunt"/>. Es parte del tipo cerrado <see cref="ContenidoReporteVehiculo"/> (R-20): sin proveedor,
/// transacción, auditoría (<c>serialVersionUID</c>, <c>url</c>, <c>informacionConsistente</c>) ni nombres de personas. Un acreedor
/// persona natural sale como <see cref="DetalleRuntGarantia.PersonaNatural"/> y con el documento enmascarado.
/// <para>
/// Textos: recortados; vacío ⇒ <c>null</c> (el PDF dice «No registra»). Fechas: texto tal cual llega de la fuente
/// (<c>dd/MM/yyyy</c> según el contrato <c>runt_intempo.proto</c>), sin reinterpretarlas.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var detalle = contenido.DetalleRunt;            // null ⇒ «No registra»
/// foreach (var soat in detalle?.Soat ?? []) { /* soat.Estado, soat.NumeroPoliza, soat.FechaVencimiento… */ }
/// </code>
/// </example>
public sealed record DetalleRuntReporte(
    DetalleRuntVehiculo Vehiculo,
    DetalleRuntGeneral General,
    DetalleRuntTecnico DatosTecnicos,
    IReadOnlyList<DetalleRuntSoat> Soat,
    IReadOnlyList<DetalleRuntRtm> Rtm,
    IReadOnlyList<DetalleRuntSolicitud> Solicitudes,
    IReadOnlyList<DetalleRuntGarantia> GarantiasAFavor,
    IReadOnlyList<DetalleRuntDato> Accidentalidad,
    IReadOnlyList<DetalleRuntPoliza> PolizasResponsabilidadCivil)
{
    /// <summary>Claves de la huella del detalle (prefijo <c>runt/</c>): cambiar cualquier dato cambia la huella.</summary>
    internal void Agregar(Dictionary<string, string?> claves)
    {
        claves["runt/vehiculo"] = Unir(Vehiculo.Placa, Vehiculo.LicenciaTransito, Vehiculo.TipoServicio, Vehiculo.Clasificacion, Vehiculo.Clase,
            Vehiculo.Modelo, Vehiculo.FechaMatricula, Vehiculo.OrganismoTransito);
        claves["runt/general"] = Unir(General.Marca, General.Linea, General.Cilindraje, General.Color, General.TipoCombustible, General.TipoCarroceria,
            General.Puertas, General.ClasicoAntiguo, General.Repotenciado, General.Vin, General.NumeroMotor, General.NumeroChasis, General.RegrabadoVin,
            General.RegrabadoMotor, General.RegrabadoChasis, General.RegrabadoSerie, General.EstadoVehiculo);
        claves["runt/tecnico"] = Unir(DatosTecnicos.NumeroEjes, DatosTecnicos.CapacidadCarga, DatosTecnicos.PasajerosSentados, DatosTecnicos.PesoBrutoVehicular);
        Lista(claves, "soat", Soat, s => Unir(s.Estado, s.NumeroPoliza, s.FechaVigencia, s.FechaExpedicion, s.FechaVencimiento, s.Entidad));
        Lista(claves, "rtm", Rtm, r => Unir(r.Estado, r.NumeroCertificado, r.FechaVigencia, r.FechaExpedicion, r.Cda));
        Lista(claves, "solicitud", Solicitudes, s => Unir(s.Estado, s.Entidad, s.NumeroSolicitud, s.Fecha, s.TramitesRealizados));
        Lista(claves, "garantia", GarantiasAFavor, g => Unir(g.Acreedor, g.TipoDocumento, g.NumeroDocumento, g.Confecamaras, g.FechaInscripcion));
        Lista(claves, "accidentalidad", Accidentalidad, d => Unir(d.Etiqueta, d.Valor));
        Lista(claves, "poliza_rc", PolizasResponsabilidadCivil, p => Unir(p.Estado, p.NumeroPoliza, p.Tipo, p.FechaExpedicion, p.FechaVencimiento, p.Entidad));
    }

    private static void Lista<T>(Dictionary<string, string?> claves, string nombre, IReadOnlyList<T> items, Func<T, string> texto)
    {
        for (var i = 0; i < items.Count; i++)
            claves[$"runt/{nombre}/{(i + 1).ToString(CultureInfo.InvariantCulture)}"] = texto(items[i]);
    }

    private static string Unir(params string?[] valores) => string.Join('|', valores.Select(v => v ?? string.Empty));
}

/// <summary>Información del vehículo: placa, licencia de tránsito, tipo de servicio, clasificación, clase, modelo, fecha de matrícula y organismo.</summary>
public sealed record DetalleRuntVehiculo(
    string? Placa,
    string? LicenciaTransito,
    string? TipoServicio,
    string? Clasificacion,
    string? Clase,
    string? Modelo,
    string? FechaMatricula,
    string? OrganismoTransito);

/// <summary>
/// Información general. Los regrabados, clásico antiguo y repotenciado vienen como SI/NO (texto de la fuente). <see cref="NumeroChasis"/>
/// es el n.º de chasis del RUNT; la fuente no trae un «n.º de serie» aparte.
/// </summary>
public sealed record DetalleRuntGeneral(
    string? Marca,
    string? Linea,
    string? Cilindraje,
    string? Color,
    string? TipoCombustible,
    string? TipoCarroceria,
    string? Puertas,
    string? ClasicoAntiguo,
    string? Repotenciado,
    string? Vin,
    string? NumeroMotor,
    string? NumeroChasis,
    string? RegrabadoVin,
    string? RegrabadoMotor,
    string? RegrabadoChasis,
    string? RegrabadoSerie,
    string? EstadoVehiculo);

/// <summary>Datos teóricos (técnicos): n.º de ejes, capacidad de carga, pasajeros sentados y peso bruto vehicular.</summary>
public sealed record DetalleRuntTecnico(string? NumeroEjes, string? CapacidadCarga, string? PasajerosSentados, string? PesoBrutoVehicular);

/// <summary>Una póliza SOAT: estado (VIGENTE, NO VIGENTE, NO REQUIERE), n.º de póliza, inicio de vigencia, expedición, vencimiento y aseguradora.</summary>
public sealed record DetalleRuntSoat(
    string? Estado, string? NumeroPoliza, string? FechaVigencia, string? FechaExpedicion, string? FechaVencimiento, string? Entidad);

/// <summary>Una revisión técnico-mecánica: estado (VIGENTE / NO VIGENTE, del SI/NO de la fuente), n.º de certificado, vigencia, expedición y CDA.</summary>
public sealed record DetalleRuntRtm(string? Estado, string? NumeroCertificado, string? FechaVigencia, string? FechaExpedicion, string? Cda);

/// <summary>
/// Una solicitud ante el RUNT. La fuente (Intempo, especificación §5.4) la manda siempre vacía y el contrato no tiene sus campos:
/// la lista sale vacía hasta que el contrato los traiga.
/// </summary>
public sealed record DetalleRuntSolicitud(string? Estado, string? Entidad, string? NumeroSolicitud, string? Fecha, string? TramitesRealizados);

/// <summary>
/// Una garantía a favor de un acreedor. Persona jurídica (tipo NIT con número en el rango DIAN de personas jurídicas, ver
/// <c>LectorDetalleRunt.EsNitPersonaJuridica</c>): razón social y NIT completos. Cualquier otro caso (otro tipo, sin tipo o un NIT de
/// persona natural, p. ej. la cédula usada como NIT): <see cref="Acreedor"/> = <see cref="PersonaNatural"/> y
/// <see cref="NumeroDocumento"/> enmascarado (<see cref="EnmascaradorDocumento"/>, «****» + 4 últimos, el mismo de las personas del
/// reporte). <see cref="TipoDocumento"/> en código del producto (NIT, CC, CE, PAS, TI).
/// </summary>
public sealed record DetalleRuntGarantia(string? Acreedor, string? TipoDocumento, string? NumeroDocumento, string? Confecamaras, string? FechaInscripcion)
{
    /// <summary>Lo que sale en lugar del nombre de un acreedor persona natural.</summary>
    public const string PersonaNatural = "Persona natural";
}

/// <summary>
/// Un par etiqueta/valor de la accidentalidad. La fuente no le da forma fija, así que solo salen las claves del catálogo cerrado de
/// <c>LectorDetalleRunt</c> con su etiqueta fija en español.
/// </summary>
public sealed record DetalleRuntDato(string Etiqueta, string Valor);

/// <summary>
/// Una póliza de responsabilidad civil. El contrato de la fuente solo trae un campo de auditoría (<c>serialVersionUID</c>, que no se
/// proyecta): la lista sale vacía hasta que el contrato traiga sus campos.
/// </summary>
public sealed record DetalleRuntPoliza(string? Estado, string? NumeroPoliza, string? Tipo, string? FechaExpedicion, string? FechaVencimiento, string? Entidad);

/// <summary>
/// Lee el <see cref="DetalleRuntReporte"/> de la respuesta cifrada de la celda RUNT: la descifra con
/// <see cref="RespuestaFuenteCelda.TryLeerVehiculo"/> y la interpreta como <see cref="ConsultarVehiculoIntempoResponse"/> con el
/// lector de proto-JSON tolerante (nombres del .proto o lowerCamelCase, campos nuevos ignorados). Sin respuesta, cifrado ilegible,
/// JSON que no es del contrato, resultado distinto de ENCONTRADO o cualquier fallo no previsto (salvo la cancelación) ⇒ <c>null</c>,
/// nunca una excepción. Puro y sin logs: ni el mensaje ni la excepción se registran, porque pueden citar la respuesta en claro (trae
/// datos personales).
/// <para>
/// Accidentalidad por lista de PERMITIDOS (revisión de seguridad 2026-10-27): la clave del origen se normaliza (sin tildes,
/// minúsculas invariantes, sin <c>_</c>, <c>-</c>, espacios ni puntos) y se compara exacta contra <see cref="CatalogoAccidentalidad"/>;
/// lo que no está (incluido el texto libre: descripción, observación, detalle) no sale. Etiqueta fija por entrada y orden del catálogo.
/// </para>
/// </summary>
internal static class LectorDetalleRunt
{
    private static readonly JsonParser Lector = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    /// <summary>
    /// Catálogo cerrado de la accidentalidad, en el orden de salida: etiqueta fija en español y claves normalizadas aceptadas (ver
    /// <see cref="Normalizar"/>). Agregar un dato = agregar una entrada aquí; nunca derivar la etiqueta de la clave del origen.
    /// </summary>
    internal static readonly IReadOnlyList<(string Etiqueta, string[] Claves)> CatalogoAccidentalidad =
    [
        ("Fecha", ["fecha", "fechaaccidente"]),
        ("Gravedad", ["gravedad"]),
        ("Clase de accidente", ["claseaccidente", "clase"]),
        ("Tipo de accidente", ["tipoaccidente", "tipo"]),
        ("Con heridos", ["conheridos"]),
        ("Con muertos", ["conmuertos", "confallecidos"]),
        ("Municipio", ["municipio"]),
        ("Departamento", ["departamento"]),
        ("Organismo de tránsito", ["organismo", "organismotransito", "secretaria", "secretariatransito"]),
    ];

    /// <summary>Equivalencias explícitas de vocales con tilde, ñ y ü: no dependen de los datos de ICU (globalización invariante).</summary>
    private static readonly Dictionary<char, char> SinTilde = new()
    {
        ['á'] = 'a', ['é'] = 'e', ['í'] = 'i', ['ó'] = 'o', ['ú'] = 'u', ['à'] = 'a', ['è'] = 'e', ['ì'] = 'i', ['ò'] = 'o', ['ù'] = 'u',
        ['ä'] = 'a', ['ë'] = 'e', ['ï'] = 'i', ['ö'] = 'o', ['ü'] = 'u', ['ñ'] = 'n', ['Á'] = 'a', ['É'] = 'e', ['Í'] = 'i', ['Ó'] = 'o',
        ['Ú'] = 'u', ['À'] = 'a', ['È'] = 'e', ['Ì'] = 'i', ['Ò'] = 'o', ['Ù'] = 'u', ['Ä'] = 'a', ['Ë'] = 'e', ['Ï'] = 'i', ['Ö'] = 'o',
        ['Ü'] = 'u', ['Ñ'] = 'n',
    };

    /// <summary>El detalle de la respuesta cifrada; null si no hay o no se puede leer (la cancelación sí se propaga).</summary>
    public static DetalleRuntReporte? Leer(IProtectorRespuestaFuente protector, byte[]? cifrada)
    {
        ArgumentNullException.ThrowIfNull(protector);
        if (cifrada is null)
            return null;
        try
        {
            if (!RespuestaFuenteCelda.TryLeerVehiculo(protector, cifrada, out var json) || json is not { } respuesta)
                return null;
            return Desde(Lector.Parse<ConsultarVehiculoIntempoResponse>(respuesta.GetRawText()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin registrar ni el mensaje ni la excepción: pueden citar el JSON descifrado (datos personales).
            return null;
        }
    }

    /// <summary>El detalle de la respuesta tipada; null si no es ENCONTRADO con vehículo.</summary>
    public static DetalleRuntReporte? Desde(ConsultarVehiculoIntempoResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        if (respuesta.Resultado != ResultadoRuntIntempo.Encontrado || respuesta.Vehiculo is not { } v)
            return null;
        var g = v.InformacionGeneral ?? new InformacionGeneralIntempo();
        var t = v.DatosTecnicos ?? new DatosTecnicosIntempo();

        return new DetalleRuntReporte(
            new DetalleRuntVehiculo(
                T(v.Plate) ?? T(g.NoPlaca), T(g.NoLicenciaTransito), T(g.TipoServicio), T(g.Clasificacion), T(g.ClaseVehiculo), T(g.Modelo),
                T(g.FechaMatricula), T(g.OrganismoTransito)),
            new DetalleRuntGeneral(
                T(g.Marca), T(g.Linea), T(g.Cilidraje), T(g.Color), T(g.TipoCombustible), T(g.TipoCarroceria),
                g.HasPuertas ? T(g.Puertas) : null, T(g.ClasicoAntiguo), T(g.Repotenciado), T(v.Vin) ?? T(g.NoVin),
                g.HasNoMotor ? T(g.NoMotor) : null, T(g.NoChasis), T(g.EsRegrabadoVin), T(g.EsRegrabadoMotor), T(g.EsRegrabadoChasis),
                T(g.EsRegrabadoSerie), T(g.EstadoDelVehiculo)),
            new DetalleRuntTecnico(T(t.NoEjes), T(t.CapacidadCarga), T(t.PasajerosSentados), T(t.PesoBrutoVehicular)),
            [.. v.Soat
                .Select(s => new DetalleRuntSoat(T(s.Estado), T(s.NoPoliza), T(s.FechaVigencia), T(s.FechaExpedicion), T(s.FechaVencimiento), T(s.EntidadExpideSoat)))
                .Where(s => s != new DetalleRuntSoat(null, null, null, null, null, null))],
            [.. v.TecnoMecanica
                .Select(r => new DetalleRuntRtm(EstadoRtm(r.Vigente), T(r.NroCertificado), T(r.FechaVigente), T(r.FechaExpedicion), T(r.CdaExpide)))
                .Where(r => r != new DetalleRuntRtm(null, null, null, null, null))],
            [],
            [.. v.GarantiasFavorDe.Select(Garantia).Where(x => x is not null).Select(x => x!)],
            [.. v.AccidentRate.SelectMany(Accidente).Distinct()],
            []);
    }

    private static DetalleRuntGarantia? Garantia(GarantiaFavorDeIntempo g)
    {
        var tipo = T(g.TipoDocumentoAcreedor) is { } texto ? MapperIntempo.TipoDocumentoDeTexto(texto) : null;
        var numero = T(g.NumeroDocumentoAcreedor);
        var acreedor = T(g.Acreedor);
        if (tipo is null && numero is null && acreedor is null && T(g.Confecamaras) is null && T(g.FechaInscripcion) is null)
            return null;
        var juridica = string.Equals(tipo, "NIT", StringComparison.Ordinal) && EsNitPersonaJuridica(numero);
        return new DetalleRuntGarantia(
            juridica ? acreedor : DetalleRuntGarantia.PersonaNatural,
            tipo,
            juridica ? numero : numero is null ? null : EnmascaradorDocumento.Enmascarar(numero),
            T(g.Confecamaras),
            T(g.FechaInscripcion));
    }

    private static string? EstadoRtm(string vigente) => T(vigente)?.ToUpperInvariant() switch
    {
        null => null,
        "SI" or "SÍ" or "S" or "TRUE" or "VIGENTE" => "VIGENTE",
        "NO" or "N" or "FALSE" or "NO VIGENTE" => "NO VIGENTE",
        var otro => otro,
    };

    /// <summary>
    /// NIT de persona jurídica según el rango que asigna la DIAN: quitando todo lo no numérico y el dígito de verificación (cuando
    /// viene separado por guion, o cuando quedan 10 dígitos), el número tiene 9 dígitos y empieza por 8 o 9. Una persona natural
    /// usa como NIT su cédula (nunca en ese rango), así que cualquier otro número se trata como persona natural.
    /// </summary>
    internal static bool EsNitPersonaJuridica(string? numero)
    {
        if (numero is null)
            return false;
        var guion = numero.LastIndexOf('-');
        var cuerpo = guion > 0 ? numero[..guion] : numero;
        var digitos = new string([.. cuerpo.Where(char.IsAsciiDigit)]);
        if (guion <= 0 && digitos.Length == 10)
            digitos = digitos[..9];
        return digitos.Length == 9 && digitos[0] is '8' or '9';
    }

    /// <summary>Los datos permitidos de un accidente, en el orden del catálogo: uno por etiqueta (la primera clave con valor).</summary>
    private static IEnumerable<DetalleRuntDato> Accidente(AccidenteIntempo accidente)
    {
        var campos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (clave, valor) in accidente.Campos)
        {
            if (T(valor) is { } texto)
                campos.TryAdd(Normalizar(clave), texto);
        }

        foreach (var (etiqueta, claves) in CatalogoAccidentalidad)
        {
            foreach (var clave in claves)
            {
                if (campos.TryGetValue(clave, out var valor))
                {
                    yield return new DetalleRuntDato(etiqueta, Booleano(valor));
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Clave comparable: sin tildes (tabla explícita y, si la plataforma la soporta, descomposición Unicode quitando las marcas),
    /// minúsculas invariantes y sin <c>_</c>, <c>-</c>, espacios ni puntos: <c>fechaAccidente</c>, <c>fecha_accidente</c> y
    /// <c>FECHA ACCIDENTE</c> dan <c>fechaaccidente</c>; <c>Dirección</c> da <c>direccion</c>.
    /// </summary>
    internal static string Normalizar(string clave)
    {
        var descompuesta = clave;
        try
        {
            descompuesta = clave.Normalize(NormalizationForm.FormD);
        }
        catch (PlatformNotSupportedException)
        {
            // Sin datos de normalización: queda la tabla explícita de abajo.
        }

        var texto = new StringBuilder(descompuesta.Length);
        foreach (var c in descompuesta)
        {
            if (c is '_' or '-' or '.' || char.IsWhiteSpace(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            texto.Append(SinTilde.TryGetValue(c, out var simple) ? simple : char.ToLowerInvariant(c));
        }

        return texto.ToString();
    }

    private static string Booleano(string valor) => valor.ToUpperInvariant() switch
    {
        "TRUE" => "Sí",
        "FALSE" => "No",
        _ => valor,
    };

    private static string? T(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
