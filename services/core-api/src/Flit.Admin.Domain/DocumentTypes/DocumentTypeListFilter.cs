namespace Flit.Admin.Domain.DocumentTypes;

/// <summary>
/// Paginación del listado del catálogo de tipos de documento (HU #10193, AC2).
/// <see cref="Page"/> y <see cref="PageSize"/> ya vienen normalizados por la capa
/// de aplicación. El orden es siempre por nombre ascendente.
/// </summary>
public sealed class DocumentTypeListFilter
{
    /// <summary>Página solicitada (1-based, ya normalizada).</summary>
    public required int Page { get; init; }

    /// <summary>Tamaño de página (ya normalizado dentro de límites válidos).</summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// Si es <c>true</c> incluye también los inactivos (soft-deleted). Por defecto
    /// el listado devuelve solo los activos (regla de negocio del handoff).
    /// </summary>
    public bool IncludeInactive { get; init; }

    /// <summary>Texto libre sobre código o nombre (ya recortado).</summary>
    public string? Search { get; init; }

    /// <summary>
    /// <c>true</c> = solo autogenerados, <c>false</c> = solo cargue, <c>null</c> = ambos.
    /// </summary>
    public bool? IsSystemGenerated { get; init; }

    /// <summary>
    /// <c>true</c> = solo activos, <c>false</c> = solo inactivos, <c>null</c> = según
    /// <see cref="IncludeInactive"/>.
    /// </summary>
    public bool? IsActive { get; init; }
}
