namespace Flit.Tramites.Domain.Certifications;

/// <summary>Quién afirmó el dato. Determina la precedencia y lo que el certificado declara al pie.</summary>
public enum CertificationSourceKind
{
    /// <summary>Derivado por el sistema (proyección, backfill, valor por defecto). El más débil.</summary>
    System = 0,

    /// <summary>Leído de un PDF cargado por el usuario. Puede equivocarse al reconocer caracteres.</summary>
    Ocr = 1,

    /// <summary>Escrito o corregido a mano por un operador.</summary>
    User = 2,

    /// <summary>Respuesta de una consulta a la fuente oficial. El más fuerte.</summary>
    Consultation = 3,
}

/// <summary>
/// Procedencia de una fila de certificación: quién lo dijo, cuándo y con qué versión de mapeo.
/// </summary>
/// <remarks>
/// Hoy toda esta información se comprime en un <c>source varchar(20)</c> que no dice quién consultó,
/// ni cuándo, ni si el dato salió de un PDF escaneado — mientras el certificado afirma, en texto fijo,
/// <i>"En la consulta realizada al RUNT 2.0 el día X"</i>. Con esto el documento deja de afirmar un
/// RUNT que puede no haber ocurrido y declara la fuente real al pie de cada tabla.
///
/// <para><see cref="MapperVersion"/> no es adorno: cuando se corrige un mapper, es lo que permite
/// saber qué filas se produjeron con el mapeo viejo y reprocesarlas desde
/// <see cref="RawPayloadId"/> sin volver a pagar la consulta.</para>
/// </remarks>
public sealed record CertificationProvenance(
    CertificationSourceKind Source,
    string ProviderKey,
    DateTimeOffset ObservedAt,
    Guid? RawPayloadId = null,
    string MapperVersion = CertificationProvenance.UnknownMapperVersion)
{
    public const string UnknownMapperVersion = "unknown";

    /// <summary>Clave del productor cuando el dato lo escribió una persona.</summary>
    public const string ManualProviderKey = "manual";

    /// <summary>Clave del productor cuando el dato lo trasladó el backfill desde el almacén anterior.</summary>
    public const string LegacyProviderKey = "legacy";

    public static CertificationProvenance Manual(DateTimeOffset observedAt) =>
        new(CertificationSourceKind.User, ManualProviderKey, observedAt);

    public static CertificationProvenance Legacy(DateTimeOffset observedAt) =>
        new(CertificationSourceKind.System, LegacyProviderKey, observedAt, null, LegacyProviderKey);

    /// <summary>Texto del pie de tabla del certificado: <c>Fuente: … · consultado 2026/08/07</c>.</summary>
    public string ToDocumentFooter(string sourceLabel) =>
        $"Fuente: {sourceLabel} vía {ProviderKey} · consultado {ObservedAt.ToOffset(TimeSpan.FromHours(-5)):yyyy/MM/dd}";
}

/// <summary>Códigos persistidos de <see cref="CertificationSourceKind"/>. El CHECK del DDL usa estos mismos.</summary>
public static class CertificationSourceCodes
{
    public const string System = "system";
    public const string Ocr = "ocr";
    public const string User = "user";
    public const string Consultation = "consultation";

    public static string ToCode(CertificationSourceKind kind) => kind switch
    {
        CertificationSourceKind.Consultation => Consultation,
        CertificationSourceKind.User => User,
        CertificationSourceKind.Ocr => Ocr,
        _ => System,
    };

    public static CertificationSourceKind FromCode(string? code) => code switch
    {
        Consultation => CertificationSourceKind.Consultation,
        User => CertificationSourceKind.User,
        Ocr => CertificationSourceKind.Ocr,
        _ => CertificationSourceKind.System,
    };
}
