namespace Flit.Tramites.Domain.Certifications;

/// <summary>
/// Regla transversal del modelo canónico: <b>canónico + crudo, siempre los dos</b>.
///
/// <para>Lo que no se puede interpretar no se inventa ni se descarta: <c>Value</c> queda en
/// <c>null</c>, <c>Raw</c> se conserva intacto, y el documento imprime el crudo antes que dejar la
/// celda en blanco. Es la diferencia con el diseño anterior, donde un formato inesperado se traducía
/// en un dato perdido para siempre — y sin rastro de qué había mandado el proveedor.</para>
///
/// <para>Un valor con <see cref="ICertifiedValue.Unresolved"/> = true alimenta
/// <c>normalization_issues</c> en la fila persistida: es la lista de campos que llegaron pero no se
/// supieron leer, y por tanto la lista de trabajo para corregir un mapper sin volver a pagar la
/// consulta.</para>
/// </summary>
public interface ICertifiedValue
{
    /// <summary>Texto tal como lo mandó el proveedor, sin tocar.</summary>
    string? Raw { get; }

    /// <summary>¿Hay dato canónico utilizable?</summary>
    bool HasValue { get; }

    /// <summary>
    /// Llegó algo del proveedor pero no se pudo normalizar. Alimenta <c>normalization_issues</c>.
    /// <para>Se declara sin implementación por defecto para que sea accesible desde el tipo concreto
    /// y no solo a través de la interfaz: es la propiedad que más se consulta al diagnosticar.</para>
    /// </summary>
    bool Unresolved { get; }
}

/// <summary>
/// Fecha certificada. <see cref="Value"/> es <see cref="DateOnly"/> y no <c>DateTimeOffset</c> a
/// propósito: un certificado imprime un día calendario, y arrastrar hora + zona es justo lo que hace
/// que <c>AdjustToUniversal</c> pueda correr la fecha un día en un documento oficial.
/// </summary>
public sealed record CertifiedDate(DateOnly? Value, string? Raw) : ICertifiedValue
{
    public static readonly CertifiedDate Empty = new(null, null);

    public bool HasValue => Value.HasValue;

    /// <inheritdoc />
    public bool Unresolved => !HasValue && !string.IsNullOrWhiteSpace(Raw);

    /// <summary>Formato de impresión en certificados FLIT.</summary>
    public string? ToDocumentText() =>
        Value?.ToString("yyyy/MM/dd") ?? (string.IsNullOrWhiteSpace(Raw) ? null : Raw!.Trim());
}

/// <summary>Estado de vigencia certificado. <see cref="Raw"/> es lo que el documento imprime cuando el canónico es <see cref="VigencyStatus.Unknown"/> (D5).</summary>
public sealed record CertifiedStatus(VigencyStatus Value, string? Raw) : ICertifiedValue
{
    public static readonly CertifiedStatus Empty = new(VigencyStatus.Unknown, null);

    public bool HasValue => Value != VigencyStatus.Unknown;

    /// <inheritdoc />
    public bool Unresolved => !HasValue && !string.IsNullOrWhiteSpace(Raw);

    /// <summary>
    /// Texto para el certificado. <see cref="VigencyStatus.Unknown"/> con crudo imprime el crudo
    /// (D5); sin crudo deja la celda en blanco (regla HU #10856: ausente ⇒ vacío, sin guion ni "N/A").
    /// </summary>
    public string? ToDocumentText() => Value switch
    {
        VigencyStatus.Vigente => "VIGENTE",
        VigencyStatus.Vencido => "VENCIDO",
        VigencyStatus.NoAplica => "NO APLICA",
        _ => string.IsNullOrWhiteSpace(Raw) ? null : Raw!.Trim().ToUpperInvariant(),
    };
}

/// <summary>
/// Número de certificado (póliza SOAT, certificado de RTM, matrícula mercantil). Es <b>texto</b> y no
/// numérico: <c>numSoat</c> del RUNT llega con 16 dígitos, por encima de <c>int</c>, y hay proveedores
/// que anteponen ceros que forman parte del número impreso.
/// </summary>
public sealed record CertifiedNumber(string? Value, string? Raw) : ICertifiedValue
{
    public static readonly CertifiedNumber Empty = new(null, null);

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    /// <inheritdoc />
    public bool Unresolved => !HasValue && !string.IsNullOrWhiteSpace(Raw);

    public string? ToDocumentText() => HasValue ? Value : (string.IsNullOrWhiteSpace(Raw) ? null : Raw!.Trim());
}

/// <summary>Nombre de entidad (aseguradora, CDA, razón social, cámara de comercio).</summary>
public sealed record CertifiedName(string? Value, string? Raw) : ICertifiedValue
{
    public static readonly CertifiedName Empty = new(null, null);

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    /// <inheritdoc />
    public bool Unresolved => !HasValue && !string.IsNullOrWhiteSpace(Raw);

    public string? ToDocumentText() => HasValue ? Value : (string.IsNullOrWhiteSpace(Raw) ? null : Raw!.Trim());
}
