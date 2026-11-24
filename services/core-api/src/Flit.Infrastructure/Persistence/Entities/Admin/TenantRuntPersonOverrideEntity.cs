namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Override manual de respuesta de conductor RUNT para un tenant (HU #14642).
/// El SuperAdmin lo crea para que la cadena de consultas devuelva este registro
/// sin llamar a Kyverum/Verifik, cuando el RUNT no tiene el documento o lo tiene mal.
/// </summary>
public sealed class TenantRuntPersonOverrideEntity
{
    public Guid Id { get; set; }

    /// <summary>Empresa a la que aplica este override.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Tipo de documento (CC | CE | PAS | TI | PPT).</summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Número de documento (texto libre, case-insensitive en el lookup).</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    // ── Nombre desglosado (mismo vocabulario que DriverNameResolver) ──────────
    public string FirstName { get; set; } = string.Empty;
    public string? SecondName { get; set; }
    public string FirstLastName { get; set; } = string.Empty;
    public string? SecondLastName { get; set; }

    // ── Estado de la persona / conductor ─────────────────────────────────────
    /// <summary>Estado de la persona en el RUNT (ej. ACTIVA, FALLECIDA). Default ACTIVA.</summary>
    public string CitizenStatus { get; set; } = "ACTIVA";

    /// <summary>Estado del conductor en el RUNT (ej. ACTIVO, SUSPENDIDO). Default ACTIVO.</summary>
    public string DriverStatus { get; set; } = "ACTIVO";

    // ── Licencia ──────────────────────────────────────────────────────────────
    public bool HasActiveLicense { get; set; } = true;

    /// <summary>Categorías de licencia (p. ej. "B1,C1"), nulo si no aplica.</summary>
    public string? LicenseCategories { get; set; }

    // ── Multas ────────────────────────────────────────────────────────────────
    public bool HasPendingFines { get; set; }
    public string? NroPazYSalvo { get; set; }

    // ── Control del override ──────────────────────────────────────────────────
    /// <summary>Motivo obligatorio por el que se creó este override.</summary>
    public string Reason { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Fecha de expiración opcional; nulo = sin expiración automática.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    // ── Auditoría ─────────────────────────────────────────────────────────────
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
