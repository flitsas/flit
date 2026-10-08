using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.Tests.ConsolidadoLotes;

/// <summary>HU #13379 — reloj fijo y lotes/partes de prueba para la consulta, la descarga y la purga.</summary>
internal sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora;
}

internal static class LoteConsulta
{
    public static readonly DateTimeOffset Ahora = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

    /// <summary>2026-10-06 19:30 UTC = 14:30 en Colombia ⇒ <c>consolidados_20261006_1430</c>.</summary>
    public static readonly DateTimeOffset Creado = new(2026, 10, 6, 19, 30, 0, TimeSpan.Zero);

    public static ConsolidadoExportBatch Lote(
        string estado = ConsolidadoExportStatus.Completado,
        Guid? dueno = null,
        short partes = 1,
        DateTimeOffset? expira = null,
        DateTimeOffset? purgado = null)
    {
        var terminal = !ConsolidadoExportStatus.EsActivo(estado);
        return new ConsolidadoExportBatch
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            RequestedByUserId = dueno ?? Guid.NewGuid(),
            RequestedRoleCode = "Radicador",
            Origin = ConsolidadoExportOrigin.Tramites,
            DocumentType = ConsolidadoExportDocumentType.Consolidado,
            Status = estado,
            TotalItems = 10,
            IncludedCount = 7,
            OmittedCount = 2,
            GeneratedCount = 3,
            PartsCount = partes,
            DekWrapped = purgado is null && estado != ConsolidadoExportStatus.Fallido ? [1, 2, 3] : null,
            CreatedAt = Creado,
            FinishedAt = terminal ? Ahora.AddHours(-1) : null,
            ExpiresAt = terminal ? expira ?? Ahora.AddHours(23) : null,
            PurgedAt = purgado,
        };
    }

    public static ConsolidadoExportBatchPart Parte(
        ConsolidadoExportBatch lote, short numero, string estado = ConsolidadoExportPartStatus.Cerrada, long bytes = 1000) => new()
        {
            Id = Guid.NewGuid(),
            BatchId = lote.Id,
            PartNumber = numero,
            Status = estado,
            PdfCount = 4,
            OmittedCount = 1,
            PlainSizeBytes = estado == ConsolidadoExportPartStatus.Cerrada ? bytes : null,
            StoredSizeBytes = estado == ConsolidadoExportPartStatus.Cerrada ? bytes + 100 : null,
            StoredSha256 = estado == ConsolidadoExportPartStatus.Cerrada ? new string('a', 64) : null,
            StoragePath = estado == ConsolidadoExportPartStatus.Cerrada ? $"fm/parte-{numero}" : null,
            ClosedAt = estado == ConsolidadoExportPartStatus.Cerrada ? Ahora.AddHours(-1) : null,
        };
}
