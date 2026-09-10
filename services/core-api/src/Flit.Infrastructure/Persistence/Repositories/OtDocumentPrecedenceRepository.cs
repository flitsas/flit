using Flit.Admin.Domain.OtDocumentPrecedence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Prelación documental OT (HU #10222; unión y upsert en la HU #11182).
/// <para>
/// Hasta la HU #11182 este repositorio solo devolvía las filas ya persistidas en
/// <c>admin.ot_document_precedence</c> y nada las sembraba, así que la pantalla de prelación salía
/// vacía y el <c>PATCH</c> respondía 422 porque exigía que la fila existiera: era inoperante. Ahora
/// la lista es la <b>unión</b> de la matriz base del trámite, los documentos que genera el sistema
/// (<c>document_types.is_system_generated</c>) y los overrides del OT, y el guardado es un upsert.
/// </para>
/// </summary>
internal sealed class OtDocumentPrecedenceRepository : IOtDocumentPrecedenceRepository
{
    private const int MaxBatchSize = 50;

    /// <summary>
    /// Desplazamiento del orden base para que los documentos sin configurar salgan agrupados:
    /// primero los generados (por <c>generated_sort_order</c>) y después los de la matriz del
    /// trámite (por <c>default_sort_order</c>), que es como se arma hoy el expediente.
    /// </summary>
    private const int OrdenBaseOffset = 1000;

    private readonly FlitDbContext _context;

    public OtDocumentPrecedenceRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<OtDocumentPrecedenceItem>> ListByProcedureTypeAsync(
        Guid tenantId,
        Guid procedureTypeId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            () => ComposeListAsync(tenantId, procedureTypeId, cancellationToken),
            cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<OtDocumentPrecedenceItem>?> ReorderBatchAsync(
        Guid tenantId,
        Guid procedureTypeId,
        IReadOnlyList<OtDocumentPrecedenceOrderItem> items,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0 || items.Count > MaxBatchSize)
        {
            return null;
        }

        return await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                // El documento debe existir en el catálogo: es FK de la tabla y, sobre todo, evita
                // persistir un orden para un id inventado que luego nadie podría ver ni corregir.
                var documentTypeIds = items.Select(i => i.DocumentTypeId).Distinct().ToList();
                var conocidos = await _context.DocumentTypes
                    .AsNoTracking()
                    .Where(d => documentTypeIds.Contains(d.Id))
                    .Select(d => d.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (conocidos.Count != documentTypeIds.Count)
                {
                    return null;
                }

                var existing = await _context.OtDocumentPrecedences
                    .Where(p => p.TenantId == tenantId && p.ProcedureTypeId == procedureTypeId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var lookup = existing.ToDictionary(e => e.DocumentTypeId);

                foreach (var item in items)
                {
                    // Upsert: si el OT nunca había tocado este documento, la fila se crea ahora.
                    if (lookup.TryGetValue(item.DocumentTypeId, out var fila))
                    {
                        fila.SortOrder = item.SortOrder;
                        continue;
                    }

                    _context.OtDocumentPrecedences.Add(new OtDocumentPrecedenceEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        ProcedureTypeId = procedureTypeId,
                        DocumentTypeId = item.DocumentTypeId,
                        SortOrder = item.SortOrder,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CreatedBy = changedBy,
                    });
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return await ComposeListAsync(tenantId, procedureTypeId, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unión matriz base ∪ generados ∪ overrides, deduplicada por documento (AC1–AC4). Un documento
    /// que está a la vez en la matriz y marcado como generado —<c>compraventa</c>, <c>impronta</c>—
    /// aparece una sola vez.
    /// </summary>
    private async Task<IReadOnlyList<OtDocumentPrecedenceItem>> ComposeListAsync(
        Guid tenantId,
        Guid procedureTypeId,
        CancellationToken cancellationToken)
    {
        var baseDocs = await (
                from r in _context.ProcedureDocumentRequirements.AsNoTracking()
                where r.ProcedureTypeId == procedureTypeId
                join d in _context.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals d.Id
                select new Candidato(d.Id, d.Code, d.Name, d.IsSystemGenerated, d.GeneratedSortOrder, r.DefaultSortOrder))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var generados = await _context.DocumentTypes
            .AsNoTracking()
            .Where(d => d.IsSystemGenerated && d.IsActive)
            .Select(d => new Candidato(d.Id, d.Code, d.Name, true, d.GeneratedSortOrder, null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var overrides = await (
                from p in _context.OtDocumentPrecedences.AsNoTracking()
                where p.TenantId == tenantId && p.ProcedureTypeId == procedureTypeId
                join d in _context.DocumentTypes.AsNoTracking() on p.DocumentTypeId equals d.Id
                select new
                {
                    p.Id,
                    p.DocumentTypeId,
                    p.SortOrder,
                    d.Code,
                    d.Name,
                    d.IsSystemGenerated,
                    d.GeneratedSortOrder,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var candidatos = new Dictionary<Guid, Candidato>();
        foreach (var candidato in baseDocs.Concat(generados))
        {
            // Dedup por documento (AC3). La matriz base gana porque aporta el default_sort_order del
            // trámite; el orden de los generados se conserva igual (viene del propio document_type).
            if (!candidatos.TryGetValue(candidato.DocumentTypeId, out var previo) || previo.DefaultSortOrder is null)
            {
                candidatos[candidato.DocumentTypeId] = candidato;
            }
        }

        // Un override puede apuntar a un documento que ya no está en la matriz ni marcado como
        // generado (p. ej. tras retirarlo del trámite): se conserva para no perder el orden guardado.
        foreach (var o in overrides)
        {
            if (!candidatos.ContainsKey(o.DocumentTypeId))
            {
                candidatos[o.DocumentTypeId] = new Candidato(
                    o.DocumentTypeId, o.Code, o.Name, o.IsSystemGenerated, o.GeneratedSortOrder, null);
            }
        }

        var configurados = overrides.ToDictionary(o => o.DocumentTypeId, o => (o.Id, o.SortOrder));

        var ordenados = candidatos.Values
            .Select(c =>
            {
                var configurado = configurados.TryGetValue(c.DocumentTypeId, out var cfg);
                return new
                {
                    Candidato = c,
                    Configurado = configurado,
                    FilaId = configurado ? cfg.Id : Guid.Empty,
                    // AC2 — lo configurado manda y va primero; el resto conserva su orden por defecto.
                    Orden = configurado ? cfg.SortOrder : OrdenPorDefecto(c),
                };
            })
            .OrderBy(x => x.Configurado ? 0 : 1)
            .ThenBy(x => x.Orden)
            .ThenBy(x => x.Candidato.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return [.. ordenados.Select((x, index) => new OtDocumentPrecedenceItem
        {
            Id = x.FilaId,
            TenantId = tenantId,
            ProcedureTypeId = procedureTypeId,
            DocumentTypeId = x.Candidato.DocumentTypeId,
            DocumentCode = x.Candidato.Code,
            DocumentName = x.Candidato.Name,
            // Posición contigua 1..N: es lo que ve y reenvía la pantalla al reordenar.
            SortOrder = (short)(index + 1),
            IsSystemGenerated = x.Candidato.IsSystemGenerated,
            IsConfigured = x.Configurado,
        })];
    }

    /// <summary>
    /// Orden por defecto: primero los generados por su <c>generated_sort_order</c> y luego los de la
    /// matriz por su <c>default_sort_order</c>. Un documento que es las dos cosas —<c>compraventa</c>,
    /// <c>impronta</c>— usa el orden de generado, que es donde sale hoy en el expediente.
    /// </summary>
    private static int OrdenPorDefecto(Candidato candidato) =>
        candidato.IsSystemGenerated && candidato.GeneratedSortOrder is { } generado
            ? generado
            : candidato.DefaultSortOrder is { } orden
                ? OrdenBaseOffset + orden
                : OrdenBaseOffset + short.MaxValue;

    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid tenantId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }

    /// <param name="DefaultSortOrder">Orden del documento en la matriz del trámite; null si solo es generado.</param>
    private sealed record Candidato(
        Guid DocumentTypeId,
        string Code,
        string Name,
        bool IsSystemGenerated,
        short? GeneratedSortOrder,
        short? DefaultSortOrder);
}
