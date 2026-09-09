using Flit.Admin.Domain.OtClientProcedures;
using Flit.Admin.Domain.OtQueries;
using Flit.Queries.Domain;

namespace Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;

/// <summary>
/// Por qué puede filtrar el organismo su bandeja (HU #12217), con las opciones que dependen de él ya
/// resueltas por el repositorio.
///
/// <para>El panel de filtros se pinta a partir de esta respuesta, así que un campo nuevo aparece en
/// pantalla sin desplegar frontend.</para>
/// </summary>
public sealed class GetOtBandejaFilterFieldsHandler(IOtClientProcedureRepository repository)
{
    public Task<IReadOnlyList<QueryFieldDto>?> HandleAsync(
        Guid otTenantId,
        Guid? transitOfficeIdOverride = null,
        CancellationToken cancellationToken = default) =>
        repository.GetBandejaFilterFieldsAsync(otTenantId, transitOfficeIdOverride, cancellationToken);
}

/// <summary>
/// Valida las condiciones de la bandeja contra su catálogo antes de que lleguen al repositorio.
///
/// <para>Las reglas son las del motor y no se reescriben; lo único propio es cómo se nombra la
/// pantalla cuando el campo no existe, que es lo que hace útil el mensaje.</para>
/// </summary>
public static class OtBandejaQueryConditions
{
    /// <summary>Mensaje del primer problema encontrado, o <c>null</c> si todas son válidas.</summary>
    public static string? Validate(IReadOnlyList<QueryCondition>? condiciones) =>
        QueryConditionValidator.Validate(
            OtBandejaQueryFieldCatalog.Instance, condiciones, "la bandeja del organismo");
}
