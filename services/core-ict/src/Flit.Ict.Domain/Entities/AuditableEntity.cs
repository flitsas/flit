namespace Flit.Ict.Domain.Entities;

/// <summary>
/// Base de entidades con la auditoría estándar del monorepo (created/updated/deleted +
/// row_version). Los triggers de Postgres (public.trg_row_version / public.trg_audit_log)
/// mantienen row_version y el audit log; aquí solo se declaran las columnas.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public long RowVersion { get; set; }
}
