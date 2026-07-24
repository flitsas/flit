namespace Flit.Admin.Application.Companies.Deeds;

/// <summary>
/// Validaciones compartidas del alta/edición de escrituras (HU #10902): campos requeridos, rango de
/// vigencia (<c>hasta ≥ desde</c>) y multi-selección de compañías. Emite <see cref="DeedValidationError"/>
/// con códigos estables para el FE (422). No incluye PII.
/// </summary>
internal static class DeedValidation
{
    public static List<DeedValidationError> ValidateMetadata(
        string? description,
        DateOnly vigenciaDesde,
        DateOnly vigenciaHasta,
        IReadOnlyList<Guid>? representedCompanyIds)
    {
        var errors = new List<DeedValidationError>();

        if (string.IsNullOrWhiteSpace(description))
        {
            errors.Add(new DeedValidationError(
                "description", "requerido", "La descripción es obligatoria."));
        }

        if (vigenciaHasta < vigenciaDesde)
        {
            errors.Add(new DeedValidationError(
                "vigenciaHasta", "vigencia_invalida",
                "La vigencia hasta no puede ser anterior a la vigencia desde."));
        }

        var companies = representedCompanyIds ?? [];
        if (companies.Count == 0 || companies.All(id => id == Guid.Empty))
        {
            errors.Add(new DeedValidationError(
                "representedCompanyIds", "companias_requeridas",
                "Debe seleccionarse al menos una compañía registrada."));
        }

        return errors;
    }

    /// <summary>Compañías válidas (sin <see cref="Guid.Empty"/> ni duplicados), preservando el orden.</summary>
    public static IReadOnlyList<Guid> NormalizeCompanies(IReadOnlyList<Guid>? representedCompanyIds) =>
        [.. (representedCompanyIds ?? []).Where(id => id != Guid.Empty).Distinct()];
}
