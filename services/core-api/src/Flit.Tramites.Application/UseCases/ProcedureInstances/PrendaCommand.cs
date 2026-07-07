using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Contrato congelado de una decisión de prenda (compartido con el front).</summary>
public sealed record PrendaDto(
    Guid Id,
    string Decision,
    string Estado,
    string? AcreedorNombre,
    string? AcreedorDocumento,
    DateTimeOffset CreatedAt);

/// <summary>Datos de una decisión de prenda a registrar.</summary>
public sealed record RegistrarPrendaInput(
    string Decision,
    string? AcreedorNombre = null,
    string? AcreedorDocumento = null,
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
    IProcedureInstancePrendaRepository prendas)
{
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

        var decision = input.Decision.Trim().ToLowerInvariant();
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
            Metadata = string.IsNullOrWhiteSpace(input.MetadataJson) ? "{}" : input.MetadataJson,
            CreatedAt = now,
            CreatedBy = userId,
        };
        await prendas.AddAsync(nueva, ct);
        await prendas.SaveChangesAsync(ct);

        return (ToDto(nueva), null);
    }

    internal static PrendaDto ToDto(ProcedureInstancePrenda p) =>
        new(p.Id, p.Decision, p.Estado, p.AcreedorNombre, p.AcreedorDocumento, p.CreatedAt);

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
