using Flit.Tramites.Domain.Identity;

namespace Flit.Tramites.Application.UseCases.Persons;

/// <summary>
/// Respuesta del endpoint admin de vigencia de identidad por documento (HU #11751, ADR-0050): UN único
/// registro (no una grilla), con el documento ya normalizado devuelto para que el cliente confirme
/// contra qué llave resolvió. Nunca expone el certificado crudo del sello ni PII adicional del
/// documento más allá de lo que el propio caller ya envió.
/// </summary>
public sealed record IdentityVigenciaPorDocumentoResponse(
    string DocumentType,
    string DocumentNumber,
    string Status,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? ValidUntil);

/// <summary>
/// HU #11751 (ADR-0050) — handler del endpoint admin de consulta de vigencia por documento. Deriva el
/// tenant de la RUTA admin (lo resuelve el caller, vía <c>CompanyOwnTenantFilter</c>); este handler no
/// exige ni lee <c>X-Tenant-Id</c>. Delega la lectura y clasificación en
/// <see cref="IdentityVigenciaPorDocumentoResolver"/> — el mismo componente que reutiliza
/// <c>MandateSignerDirectory</c> (HU #11752) — para no duplicar la normalización ni la vigencia.
/// </summary>
public sealed class GetIdentityVigenciaPorDocumentoHandler(IdentityVigenciaPorDocumentoResolver resolver)
{
    public async Task<(IdentityVigenciaPorDocumentoResponse? Result, string? Error)> HandleAsync(
        Guid tenantId,
        string? documentType,
        string? documentNumber,
        CancellationToken ct = default)
    {
        var (tipo, numero) = DocumentCanonicalNormalization.Normalize(documentType, documentNumber);
        if (tipo.Length == 0 || numero.Length == 0)
            return (null, "documento_requerido");

        var result = await resolver.ResolveAsync(tenantId, tipo, numero, DateTimeOffset.UtcNow, ct)
            .ConfigureAwait(false);

        return (new IdentityVigenciaPorDocumentoResponse(
            tipo, numero, result.Status, result.ValidatedAt, result.ValidUntil), null);
    }
}
