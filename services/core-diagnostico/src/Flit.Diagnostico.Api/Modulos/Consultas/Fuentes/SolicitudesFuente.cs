using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>Lo que se le pide al RUNT del vehículo: una placa o un VIN ya normalizado por MOD02.</summary>
/// <param name="Tipo">Clase del identificador (placa de carro, moto, remolque, maquinaria o VIN).</param>
/// <param name="Valor">Identificador normalizado (mayúsculas, sin separadores).</param>
public sealed record IdentificadorVehiculo(TipoIdentificador Tipo, string Valor)
{
    public bool EsVin => Tipo == TipoIdentificador.Vin;
}

/// <summary>
/// Qué se avalúa (HU #13795, F05-C7): la placa definitiva y el VIN que dejó el RUNT (al menos uno) y los datos técnicos del
/// bien que algunas fuentes necesitan para identificar la referencia (Fasecolda): modelo (año), cilindraje, combustible y
/// pasajeros, leídos de <c>datos_consolidados.vehiculo</c>. Un dato técnico que el RUNT no trae va nulo (en el proto, 0 o
/// vacío). Son datos del bien, sin documentos ni nombres.
/// </summary>
/// <example>
/// <code>
/// var solicitud = new SolicitudAvaluo("TST201", "9ZZPRUEBA00000001", 2020, 1598, "GASOLINA", 5);
/// </code>
/// </example>
public sealed record SolicitudAvaluo(string? Placa, string? Vin, int? Modelo, int? Cilindraje, string? Combustible, int? Pasajeros)
{
    /// <summary>La placa o, sin placa, el VIN.</summary>
    public string Clave => Placa ?? Vin ?? string.Empty;
}

/// <summary>
/// Qué se consulta del impuesto departamental (HU #13862, F09-C5): la placa definitiva que dejó el RUNT y el departamento
/// derivado de su organismo (<c>vehiculos.departamento_codigo</c>, DIVIPOLA de dos dígitos). Son datos del bien, sin documentos.
/// </summary>
/// <example>
/// <code>
/// var solicitud = new SolicitudImpuesto("TST301", "05");
/// </code>
/// </example>
public sealed record SolicitudImpuesto(string Placa, string DepartamentoCodigo);

/// <summary>
/// Qué se consulta en SIMIT (HU #13793): la placa del vehículo (celda SIMIT_VEHICULO) o el documento de una persona (celdas
/// SIMIT_PROPIETARIO y SIMIT_LOCATARIO, F05-H5), nunca los dos. El documento solo vive en memoria mientras se arma la solicitud
/// (<see cref="DocumentoPersona"/>, que enmascara su número en <see cref="ToString"/>).
/// </summary>
/// <example>
/// <code>
/// var porPlaca = SolicitudSimit.PorPlaca("TST102");
/// var porPersona = SolicitudSimit.PorDocumento(new DocumentoPersona("NIT", "999990305")); // EsJuridica = true
/// </code>
/// </example>
public sealed record SolicitudSimit
{
    private SolicitudSimit(string? placa, DocumentoPersona? documento)
    {
        Placa = placa;
        Documento = documento;
    }

    /// <summary>Placa definitiva que dejó el RUNT; null si la consulta es por persona.</summary>
    public string? Placa { get; }

    /// <summary>Documento de la persona en claro; null si la consulta es por placa.</summary>
    public DocumentoPersona? Documento { get; }

    /// <summary>Verdadero si se consulta a una persona jurídica (NIT): <c>tipo_persona=JURIDICA</c> en core-consultas.</summary>
    public bool EsJuridica => Documento?.EsNit == true;

    public static SolicitudSimit PorPlaca(string placa)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placa);
        return new SolicitudSimit(placa.Trim().ToUpperInvariant(), null);
    }

    public static SolicitudSimit PorDocumento(DocumentoPersona documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        return new SolicitudSimit(null, documento);
    }

    /// <summary>Sin el documento: un log o una excepción nunca lo filtran.</summary>
    public override string ToString() => Placa is not null ? $"placa {Placa}" : $"documento {Documento}";
}
