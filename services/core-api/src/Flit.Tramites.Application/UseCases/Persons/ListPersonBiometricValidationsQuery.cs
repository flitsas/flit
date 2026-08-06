using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Persons;

/// <summary>
/// Detalle multi-validación por persona (HU #11272 / CF-06 / ADR-0040): una petición devuelve
/// hasta <see cref="MaxPageSize"/> validaciones del documento normalizado, con trámites vinculados
/// y metadatos de tracking. Supersede el ancla exclusivo a <c>validationId</c> de ADR-0036 (decisión 2);
/// el endpoint por id permanece.
/// </summary>
public sealed record PersonBiometricValidationsResponse(
    string DocumentType,
    string DocumentNumber,
    string? Name,
    IReadOnlyList<BiometricValidationDto> Validations,
    int Page,
    int PageSize,
    int Total,
    /// <summary>True si TODAS las validaciones de la página (y, si Total ≤ PageSize, del historial) son terminales.</summary>
    bool AllTerminal);

public sealed class ListPersonBiometricValidationsHandler(IProcedureInstanceRepository repo)
{
    public const int MinPageSize = 1;
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;

    public async Task<(PersonBiometricValidationsResponse? Result, string? Error)> HandleAsync(
        Guid tenantId,
        string? documentType,
        string? documentNumber,
        int page = 1,
        int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        var (tipo, numero) = DocumentCanonicalNormalization.Normalize(documentType, documentNumber);
        if (tipo.Length == 0 || numero.Length == 0)
            return (null, "documento_requerido");

        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, MinPageSize, MaxPageSize);
        var now = DateTimeOffset.UtcNow;

        var (rows, total, anyNonTerminal) = await repo.ListBiometricValidationsByPersonAsync(
            tenantId, tipo, numero, (page - 1) * pageSize, pageSize, ct);

        if (total == 0)
            return (null, "not_found");

        var linked = await LoadLinkedAsync(tenantId, rows, ct);
        var dtos = rows.Select(v => Enrich(v, now, linked)).ToList();
        var name = rows.Count > 0 ? rows[0].Name : null;

        return (new PersonBiometricValidationsResponse(
            rows[0].DocumentType,
            rows[0].DocumentNumber,
            name,
            dtos,
            page,
            pageSize,
            total,
            AllTerminal: !anyNonTerminal), null);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<LinkedProcedureDto>>> LoadLinkedAsync(
        Guid tenantId,
        IReadOnlyList<ProcedureInstanceBiometricValidation> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
            return new Dictionary<string, IReadOnlyList<LinkedProcedureDto>>();

        var documents = rows
            .Select(v => (v.DocumentType, v.DocumentNumber))
            .Distinct()
            .ToList();

        var summaries = await repo.ListLinkedProceduresByIdentityDocumentsAsync(tenantId, documents, ct);
        return summaries.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<LinkedProcedureDto>)kv.Value
                .Select(s => new LinkedProcedureDto(s.InstanceId, s.ReferenceNumber, s.Status, s.Modalidad))
                .ToList());
    }

    private static BiometricValidationDto Enrich(
        ProcedureInstanceBiometricValidation v,
        DateTimeOffset now,
        IReadOnlyDictionary<string, IReadOnlyList<LinkedProcedureDto>> linkedByIdentity)
    {
        var baseDto = IniciarBiometriaHandler.ToDto(v, now);
        var identityKey = BiometricRules.IdentidadKey(v.TenantId, v.DocumentType, v.DocumentNumber);
        var linked = linkedByIdentity.GetValueOrDefault(identityKey) ?? [];
        var linkedOthers = v.ProcedureInstanceId is { } primaryId
            ? linked.Where(p => p.InstanceId != primaryId).ToList()
            : linked;

        return baseDto with
        {
            ProcedureInstanceId = v.ProcedureInstanceId,
            ReferenceNumber = v.ProcedureInstance?.ReferenceNumber,
            Modalidad = v.ProcedureInstance?.ModalidadEntrada,
            LinkedProcedures = linkedOthers,
        };
    }
}
