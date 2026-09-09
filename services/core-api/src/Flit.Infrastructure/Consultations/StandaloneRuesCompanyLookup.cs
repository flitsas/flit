using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Adaptador del puerto <see cref="IStandaloneRuesCompanyLookup"/> (Feature #12201,
/// ADR-0056-generacion-documental-standalone): consulta el RUES por NIT reusando
/// <see cref="RuesActorJuridicalLookup"/>, el MISMO núcleo que usan
/// <c>RuesPersonLookupHandler</c> (con instancia) y <c>RuesPreviewHandler</c> (sin instancia).
///
/// <para>Se pasa <see cref="Guid.Empty"/> como instancia —convenio «sin trámite» que ese núcleo ya
/// acepta— para no abrir una segunda resolución del proveedor <c>verifik_rues</c> ni una segunda
/// plantilla <c>RUES_ACTOR_JURIDICAL</c> que puedan divergir. El único cambio en
/// <c>Flit.Tramites.*</c> es la visibilidad de esa clase: ni una firma ni un comportamiento.</para>
///
/// <para>La consulta es EN VIVO y efímera: aquí no se persiste nada (ni <c>field_values</c> ni
/// certificaciones canónicas). La fila del documento y su snapshot los escribe el handler.</para>
/// </summary>
internal sealed class StandaloneRuesCompanyLookup : IStandaloneRuesCompanyLookup
{
    private readonly IConsultationProviderRegistry _registry;
    private readonly TimeProvider _timeProvider;

    public StandaloneRuesCompanyLookup(
        IConsultationProviderRegistry registry,
        TimeProvider? timeProvider = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StandaloneRuesLookupResult> ConsultAsync(
        Guid tenantId,
        string nit,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        var (result, error) = await RuesActorJuridicalLookup
            .ConsultAsync(_registry, Guid.Empty, tenantId, nit, cancellationToken)
            .ConfigureAwait(false);

        if (error is not null)
        {
            // "provider_not_found" / "provider_unavailable": normalizados, sin excepción cruda.
            return new StandaloneRuesLookupResult(false, EmptyFields, now, error);
        }

        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in result!.HydratedFields)
        {
            fields[field.FieldKey] = field.ValueText;
        }

        // Sin razón social no hay coincidencia: el RUES respondió, pero el NIT no existe. No es una
        // caída del proveedor (esa ya salió arriba con Error).
        var found = !string.IsNullOrWhiteSpace(
            RuesActorJuridicalLookup.GetHydrated(result.HydratedFields, "rues_razon_social"));

        return new StandaloneRuesLookupResult(
            found,
            found ? fields : EmptyFields,
            result.QueriedAt ?? now,
            null);
    }

    private static readonly IReadOnlyDictionary<string, string?> EmptyFields =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
