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
            var query = db.ProcedureInstanceBiometricValidations
                .AsNoTracking()
                .Where(v => v.Provider == BiometricProviders.Manual
                    && (v.Status == BiometricEstados.ManualActivo
                        || v.Status == BiometricEstados.PendienteRevisionManual
                        || v.Status == BiometricEstados.Rechazado
                        || v.Status == BiometricEstados.Expirado
                        || (v.Status == BiometricEstados.Aprobado
                            && v.ApprovalOrigin == BiometricApprovalOrigins.Manual)));

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

            IReadOnlyList<ManualIdentityReviewRow> items =
            [
                .. rows.Select(x => new ManualIdentityReviewRow(
                    x.Id,
                    x.Name,
                    x.DocumentNumber,
                    x.TenantName,
                    OriginOf(x.PartyRole, x.ProcedureInstanceId),
                    x.Status,
                    x.ManualActivatedAt)),
            ];

            return (items, total);
        }, ct);
    }

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
