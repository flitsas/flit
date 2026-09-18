using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

// ── HU #12708 — Validación de Identidad de la red (cabeza de grupo, solo lectura) ───────────────
//
// Misma lectura que la vista propia de la compañía (#12706) con el alcance de la red: la cabeza lee
// {cabeza} ∪ hijas vía TenantScope (resuelto en BD por el middleware, nunca por la petición). Qué NO ve
// la cabeza de los datos de una hija, por minimización: el correo completo (va enmascarado) y el enlace
// de captura. Ningún DTO de estas rutas trae certificado, fotografías ni token.

/// <summary>Errores propios de las rutas de identidad de la red (además de los de <see cref="NetworkScopePolicy"/>).</summary>
public static class NetworkIdentityErrors
{
    /// <summary>El historial de una persona exige la compañía: la misma cédula puede estar en dos hijas.</summary>
    public const string ChildRequired = "network_child_required";
}

/// <summary>
/// Fila de la grilla por persona de la red: los mismos campos que <see cref="TenantBiometricPersonDto"/>
/// salvo el enlace de captura y su vencimiento, y con el correo enmascarado.
/// </summary>
public sealed record NetworkIdentityPersonDto(
    Guid TenantId,
    string? TenantName,
    string DocumentType,
    string DocumentNumber,
    string Name,
    string Status,
    int ValidationCount,
    string? WorstAlertKind,
    Guid LatestValidationId,
    Guid? InstanceId,
    string? ReferenceNumber,
    string? Modalidad,
    string? PartyRole,
    string Email,
    string Provider,
    int? Score,
    bool Expired,
    int Intentos,
    int MaxIntentos,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ValidatedAt,
    DateTimeOffset? ValidUntil,
    int? DaysRemaining);

/// <summary>Respuesta de <c>GET /network/identity-validations/by-person</c>.</summary>
public sealed record NetworkIdentityPersonsResponse(
    IReadOnlyList<NetworkIdentityPersonDto> Persons,
    BiometricValidationStatsDto Stats,
    int Page,
    int PageSize,
    int Total);

/// <summary>Minimización de datos personales para la cabeza de red.</summary>
public static class NetworkIdentityRedaction
{
    public static NetworkIdentityPersonDto ToNetwork(TenantBiometricPersonDto p) => new(
        p.TenantId,
        p.TenantName,
        p.DocumentType,
        p.DocumentNumber,
        p.Name,
        p.Status,
        p.ValidationCount,
        p.WorstAlertKind,
        p.LatestValidationId,
        p.InstanceId,
        p.ReferenceNumber,
        p.Modalidad,
        p.PartyRole,
        EditarPrevalidacionHandler.MaskEmail(p.Email),
        p.Provider,
        p.Score,
        p.Expired,
        p.Intentos,
        p.MaxIntentos,
        p.CreatedAt,
        p.ValidatedAt,
        p.ValidUntil,
        p.DaysRemaining);

    /// <summary>Validación del historial de una persona: correo enmascarado y sin enlace de captura.</summary>
    public static BiometricValidationDto ToNetwork(BiometricValidationDto v) => v with
    {
        Email = EditarPrevalidacionHandler.MaskEmail(v.Email),
        RegisteredEmail = v.RegisteredEmail is null ? null : EditarPrevalidacionHandler.MaskEmail(v.RegisteredEmail),
        CaptureUrl = null,
    };
}

/// <summary>
/// <c>GET /network/identity-validations/by-person</c> — personas de la cabeza y sus hijas (o de una sola
/// con <c>childTenantId</c>), con la compañía de cada fila y los KPIs del mismo conjunto.
/// </summary>
public sealed class NetworkListIdentityPersonsHandler(ListTenantBiometricPersonsHandler inner)
{
    public async Task<(NetworkIdentityPersonsResponse? Result, string? Error)> HandleAsync(
        TenantScope? scope,
        Guid? childTenantId,
        TenantBiometricPersonListQuery query,
        CancellationToken ct = default)
    {
        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return (null, scopeError);

        var (effective, narrowError) = NetworkScopePolicy.Narrow(scope!, childTenantId);
        if (narrowError is not null)
            return (null, narrowError);

        var (result, error) = await inner.HandleAsync(effective!, query, ct);
        if (error is not null)
            return (null, error);

        return (new NetworkIdentityPersonsResponse(
            result!.Persons.Select(NetworkIdentityRedaction.ToNetwork).ToList(),
            result.Stats,
            result.Page,
            result.PageSize,
            result.Total), null);
    }
}

/// <summary>
/// <c>GET /network/identity-validations/by-person/detail</c> — historial de UNA persona en UNA compañía
/// de la red. La compañía es obligatoria (<see cref="NetworkIdentityErrors.ChildRequired"/>): sin ella
/// habría que adivinar en qué hija está la cédula, y podría estar en dos.
/// </summary>
public sealed class NetworkListPersonIdentityValidationsHandler(ListPersonBiometricValidationsHandler inner)
{
    public async Task<(PersonBiometricValidationsResponse? Result, string? Error)> HandleAsync(
        TenantScope? scope,
        Guid? childTenantId,
        string? documentType,
        string? documentNumber,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return (null, scopeError);

        if (childTenantId is not { } child || child == Guid.Empty)
            return (null, NetworkIdentityErrors.ChildRequired);

        var (effective, narrowError) = NetworkScopePolicy.Narrow(scope!, child);
        if (narrowError is not null)
            return (null, narrowError);

        var tenantId = effective!.WriteTenantId!.Value;
        var (result, error) = await inner.HandleAsync(tenantId, documentType, documentNumber, page, pageSize, ct);
        if (error is not null)
            return (null, error);

        return (result! with
        {
            Validations = result.Validations.Select(NetworkIdentityRedaction.ToNetwork).ToList(),
        }, null);
    }
}

/// <summary>
/// <c>GET /network/identity-validations/{validationId}/audit</c> — bitácora de una validación de la red.
/// Una validación fuera del alcance responde exactamente igual que un id inexistente (<c>not_found</c>).
/// Devuelve también la compañía dueña para la auditoría del acceso.
/// </summary>
public sealed class NetworkGetIdentityAuditHandler(
    IProcedureInstanceRepository repo,
    GetIdentityAuditByValidationHandler inner)
{
    public async Task<(IdentityAuditResponse? Result, Guid? OwnerTenantId, string? Error)> HandleAsync(
        TenantScope? scope,
        Guid validationId,
        CancellationToken ct = default)
    {
        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return (null, null, scopeError);

        var validation = await repo.GetBiometricByIdAsync(validationId, ct);
        if (validation is null || !scope!.ReadTenantIds.Contains(validation.TenantId))
            return (null, null, "not_found");

        var (result, error) = await inner.HandleAsync(validation.TenantId, validationId, ct);
        return (result, error is null ? validation.TenantId : null, error);
    }
}
