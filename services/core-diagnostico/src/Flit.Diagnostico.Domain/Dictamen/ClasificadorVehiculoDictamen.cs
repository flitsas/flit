using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Domain.Dictamen;

/// <summary>Clase del vehículo para las reglas de SOAT y RTM del dictamen.</summary>
public enum ClaseVehiculoDictamen
{
    /// <summary>El RUNT no trajo una clase conocida: se conserva el plazo de 5 años de la RTM.</summary>
    Desconocida,

    /// <summary>Automóvil, camioneta, campero, bus, buseta, microbús, camión, tractocamión o volqueta.</summary>
    Carro,

    /// <summary>Motocicleta o motociclo.</summary>
    Moto,

    /// <summary>Motocarro, mototriciclo, tricimoto, cuatrimoto o cuadriciclo.</summary>
    Motocarro,

    /// <summary>Maquinaria agrícola, industrial o de construcción autopropulsada.</summary>
    Maquinaria,

    /// <summary>Remolque o semirremolque.</summary>
    Remolque,
}

/// <summary>Servicio del vehículo según el RUNT.</summary>
public enum ServicioVehiculoDictamen
{
    /// <summary>El RUNT no trajo el servicio.</summary>
    Desconocido,

    /// <summary>Servicio particular.</summary>
    Particular,

    /// <summary>Servicio público.</summary>
    Publico,

    /// <summary>Oficial, diplomático u otro servicio que no es público.</summary>
    Otro,
}

/// <summary>
/// Clase y servicio del vehículo a partir de los textos del RUNT, y plazo de la primera RTM (ola 2026-11-11, hallazgos V4/V5;
/// dictamen de expert-doc-engine sobre la Ley 769 art. 52, mod. Decreto 019/2012 art. 202): 2 años de matrícula para moto,
/// motocarro y carro de servicio público; 5 para el resto de carros; maquinaria y remolque sin RTM. Una clase que no se
/// reconoce queda <see cref="ClaseVehiculoDictamen.Desconocida"/> y conserva los 5 años (comportamiento anterior).
/// Normaliza como <c>NombreNormalizado</c>: mayúsculas, sin tildes y con los espacios colapsados; compara por igualdad
/// exacta salvo «MAQUINARIA», que basta con que aparezca en la clase o en la clasificación.
/// </summary>
/// <example>
/// <code>
/// var clase = ClasificadorVehiculoDictamen.Clase("MOTOCARRO", null, null);          // Motocarro
/// var servicio = ClasificadorVehiculoDictamen.Servicio("Público");                  // Publico
/// var anios = ClasificadorVehiculoDictamen.AniosPrimeraRtm(clase, servicio);        // 2
/// </code>
/// </example>
public static class ClasificadorVehiculoDictamen
{
    private static readonly HashSet<string> Carros = new(StringComparer.Ordinal)
    {
        "AUTOMOVIL", "CAMIONETA", "CAMPERO", "BUS", "BUSETA", "MICROBUS", "CAMION", "TRACTOCAMION", "VOLQUETA",
    };

    private static readonly HashSet<string> Motos = new(StringComparer.Ordinal) { "MOTOCICLETA", "MOTOCICLO" };

    private static readonly HashSet<string> Motocarros = new(StringComparer.Ordinal)
    {
        "MOTOCARRO", "MOTOTRICICLO", "TRICIMOTO", "CUATRIMOTO", "CUADRICICLO",
    };

    private static readonly HashSet<string> Remolques = new(StringComparer.Ordinal)
    {
        "REMOLQUE", "SEMIRREMOLQUE", "SEMIREMOLQUE", "REMOLQUE Y SEMIRREMOLQUE",
    };

    /// <summary>
    /// La clase del dictamen. Manda la clase del RUNT; si no dice nada conocido, la clasificación; si tampoco, una placa de
    /// maquinaria (<c>AA999999</c>) o de remolque (<c>R/S</c> y dígitos), que solo existen en esas clases.
    /// </summary>
    public static ClaseVehiculoDictamen Clase(string? clase, string? clasificacion, TipoIdentificador? tipoPlaca)
    {
        var deClase = DeTexto(Normalizar(clase));
        if (deClase != ClaseVehiculoDictamen.Desconocida)
            return deClase;
        var deClasificacion = DeTexto(Normalizar(clasificacion));
        if (deClasificacion != ClaseVehiculoDictamen.Desconocida)
            return deClasificacion;
        return tipoPlaca switch
        {
            TipoIdentificador.PlacaMaquinaria => ClaseVehiculoDictamen.Maquinaria,
            TipoIdentificador.PlacaRemolque => ClaseVehiculoDictamen.Remolque,
            _ => ClaseVehiculoDictamen.Desconocida,
        };
    }

    /// <summary>Público si alguna palabra es «PUBLICO»; particular si alguna es «PARTICULAR»; vacío es desconocido.</summary>
    public static ServicioVehiculoDictamen Servicio(string? servicio)
    {
        var normalizado = Normalizar(servicio);
        if (normalizado.Length == 0)
            return ServicioVehiculoDictamen.Desconocido;
        var palabras = normalizado.Split(' ');
        if (palabras.Contains("PUBLICO", StringComparer.Ordinal))
            return ServicioVehiculoDictamen.Publico;
        return palabras.Contains("PARTICULAR", StringComparer.Ordinal)
            ? ServicioVehiculoDictamen.Particular
            : ServicioVehiculoDictamen.Otro;
    }

    /// <summary>Años de matrícula a partir de los que se exige la RTM; null si la clase no tiene RTM.</summary>
    public static int? AniosPrimeraRtm(ClaseVehiculoDictamen clase, ServicioVehiculoDictamen servicio) => clase switch
    {
        ClaseVehiculoDictamen.Maquinaria or ClaseVehiculoDictamen.Remolque => null,
        ClaseVehiculoDictamen.Moto or ClaseVehiculoDictamen.Motocarro => 2,
        ClaseVehiculoDictamen.Carro when servicio == ServicioVehiculoDictamen.Publico => 2,
        _ => 5,
    };

    private static ClaseVehiculoDictamen DeTexto(string texto)
    {
        if (texto.Length == 0)
            return ClaseVehiculoDictamen.Desconocida;
        if (texto.Contains("MAQUINARIA", StringComparison.Ordinal))
            return ClaseVehiculoDictamen.Maquinaria;
        if (Remolques.Contains(texto))
            return ClaseVehiculoDictamen.Remolque;
        if (Motocarros.Contains(texto))
            return ClaseVehiculoDictamen.Motocarro;
        if (Motos.Contains(texto))
            return ClaseVehiculoDictamen.Moto;
        return Carros.Contains(texto) ? ClaseVehiculoDictamen.Carro : ClaseVehiculoDictamen.Desconocida;
    }

    private static string Normalizar(string? texto) => Catalogos.NombreNormalizado.Normalizar(texto);
}
