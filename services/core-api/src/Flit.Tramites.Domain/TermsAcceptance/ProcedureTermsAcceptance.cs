namespace Flit.Tramites.Domain.TermsAcceptance;

/// <summary>
/// Una aceptación de Términos y Condiciones al iniciar la creación de un trámite (Epic #12543).
/// Fila de <c>tramites.procedure_terms_acceptances</c>: evidencia legal append-only, una por cada
/// vez que el usuario marca el checkbox y pulsa Continuar — se pide en CADA creación (RN-05), así
/// que no hay unicidad por usuario ni por tipo.
/// </summary>
public sealed class ProcedureTermsAcceptance
{
    /// <summary>uuid asignado por la base (uuidv7).</summary>
    public Guid Id { get; set; }

    /// <summary>Compañía desde la que se creaba el trámite; <c>null</c> si el SuperAdmin no acotó ninguna.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Usuario que aceptó (<c>user_id</c> de la epic).</summary>
    public Guid UserId { get; set; }

    /// <summary>Code del tipo de trámite que iba a crear (<c>tramite_type</c> de la epic).</summary>
    public string ProcedureTypeCode { get; set; } = string.Empty;

    /// <summary>URL del documento de T&amp;C vigente al aceptar.</summary>
    public string TermsUrl { get; set; } = string.Empty;

    /// <summary>Instante UTC de la aceptación (<c>accepted_at</c> de la epic).</summary>
    public DateTimeOffset AcceptedAt { get; set; }

    /// <summary>IP de origen ya normalizada (<c>session_ip</c> de la epic), o <c>null</c>.</summary>
    public string? ClientIp { get; set; }

    public string? UserAgent { get; set; }
}
