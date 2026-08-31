using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Contrato congelado de una decisión de prenda (compartido con el front).</summary>
public sealed record PrendaDto(
    Guid Id,
    string Decision,
    string Estado,
    string? AcreedorNombre,
    string? AcreedorDocumento,
    /// <summary>Entidad ante la que se levantó el gravamen; solo la usa el trámite de levantamiento.</summary>
    string? LevantamientoEntidad,
    DateTimeOffset CreatedAt);

/// <summary>Datos de una decisión de prenda a registrar.</summary>
public sealed record RegistrarPrendaInput(
    string Decision,
    string? AcreedorNombre = null,
    string? AcreedorDocumento = null,
    string? LevantamientoEntidad = null,
    string? MetadataJson = null);

/// <summary>
/// Registra una decisión de prenda VIGENTE para un trámite — comando base del cimiento IT-3 (HU-F2-01) que
/// también sirve la modificación post-registro versionada (R17, HU-F2-06). Versionado intrínseco: si ya existe
/// una fila vigente, se marca <c>reemplazada</c> ANTES de insertar la nueva (dos <c>SaveChanges</c>: garantiza
/// que el índice único parcial "una vigente por instancia" nunca vea dos filas vigentes a la vez) y se registra
/// un evento de auditoría <c>prenda_modificada</c> (old/new). Único gate de estado: el trámite en estado FINAL
/// (aprobado/anulado) es inmutable. La prenda vive en su propia tabla, así que modificar fuera de borrador NO
/// viola la inmutabilidad de <c>field_values</c>: por eso la modificación post-registro es posible y auditada.
/// </summary>
public sealed class RegistrarPrendaHandler(
    IProcedureInstanceRepository instances,
    IProcedureInstancePrendaRepository prendas,
    IPrendaDocumentRequirementPolicy? prendaDocumentRequirementPolicy = null)
{
    /// <summary>Error: el OT exige el certificado de prenda, así que "omitir" no es elegible.</summary>
    public const string OmitirNoAdmitidoError = "prenda_omitir_no_admitido";

    /// <summary>Error: este tipo no tiene dimensión de gravamen (familia OTROS, tipo no prendario).</summary>
    public const string PrendaNoAdmitidaError = "prenda_no_admitida_en_tipo";

    private readonly IPrendaDocumentRequirementPolicy _documentPolicy =
        prendaDocumentRequirementPolicy ?? NullPrendaDocumentRequirementPolicy.Instance;

    public async Task<(PrendaDto? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        RegistrarPrendaInput input,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        if (input is null || !PrendaDecision.IsValid(input.Decision))
            return (null, "prenda_decision_invalida");

        var instance = await instances.GetByIdAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // R17 (HU #10599) — un trámite en estado final (aprobado/anulado) es inmutable: no admite
        // modificar la elección de prenda.
        if (TramiteEstado.EsFinal(instance.Status))
            return (null, TramiteEstadoErrores.EstadoFinal);

        // ADR-0050 — en la familia OTROS la prenda no es una capa que se añada: o el tipo ES el
        // trámite de gravamen (inscribir, levantar, cambiar de acreedor) o el expediente no tiene
        // dimensión de prenda en absoluto. Un duplicado de tarjeta con un gravamen encima son dos
        // trámites, y el organismo devuelve el FUR que los mezcla. Matrícula y traspaso conservan la
        // prenda complementaria del art. 5.1.8 y no pasan por aquí.
        var perfil = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        if (!perfil.AdmiteDimensionDePrenda(instance.ProcedureType?.Family, instance.ProcedureType?.Code))
        {
            return (null, PrendaNoAdmitidaError);
        }

        var decision = input.Decision.Trim().ToLowerInvariant();

        // CF-06 (HU #10881) — "omitir" es la vía "asumo el riesgo", y con un OT que exige el
        // certificado de prenda no hay riesgo que el gestor pueda asumir por su cuenta: la regla es
        // del organismo. Se rechaza AL ELEGIR, que es donde el gate de radicación decía que había que
        // decidirlo (ver PrendaGate.EvaluateOtOverride). Bloquear después dejaría guardada una
        // decisión que ningún adjunto puede satisfacer —el paso de prenda no ofrece cargar documento
        // para "omitir"—, que es exactamente el atasco que corrigió esta tanda. Las decisiones ya
        // guardadas no se revisan: la regla mira la elección nueva, no reabre trámites en curso.
        if (string.Equals(decision, PrendaDecision.Omitir, StringComparison.OrdinalIgnoreCase)
            && await _documentPolicy
                .IsRequiredAsync(tenantId, instance.TransitOfficeId, instance.CreatedAt, ct)
                .ConfigureAwait(false))
        {
            return (null, OmitirNoAdmitidoError);
        }

        var now = DateTimeOffset.UtcNow;

        // Versionado: la decisión vigente anterior queda reemplazada. Se persiste PRIMERO (libera el índice
        // único parcial) y luego se inserta la nueva vigente. La modificación se audita como evento.
        var vigente = await prendas.GetVigenteAsync(instanceId, tenantId, ct);
        if (vigente is not null)
        {
            var anterior = vigente.Decision;
            vigente.Estado = PrendaEstado.Reemplazada;
            vigente.UpdatedAt = now;
            vigente.UpdatedBy = userId;
            await prendas.SaveChangesAsync(ct);

            await instances.AddEventAsync(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = instanceId,
                Tipo = "prenda_modificada",
                Payload = JsonSerializer.Serialize(new { anterior, nueva = decision }),
                CreatedAt = now,
                CreatedBy = userId,
            }, ct);
        }

        var nueva = new ProcedureInstancePrenda
        {
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            Decision = decision,
            Estado = PrendaEstado.Vigente,
            AcreedorNombre = Trimmed(input.AcreedorNombre),
            AcreedorDocumento = Trimmed(input.AcreedorDocumento),
            LevantamientoEntidad = Trimmed(input.LevantamientoEntidad),
            Metadata = string.IsNullOrWhiteSpace(input.MetadataJson) ? "{}" : input.MetadataJson,
            CreatedAt = now,
            CreatedBy = userId,
        };
        await prendas.AddAsync(nueva, ct);
        await prendas.SaveChangesAsync(ct);

        return (ToDto(nueva), null);
    }

    internal static PrendaDto ToDto(ProcedureInstancePrenda p) =>
        new(p.Id, p.Decision, p.Estado, p.AcreedorNombre, p.AcreedorDocumento, p.LevantamientoEntidad, p.CreatedAt);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Lee la decisión de prenda vigente de un trámite (o <c>null</c> si no hay).</summary>
public sealed class GetPrendaVigenteHandler(IProcedureInstancePrendaRepository prendas)
{
    public async Task<PrendaDto?> HandleAsync(Guid instanceId, Guid tenantId, CancellationToken ct = default)
    {
        var vigente = await prendas.GetVigenteAsync(instanceId, tenantId, ct);
        return vigente is null ? null : RegistrarPrendaHandler.ToDto(vigente);
    }
}
