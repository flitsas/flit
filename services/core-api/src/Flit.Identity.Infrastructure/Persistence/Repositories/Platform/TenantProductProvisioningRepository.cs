using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Modules.Platform.Application.TenantProducts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1, DDL 205) — <c>platform.tenant_product_provisioning</c>: el último estado que reporta
/// el servicio del producto. El guardado es un solo
/// <c>INSERT … ON CONFLICT … DO UPDATE … WHERE reported_version &lt; EXCLUDED.reported_version</c>, así dos reportes concurrentes
/// no se pisan y una versión igual o menor no cambia nada. Filtro explícito por <c>tenant_id</c> (sin <c>HasQueryFilter</c>,
/// convención del repo). Columnas en inglés; el registro de dominio conserva los nombres del contrato gRPC. Una FK rota
/// (<c>23503</c>) sale como error de dominio: producto fuera del catálogo ⇒ <see cref="TenantProductException.ProductNotFound"/>;
/// empresa borrada entre la comprobación y el guardado ⇒ <c>EmpresaExiste = false</c>.
/// </summary>
internal sealed class TenantProductProvisioningRepository(IIdentityDb context) : ITenantProductProvisioningStore
{
    private readonly IIdentityDb _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ProvisioningGuardado> GuardarSiEsMasNuevoAsync(TenantProductProvisioning reporte, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reporte);

        if (!await _context.Tenants.AsNoTracking().AnyAsync(t => t.Id == reporte.TenantId, cancellationToken).ConfigureAwait(false))
            return new ProvisioningGuardado(EmpresaExiste: false, Aplicado: false, VersionGuardada: 0);

        List<long> aplicadas;
        try
        {
            // Parametrizado (FormattableString): EF crea un parámetro por cada valor interpolado.
            aplicadas = await _context.Database.SqlQuery<long>($"""
                INSERT INTO platform.tenant_product_provisioning
                       (tenant_id, product_code, status, reason_code, message, action, signature_confirmed, reported_version, reported_at)
                VALUES ({reporte.TenantId}, {reporte.ProductCode}, {reporte.Estado}, {reporte.MotivoCodigo}, {reporte.Mensaje},
                        {reporte.Accion}, {reporte.FirmaConfirmada}, {reporte.Version}, {reporte.ActualizadoEn})
                ON CONFLICT (tenant_id, product_code) DO UPDATE
                   SET status = EXCLUDED.status,
                       reason_code = EXCLUDED.reason_code,
                       message = EXCLUDED.message,
                       action = EXCLUDED.action,
                       signature_confirmed = EXCLUDED.signature_confirmed,
                       reported_version = EXCLUDED.reported_version,
                       reported_at = EXCLUDED.reported_at
                 WHERE platform.tenant_product_provisioning.reported_version < EXCLUDED.reported_version
                RETURNING reported_version AS "Value"
                """).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (FkRota(ex) is { } fk)
        {
            // La empresa se borró entre la comprobación de arriba y el guardado: igual que si no existiera.
            if (string.Equals(fk.ConstraintName, FkEmpresa, StringComparison.Ordinal))
                return new ProvisioningGuardado(EmpresaExiste: false, Aplicado: false, VersionGuardada: 0);
            throw new TenantProductException(
                TenantProductException.ProductNotFound, $"El producto '{reporte.ProductCode}' no existe.");
        }

        if (aplicadas.Count == 1)
            return new ProvisioningGuardado(EmpresaExiste: true, Aplicado: true, VersionGuardada: aplicadas[0]);

        var vigente = await _context.Set<TenantProductProvisioningEntity>().AsNoTracking()
            .Where(p => p.TenantId == reporte.TenantId && p.ProductCode == reporte.ProductCode)
            .Select(p => p.ReportedVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ProvisioningGuardado(EmpresaExiste: true, Aplicado: false, VersionGuardada: vigente);
    }

    public async Task<IReadOnlyList<TenantProductProvisioning>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.Set<TenantProductProvisioningEntity>().AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.ProductCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(p => new TenantProductProvisioning(
                p.TenantId, p.ProductCode, p.Status, p.ReasonCode, p.Message, p.Action, p.SignatureConfirmed, p.ReportedVersion, p.ReportedAt))
            .ToList();
    }

    /// <summary>
    /// HU #14711 — lee con seguimiento la fila de <c>diagnostico</c> y devuelve si existe y la acción que (justo antes del
    /// guardado del cambio del producto) la crea o la reinicia en <c>PENDIENTE</c>: sin motivo, intentos en 0 y
    /// <c>reported_version</c> + 1 (1 al crearla). El próximo intento queda en null, salvo que una ronda la tenga reservada
    /// (<c>PENDIENTE</c> con <c>next_attempt_at</c> futuro): entonces se conserva, recortado a una reserva desde ahora, para que
    /// otra réplica no la tome mientras la ronda en curso la atiende (su resultado se descartará por la versión). Si la acción no
    /// se invoca, la fila leída queda sin cambios.
    /// </summary>
    public async Task<PreparacionPendienteAltaDiagnostico> PrepararPendienteAltaDiagnosticoAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var fila = await _context.Set<TenantProductProvisioningEntity>()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ProductCode == ProductoDiagnostico, cancellationToken)
            .ConfigureAwait(false);
        var existe = fila is not null;
        return new PreparacionPendienteAltaDiagnostico(existe, en =>
        {
            if (fila is null)
            {
                fila = new TenantProductProvisioningEntity { TenantId = tenantId, ProductCode = ProductoDiagnostico, ReportedVersion = 0 };
                _context.Set<TenantProductProvisioningEntity>().Add(fila);
            }

            var reservadaHasta = fila.Status == EstadosAltaDiagnostico.Pendiente && fila.NextAttemptAt is { } hasta && hasta > en
                ? (DateTimeOffset?)Min(hasta, en + AltaCredencialDiagnosticoAutomatica.Reserva)
                : null;
            fila.Status = EstadosAltaDiagnostico.Pendiente;
            fila.ReasonCode = null;
            fila.Message = null;
            fila.Action = null;
            fila.SignatureConfirmed = null;
            fila.Attempts = 0;
            fila.NextAttemptAt = reservadaHasta;
            fila.ReportedVersion++;
            fila.ReportedAt = en;
        });
    }

    /// <summary>
    /// HU #14711 — en UNA transacción con <c>app.is_superadmin</c> (la ronda lee entre empresas; como
    /// <c>AdminAuditWriter</c>), un solo <c>UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING</c>: reserva hasta
    /// <paramref name="lote"/> filas de <c>diagnostico</c> en <c>PENDIENTE</c> con el próximo intento vencido (o sin él), les
    /// corre <c>next_attempt_at</c> al fin de la reserva y devuelve la empresa, sus intentos y su <c>reported_version</c> (que
    /// condiciona el guardado del resultado). Una fila bloqueada por otra réplica se salta. La lista entre empresas solo lleva ids
    /// y contadores.
    /// </summary>
    public async Task<IReadOnlyList<PendienteAltaDiagnostico>> ReservarPendientesAltaDiagnosticoAsync(
        DateTimeOffset ahora, TimeSpan reserva, int lote, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lote, 1);
        var hasta = ahora + reserva;
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var tx = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlAsync($"SELECT set_config('app.is_superadmin', 'true', true)", cancellationToken).ConfigureAwait(false);
                var filas = await _context.Database.SqlQuery<FilaReservada>($"""
                    UPDATE platform.tenant_product_provisioning p
                       SET next_attempt_at = {hasta}
                     WHERE p.id IN (
                           SELECT c.id FROM platform.tenant_product_provisioning c
                            WHERE c.product_code = {ProductoDiagnostico} AND c.status = {EstadosAltaDiagnostico.Pendiente}
                              AND (c.next_attempt_at IS NULL OR c.next_attempt_at <= {ahora})
                            ORDER BY c.next_attempt_at NULLS FIRST, c.reported_at
                            LIMIT {lote}
                              FOR UPDATE SKIP LOCKED)
                    RETURNING p.tenant_id, p.attempts, p.reported_version
                    """).ToListAsync(cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<PendienteAltaDiagnostico>)filas.Select(f => new PendienteAltaDiagnostico(f.TenantId, f.Attempts, f.ReportedVersion)).ToList();
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// HU #14711 (M-1) — upsert del estado que dejó una ronda (parametrizado), condicionado a la versión reservada:
    /// <c>ON CONFLICT … DO UPDATE … WHERE reported_version = {versionReservada}</c>, que la sube en 1. 0 filas ⇒ la fila cambió
    /// durante la ronda (se reinició a <c>PENDIENTE</c> o el botón guardó otro estado): <c>false</c>. Sin fila la crea con la
    /// versión siguiente. Una empresa borrada entre la reserva y el guardado (<c>23503</c>) se ignora (<c>false</c>).
    /// </summary>
    public async Task<bool> RegistrarEstadoAltaDiagnosticoAsync(EstadoAltaDiagnostico estado, long versionReservada, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(estado);
        var siguiente = versionReservada + 1;
        try
        {
            var filas = await _context.Database.ExecuteSqlAsync($"""
                INSERT INTO platform.tenant_product_provisioning
                       (tenant_id, product_code, status, reason_code, message, action, signature_confirmed, reported_version, reported_at,
                        attempts, next_attempt_at)
                VALUES ({estado.TenantId}, {ProductoDiagnostico}, {estado.Estado}, {estado.MotivoCodigo}, {estado.Mensaje}, {estado.Accion},
                        NULL, {siguiente}, {estado.En}, {estado.Intentos}, {estado.ProximoIntentoEn})
                ON CONFLICT (tenant_id, product_code) DO UPDATE
                   SET status = EXCLUDED.status,
                       reason_code = EXCLUDED.reason_code,
                       message = EXCLUDED.message,
                       action = EXCLUDED.action,
                       signature_confirmed = NULL,
                       reported_version = EXCLUDED.reported_version,
                       reported_at = EXCLUDED.reported_at,
                       attempts = EXCLUDED.attempts,
                       next_attempt_at = EXCLUDED.next_attempt_at
                 WHERE platform.tenant_product_provisioning.reported_version = {versionReservada}
                """, cancellationToken).ConfigureAwait(false);
            return filas == 1;
        }
        catch (Exception ex) when (FkRota(ex) is { ConstraintName: FkEmpresa })
        {
            // La empresa se borró mientras se atendía: no hay estado que guardar.
            return false;
        }
    }

    /// <summary>
    /// HU #14711 (N-3) — upsert del estado que deja el botón (parametrizado): crea la fila con <c>reported_version</c> 1 o la
    /// reemplaza subiéndola en 1, solo si el estado cambia (<c>IS DISTINCT FROM</c> sobre estado, motivo, mensaje, acción, firma,
    /// intentos y próximo intento). Sin cambio de estado no se toca la fila. Una empresa borrada (<c>23503</c>) se ignora.
    /// </summary>
    public async Task RegistrarEstadoAltaDiagnosticoManualAsync(EstadoAltaDiagnostico estado, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(estado);
        try
        {
            await _context.Database.ExecuteSqlAsync($"""
                INSERT INTO platform.tenant_product_provisioning AS p
                       (tenant_id, product_code, status, reason_code, message, action, signature_confirmed, reported_version, reported_at,
                        attempts, next_attempt_at)
                VALUES ({estado.TenantId}, {ProductoDiagnostico}, {estado.Estado}, {estado.MotivoCodigo}, {estado.Mensaje}, {estado.Accion},
                        NULL, 1, {estado.En}, {estado.Intentos}, {estado.ProximoIntentoEn})
                ON CONFLICT (tenant_id, product_code) DO UPDATE
                   SET status = EXCLUDED.status,
                       reason_code = EXCLUDED.reason_code,
                       message = EXCLUDED.message,
                       action = EXCLUDED.action,
                       signature_confirmed = NULL,
                       reported_version = p.reported_version + 1,
                       reported_at = EXCLUDED.reported_at,
                       attempts = EXCLUDED.attempts,
                       next_attempt_at = EXCLUDED.next_attempt_at
                 WHERE (p.status, p.reason_code, p.message, p.action, p.signature_confirmed, p.attempts, p.next_attempt_at)
                       IS DISTINCT FROM
                       (EXCLUDED.status, EXCLUDED.reason_code, EXCLUDED.message, EXCLUDED.action, NULL::boolean, EXCLUDED.attempts, EXCLUDED.next_attempt_at)
                """, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (FkRota(ex) is { ConstraintName: FkEmpresa })
        {
            // La empresa se borró mientras se atendía: no hay estado que guardar.
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    /// <summary>Producto del alta automática de la credencial del impuesto (HU #14711).</summary>
    private const string ProductoDiagnostico = "diagnostico";

    /// <summary>Fila que devuelve la reserva (snake_case por la convención del contexto: tenant_id, attempts, reported_version).</summary>
    private sealed class FilaReservada
    {
        public Guid TenantId { get; set; }

        public int Attempts { get; set; }

        public long ReportedVersion { get; set; }
    }

    /// <summary>FK de la empresa (DDL 205); la otra FK de la tabla es la del catálogo de productos.</summary>
    private const string FkEmpresa = "fk_tenant_product_provisioning_tenants";

    /// <summary>La violación de FK (<c>23503</c>) de la consulta, venga sola o envuelta por EF.</summary>
    private static PostgresException? FkRota(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.ForeignKeyViolation } pg ? pg : null;
}
