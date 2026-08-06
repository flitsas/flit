using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record CreateProcedureInstanceRequest(
    Guid TenantId,
    Guid? ProcedureTypeId,
    Guid CreatedByUserId,
    Guid? TransitOfficeId,
    string? Modalidad = null,
    // FEATURE-08 / HU-BE-08 (CFD-12): código del tipo. Tiene precedencia sobre modalidad.
    string? ProcedureTypeCode = null,
    // ICT — origen ('ict') y referencia externa (id del pre-trámite) para materialización idempotente.
    string? Origin = null,
    string? ExternalRef = null);

public sealed record ProcedureInstanceSummary(
    Guid Id,
    string ReferenceNumber,
    string Status,
    Guid ProcedureTypeId,
    Guid TenantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DraftFinalizedAt = null);

public sealed class CreateProcedureInstanceHandler(
    IProcedureInstanceRepository repo,
    IProcedureTypeRepository typeRepo,
    CaptureTypeSnapshotHandler? snapshotCapture = null,
    IOtOperabilityGate? otOperability = null,
    ITransitOfficeGrantGate? companyGrant = null,
    IProcedureFamilyCreationGate? familyCreationGate = null)
{
    // M0: mapeo modalidad → código canónico del procedure_type sembrado (dev seed).
    // matricula_inicial → MATRICULA_NUEVA (familia MATRICULAS), traspaso → TRASPASO_STANDARD (familia TRASPASO).
    // Resolvemos por el código estable y publicado para que la selección sea determinista incluso
    // si en el futuro coexisten varios tipos publicados en la misma familia.
    private static readonly Dictionary<string, string> ModalidadToCanonicalCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [TramiteModalidadEntradaCodes.MatriculaInicial] = "MATRICULA_NUEVA",
            [TramiteModalidadEntradaCodes.Traspaso] = "TRASPASO_STANDARD",
        };

    public async Task<(ProcedureInstanceSummary? Result, string? Error)> HandleAsync(
        CreateProcedureInstanceRequest request,
        CancellationToken ct = default)
    {
        var hasCode = !string.IsNullOrWhiteSpace(request.ProcedureTypeCode);
        var hasTypeId = request.ProcedureTypeId is { } id && id != Guid.Empty;
        var hasModalidad = !string.IsNullOrWhiteSpace(request.Modalidad);

        ProcedureType? procedureType;
        if (hasCode)
        {
            // CFD-12: el código tiene precedencia. Resuelve solo tipos publicados.
            procedureType = await typeRepo.GetByCodePublishedAsync(request.ProcedureTypeCode!.Trim(), ct);
            if (procedureType is null)
                return (null, "not_found");
        }
        else if (hasTypeId == hasModalidad)
        {
            // Exactamente uno de {procedureTypeId, modalidad} debe venir cuando no hay código.
            return (null, "invalid_request");
        }
        else if (hasTypeId)
        {
            procedureType = await typeRepo.GetByIdAsync(request.ProcedureTypeId!.Value, ct);
            if (procedureType is null)
                return (null, "not_found");

            if (procedureType.PublicationStatus != PublicationStatus.Published)
                return (null, "not_published");
        }
        else
        {
            // Resolución por modalidad: si la modalidad no es canónica o no hay tipo publicado → no disponible.
            if (!ModalidadToCanonicalCode.TryGetValue(request.Modalidad!.Trim(), out var canonicalCode))
                return (null, "modalidad_not_available");

            procedureType = await typeRepo.GetByCodePublishedAsync(canonicalCode, ct);
            if (procedureType is null)
                return (null, "modalidad_not_available");
        }

        // Compañía puede bloquear creación por familia (config admin → Trámites).
        var familyGate = familyCreationGate ?? new NullProcedureFamilyCreationGate();
        if (await familyGate.IsFamilyBlockedAsync(request.TenantId, procedureType.Family, ct))
            return (null, "procedure_family_blocked");

        // FEATURE-08 / HU-BE-02 (CFD-03): validaciones iniciales configurables por gate_profile,
        // evaluadas ANTES de persistir. La duplicidad por placa/VIN no se evalúa aquí (la instancia
        // nace vacía, sin placa/VIN) — se difiere al preflight (VinPolicyEvaluator).
        var gateProfile = ProcedureTypeGateProfile.FromJson(procedureType.GateProfile);
        if (gateProfile.RequiresInitialValidation)
        {
            var validation = await EvaluateInitialValidationsAsync(gateProfile, request, ct);
            if (!validation.Ok)
                return (null, validation.Code);
        }

        var now = DateTimeOffset.UtcNow;
        var year = now.Year;

        // Slice 4b: deriva modalidad/tipología desde la familia del tipo elegido para que el
        // wizard y el gating de documentos apliquen la modalidad correcta en runtime.
        var (modalidad, tipologia) = TipologiaResolver.FromFamily(procedureType.Family);

        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ProcedureTypeId = procedureType.Id,
            ReferenceNumber = string.Empty, // generado de forma resiliente en el repo (retry ante colisión)
            Status = TramiteEstado.Borrador,
            ModalidadEntrada = modalidad,
            TipologiaCodigo = tipologia,
            TransitOfficeId = request.TransitOfficeId,
            CreatedByUserId = request.CreatedByUserId,
            // ICT — correlación idempotente (null para trámites de plataforma). Van en el MISMO INSERT
            // que AddWithUniqueReferenceAsync: nada de UPDATE posterior que dejaría external_ref sin grabar.
            Origin = request.Origin,
            ExternalRef = request.ExternalRef,
            CreatedAt = now,
            CreatedBy = request.CreatedByUserId
        };

        instance.StatusHistory.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ProcedureInstanceId = instance.Id,
            FromStatus = null,
            ToStatus = TramiteEstado.Borrador,
            ChangedAt = now,
            ChangedBy = request.CreatedByUserId
        });

        var outcome = await repo.AddWithUniqueReferenceAsync(instance, year, ct);
        if (outcome == AddProcedureInstanceOutcome.ReferenceConflict)
            return (null, "reference_conflict");
        if (outcome == AddProcedureInstanceOutcome.ReferencedEntityMissing)
            return (null, "invalid_reference");

        // CFD-01 / AC#5 (BE-01-AC-03): al crear la instancia se captura un snapshot inmutable del
        // perfil de conformación del tipo. La instancia en curso quedará protegida de cambios de
        // configuración posteriores del tipo (lo consume el wizard dinámico en HU-BE-06). Se ejecuta
        // tras persistir la instancia (la FK del snapshot exige que exista). Opcional para no romper
        // callers/tests que no ejercen versionado; en runtime siempre se inyecta.
        if (snapshotCapture is not null)
        {
            await snapshotCapture.HandleAsync(
                instance.Id, procedureType.Id, request.TenantId, request.CreatedByUserId, ct);
        }

        return (ToSummary(instance), null);
    }

    /// <summary>
    /// Resuelve las señales disponibles al crear y delega en el gate puro (CFD-03). La regla de
    /// compañía se interpreta como el grant OT↔compañía (<see cref="ITransitOfficeGrantGate"/>) y la
    /// operabilidad como <see cref="IOtOperabilityGate"/>: ambas solo son evaluables cuando el OT
    /// destino es conocido en la creación; si no, quedan en <c>null</c> (no bloquean, se validan al
    /// radicar). La duplicidad por placa/VIN queda en <c>null</c> (no evaluable en creación).
    /// </summary>
    private async Task<GateResult> EvaluateInitialValidationsAsync(
        ProcedureTypeGateProfile profile,
        CreateProcedureInstanceRequest request,
        CancellationToken ct)
    {
        bool? companyRuleOk = null;
        bool? otAuthorized = null;

        if (request.TransitOfficeId is { } otId && otId != Guid.Empty)
        {
            if (profile.ValidateCompanyRule && companyGrant is not null)
                companyRuleOk = await companyGrant.IsEnabledForTenantAsync(request.TenantId, otId, ct);

            if (profile.ValidateOtOperability && otOperability is not null)
                otAuthorized = await otOperability.IsOperableAsync(otId, ct);
        }

        var context = new InitialProcedureValidationContext(
            CompanyRuleSatisfied: companyRuleOk,
            OtAuthorized: otAuthorized,
            DuplicateActiveExists: null);

        return InitialProcedureValidationGate.Evaluate(profile, context);
    }

    internal static ProcedureInstanceSummary ToSummary(ProcedureInstance e) =>
        new(e.Id, e.ReferenceNumber, e.Status, e.ProcedureTypeId, e.TenantId, e.CreatedAt, e.SubmittedAt, e.DraftFinalizedAt);
}
