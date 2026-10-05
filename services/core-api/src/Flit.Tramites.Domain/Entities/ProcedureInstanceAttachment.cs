namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Documento adjunto a una instancia de trámite (almacenamiento en disco local; deuda prod multi-instancia).
/// Slice 1 — schema núcleo del rework de trámites.
/// </summary>
public sealed class ProcedureInstanceAttachment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string Mimetype { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string Source { get; set; } = "user";

    /// <summary>
    /// Proveedor externo del documento cuando FLIT lo generó vía integración (p. ej.
    /// <c>kyverum</c> para Certificado de Improntas Digitales). <c>null</c> = carga manual
    /// u origen no proveedor. No confundir con <see cref="Source"/>.
    /// </summary>
    public string? Provider { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
    public Guid? UploadedBy { get; set; }

    /// <summary>
    /// HU #10936 — referencia a la escritura (admin.company_deeds.id) que entró al registro cuando este
    /// adjunto es una escritura de sistema (tipos 'escritura'/'escritura_comprador'). Deja trazable cuál
    /// escritura se usó y permite "congelar" la utilizada tras la entrega del trámite. <c>null</c> en
    /// cualquier otro adjunto (FUR, certificados, cargas de usuario).
    /// </summary>
    public Guid? SourceDeedId { get; set; }

    /// <summary>
    /// HU #11313/#11316 (ADR-0042) — referencia a la versión (admin.company_personalized_documents.id)
    /// que entró al registro cuando este adjunto es un documento personalizado de compañía
    /// (<c>Source = "company"</c>). Espejo exacto de <see cref="SourceDeedId"/>. <c>null</c> en
    /// cualquier otro adjunto.
    /// </summary>
    public Guid? SourcePersonalizedDocumentId { get; set; }

    /// <summary>
    /// HU #12166 (Feature #12156) — al revocar un trámite Aprobado, el FUR/certificados vigentes
    /// quedan marcados como históricos (visibles, no borrados): documentan lo que el OT tuvo a la
    /// vista al aprobar, pero ya no representan el estado vigente del vehículo tras la revocación.
    /// Default false. Ningún otro flujo lo pone en true hoy.
    /// </summary>
    public bool IsHistorico { get; set; }

    /// <summary>
    /// HU #13172 (Feature #13118) — código del formato de mandato (<c>template_code</c>) con el que se generó este
    /// adjunto cuando es el contrato de mandato del sistema. <c>null</c> en cualquier otro adjunto.
    /// </summary>
    public string? MandateFormatCode { get; set; }

    /// <summary>
    /// HU #13172 — versión de la plantilla del formato usada al emitir el mandato: <c>null</c> = no aplica (otro adjunto o
    /// plantilla propia heredada del OT); <c>0</c> = redacción del generador; <c>N</c> = versión N publicada. La
    /// regeneración reproduce esta versión y no la vigente, para no alterar un contrato ya emitido.
    /// </summary>
    public int? MandateFormatVersion { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}
