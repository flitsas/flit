using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
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
/// Registra una decisión de prenda VIGENTE para un trámite — comando base del cimiento IT-3 (HU-F2-01).
/// Versionado intrínseco (R17): si ya existe una fila vigente, se marca <c>reemplazada</c> ANTES de insertar la
/// nueva (dos <c>SaveChanges</c>: garantiza que el índice único parcial "una vigente por instancia" nunca vea
/// dos filas vigentes a la vez). No aplica gates de estado del trámite: eso lo deciden los casos de uso que lo
/// consumen (matrícula en HU-F2-02, gate de traspaso en HU-F2-04, modificación post-registro en HU-F2-06).
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

        var decision = input.Decision.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        // Versionado: la decisión vigente anterior queda reemplazada. Se persiste PRIMERO (libera el índice
        // único parcial) y luego se inserta la nueva vigente.
        var vigente = await prendas.GetVigenteAsync(instanceId, tenantId, ct);
        if (vigente is not null)
        {
            vigente.Estado = PrendaEstado.Reemplazada;
            vigente.UpdatedAt = now;
            vigente.UpdatedBy = userId;
            await prendas.SaveChangesAsync(ct);
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
