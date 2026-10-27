using System.Text.Json;
using Flit.Consultas.Grpc.V1;

namespace Flit.PersonaNatural.Api.Consultas;

/// <summary>
/// Vehículo para un ciudadano anónimo (política de dato mínimo N0, diseño §8): lo que dice si puede iniciar el trámite y
/// nada de quién es el propietario. Un campo nuevo aquí exige revisión de seguridad (riesgo Habeas Data de la HU #14039).
/// </summary>
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
/// Licencia de un conductor para un ciudadano anónimo (HU #14040): solo si existe, el estado y si está vigente. Sin nombres,
/// fechas de nacimiento, dirección, categorías, multas ni sanciones: ni siquiera el nombre enmascarado.
/// </summary>
public sealed record ConductorMinimo(bool Existe, string? EstadoLicencia, string? Vigencia);

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
