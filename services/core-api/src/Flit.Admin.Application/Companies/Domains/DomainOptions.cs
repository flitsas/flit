namespace Flit.Admin.Application.Companies.Domains;

/// <summary>
/// Configuración de dominios (HU #12416 AC3, sección <c>Domains</c>). Infraestructura la liga con
/// <c>IOptions&lt;DomainOptions&gt;</c> y expone el POCO resuelto (patrón
/// <c>ImprontaValidationPolicyOptions</c>) para que <see cref="RegisterDomain.RegisterDomainHandler"/>
/// no dependa de <c>Microsoft.Extensions.Options</c>.
/// </summary>
public sealed class DomainOptions
{
    public const string SectionName = "Domains";

    /// <summary>Dominio de FLIT y sus subdominios, y el portal de organismos de tránsito. Ver <see cref="Flit.Admin.Domain.Companies.Domains.ReservedHosts"/>.</summary>
    public IReadOnlyList<string> Reserved { get; set; } = [];

    /// <summary>Destino CNAME que el SuperAdmin debe apuntar (borde #12421), informativo en el contrato de instrucciones DNS.</summary>
    public string EdgeTarget { get; set; } = string.Empty;
}
