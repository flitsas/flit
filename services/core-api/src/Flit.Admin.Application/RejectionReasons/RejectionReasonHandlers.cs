using Flit.Admin.Domain.RejectionReasons;

namespace Flit.Admin.Application.RejectionReasons;

/// <summary>Desenlace común de las escrituras del catálogo.</summary>
public enum RejectionReasonOutcome
{
    Ok,
    NotFound,
    ValidationFailed,
}

public sealed record RejectionReasonResult(
    RejectionReasonOutcome Outcome,
    RejectionReasonResponse? Reason = null,
    string? Error = null)
{
    public static RejectionReasonResult Ok(RejectionReasonResponse reason) =>
        new(RejectionReasonOutcome.Ok, reason);

    public static RejectionReasonResult NotFound() => new(RejectionReasonOutcome.NotFound);

    public static RejectionReasonResult Invalid(string error) =>
        new(RejectionReasonOutcome.ValidationFailed, Error: error);
}

/// <summary>
/// Lista el catálogo. El modal de rechazo del OT pide solo las activas de una familia; la
/// consola de SuperAdmin pide todas.
/// </summary>
public sealed class ListRejectionReasonsHandler(IRejectionReasonRepository repository)
{
    public async Task<IReadOnlyList<RejectionReasonResponse>> HandleAsync(
        string? familia,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var items = await repository
            .ListAsync(NormalizeFamilia(familia), includeInactive, cancellationToken)
            .ConfigureAwait(false);

        return items.Select(RejectionReasonMapper.ToResponse).ToList();
    }

    // Una familia desconocida devuelve lista vacía en vez de todas: es preferible que el modal
    // se vea vacío (y se note) a que ofrezca causales de otro proceso.
    // Se normaliza a mayúsculas porque la comparación contra la columna es exacta: el mismo valor
    // escrito en minúsculas habría vaciado el modal sin que nada lo explicara.
    private static string? NormalizeFamilia(string? familia) =>
        string.IsNullOrWhiteSpace(familia) ? null : familia.Trim().ToUpperInvariant();
}

public sealed class CreateRejectionReasonHandler(IRejectionReasonRepository repository)
{
    public async Task<RejectionReasonResult> HandleAsync(
        CreateRejectionReasonRequest request,
        Guid? createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var error = RejectionReasonValidator.Validate(
            request.Code, request.Description, request.Familia);
        if (error is not null)
        {
            return RejectionReasonResult.Invalid(error);
        }

        var code = RejectionReasonValidator.NormalizeCode(request.Code!);
        if (await repository.CodeExistsAsync(code, null, cancellationToken).ConfigureAwait(false))
        {
            return RejectionReasonResult.Invalid($"Ya existe una causal con el código '{code}'.");
        }

        var created = await repository
            .CreateAsync(
                code,
                request.Description!.Trim(),
                request.Familia!,
                request.SortOrder ?? DefaultSortOrder,
                createdBy,
                cancellationToken)
            .ConfigureAwait(false);

        return RejectionReasonResult.Ok(RejectionReasonMapper.ToResponse(created));
    }

    // Sin orden explícito la causal nueva va al final, junto a «Otros», en vez de colarse arriba.
    private const int DefaultSortOrder = 500;
}

public sealed class UpdateRejectionReasonHandler(IRejectionReasonRepository repository)
{
    public async Task<RejectionReasonResult> HandleAsync(
        Guid id,
        UpdateRejectionReasonRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return RejectionReasonResult.NotFound();
        }

        var error = RejectionReasonValidator.Validate(
            request.Code, request.Description, request.Familia);
        if (error is not null)
        {
            return RejectionReasonResult.Invalid(error);
        }

        var code = RejectionReasonValidator.NormalizeCode(request.Code!);
        if (await repository.CodeExistsAsync(code, id, cancellationToken).ConfigureAwait(false))
        {
            return RejectionReasonResult.Invalid($"Ya existe una causal con el código '{code}'.");
        }

        var updated = await repository
            .UpdateAsync(
                id,
                code,
                request.Description!.Trim(),
                request.Familia!,
                request.SortOrder ?? existing.SortOrder,
                updatedBy,
                cancellationToken)
            .ConfigureAwait(false);

        return updated is null
            ? RejectionReasonResult.NotFound()
            : RejectionReasonResult.Ok(RejectionReasonMapper.ToResponse(updated));
    }
}

public sealed class SetRejectionReasonActiveHandler(IRejectionReasonRepository repository)
{
    public async Task<RejectionReasonResult> HandleAsync(
        Guid id,
        bool isActive,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var updated = await repository
            .SetActiveAsync(id, isActive, updatedBy, cancellationToken)
            .ConfigureAwait(false);

        return updated is null
            ? RejectionReasonResult.NotFound()
            : RejectionReasonResult.Ok(RejectionReasonMapper.ToResponse(updated));
    }
}
