using Flit.Ict.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Application.Register;

/// <summary>
/// Registro por lote de pre-trámites (equivalente al createList v1). Multi-tenant desde el token ICT,
/// límite de lote configurable, deduplicación intra-lote por tipo, y respuesta con el contrato v1
/// { TotalRows, TotalRowsProcessed, Detail[] }. No crea nada en tramites.* (eso ocurre tras validar).
/// </summary>
public sealed class RegisterIctBatchHandler(
    IPreTramiteRepository repository,
    ICurrentTenant currentTenant,
    IOptions<IctIngestOptions> options)
{
    private readonly RegisterRowValidator _validator = new();
    private readonly int _maxItems = options.Value.MaxItemsPerBatch;

    public async Task<(RegisterBatchResult? Result, string? Error)> HandleAsync(
        RegisterBatchCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (null, "unauthenticated");
        }

        var rows = command.Rows;
        if (rows.Count == 0)
        {
            return (null, "empty_batch");
        }

        if (rows.Count > _maxItems)
        {
            return (null, "batch_limit_exceeded");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var details = new List<RegisterDetail>(rows.Count);
        var processed = 0;

        foreach (var row in rows)
        {
            var plateLabel = row.Plate?.Trim().ToUpperInvariant() ?? string.Empty;
            var flit = row.ManagerIdTransaction ?? string.Empty;

            // Multi-tenant: el company_manager_document del payload debe coincidir con el NIT del token.
            if (!string.IsNullOrWhiteSpace(row.CompanyManagerDocument)
                && !string.Equals(row.CompanyManagerDocument.Trim(), currentTenant.CompanyNit, StringComparison.Ordinal))
            {
                details.Add(new RegisterDetail(plateLabel, 2,
                    "company_manager_document no corresponde a la compañía autenticada", flit));
                continue;
            }

            var validation = _validator.Validate(row);
            if (!validation.IsValid)
            {
                details.Add(new RegisterDetail(plateLabel, 2,
                    string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)), flit));
                continue;
            }

            var key = IctPayloadNormalizer.DedupKey(row);
            if (!seen.Add(key))
            {
                details.Add(new RegisterDetail(plateLabel, 2, "registro duplicado dentro del lote", flit));
                continue;
            }

            var master = IctPayloadNormalizer.ToMaster(row, tenantId.Value);
            // created_at explícito: sin esto la entidad queda en DateTime.MinValue y Npgsql lo envía
            // como -infinity (pisa el DEFAULT now() del DDL), lo que rompe las métricas ICT y la
            // retención, que filtran por created_at.
            master.CreatedAt = DateTime.UtcNow;
            master.CreatedBy = currentTenant.IntegrationClientId;
            await repository.AddAsync(master, tenantId.Value, ct);
            processed++;
            details.Add(new RegisterDetail(master.Plate, 1, "registrado", master.ManagerIdTransaction));
        }

        return (new RegisterBatchResult(rows.Count, processed, details), null);
    }
}
