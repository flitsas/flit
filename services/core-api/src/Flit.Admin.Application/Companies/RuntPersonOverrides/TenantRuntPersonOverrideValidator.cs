namespace Flit.Admin.Application.Companies.RuntPersonOverrides;

/// <summary>
/// Validador de los datos de entrada para crear/actualizar overrides de persona RUNT (HU #14642).
/// </summary>
internal static class TenantRuntPersonOverrideValidator
{
    private static readonly string[] AllowedDocTypes = ["CC", "CE", "PAS", "TI", "PPT"];

    /// <summary>
    /// Valida la solicitud de creación. Devuelve la lista de errores; vacía = válida.
    /// </summary>
    public static List<TenantRuntPersonOverrideValidationError> ValidateCreate(
        CreateTenantRuntPersonOverrideRequest request)
    {
        var errors = new List<TenantRuntPersonOverrideValidationError>();

        if (request.TenantId == Guid.Empty)
            errors.Add(new("tenantId", "El tenantId es requerido."));

        if (!AllowedDocTypes.Contains(request.DocumentType, StringComparer.OrdinalIgnoreCase))
            errors.Add(new("documentType", $"Tipo de documento no permitido. Válidos: {string.Join(", ", AllowedDocTypes)}."));

        if (string.IsNullOrWhiteSpace(request.DocumentNumber))
            errors.Add(new("documentNumber", "El número de documento es requerido."));

        if (string.IsNullOrWhiteSpace(request.FirstName))
            errors.Add(new("firstName", "El primer nombre es requerido."));

        if (string.IsNullOrWhiteSpace(request.FirstLastName))
            errors.Add(new("firstLastName", "El primer apellido es requerido."));

        if (string.IsNullOrWhiteSpace(request.Reason))
            errors.Add(new("reason", "El motivo (reason) es requerido."));

        return errors;
    }

    /// <summary>
    /// Valida la solicitud de actualización. Devuelve la lista de errores; vacía = válida.
    /// Si lleva campos, verifica que no estén vacíos.
    /// </summary>
    public static List<TenantRuntPersonOverrideValidationError> ValidateUpdate(
        UpdateTenantRuntPersonOverrideRequest request)
    {
        var errors = new List<TenantRuntPersonOverrideValidationError>();

        if (request.FirstName is not null && string.IsNullOrWhiteSpace(request.FirstName))
            errors.Add(new("firstName", "El primer nombre no puede quedar vacío."));

        if (request.FirstLastName is not null && string.IsNullOrWhiteSpace(request.FirstLastName))
            errors.Add(new("firstLastName", "El primer apellido no puede quedar vacío."));

        if (request.Reason is not null && string.IsNullOrWhiteSpace(request.Reason))
            errors.Add(new("reason", "El motivo (reason) no puede quedar vacío."));

        return errors;
    }
}
