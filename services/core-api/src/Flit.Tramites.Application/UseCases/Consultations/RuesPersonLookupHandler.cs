using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Lookup dedicado de persona JURÍDICA en RUES por NIT, para autopoblar la razón social del
/// actor jurídico en el wizard (bifurcación del "Consultar RUNT" cuando personType=juridical).
/// Delega en el provider <c>verifik_rues</c> y, si la instancia está en borrador, PERSISTE los campos
/// RUES en <c>field_values</c> (Source="consultation", HU #10856) para que el certificado RUES los
/// muestre — igual que hace el RUNT. Fuera de draft no persiste (el autopoblado sigue funcionando).
/// Es el análogo jurídico de <see cref="RuntPersonLookupHandler"/> (conductor / persona natural).
/// </summary>
/// <remarks>
/// <para>HU #10878 (Feature #10862, CF-04, ADR-0030/ADR-0031): fuente <c>RUES</c>,
/// <c>documentType = "NIT"</c> implícito.</para>
/// <para><b>HU #10955 (AC1, extendido a RUES; revierte parcialmente HU #10878/ADR-0031):</b> igual
/// que <see cref="RuntPersonLookupHandler"/>, la IDENTIDAD de un actor jurídico se consulta SIEMPRE
/// en vivo contra el RUES. Este handler YA NO llama a
/// <see cref="ExternalQueryCacheService.TryReusePersonAsync"/> antes de resolver el proveedor —
/// nunca sirve un HIT cacheado, exista o no <c>PersonDataConsent</c> en <c>granted</c> para ese NIT.
/// Sí se conserva <see cref="ExternalQueryCacheService.SavePersonResultAsync"/> al final: la caché se
/// sigue escribiendo con el resultado fresco para no tocar otros consumidores del mismo mecanismo.
/// <c>PersonDataConsent</c> NO se borra ni se toca: se conserva para auditoría.</para>
/// </remarks>
public sealed class RuesPersonLookupHandler(
    IProcedureInstanceRepository repo,
    IConsultationProviderRegistry registry,
    ExternalQueryCacheService cacheService,
    Certifications.ICertificationIngestionService? certificationIngestion = null)
{
    private const string ConsultationSource = "consultation";

    private const string RuesSourceCode = "RUES";
    private const string DocumentTypeNit = "NIT";

    public async Task<(RuesPersonDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        string? documentNumber,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            return (null, "invalid_request");

        var instance = await repo.GetByIdWithDetailsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var nit = documentNumber.Trim();
        var now = DateTimeOffset.UtcNow;

        // HU #10955 (AC1, extendido a RUES) — la identidad SIEMPRE se consulta en vivo: ya no se
        // intenta un HIT de ExternalQueryCacheService.TryReusePersonAsync antes de resolver el
        // proveedor (ver remarks de la clase). El resultado fresco SÍ se sigue cacheando al final
        // (SavePersonResultAsync).
        var (consultResult, consultError) = await RuesActorJuridicalLookup.ConsultAsync(
            registry, instanceId, tenantId, nit, ct);
        if (consultError is not null)
            return (null, consultError);
        var result = consultResult!;

        // Persistencia de campos RUES: solo cuando el expediente admite edición (borrador o
        // rechazado+subsanación). Fuera de eso el trigger de BD bloquea field_values.
        if (TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva)
            && result.HydratedFields.Count > 0)
        {
            UpsertHydrated(instance, tenantId, result.HydratedFields);

            // HU #11133 — además de las llaves `rues_*` de instancia (que autopoblan el asistente y
            // solo pueden representar a UNA compañía), se congela el resultado POR NIT. Es la fuente
            // del certificado: se consulta al registrar y no se vuelve a preguntar al proveedor.
            var snapshotPrevio = instance.FieldValues
                .FirstOrDefault(f => string.Equals(f.FieldKey, RuesSnapshots.FieldKey, StringComparison.OrdinalIgnoreCase))
                ?.ValueText;
            var snapshot = RuesSnapshots.Merge(snapshotPrevio, nit, result.HydratedFields, now);
            if (snapshot is not null)
            {
                UpsertHydrated(instance, tenantId, [new HydratedField(RuesSnapshots.FieldKey, snapshot, null)]);
            }

            await repo.SaveChangesAsync(ct);
        }

        // HU #11306 (Feature #11301, ADR-0041) — el registro mercantil entra al almacén canónico.
        // Va FUERA del gate de edición de arriba a propósito: ese gate existe porque el trigger de
        // inmutabilidad bloquea field_values fuera de borrador, y la tabla canónica no tiene esa
        // restricción (el congelamiento es explícito, por frozen_at). Así, una consulta hecha con el
        // trámite ya avanzado deja de tirarse a la basura.
        await IngestCertificationsAsync(instanceId, tenantId, result, now, ct);

        var dto = BuildDtoFromFields(result.HydratedFields, nit, ResolveMode());

        // HU #10878 (AC2): cachea el resultado fresco para reúsos futuros dentro del TTL de la fuente.
        await cacheService.SavePersonResultAsync(
            tenantId, RuesSourceCode, DocumentTypeNit, nit, instanceId, result.HydratedFields, now, ct);

        return (dto, null);
    }

    /// <summary>
    /// Entrega el registro mercantil al almacén canónico. Best-effort: la consulta ya se respondió y
    /// el asistente ya tiene sus datos.
    /// </summary>
    private async Task IngestCertificationsAsync(
        Guid instanceId, Guid tenantId, ConsultationResult result, DateTimeOffset now, CancellationToken ct)
    {
        if (certificationIngestion is null || result.Certifications is null)
            return;

        var provenance = new Domain.Certifications.CertificationProvenance(
            Domain.Certifications.CertificationSourceKind.Consultation,
            result.Provider,
            result.QueriedAt ?? now);

        try
        {
            await certificationIngestion.IngestAsync(
                instanceId, tenantId, result.Certifications, provenance, result.RawPayload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Silencio acotado: ver RunConsultationHandler.IngestCertificationsAsync.
        }
    }

    // Upsert de los campos hidratados en field_values (mismo patrón que RunConsultationCommand, HU #10856).
    private void UpsertHydrated(ProcedureInstance instance, Guid tenantId, IReadOnlyList<HydratedField> hydratedFields)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var field in hydratedFields)
        {
            var existing = instance.FieldValues.FirstOrDefault(f => f.FieldKey == field.FieldKey);
            if (existing is not null)
            {
                existing.ValueText = field.ValueText;
                existing.ValueJson = field.ValueJson;
                existing.Source = ConsultationSource;
                existing.UpdatedAt = now;
            }
            else
            {
                var fieldValue = new ProcedureInstanceFieldValue
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProcedureInstanceId = instance.Id,
                    FormFieldId = null,
                    FieldKey = field.FieldKey,
                    ValueText = field.ValueText,
                    ValueJson = field.ValueJson,
                    Source = ConsultationSource,
                    CreatedAt = now,
                };
                instance.FieldValues.Add(fieldValue);
                // PK store-generated con Id seteado: marcar Added explícito para forzar INSERT.
                repo.Add(fieldValue);
            }
        }
    }

    /// <summary>Arma el DTO leyendo el shape común HydratedField[] — usado tanto en el HIT de caché como en el consult en vivo.</summary>
    private static RuesPersonDto BuildDtoFromFields(IReadOnlyList<HydratedField> fields, string nit, string mode)
    {
        var razonSocial = RuesActorJuridicalLookup.GetHydrated(fields, "rues_razon_social");
        var found = !string.IsNullOrWhiteSpace(razonSocial);

        return new RuesPersonDto(
            Found: found,
            RazonSocial: found ? razonSocial : null,
            Estado: found ? RuesActorJuridicalLookup.GetHydrated(fields, "rues_estado") : null,
            DocumentNumber: nit,
            MatriculaMercantil: found ? RuesActorJuridicalLookup.GetHydrated(fields, "rues_matricula_mercantil") : null,
            CamaraComercio: found ? RuesActorJuridicalLookup.GetHydrated(fields, "rues_camara_comercio") : null,
            Mode: mode);
    }

    // Modo real|mock informativo para el wizard. Replica la semántica de
    // ConsultationProviderModeOptions.IsMock (VERIFIK_RUES_MODE, default "mock") sin que Application
    // referencie Infrastructure.
    private static string ResolveMode()
    {
        var mode = Environment.GetEnvironmentVariable("VERIFIK_RUES_MODE") ?? "mock";
        return string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase) ? "real" : "mock";
    }
}

/// <summary>
/// Persona jurídica resuelta en RUES (sin persistir). <see cref="Found"/> = se hidrató una
/// razón social no vacía. Cuando Found=false, los campos van en null y el frontend cae al ingreso
/// manual (registro sin resultado de consulta).
/// </summary>
public sealed record RuesPersonDto(
    bool Found,
    string? RazonSocial,
    string? Estado,
    string DocumentNumber,
    string? MatriculaMercantil,
    string? CamaraComercio,
    string Mode,
    string DocumentType = "NIT",
    string Source = "RUES");
