using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13296 (Feature #13282, Épica #13202) — lectura cross-tenant de las validaciones manuales para el Super Admin.
/// <c>procedure_instance_biometric_validations</c> e <c>identity.tenants</c> tienen RLS por tenant; aquí se cruzan
/// TODAS las compañías a propósito, así que la consulta corre bajo <c>SET LOCAL row_security = off</c> dentro de una
/// transacción (<see cref="CrossTenantRead"/>). Este es el sitio que hay que revisar si se sospecha una fuga: el
/// repositorio no conoce al usuario, y el endpoint exige Super Admin antes de llegar aquí.
/// <para>
/// Solo cuenta lo manual: <c>provider = 'manual'</c> con estado en la lista cerrada de estados manuales; una
/// aprobada solo entra si su <c>approval_origin</c> es <c>manual</c>. Búsqueda de texto con ILIKE parametrizado y
/// comodines escapados. Nada de lo leído se escribe en logs (nombres y documentos son PII).
/// </para>
/// </summary>
internal sealed class ManualIdentityReviewReadRepository(FlitDbContext db) : IManualIdentityReviewReadRepository
{
    private const string LikeEscapeChar = "\\";

    public Task<(IReadOnlyList<ManualIdentityReviewRow> Items, int Total)> ListAsync(
        ManualIdentityReviewFilter filter,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return CrossTenantRead.ExecuteAsync(db, async () =>
        {
            var query = ManualValidations();

            if (filter.Status is { } status)
                query = query.Where(v => v.Status == status);

            // Mismo criterio que el cálculo del origen en la proyección: mandatario > trámite > prevalidación.
            if (filter.Origin is { } origin)
            {
                query = origin switch
                {
                    ManualIdentityReviewOrigins.Mandatario =>
                        query.Where(v => v.PartyRole == BiometricRules.ParteMandatario),
                    ManualIdentityReviewOrigins.Tramite =>
                        query.Where(v => v.PartyRole != BiometricRules.ParteMandatario && v.ProcedureInstanceId != null),
                    ManualIdentityReviewOrigins.Prevalidacion =>
                        query.Where(v => v.PartyRole != BiometricRules.ParteMandatario && v.ProcedureInstanceId == null),
                    // representante_legal: ninguna fila lo identifica hoy (duda abierta), no se infiere.
                    _ => query.Where(_ => false),
                };
            }

            if (filter.Text is { } text)
            {
                var pattern = $"%{EscapeLike(text.ToLowerInvariant())}%";
                query = query.Where(v =>
                    EF.Functions.ILike(v.Name, pattern, LikeEscapeChar)
                    || EF.Functions.ILike(v.DocumentNumber, pattern, LikeEscapeChar));
            }

            var total = await query.CountAsync(ct).ConfigureAwait(false);

            // Pendientes de revisión primero, luego los que esperan captura; ambos con los más antiguos arriba.
            // Los cerrados al final, el más reciente primero. Id como desempate para que la paginación sea estable.
            var rows = await query
                .Join(db.Tenants, v => v.TenantId, t => t.Id, (v, t) => new
                {
                    v.Id,
                    v.Name,
                    v.DocumentNumber,
                    TenantName = t.LegalName,
                    v.PartyRole,
                    v.ProcedureInstanceId,
                    v.Status,
                    v.ManualActivatedAt,
                    v.UpdatedAt,
                    Rank = v.Status == BiometricEstados.PendienteRevisionManual ? 0
                        : v.Status == BiometricEstados.ManualActivo ? 1 : 2,
                })
                .OrderBy(x => x.Rank)
                .ThenBy(x => x.Rank < 2 ? x.ManualActivatedAt : null)
                .ThenByDescending(x => x.Rank == 2 ? x.ManualActivatedAt : null)
                .ThenBy(x => x.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var pendientes = rows
                .Where(x => x.Status == BiometricEstados.PendienteRevisionManual)
                .ToDictionary(x => x.Id, x => x.UpdatedAt);
            var recibidas = await CapturasRecibidasAsync(pendientes.Keys.ToList(), ct).ConfigureAwait(false);

            IReadOnlyList<ManualIdentityReviewRow> items =
            [
                .. rows.Select(x => new ManualIdentityReviewRow(
                    x.Id,
                    x.Name,
                    x.DocumentNumber,
                    x.TenantName,
                    OriginOf(x.PartyRole, x.ProcedureInstanceId),
                    x.Status,
                    x.ManualActivatedAt,
                    pendientes.ContainsKey(x.Id) ? (recibidas.TryGetValue(x.Id, out var at) ? at : x.UpdatedAt) : null)),
            ];

            return (items, total);
        }, ct);
    }

    public Task<ManualIdentityReviewDetailRow?> GetDetailAsync(Guid id, CancellationToken ct = default) =>
        CrossTenantRead.ExecuteAsync(db, async () =>
        {
            var row = await ManualValidations()
                .Where(v => v.Id == id)
                .Join(db.Tenants, v => v.TenantId, t => t.Id, (v, t) => new { v, TenantName = t.LegalName })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (row is null)
                return null;

            var v = row.v;
            string? reviewerName = null;
            if (v.ReviewedBy is { } reviewerId)
            {
                reviewerName = await db.Users.AsNoTracking()
                    .Where(u => u.Id == reviewerId)
                    .Select(u => u.DisplayName)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);
            }

            var current = TieneCapturaVigente(v.Status);
            var consentCurrent = v.ConsentAt is { } consent && v.ManualActivatedAt is { } activated && consent >= activated;
            // reviewedBy: el nombre visible del revisor; si ya no se resuelve, su id (nunca vacío si hubo revisión).
            var reviewer = v.ReviewedBy is null ? null
                : string.IsNullOrWhiteSpace(reviewerName) ? v.ReviewedBy.Value.ToString() : reviewerName;

            DateTimeOffset? waitingSince = null;
            if (v.Status == BiometricEstados.PendienteRevisionManual)
            {
                var recibidas = await CapturasRecibidasAsync([v.Id], ct).ConfigureAwait(false);
                waitingSince = recibidas.TryGetValue(v.Id, out var at) ? at : v.UpdatedAt;
            }

            return new ManualIdentityReviewDetailRow(
                v.Id, v.TenantId, v.ProcedureInstanceId, v.PartyRole, v.Name, v.DocumentNumber, row.TenantName,
                OriginOf(v.PartyRole, v.ProcedureInstanceId), v.Status, v.ManualActivatedAt,
                consentCurrent ? v.ConsentAt : null, consentCurrent ? v.ConsentTextVersion : null,
                current && !string.IsNullOrWhiteSpace(v.FacePhotoPath),
                current && !string.IsNullOrWhiteSpace(v.IdFrontPhotoPath),
                current && !string.IsNullOrWhiteSpace(v.IdBackPhotoPath),
                current && !string.IsNullOrWhiteSpace(v.SignatureImagePath),
                v.ReviewedAt, reviewer, v.RejectionReasonCode,
                (v.Status == BiometricEstados.ManualActivo
                    || (v.Status == BiometricEstados.Rechazado && v.RejectionReasonCode != null)) ? v.ExpiresAt : null,
                waitingSince);
        }, ct);

    /// <summary>
    /// HU #13296: instante de la captura recibida de cada validación = evento de auditoría <c>manual_captura_recibida</c> MÁS
    /// RECIENTE (un rechazo y una nueva captura generan otro evento). No hay columna propia, así que no se agrega ninguna.
    /// Sin evento, el llamador cae a <c>UpdatedAt</c> de la fila.
    /// </summary>
    private async Task<Dictionary<Guid, DateTimeOffset>> CapturasRecibidasAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        var rows = await db.IdentityValidationAudits
            .AsNoTracking()
            .Where(a => a.Stage == IdentityValidationAuditStages.ManualCapturaRecibida
                && a.ValidationId != null && ids.Contains(a.ValidationId.Value))
            .GroupBy(a => a.ValidationId!.Value)
            .Select(g => new { Id = g.Key, At = g.Max(a => a.OccurredAt) })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => r.At);
    }

    public Task<ManualIdentityImageRef?> GetImageRefAsync(Guid id, string kind, CancellationToken ct = default) =>
        CrossTenantRead.ExecuteAsync(db, async () =>
        {
            var row = await ManualValidations()
                .Where(v => v.Id == id)
                .Select(v => new
                {
                    v.Id, v.TenantId, v.ProcedureInstanceId, v.PartyRole, v.Status,
                    v.FacePhotoPath, v.IdFrontPhotoPath, v.IdBackPhotoPath, v.SignatureImagePath,
                })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (row is null)
                return null;

            var path = !TieneCapturaVigente(row.Status) ? null : kind switch
            {
                ManualImageKinds.Rostro => row.FacePhotoPath,
                ManualImageKinds.Anverso => row.IdFrontPhotoPath,
                ManualImageKinds.Reverso => row.IdBackPhotoPath,
                ManualImageKinds.Firma => row.SignatureImagePath,
                _ => null,
            };
            return new ManualIdentityImageRef(
                row.Id, row.TenantId, row.ProcedureInstanceId, row.PartyRole, string.IsNullOrWhiteSpace(path) ? null : path);
        }, ct);

    /// <summary>
    /// Las validaciones del flujo manual (mismo conjunto que el listado): <c>provider = 'manual'</c> con estado manual; una
    /// aprobada solo entra si su <c>approval_origin</c> es <c>manual</c>.
    /// </summary>
    private IQueryable<ProcedureInstanceBiometricValidation> ManualValidations() =>
        db.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => v.Provider == BiometricProviders.Manual
                && (v.Status == BiometricEstados.ManualActivo
                    || v.Status == BiometricEstados.PendienteRevisionManual
                    || v.Status == BiometricEstados.Rechazado
                    || v.Status == BiometricEstados.Expirado
                    || (v.Status == BiometricEstados.Aprobado
                        && v.ApprovalOrigin == BiometricApprovalOrigins.Manual)));

    /// <summary>
    /// ¿Las rutas de imagen de la fila son las de la captura que se revisó? Pendiente de revisión, aprobada o rechazada (HU #13299:
    /// el rechazo conserva la captura rechazada hasta que el cliente envíe la nueva). En <c>manual_activo</c> (activación o
    /// reactivación del Super Admin) las rutas son las de un ciclo anterior: se conservan en storage y en la fila, pero no se
    /// muestran.
    /// </summary>
    private static bool TieneCapturaVigente(string status) =>
        status is BiometricEstados.PendienteRevisionManual or BiometricEstados.Aprobado or BiometricEstados.Rechazado;

    /// <summary>
    /// Origen según el modelo actual: la ficha de mandatario manda; luego el trámite; el resto es prevalidación
    /// standalone. <c>representante_legal</c> no se produce todavía: no hay ancla que lo distinga.
    /// </summary>
    internal static string OriginOf(string? partyRole, Guid? procedureInstanceId) =>
        partyRole == BiometricRules.ParteMandatario ? ManualIdentityReviewOrigins.Mandatario
        : procedureInstanceId is not null ? ManualIdentityReviewOrigins.Tramite
        : ManualIdentityReviewOrigins.Prevalidacion;

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
