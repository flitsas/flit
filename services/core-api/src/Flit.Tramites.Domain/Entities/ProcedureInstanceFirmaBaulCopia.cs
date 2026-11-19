namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Copia, tomada al radicar, de la firma de baúl que usa una parte/firmante del trámite (tabla
/// <c>tramites.procedure_instance_signature_vault_copies</c>, DDL 195). Con ella el FUR, sus regeneraciones, el
/// gate de identidad y el listado dejan de consultar el baúl en vivo: un baúl que vence o se desactiva después de
/// radicar ya no le quita la firma al trámite. El archivo de la imagen se copia a un almacenamiento propio del
/// trámite (<see cref="StoragePath"/>); <see cref="SourceStoragePath"/> es solo traza.
/// </summary>
public sealed class ProcedureInstanceFirmaBaulCopia
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Slot de firma del documento (<c>comprador</c>, <c>vendedor</c>, <c>comprador_2</c>…).</summary>
    public string SlotKey { get; set; } = string.Empty;

    /// <summary>Rol de la parte: <c>comprador</c> | <c>vendedor</c> | <c>locatario</c>.</summary>
    public string PartyRole { get; set; } = string.Empty;

    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Documento del representante legal dueño de la firma. PII (Ley 1581): no loguear.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>Id de la fila del baúl copiada (sin FK: la copia sobrevive al baúl).</summary>
    public Guid SignatureVaultId { get; set; }

    /// <summary>Nombre del titular en el baúl al capturar. PII (Ley 1581): no loguear.</summary>
    public string FullName { get; set; } = string.Empty;

    public string SignatureHash { get; set; } = string.Empty;

    public string? CodigoHash { get; set; }

    public DateOnly VigenciaDesde { get; set; }

    public DateOnly VigenciaHasta { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>Archivo de la firma COPIADO al trámite.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>SHA-256 del contenido del archivo copiado.</summary>
    public string StorageSha256 { get; set; } = string.Empty;

    /// <summary>Ruta original en el baúl (traza; puede dejar de existir).</summary>
    public string? SourceStoragePath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }
}
