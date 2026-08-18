namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Una consulta guardada por un usuario de la empresa sobre sus propios pre-trámites de
/// Integración con Terceros (ICT).
///
/// <para>El gemelo de <see cref="CompanySavedQueryEntity"/>, mismo alcance <c>empresa + usuario</c>
/// y misma razón para vivir en la base y no en <c>localStorage</c>: se pierden al cambiar de equipo
/// o de navegador, y una consulta que hay que volver a armar cada vez no llega a usarse.</para>
///
/// <para>Es una tabla propia y no una fila más de <c>company_saved_queries</c> aunque el alcance sea
/// idéntico: lo que cada una nombra en su <c>definicion</c> es un catálogo de campos distinto
/// (<c>IctQueryFieldCatalog</c> pregunta por el pipeline de validación de pre-trámites,
/// <c>CompanyQueryFieldCatalog</c> por el ciclo del trámite ya radicado), y mezclarlas en una sola
/// tabla obligaría a una columna de discriminación que solo sirve para volver a separarlas en cada
/// consulta.</para>
/// </summary>
public sealed class IctSavedQueryEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string? Descripcion { get; set; }

    /// <summary>
    /// La definición serializada (<c>QueryDefinition</c>). Se guarda como JSON y no en columnas
    /// porque el catálogo de campos crece: una tabla de condiciones obligaría a una migración cada
    /// vez que se agrega un campo consultable, que es exactamente lo que el catálogo evita.
    /// </summary>
    public string Definicion { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
