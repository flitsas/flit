namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Mandatario (firmante de mandato) gestionado dentro de un organismo de tránsito
/// (ADR-0023). <c>DocumentNumber</c> es PII (Ley 1581): no debe registrarse en logs ni
/// exponerse en mensajes de error. <c>IntegrityHash</c> es una huella determinista
/// <c>SHA-256(full_name + document_number + registered_at)</c>, no un anonimizador.
/// </summary>
public sealed class MandateSigner
{
    public Guid Id { get; set; }
    public Guid TransitOfficeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string IntegrityHash { get; set; } = string.Empty;

    /// <summary>Insumo del hash; se fija en el registro y no cambia al editar.</summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>Baja lógica (soft-delete): al inactivar se liberan las compañías del mandatario.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
