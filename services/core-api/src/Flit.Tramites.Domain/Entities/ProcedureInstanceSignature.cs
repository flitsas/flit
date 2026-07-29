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
/// HU #11056 — estado de firma de UNA parte para el listado de trámites. Son los de
/// <see cref="SignatureEstados"/> más <see cref="NoSolicitada"/>, que no es un estado persistido sino
/// la ausencia de fila: la firma de esa parte todavía no se ha solicitado. Se distingue de
/// <see cref="SignatureEstados.PendienteEnvio"/> (ya solicitada, sin enviar al proveedor) porque para
/// el gestor son acciones distintas.
/// </summary>
public static class FirmaParteEstados
{
    public const string NoSolicitada = "no_solicitada";
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
