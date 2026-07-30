namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Firma electrónica de un documento (compraventa) por una parte de un trámite.
/// Slice 7 — firma electrónica (mock). Solo aplica a traspaso_standard (matrícula no tiene
/// compraventa). El proveedor es un MOCK (ver ISignatureProvider en Application); la integración
/// real (ZapSign) se diferirá. Idempotencia: una sola firma activa por (parte, doc_tipo).
/// </summary>
public sealed class ProcedureInstanceSignature
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>'comprador' | 'vendedor'.</summary>
    public string Parte { get; set; } = string.Empty;

    /// <summary>Tipo de documento firmado. Default 'compraventa'.</summary>
    public string DocTipo { get; set; } = SignatureDocTipos.Compraventa;

    /// <summary>Proveedor de firma. Default 'mock' (real later = zapsign).</summary>
    public string Proveedor { get; set; } = "mock";

    /// <summary>pendiente_envio | enviada | firmada | rechazada | cancelada.</summary>
    public string Estado { get; set; } = SignatureEstados.PendienteEnvio;

    /// <summary>Identificador del sobre devuelto por el proveedor (mock). Null hasta enviar.</summary>
    public string? EnvelopeId { get; set; }

    /// <summary>Ruta de almacenamiento del PDF firmado (mock). Null hasta firmar.</summary>
    public string? PdfPath { get; set; }

    /// <summary>SHA-256 (hex) del documento firmado. Null hasta firmar.</summary>
    public string? Sha256 { get; set; }

    /// <summary>jsonb con metadata del proveedor (p. ej. signUrl). Null por defecto.</summary>
    public string? Metadata { get; set; }

    public DateTimeOffset SolicitadoAt { get; set; }
    public DateTimeOffset? FirmadoAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public ProcedureInstance? ProcedureInstance { get; set; }
}

/// <summary>Estados de la máquina de firma electrónica.</summary>
public static class SignatureEstados
{
    public const string PendienteEnvio = "pendiente_envio";
    public const string Enviada = "enviada";
    public const string Firmada = "firmada";
    public const string Rechazada = "rechazada";
    public const string Cancelada = "cancelada";
}

/// <summary>
/// Estado de "Firmado" de UNA parte en el listado de trámites.
/// <para>
/// Ajuste del PO sobre la HU #11056: la columna NO habla de la firma electrónica de la compraventa
/// (<see cref="SignatureEstados"/>), sino de <b>cómo queda acreditada la parte</b> — por validación de
/// identidad o por firma del baúl. Solo hay tres estados válidos, y son excluyentes.
/// </para>
/// </summary>
public static class FirmaParteEstados
{
    /// <summary>La validación de identidad aún no se ha realizado (y no hay firma del baúl).</summary>
    public const string Pendiente = "pendiente";

    /// <summary>Identidad validada y aprobada, o firma del baúl vigente.</summary>
    public const string Firmado = "firmado";

    /// <summary>
    /// Identidad rechazada, o firma del baúl ya vencida (típicamente porque el trámite llevaba mucho
    /// tiempo en el mismo estado sin que el organismo lo aprobara).
    /// </summary>
    public const string Rechazado = "rechazado";
}

/// <summary>Tipos de documento firmable.</summary>
public static class SignatureDocTipos
{
    public const string Compraventa = "compraventa";
}

/// <summary>Reglas de negocio de la firma electrónica (compartidas Application/Domain).</summary>
public static class SignatureRules
{
    public const string ParteComprador = "comprador";
    public const string ParteVendedor = "vendedor";
}
