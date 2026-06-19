namespace Flit.Admin.Domain.Companies.Whitelist;

/// <summary>
/// Resultado de un alta masiva en la lista blanca (HU #10191, AC4). Distingue los
/// correos efectivamente insertados de los omitidos por ya existir
/// (idempotencia frente a <c>uq_tenant_whitelist_users_tenant_email</c>).
/// </summary>
/// <param name="Added">Correos nuevos insertados (uno por fila + una fila de auditoría).</param>
/// <param name="Skipped">Correos ya presentes, omitidos sin error.</param>
public sealed record WhitelistAddOutcome(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped);
