using Flit.DrFlit.Application.Abstractions;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure.DrFlit;

/// <summary><c>DrFlit:Consent</c> — versión vigente del texto de tratamiento de datos (HU #12931).</summary>
public sealed class DrFlitConsentOptions
{
    public const string SectionName = "DrFlit:Consent";

    /// <summary>Se cambia cuando Legal cambia el texto: vuelve a pedir la aceptación a todos.</summary>
    public string Version { get; set; } = "2026-09-25";
}

internal sealed class DrFlitConsentSettings(IOptions<DrFlitConsentOptions> options) : IDrFlitConsentSettings
{
    public string CurrentVersion { get; } = string.IsNullOrWhiteSpace(options.Value.Version) ? "2026-09-25" : options.Value.Version.Trim();
}

/// <summary><c>dr_flit.consent_acceptances</c> por SQL directo (HU #12931).</summary>
internal sealed class DrFlitConsentStore(FlitDbContext db) : IDrFlitConsentStore
{
    public async Task<bool> HasAcceptedAsync(Guid userId, string version, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM dr_flit.consent_acceptances
                 WHERE user_id = {userId} AND consent_version = {version}
                 LIMIT 1
                """)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Count > 0;
    }

    public Task RecordAsync(DrFlitConsentAcceptance a, CancellationToken ct) =>
        // ON CONFLICT DO NOTHING: aceptar otra vez la misma versión conserva la primera evidencia.
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dr_flit.consent_acceptances (tenant_id, user_id, consent_version, client_ip, user_agent, created_by, updated_by)
            VALUES ({a.TenantId}, {a.UserId}, {a.Version}, {Truncate(a.ClientIp, 64)}, {Truncate(a.UserAgent, 512)}, {a.UserId}, {a.UserId})
            ON CONFLICT (user_id, consent_version) DO NOTHING
            """, ct);

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
