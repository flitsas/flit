namespace Flit.Admin.Domain.OtRequirements;

/// <summary>
/// Se lanza cuando un tenant intenta configurar requisitos de OT sin ser un organismo de tránsito
/// aprovisionado (sin perfil ni grant), o cuando la oficina resuelta ya pertenece a otro tenant.
/// Evita que un tenant sin oficina propia caiga al OT por defecto y ocupe (unique
/// <c>transit_office_id</c>) la configuración de otro OT. La capa API lo mapea a HTTP 422.
/// </summary>
public sealed class OtRequirementsScopeException : Exception
{
    public OtRequirementsScopeException(string message)
        : base(message)
    {
    }
}
