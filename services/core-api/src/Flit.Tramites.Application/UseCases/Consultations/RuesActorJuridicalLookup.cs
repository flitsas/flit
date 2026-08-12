namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Núcleo de resolución del proveedor RUES (persona JURÍDICA por NIT) compartido entre
/// <see cref="RuesPersonLookupHandler"/> (con instancia, persiste) y <see cref="RuesPreviewHandler"/>
/// (sin instancia, HU sin ADO 2026-08-11, NO persiste). Antes de esta extracción cada handler
/// resolvía el proveedor y armaba el <see cref="ConsultationContext"/> por su cuenta: dos copias del
/// mismo <c>registry.Resolve("verifik_rues")</c> + plantilla <c>RUES_ACTOR_JURIDICAL</c> que se
/// habrían podido divergir con el tiempo (p. ej. si alguien cambiaba la clave del provider en un
/// handler y olvidaba el otro).
/// </summary>
internal static class RuesActorJuridicalLookup
{
    internal const string ProviderKey = "verifik_rues";
    internal const string TemplateCode = "RUES_ACTOR_JURIDICAL";

    /// <summary>
    /// Resuelve el proveedor RUES y consulta el NIT. <paramref name="instanceId"/> es
    /// <see cref="Guid.Empty"/> cuando no hay trámite todavía (preview) — mismo convenio que usa
    /// <c>RunPreflightPreviewHandler.RunVehiculoAsync</c> para "sin instancia". Devuelve
    /// <c>Error = "provider_not_found"</c> si el proveedor no está registrado; nunca lanza.
    /// </summary>
    internal static async Task<(ConsultationResult? Result, string? Error)> ConsultAsync(
        IConsultationProviderRegistry registry,
        Guid instanceId,
        Guid tenantId,
        string nit,
        CancellationToken ct)
    {
        var provider = registry.Resolve(ProviderKey);
        if (provider is null)
            return (null, "provider_not_found");

        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nit"] = nit,
            ["documentNumber"] = nit,
        };

        var ctx = new ConsultationContext(instanceId, tenantId, TemplateCode, fieldValues);
        var result = await provider.ConsultAsync(ctx, ct);
        return (result, null);
    }

    /// <summary>Busca <paramref name="fieldKey"/> en los campos hidratados por la consulta.</summary>
    internal static string? GetHydrated(IReadOnlyList<HydratedField> fields, string fieldKey)
    {
        foreach (var f in fields)
        {
            if (string.Equals(f.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
                return f.ValueText;
        }

        return null;
    }
}
