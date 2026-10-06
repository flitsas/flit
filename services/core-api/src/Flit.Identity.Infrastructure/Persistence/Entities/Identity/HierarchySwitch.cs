namespace Flit.Infrastructure.Persistence.Entities.Identity;

/// <summary>
/// Interruptor global de la jerarquía de clientes — <c>identity.hierarchy_switches</c>
/// (HU #12323, ADR-0057). Tres filas fijas: <see cref="GroupReadScopeKey"/>,
/// <see cref="InheritedConfigurationKey"/> y <see cref="NetworkDocumentsConcesionKey"/> (HU #12410). Se conmutan sin despliegue con un UPDATE de
/// <see cref="IsEnabled"/>; apagado ⇒ degradación segura (alcance <c>Single</c> / sin herencia),
/// nunca fuga. Ninguno reutiliza <see cref="Tenant.IsGroupParent"/>.
/// </summary>
public sealed class HierarchySwitch
{
    /// <summary>Apagado: toda cabeza de grupo resuelve alcance <c>Single</c> (deja de leer a sus hijos).</summary>
    public const string GroupReadScopeKey = "group_read_scope";

    /// <summary>Apagado: los hijos dejan de heredar la configuración del padre (lista de OT; Feature #12256).</summary>
    public const string InheritedConfigurationKey = "inherited_configuration";

    /// <summary>
    /// HU #12410: apagado (por defecto) ⇒ una cabeza CONCESION recibe 403 en las rutas de documentos de
    /// la red (<c>/api/v1/tramites/network/instances/{id}/attachments*</c>); MARCA_BLANCA no lo consulta.
    /// </summary>
    public const string NetworkDocumentsConcesionKey = "network_documents_concesion";

    public Guid Id { get; set; }

    public string SwitchKey { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public long RowVersion { get; set; }
}
