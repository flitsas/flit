using System.Text.Json;
using Flit.Consultas.Api.Persistence;
using Flit.Infrastructure.Consultations.Avaluos;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.EntityFrameworkCore;

namespace Flit.Consultas.Api.Configuracion;

/// <summary>
/// Cadena y presupuesto de failover de la empresa desde <c>consultas.configuracion_empresa</c> (ADR-0065). Las políticas
/// de Trámites que viajaban en el mismo override (solo vehículos propios, bloqueos por familia) no son de Consultas y
/// quedan en sus valores neutros.
/// </summary>
internal sealed class ConsultasTenantOverrideProvider(ConsultasDb db) : IConsultationTenantOverrideProvider
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record SeleccionJson(string? Primary, List<string>? Fallback);

    public async Task<ConsultationTenantOverride?> GetAsync(Guid tenantId, CancellationToken ct)
    {
        var fila = await db.ConfiguracionEmpresas.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, ct).ConfigureAwait(false);
        if (fila is null)
            return null;

        Dictionary<string, ConsultationChainSelection>? cadenas = null;
        if (!string.IsNullOrWhiteSpace(fila.CadenasJson)
            && JsonSerializer.Deserialize<Dictionary<string, SeleccionJson>>(fila.CadenasJson, WebJson) is { Count: > 0 } raw)
        {
            cadenas = new Dictionary<string, ConsultationChainSelection>(StringComparer.OrdinalIgnoreCase);
            foreach (var (tipo, seleccion) in raw)
            {
                if (!string.IsNullOrWhiteSpace(seleccion.Primary))
                    cadenas[tipo] = new ConsultationChainSelection(seleccion.Primary, seleccion.Fallback ?? []);
            }
        }

        return new ConsultationTenantOverride(cadenas, fila.FailoverTimeoutMs, FinesQuerySource: FinesSourceCodes.Normalize(fila.FuenteMultas));
    }
}

/// <summary>Proveedores de avalúo habilitados y el sugerido de la empresa (Fasecolda siempre habilitado).</summary>
internal sealed class ConsultasAvaluoPolicy(ConsultasDb db) : IAvaluoProviderPolicy
{
    private const string Base = "fasecolda";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record AvaluosJson(string? Primary, List<string>? Enabled);

    public async Task<AvaluoEnabledSet> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var json = await db.ConfiguracionEmpresas.AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .Select(c => c.AvaluosJson)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)
            || JsonSerializer.Deserialize<AvaluosJson>(json, WebJson) is not { Enabled.Count: > 0 } raw)
        {
            return AvaluoEnabledSet.Default;
        }

        var enabled = raw.Enabled!.Contains(Base, StringComparer.OrdinalIgnoreCase) ? raw.Enabled : [Base, .. raw.Enabled];
        return new AvaluoEnabledSet(enabled, raw.Primary ?? Base);
    }
}

/// <summary>
/// Valores de avalúo para el modo mock. core-consultas aún no tiene la tabla de valores sembrados de DEV/QA: sin valor,
/// los proveedores en mock responden «sin datos», igual que con la tabla vacía de producción.
/// </summary>
internal sealed class SinValoresMockDeAvaluo : IAvaluoMockValueSource
{
    public Task<long?> GetValueAsync(string matchKey, string source, CancellationToken ct) => Task.FromResult<long?>(null);
}
