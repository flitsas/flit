namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Read model de una compañía representada para la gestión admin — HU #10900. <c>DocumentNumber</c>
/// (NIT) es PII: solo en respuestas autenticadas; no loguear.
/// </summary>
public sealed class RepresentedCompanyItem
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string DocumentType { get; init; } = "NIT";
    public string DocumentNumber { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Phone { get; init; }
    public Guid? RepresentativeId { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Read model de un representante legal para el listado/detalle admin — HU #10900. Proyecta los
/// datos de la compañía representada y las banderas de firma/identidad vigentes.
/// <c>DocumentNumber</c> es PII (@pii:high): solo en respuestas autenticadas; no loguear.
/// </summary>
public sealed class LegalRepresentativeItem
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid? RepresentedCompanyId { get; init; }

    /// <summary>NIT de la compañía representada (denormalizado para el listado).</summary>
    public string CompanyDocumentNumber { get; init; } = string.Empty;

    /// <summary>Razón social de la compañía representada (denormalizado para el listado).</summary>
    public string CompanyName { get; init; } = string.Empty;

    public string DocumentType { get; init; } = string.Empty;
    public string DocumentNumber { get; init; } = string.Empty;
    public string FirstLastName { get; init; } = string.Empty;
    public string? SecondLastName { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Phone { get; init; }
    public Guid? SignatureVaultId { get; init; }
    public Guid? IdentityValidationRef { get; init; }

    /// <summary>Ids de los tipos de trámite que el representante puede firmar (puente M:N).</summary>
    public IReadOnlyList<Guid> ProcedureTypeIds { get; init; } = [];

    /// <summary>
    /// Compañías del representante (HU #10932): un representante-persona puede tener varias. La primera
    /// es la compañía primaria (<see cref="RepresentedCompanyId"/>). Puente
    /// <c>admin.legal_representative_companies</c>.
    /// </summary>
    public IReadOnlyList<LegalRepresentativeCompanySummary> Companies { get; init; } = [];

    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>¿Tiene firma del baúl o validación de identidad vinculada?</summary>
    public bool HasSignatureOrIdentity => SignatureVaultId is not null || IdentityValidationRef is not null;

    /// <summary>
    /// HU #11059 — vigencia de la identidad del representante, con la MISMA semántica y el mismo
    /// cálculo que el mandatario del OT (<see cref="Identity.AdminIdentityVigencia"/>):
    /// <c>"valid"</c> | <c>"expired"</c> (⇒ renovar) | <c>"pending"</c> | <c>"none"</c>. Antes el detalle
    /// solo sabía si HABÍA firma o identidad (<see cref="HasSignatureOrIdentity"/>), un booleano que no
    /// distingue una identidad vigente de una vencida, así que no había forma de ofrecer la renovación.
    /// </summary>
    public string IdentityStatus { get; init; } = Identity.AdminIdentityVigencia.None;

    /// <summary>Hasta cuándo es válida la identidad; solo con <see cref="IdentityStatus"/> <c>"valid"</c>.</summary>
    public DateTimeOffset? IdentityValidUntil { get; init; }

    /// <summary>
    /// HU #11059 — vigencia de la FIRMA del baúl del representante: <c>true</c> si tiene una firma
    /// registrada y vigente hoy. La firma y la identidad vencen por separado, así que la consola tiene
    /// que poder decir "la firma está vencida" sin hablar de la identidad.
    /// </summary>
    public bool FirmaBaulVigente { get; init; }

    /// <summary>Hasta cuándo es válida la firma del baúl; <c>null</c> si no tiene o no caduca.</summary>
    public DateOnly? FirmaBaulVigenteHasta { get; init; }
}

/// <summary>
/// Resumen de una compañía representada por un representante (HU #10932): id + NIT + razón social.
/// Alimenta la vista representante-céntrica (empresas anidadas) y el consumo del wizard. Desde la
/// HU #10933 puede traer el HISTORIAL de escrituras de la compañía (<see cref="Deeds"/>): vacío en el
/// listado/consumo del wizard, poblado solo en el detalle del representante.
/// </summary>
public sealed record LegalRepresentativeCompanySummary(Guid Id, string Nit, string Name)
{
    /// <summary>
    /// HU #11177 — bandera explícita que indica si esta es la compañía principal del representante.
    /// Exactamente UNA compañía viene marcada como principal en cada respuesta. La principal es la
    /// compañía primaria registrada al crear el representante (<c>RepresentedCompanyId</c>); el resto
    /// se ordena por fecha de asociación al puente. No inferir de la columna denormalizada deprecada.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Historial de escrituras de la compañía (HU #10933): vigentes y vencidas, ordenadas por
    /// <c>VigenciaHasta</c> descendente. Solo se puebla en el detalle del representante; en el listado
    /// y en el consumo del wizard queda vacío para no cargar el M:N innecesariamente.
    /// </summary>
    public IReadOnlyList<RepresentativeDeedSummary> Deeds { get; init; } = [];

    /// <summary>
    /// HU #11058 — CONTACTO de la compañía. Se proyecta porque el formulario de edición reenvía la
    /// lista completa de compañías y el upsert sobrescribe estos campos con lo que reciba: sin
    /// proyectarlos, el formulario los mandaba en blanco y CADA edición del representante borraba
    /// silenciosamente el correo, la dirección, la ciudad y el teléfono de todas sus compañías.
    /// </summary>
    public string? Email { get; init; }

    public string? Address { get; init; }

    public string? City { get; init; }

    public string? Phone { get; init; }
}

/// <summary>
/// Escritura de una compañía dentro de la vista representante-céntrica (HU #10933): proyección ligera
/// del historial (sin storage). <see cref="Estado"/> resume la vigencia contra "hoy" en Colombia
/// (UTC-5) y la baja lógica: <c>"vigente"</c>, <c>"vencida"</c>, <c>"futura"</c> o <c>"inactiva"</c>.
/// </summary>
public sealed record RepresentativeDeedSummary(
    Guid Id,
    string Description,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    bool IsActive,
    string Estado)
{
    /// <summary>Estados posibles de una escritura en la vista de historial.</summary>
    public const string EstadoVigente = "vigente";
    public const string EstadoVencida = "vencida";
    public const string EstadoFutura = "futura";
    public const string EstadoInactiva = "inactiva";

    /// <summary>
    /// Construye el resumen calculando <see cref="Estado"/> a partir de la vigencia y de la baja
    /// lógica contra <paramref name="today"/> (día calendario en Colombia). La baja lógica tiene
    /// prioridad: una escritura inactiva es <c>"inactiva"</c> sin importar su rango de vigencia.
    /// </summary>
    public static RepresentativeDeedSummary Create(
        Guid id,
        string description,
        DateOnly vigenciaDesde,
        DateOnly vigenciaHasta,
        bool isActive,
        DateOnly today)
    {
        var estado = !isActive ? EstadoInactiva
            : today < vigenciaDesde ? EstadoFutura
            : today > vigenciaHasta ? EstadoVencida
            : EstadoVigente;

        return new RepresentativeDeedSummary(id, description, vigenciaDesde, vigenciaHasta, isActive, estado);
    }
}

/// <summary>
/// Proyección ligera de un representante para enriquecer escrituras en el wizard (nombre + documento).
/// Sin email/dirección ni historial de escrituras.
/// </summary>
public sealed record LegalRepresentativeBrief(
    Guid Id,
    string FullName,
    string DocumentType,
    string DocumentNumber,
    bool IsActive = true);

/// <summary>
/// Read model de una escritura para el listado/detalle admin y el consumo del wizard — HU #10900.
/// Proyecta las compañías (NITs) a las que aplica.
/// </summary>
public sealed class DeedItem
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Description { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public string StorageSha256 { get; init; } = string.Empty;
    public DateOnly VigenciaDesde { get; init; }
    public DateOnly VigenciaHasta { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// Representante que asoció la escritura (Feature #10929). <c>null</c> en escrituras legadas
    /// (asociadas solo a la empresa). El detalle del representante filtra por este id; el trámite usa la
    /// escritura del representante seleccionado.
    /// </summary>
    public Guid? RepresentativeId { get; init; }

    /// <summary>Ids de las compañías representadas a las que aplica la escritura (puente M:N).</summary>
    public IReadOnlyList<Guid> RepresentedCompanyIds { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
