using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

public sealed record CreateProcedureTypeRequest(
    string Family,
    string Code,
    string Name,
    string? Description);

public sealed record ProcedureTypeSummary(
    Guid Id,
    string Code,
    string Name,
    string Family,
    string PublicationStatus,
    bool IsActive,
    bool WizardEnabled,
    DateTimeOffset? PublishedAt);

public sealed class CreateProcedureTypeHandler(IProcedureTypeRepository repository)
{
    /// <summary>
    /// Forma del código: MAYÚSCULAS, dígitos y guion bajo. No es cosmética — el código es la llave
    /// con la que el tipo viaja a ICT, a Quipux y a los snapshots congelados de cada expediente, y
    /// ahí un espacio o un acento se convierte en un fallo silencioso de emparejamiento.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex FormaDelCodigo =
        new("^[A-Z][A-Z0-9_]{2,59}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<(ProcedureTypeSummary? Result, string? Error)> HandleAsync(
        CreateProcedureTypeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!FormaDelCodigo.IsMatch(code))
            return (null, "invalid_code");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "invalid_name");

        var family = request.Family?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Domain.Enums.ProcedureFamilyCodes.IsValid(family))
            return (null, "invalid_family");

        // El UNIQUE de la base lo rechazaría igual, pero con un 500 del constraint en vez de un
        // mensaje que diga qué código está ocupado.
        if (await repository.CodeExistsAsync(code, ct))
            return (null, "code_taken");

        var entity = new ProcedureType
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            Family = family,
            Description = request.Description,
            IsActive = true,
            // Nace en BORRADOR y con la barrera apagada: un tipo recién creado no tiene recorrido,
            // así que ofrecerlo al gestor sería prometer un asistente vacío.
            PublicationStatus = Domain.Enums.PublicationStatus.Draft,
            WizardEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(entity, ct);
        await repository.SaveChangesAsync(ct);

        return (ToSummary(entity), null);
    }

    internal static ProcedureTypeSummary ToSummary(ProcedureType e) =>
        new(e.Id, e.Code, e.Name, e.Family, e.PublicationStatus, e.IsActive, e.WizardEnabled, e.PublishedAt);
}
