using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.RuntConfirmation;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.RuntConfirmation;

/// <summary>
/// Fila única de <c>tramites.runt_confirmation_settings</c>. La migración la siembra, así que en
/// operación siempre existe; el fallback a defaults cubre una base donde todavía no corrió el DDL
/// (y las pruebas InMemory). Nunca inserta si ya hay fila: el índice único sobre <c>(true)</c> lo
/// impediría de todos modos.
/// </summary>
internal sealed class RuntConfirmationSettingsRepository(FlitDbContext db) : IRuntConfirmationSettingsRepository
{
    public async Task<RuntConfirmationSettings> GetAsync(CancellationToken ct = default)
    {
        var row = await db.RuntConfirmationSettings.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return row ?? RuntConfirmationSettings.Defaults();
    }

    public async Task SaveAsync(RuntConfirmationSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var row = await db.RuntConfirmationSettings.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new RuntConfirmationSettings();
            db.RuntConfirmationSettings.Add(row);
        }

        row.Enabled = settings.Enabled;
        row.RunAtLocal = settings.RunAtLocal;
        row.ProviderKey = settings.ProviderKey;
        row.GraceDays = settings.GraceDays;
        row.DiscrepancyAfterRuns = settings.DiscrepancyAfterRuns;
        row.MaxAttempts = settings.MaxAttempts;
        row.UpdatedAt = settings.UpdatedAt;
        row.UpdatedBy = settings.UpdatedBy;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        settings.Id = row.Id;
        settings.RowVersion = row.RowVersion;
    }
}
