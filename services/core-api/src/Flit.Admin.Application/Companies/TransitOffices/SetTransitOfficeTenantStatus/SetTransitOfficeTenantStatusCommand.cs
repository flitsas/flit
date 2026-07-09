namespace Flit.Admin.Application.Companies.TransitOffices.SetTransitOfficeTenantStatus;

/// <summary>Comando para activar/desactivar un tenant OT con auditoría (HU #10518).</summary>
public sealed class SetTransitOfficeTenantStatusCommand
{
    /// <summary>Identificador del tenant OT (<c>identity.tenants.id</c>).</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Estado destino: <c>true</c> activa, <c>false</c> desactiva.</summary>
    public required bool EstadoActivo { get; init; }

    /// <summary>SuperAdmin que ejecuta el cambio (auditoría <c>changed_by</c>/<c>updated_by</c>).</summary>
    public Guid? ChangedBy { get; init; }

    /// <summary>Correlación opcional para trazar el cambio en la auditoría.</summary>
    public Guid? CorrelationId { get; init; }
}
