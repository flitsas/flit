namespace Flit.Admin.Application.OtProfile.UpdateOtProfile;

public sealed class UpdateOtProfileCommand
{
    public required Guid TenantId { get; init; }

    public Guid? ChangedBy { get; init; }

    public required UpdateOtProfileRequest Request { get; init; }
}

public sealed class UpdateOtProfileRequest
{
    public string? OperationMode { get; init; }

    public bool? QuipuxReadOnly { get; init; }

    /// <summary>Ignorado — el tenant proviene del token JWT (AC5).</summary>
    public Guid? TenantId { get; init; }

    // ── Campos oficiales RUNT (RF05, ADR-0024) ─────────────────────────────────────
    // Inmutables post-creación: viven en identity.tenants y NO tienen ruta de edición.
    // Se aceptan aquí SOLO como tripwire explícito: si el payload los trae, el handler
    // responde 422 campos_oficiales_no_editables; nunca se escriben. No borrar sin revisar
    // el ADR — su ausencia reabriría el hueco de que un DTO futuro los exponga por descuido.

    /// <summary>Campo oficial RUNT (razón social). Inmutable — solo detección (RF05).</summary>
    public string? LegalName { get; init; }

    /// <summary>Campo oficial RUNT (NIT). Inmutable — solo detección (RF05).</summary>
    public string? TaxId { get; init; }

    /// <summary>Campo oficial RUNT (código). Inmutable — solo detección (RF05).</summary>
    public string? Code { get; init; }
}
