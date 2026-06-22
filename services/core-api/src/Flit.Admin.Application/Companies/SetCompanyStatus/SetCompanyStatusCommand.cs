namespace Flit.Admin.Application.Companies.SetCompanyStatus;

/// <summary>Comando para activar/desactivar una compañía (#10118).</summary>
public sealed class SetCompanyStatusCommand
{
    /// <summary>Identificador del tenant (<c>identity.tenants.id</c>).</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Estado destino: <c>true</c> activa, <c>false</c> desactiva.</summary>
    public required bool EstadoActivo { get; init; }

    /// <summary>SuperAdmin que ejecuta el cambio (para auditoría <c>updated_by</c>).</summary>
    public Guid? ChangedBy { get; init; }
}
