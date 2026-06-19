namespace Flit.Admin.Application.Companies.Whitelist.AddWhitelistEmails;

/// <summary>
/// Comando de alta masiva en la lista blanca (AC4). Combina el <see cref="TenantId"/>
/// de ruta con el payload y la identidad del SuperAdmin que ejecuta el alta (para la
/// auditoría).
/// </summary>
public sealed class AddWhitelistEmailsCommand
{
    public required Guid TenantId { get; init; }

    public required AddWhitelistEmailsRequest Request { get; init; }

    /// <summary>Id del usuario (claim <c>sub</c> del JWT) que da de alta. Opcional.</summary>
    public Guid? AddedBy { get; init; }

    /// <summary>Id de correlación opcional para trazabilidad de la auditoría.</summary>
    public Guid? CorrelationId { get; init; }
}
