using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// DTO comercial del traspaso (paso 5). Contrato congelado consumido por el frontend.
/// </summary>
public sealed record CommercialDto(
    decimal? ValorVenta,
    string? Causal,
    decimal? TasaImpuesto,
    decimal? Derechos,
    string? MetodoPago,
    // Feature #10707 — trazabilidad del avalúo (opcionales, backward-compatible).
    string? ValueOrigin = null,
    string? SuggestedSource = null,
    decimal? SuggestedValue = null);

/// <summary>
/// Captura de datos comerciales del traspaso (1:1 con la instancia). Persiste en
/// <c>procedure_instance_commercial</c>. Valida causal ∈ catálogo fijo y valorVenta &gt; 0.
/// </summary>
public sealed class PutCommercialHandler(IProcedureInstanceRepository repo)
{
    // Causales válidas del contrato congelado (front consume el mismo set).
    internal static readonly HashSet<string> ValidCausales =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "COMPRAVENTA",
            "DONACION",
            "DACION_EN_PAGO",
            "ADJUDICACION",
        };

    public async Task<(CommercialDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CommercialDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instance = await repo.GetByIdWithCommercialAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return (null, "not_draft");

        // Validación de forma. valorVenta es obligatorio (> 0) en el paso comercial.
        if (request.ValorVenta is null or <= 0)
            return (null, "invalid_valor_venta");

        if (string.IsNullOrWhiteSpace(request.Causal) || !ValidCausales.Contains(request.Causal))
            return (null, "invalid_causal");

        if (request.TasaImpuesto is < 0)
            return (null, "invalid_tasa");

        if (request.Derechos is < 0)
            return (null, "invalid_derechos");

        var now = DateTimeOffset.UtcNow;
        var causal = request.Causal.Trim().ToUpperInvariant();

        if (instance.Commercial is null)
        {
            instance.Commercial = new ProcedureInstanceCommercial
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = instance.Id,
                ValorVenta = request.ValorVenta,
                Causal = causal,
                TasaImpuesto = request.TasaImpuesto,
                Derechos = request.Derechos,
                MetodoPago = string.IsNullOrWhiteSpace(request.MetodoPago) ? null : request.MetodoPago.Trim(),
                ValueOrigin = NormalizeOrigin(request.ValueOrigin),
                SuggestedSource = string.IsNullOrWhiteSpace(request.SuggestedSource) ? null : request.SuggestedSource.Trim(),
                SuggestedValue = request.SuggestedValue,
                CreatedAt = now,
            };
            // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
            // INSERT. Sin esto, EF infiere Modified por la PK no-default → UPDATE de 0 filas.
            repo.Add(instance.Commercial);
        }
        else
        {
            instance.Commercial.ValorVenta = request.ValorVenta;
            instance.Commercial.Causal = causal;
            instance.Commercial.TasaImpuesto = request.TasaImpuesto;
            instance.Commercial.Derechos = request.Derechos;
            instance.Commercial.MetodoPago = string.IsNullOrWhiteSpace(request.MetodoPago) ? null : request.MetodoPago.Trim();
            instance.Commercial.ValueOrigin = NormalizeOrigin(request.ValueOrigin);
            instance.Commercial.SuggestedSource = string.IsNullOrWhiteSpace(request.SuggestedSource) ? null : request.SuggestedSource.Trim();
            instance.Commercial.SuggestedValue = request.SuggestedValue;
            instance.Commercial.UpdatedAt = now;
        }

        // Instancia trackeada (GetByIdWithCommercialAsync sin AsNoTracking): el change tracker
        // detecta el Commercial nuevo (INSERT) o el update del existente. NO se llama Update():
        // sobre el grafo trackeado marcaría el Commercial nuevo como Modified → UPDATE de 0 filas.
        await repo.SaveChangesAsync(ct);

        return (ToDto(instance.Commercial), null);
    }

    internal static CommercialDto ToDto(ProcedureInstanceCommercial c) =>
        new(c.ValorVenta, c.Causal, c.TasaImpuesto, c.Derechos, c.MetodoPago,
            c.ValueOrigin, c.SuggestedSource, c.SuggestedValue);

    // value_origin ∈ {suggestion, manual}; cualquier otro valor se descarta a null.
    private static string? NormalizeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return null;
        var v = origin.Trim().ToLowerInvariant();
        return v is "suggestion" or "manual" ? v : null;
    }
}

/// <summary>GET de datos comerciales. Devuelve null si aún no se han capturado.</summary>
public sealed class GetCommercialHandler(IProcedureInstanceRepository repo)
{
    public async Task<(CommercialDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithCommercialAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // 200 con null cuerpo si no hay comercial todavía (contrato: "...| null").
        return (instance.Commercial is null ? null : PutCommercialHandler.ToDto(instance.Commercial), null);
    }
}
