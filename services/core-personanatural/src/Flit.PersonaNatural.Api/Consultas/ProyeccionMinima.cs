using System.Text.Json;
using Flit.Consultas.Grpc.V1;

namespace Flit.PersonaNatural.Api.Consultas;

/// <summary>
/// Vehículo para un ciudadano anónimo (política de dato mínimo N0, diseño §8): lo que dice si puede iniciar el trámite y
/// nada de quién es el propietario. Un campo nuevo aquí exige revisión de seguridad (riesgo Habeas Data de la HU #14039).
/// </summary>
/// <summary>
/// Atributos del vehículo que FLIT 2 muestra al consultar el RUNT (placa, clase, marca, VIN, chasis, color, motor, medidas)
/// y el propietario completo (nombre y documento). Sigue sin dirección, teléfono, documentos del SOAT ni acreedor.
/// </summary>
public sealed record VehiculoDelTramite(
    bool Existe,
    string? Placa,
    string? Vin,
    string? Clase,
    string? Marca,
    string? Linea,
    string? ModeloAno,
    string? TipoServicio,
    string? Estado,
    string? NumeroChasis,
    string? Color,
    string? Combustible,
    string? NumeroSerie,
    string? Cilindraje,
    string? Capacidad,
    string? Ejes,
    string? Carroceria,
    string? NumeroMotor,
    string? Alto,
    string? Ancho,
    string? Largo,
    string? Llantas,
    bool TienePrenda,
    bool TieneLimitacion,
    bool TieneRestricciones,
    string? PropietarioNombre = null,
    string? PropietarioDocumento = null);

public sealed record VehiculoMinimo(
    bool Existe,
    string? Placa,
    string? Clase,
    string? Marca,
    string? Linea,
    string? ModeloAno,
    string? TipoServicio,
    string? Estado,
    bool TienePrenda,
    bool TieneLimitacion,
    bool TieneRestricciones);

/// <summary>
/// Vehículo para un actor con identidad validada dentro de SU trámite (nivel N2 del diseño §8): el dato mínimo más los
/// atributos del vehículo que el expediente necesita. Sigue sin traer propietario, documentos, comparendos, SOAT ni valores.
/// El acreedor prendario es el único dato de un tercero y solo existe aquí (HU #14041, riesgo Habeas Data).
/// </summary>
public sealed record VehiculoDetalle(
    VehiculoMinimo Vehiculo,
    string? Vin,
    string? NumeroMotor,
    string? NumeroSerie,
    string? Color,
    string? Combustible,
    string? Cilindraje,
    string? Acreedor);

/// <summary>
/// Licencia de un conductor para un ciudadano anónimo (HU #14040): solo si existe, el estado y si está vigente. Sin nombres,
/// fechas de nacimiento, dirección, categorías, multas ni sanciones: ni siquiera el nombre enmascarado.
/// </summary>
public sealed record ConductorMinimo(bool Existe, string? EstadoLicencia, string? Vigencia);

/// <summary>
/// Persona para el actor con sesión de la solicitud (consulta de detalle, el mismo prellenado de FLIT 2): SOLO lo que sirve para
/// rellenar los campos del actor. Nada de licencias, sanciones, fechas de nacimiento ni otros datos del RUNT. <c>Ciudad</c> queda
/// siempre vacía: en FLIT 2 sale del histórico de actores de la empresa, que el portal no conoce ni debe exponer.
/// </summary>
public sealed record PersonaDetalle(bool Existe, string? Nombres, string? Apellidos, string? NombreCompleto, string? Ciudad);

/// <summary>
/// Proyección del <see cref="ResultadoConsulta"/> de core-consultas a los DTO del portal. Es una LISTA BLANCA: solo se leen
/// las claves de campo que el DTO necesita; el resto del resultado (propietario, documento, nombres, SOAT, multas…) no se
/// toca, no se copia y no se registra. Código puro: sin red ni base, para probarlo con resultados armados a mano.
/// </summary>
public static class ProyeccionMinima
{
    // Claves de campo de la plataforma (las que escriben los mappers del RUNT en core-consultas).
    internal const string CampoPlaca = "plate";
    internal const string CampoClase = "vehicle_class";
    internal const string CampoMarca = "vehicle_brand";
    internal const string CampoLinea = "vehicle_line";
    internal const string CampoModelo = "vehicle_year";
    internal const string CampoServicio = "vehicle_service";
    internal const string CampoEstado = "vehicle_state";
    internal const string CampoPrendas = "runt_tiene_prendas";
    internal const string CampoGravamenes = "runt_tiene_gravamenes";
    internal const string CampoPrendario = "runt_prendario";
    internal const string CampoGarantias = "runt_gravamenes";
    internal const string CampoLimitaciones = "runt_limitaciones_propiedad";

    private static readonly string[] AfirmativosRunt = ["SI", "SÍ", "S", "TRUE", "1"];

    /// <summary>
    /// ¿El proveedor no pudo responder? Un proveedor caído NO es un error gRPC: llega como un chequeo en estado ERROR
    /// (contrato de <c>ConsultasService</c>). Los chequeos FAIL o UNKNOWN son veredictos («no existe», «sin dato»), no caídas.
    /// </summary>
    public static bool ProveedorCaido(ResultadoConsulta resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return resultado.Chequeos.Any(c => c.Estado == EstadoChequeo.Error);
    }

    /// <summary>
    /// Consulta dentro del trámite: los mismos atributos que FLIT 2 pinta en la ficha del vehículo,
    /// con el nombre y el documento del propietario completos. La dirección, el teléfono, el SOAT y el acreedor se descartan.
    /// Un nombre que ya llega con asteriscos no es un nombre y no se copia.
    /// </summary>
    public static VehiculoDelTramite DelTramite(ResultadoConsulta resultado, string? placaConsultada)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var minimo = Vehiculo(resultado, placaConsultada);
        string? Campo(string clave) => Texto(resultado, clave);
        var propietarioNombre = Campo("person_full_name") is { } nombre && !nombre.Contains('*', StringComparison.Ordinal) ? nombre : null;
        var tipoDocumento = Campo("owner_document_type");
        var numeroDocumento = Campo("owner_document_number");
        var propietarioDocumento = numeroDocumento is null
            ? null
            : string.IsNullOrWhiteSpace(tipoDocumento) ? numeroDocumento : $"{tipoDocumento} {numeroDocumento}";
        var vin = Campo(CampoVin);
        var chasis = Campo("vehicle_chassis");
        var color = Campo(CampoColor);
        var combustible = Campo(CampoCombustible);
        var serie = Campo(CampoSerie);
        var cilindraje = Campo(CampoCilindraje);
        var capacidad = Campo("vehicle_passengers");
        var ejes = Campo("vehicle_axles");
        var carroceria = Campo("vehicle_body_type");
        var motor = Campo(CampoMotor);
        var alto = Campo("vehicle_height");
        var ancho = Campo("vehicle_width");
        var largo = Campo("vehicle_length");
        var llantas = Campo("vehicle_tires");
        var existe = minimo.Existe || vin is not null || chasis is not null || color is not null || combustible is not null
            || serie is not null || cilindraje is not null || capacidad is not null || ejes is not null || carroceria is not null
            || motor is not null || alto is not null || ancho is not null || largo is not null || llantas is not null;
        if (!existe)
            return new VehiculoDelTramite(false, placaConsultada, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, false, false);
        return new VehiculoDelTramite(
            true, minimo.Placa, vin, minimo.Clase, minimo.Marca, minimo.Linea, minimo.ModeloAno, minimo.TipoServicio, minimo.Estado,
            chasis, color, combustible, serie, cilindraje, capacidad, ejes, carroceria, motor, alto, ancho, largo, llantas,
            minimo.TienePrenda, minimo.TieneLimitacion, minimo.TieneRestricciones, propietarioNombre, propietarioDocumento);
    }

    /// <summary>
    /// Dato mínimo del vehículo. <paramref name="placaConsultada"/> es la que el ciudadano escribió, ya normalizada: la
    /// respuesta devuelve esa y no una que venga del proveedor. Sin ningún atributo del vehículo, <c>existe = false</c> y todo
    /// lo demás vacío.
    /// </summary>
    public static VehiculoMinimo Vehiculo(ResultadoConsulta resultado, string? placaConsultada)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var clase = Texto(resultado, CampoClase);
        var marca = Texto(resultado, CampoMarca);
        var linea = Texto(resultado, CampoLinea);
        var servicio = Texto(resultado, CampoServicio);
        var estado = Texto(resultado, CampoEstado);
        if (clase is null && marca is null && linea is null && servicio is null && estado is null)
            return new VehiculoMinimo(false, placaConsultada, null, null, null, null, null, null, false, false, false);

        var prenda = TienePrenda(resultado);
        var limitacion = TieneLimitacion(resultado);
        return new VehiculoMinimo(
            true, placaConsultada ?? Texto(resultado, CampoPlaca), clase, marca, linea, Texto(resultado, CampoModelo), servicio,
            estado?.ToUpperInvariant(), prenda, limitacion, prenda || limitacion);
    }

    internal const string CampoVin = "vin";
    internal const string CampoMotor = "vehicle_engine_number";
    internal const string CampoSerie = "vehicle_series";
    internal const string CampoColor = "vehicle_color";
    internal const string CampoCombustible = "vehicle_fuel";
    internal const string CampoCilindraje = "vehicle_engine_displacement";
    internal const string CampoAcreedor = "runt_nombre_acreedor";

    /// <summary>
    /// Detalle ampliado (N2). Solo se llama con la identidad ya validada: esta proyección no decide quién puede verlo. Si el
    /// vehículo no existe, no hay detalle: solo el <c>existe = false</c> del dato mínimo.
    /// </summary>
    public static VehiculoDetalle Detalle(ResultadoConsulta resultado, string? placaConsultada)
    {
        var minimo = Vehiculo(resultado, placaConsultada);
        return minimo.Existe
            ? new VehiculoDetalle(
                minimo, Texto(resultado, CampoVin), Texto(resultado, CampoMotor), Texto(resultado, CampoSerie), Texto(resultado, CampoColor),
                Texto(resultado, CampoCombustible), Texto(resultado, CampoCilindraje), minimo.TienePrenda ? Texto(resultado, CampoAcreedor) : null)
            : new VehiculoDetalle(minimo, null, null, null, null, null, null, null);
    }

    internal const string CampoLicenciaActiva = "person_has_active_license";
    internal const string CampoLicenciaEstado = "person_license_status";

    public const string Vigente = "vigente";
    public const string NoVigente = "no_vigente";

    /// <summary>
    /// Licencia del conductor. «Sin licencia» (<c>existe = false</c>) es la persona que el RUNT no halla o la que no tiene
    /// ningún registro de licencia: ni estado del conductor ni una licencia activa. Con licencia, la vigencia es
    /// <c>vigente</c> si hay al menos una licencia activa y <c>no_vigente</c> si no.
    /// </summary>
    public static ConductorMinimo Conductor(ResultadoConsulta resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var estado = Texto(resultado, CampoLicenciaEstado);
        var activa = string.Equals(Texto(resultado, CampoLicenciaActiva), "true", StringComparison.OrdinalIgnoreCase);
        if (estado is null && !activa)
            return new ConductorMinimo(false, null, null);

        return new ConductorMinimo(true, estado?.ToUpperInvariant(), activa ? Vigente : NoVigente);
    }

    internal const string CampoNombreCompleto = "person_full_name";
    internal const string CampoPrimerNombre = "person_first_name";
    internal const string CampoSegundoNombre = "person_second_name";
    internal const string CampoPrimerApellido = "person_first_last_name";
    internal const string CampoSegundoApellido = "person_second_last_name";
    internal const string CampoApellidos = "person_last_name";

    /// <summary>
    /// Nombres de la persona, como los usa FLIT 2 para rellenar el actor: nombres de pila juntos, apellidos juntos y nombre
    /// completo. Como allí, «existe» es que haya nombre; un valor enmascarado (con <c>*</c>) no es un nombre y se descarta.
    /// </summary>
    public static PersonaDetalle PersonaConNombres(ResultadoConsulta resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var nombres = Unir(Nombre(resultado, CampoPrimerNombre), Nombre(resultado, CampoSegundoNombre));
        var apellidos = Nombre(resultado, CampoApellidos) ?? Unir(Nombre(resultado, CampoPrimerApellido), Nombre(resultado, CampoSegundoApellido));
        var completo = Nombre(resultado, CampoNombreCompleto) ?? Unir(nombres, apellidos);
        return completo is null
            ? new PersonaDetalle(false, null, null, null, null)
            : new PersonaDetalle(true, nombres, apellidos, completo, null);
    }

    private static string? Nombre(ResultadoConsulta resultado, string clave) => Texto(resultado, clave) is { } t && !t.Contains('*', StringComparison.Ordinal) ? t : null;

    private static string? Unir(params string?[] partes)
    {
        var unido = string.Join(' ', partes.Where(p => !string.IsNullOrWhiteSpace(p)));
        return unido.Length == 0 ? null : unido;
    }

    /// <summary>Prenda o gravamen: cualquier bandera afirmativa del RUNT o al menos una garantía con datos.</summary>
    internal static bool TienePrenda(ResultadoConsulta resultado) =>
        EsAfirmativo(Texto(resultado, CampoPrendas)) || EsAfirmativo(Texto(resultado, CampoGravamenes))
        || EsAfirmativo(Texto(resultado, CampoPrendario)) || ContarElementos(Json(resultado, CampoGarantias)) > 0;

    internal static bool TieneLimitacion(ResultadoConsulta resultado) => ContarElementos(Json(resultado, CampoLimitaciones)) > 0;

    /// <summary>Valor de texto de un campo, recortado, o <c>null</c> si no vino o vino en blanco.</summary>
    internal static string? Texto(ResultadoConsulta resultado, string clave)
    {
        var campo = resultado.Campos.FirstOrDefault(c => string.Equals(c.Clave, clave, StringComparison.OrdinalIgnoreCase));
        return campo is null || string.IsNullOrWhiteSpace(campo.ValorTexto) ? null : campo.ValorTexto.Trim();
    }

    private static string? Json(ResultadoConsulta resultado, string clave)
    {
        var campo = resultado.Campos.FirstOrDefault(c => string.Equals(c.Clave, clave, StringComparison.OrdinalIgnoreCase));
        return campo is null || string.IsNullOrWhiteSpace(campo.ValorJson) ? null : campo.ValorJson;
    }

    private static bool EsAfirmativo(string? valor) => valor is not null && AfirmativosRunt.Contains(valor.ToUpperInvariant());

    /// <summary>Elementos de un arreglo JSON; 0 si no hay JSON, no es arreglo o no se puede leer (nunca lanza).</summary>
    private static int ContarElementos(string? json)
    {
        if (json is null)
            return 0;

        try
        {
            using var documento = JsonDocument.Parse(json);
            return documento.RootElement.ValueKind == JsonValueKind.Array ? documento.RootElement.GetArrayLength() : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
