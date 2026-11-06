using System.Data;
using System.Data.Common;
using System.Text.Json;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using Flit.Infrastructure.Domains;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #14381–#14384 — dominio del portal por organismo sobre <c>admin.transit_office_domains</c> y
/// <c>admin.v_active_portal_domains</c> (DDL 183). SQL parametrizado con parámetros tipados (ADO.NET sobre la conexión del contexto):
/// es una tabla propia, no una entidad EF, y no comparte tabla ni disparadores con marca blanca. Las lecturas del portal no pasan
/// por ninguna caché; solo la resolución de host del <see cref="CachedTenantDomainResolver"/> (60 s) se invalida al entrar o salir
/// de <c>active</c>.
/// </summary>
internal sealed class PortalDominioRepository(IIdentityDb db, IMemoryCache? cache = null) : IPortalDominioRepository
{
    private const string Columnas = """
        id, transit_office_id, host, status, failure_reason, verification_token, verified_at, activated_at,
        certificate_issued_at, certificate_expires_at, last_checked_at, next_check_at, check_attempts, grace_until,
        updated_at, row_version, is_primary
        """;

    public async Task<IReadOnlyList<DominioDePortal>> ListarAsync(Guid transitOfficeId, CancellationToken ct = default) =>
        await ConsultarAsync(
            $"""
            SELECT {Columnas} FROM admin.transit_office_domains
             WHERE transit_office_id = @office AND deleted_at IS NULL
             ORDER BY is_primary DESC, created_at, id
            """,
            Mapear, ct, P("office", transitOfficeId)).ConfigureAwait(false);

    public async Task<DominioDePortal?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        (await ConsultarAsync(
            $"SELECT {Columnas} FROM admin.transit_office_domains WHERE id = @id AND deleted_at IS NULL",
            Mapear, ct, P("id", id)).ConfigureAwait(false)).FirstOrDefault();

    public async Task<DominioDePortal?> ObtenerPorHostAsync(string host, CancellationToken ct = default) =>
        (await ConsultarAsync(
            $"SELECT {Columnas} FROM admin.transit_office_domains WHERE host = @host AND deleted_at IS NULL",
            Mapear, ct, P("host", host)).ConfigureAwait(false)).FirstOrDefault();

    public async Task<PortalDominioRegistro> RegistrarAsync(
        Guid transitOfficeId, string host, string verificationToken, Guid? actor, int maximo, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationToken);

        var vigentes = await ListarAsync(transitOfficeId, ct).ConfigureAwait(false);
        if (vigentes.FirstOrDefault(d => string.Equals(d.Host, host, StringComparison.Ordinal)) is { } existente)
            return PortalDominioRegistro.Ok(existente);
        if (vigentes.Count >= maximo)
            return PortalDominioRegistro.Falla(PortalDominioRegistroFallo.LimiteAlcanzado);

        try
        {
            // next_check_at = now(): sin él el trabajo programado no toma la fila (solo reclama las vencidas).
            await EjecutarAsync(
                """
                INSERT INTO admin.transit_office_domains (transit_office_id, host, verification_token, next_check_at, created_by, updated_by)
                VALUES (@office, @host, @token, now(), @actor, @actor)
                """,
                ct, P("office", transitOfficeId), P("host", host), P("token", verificationToken), P("actor", actor)).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation
            && ex.ConstraintName is "uq_transit_office_domains_host" or "uq_domain_host_entre_tablas")
        {
            return PortalDominioRegistro.Falla(PortalDominioRegistroFallo.HostEnUso);
        }

        return PortalDominioRegistro.Ok((await ObtenerPorHostAsync(host, ct).ConfigureAwait(false))!);
    }

    public async Task<DominioDePortal?> RetirarAsync(Guid id, Guid? actor, CancellationToken ct = default)
    {
        var retirado = (await ConsultarAsync(
            $"""
            UPDATE admin.transit_office_domains
               SET deleted_at = now(), deleted_by = @actor, updated_at = now(), updated_by = @actor, next_check_at = NULL, is_primary = false
             WHERE id = @id AND deleted_at IS NULL
            RETURNING {Columnas}
            """,
            Mapear, ct, P("id", id), P("actor", actor)).ConfigureAwait(false)).FirstOrDefault();

        if (retirado is null)
            return null;

        await AsegurarPrincipalAsync(retirado.TransitOfficeId, ct).ConfigureAwait(false);
        Invalidar(retirado.Host);
        return retirado;
    }

    public async Task<DominioDePortal?> EstablecerPrincipalAsync(Guid id, Guid? actor, CancellationToken ct = default)
    {
        if (await ObtenerPorIdAsync(id, ct).ConfigureAwait(false) is not { Status: "active" } dominio)
            return null;
        if (dominio.IsPrimary)
            return dominio;

        // Dos sentencias en un solo comando (transacción implícita): primero cae el principal anterior, luego sube el nuevo.
        await EjecutarAsync(
            """
            UPDATE admin.transit_office_domains SET is_primary = false, updated_at = now()
             WHERE transit_office_id = @office AND deleted_at IS NULL AND is_primary AND id <> @id;
            UPDATE admin.transit_office_domains SET is_primary = true, updated_at = now(), updated_by = @actor
             WHERE id = @id AND deleted_at IS NULL;
            """,
            ct, P("id", id), P("office", dominio.TransitOfficeId), P("actor", actor)).ConfigureAwait(false);
        return await ObtenerPorIdAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>Si el organismo no tiene principal, lo es el dominio activo más antiguo. No hace nada si ya hay uno o no hay activos.</summary>
    private Task AsegurarPrincipalAsync(Guid transitOfficeId, CancellationToken ct) =>
        EjecutarAsync(
            """
            UPDATE admin.transit_office_domains
               SET is_primary = true, updated_at = now()
             WHERE id = (SELECT c.id FROM admin.transit_office_domains c
                          WHERE c.transit_office_id = @office AND c.status = 'active' AND c.deleted_at IS NULL
                          ORDER BY c.activated_at, c.created_at, c.id LIMIT 1)
               AND NOT EXISTS (SELECT 1 FROM admin.transit_office_domains p
                                WHERE p.transit_office_id = @office AND p.is_primary AND p.deleted_at IS NULL)
            """,
            ct, P("office", transitOfficeId));

    public async Task<PortalDominioOrganismo?> ObtenerOrganismoActivoPorHostAsync(string host, CancellationToken ct = default) =>
        (await ConsultarAsync(
            """
            SELECT v.transit_office_id, o.code
              FROM admin.v_active_portal_domains v
              JOIN catalogs.transit_offices o ON o.id = v.transit_office_id
             WHERE v.host = @host
            """,
            r => new PortalDominioOrganismo(r.GetGuid(0), r.GetString(1)), ct, P("host", host)).ConfigureAwait(false)).FirstOrDefault();

    public async Task<string?> ObtenerHostActivoPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        (await ConsultarAsync(
            """
            SELECT d.host
              FROM admin.transit_office_domains d
              JOIN catalogs.transit_offices o ON o.id = d.transit_office_id
             WHERE upper(o.code) = upper(@codigo) AND d.status = 'active' AND d.deleted_at IS NULL AND o.is_active
             ORDER BY d.is_primary DESC, d.activated_at, d.id
             LIMIT 1
            """,
            r => r.GetString(0), ct, P("codigo", codigo)).ConfigureAwait(false)).FirstOrDefault();

    public async Task<IReadOnlyList<string>> ListarHostsActivosAsync(CancellationToken ct = default) =>
        await ConsultarAsync("SELECT host FROM admin.v_active_portal_domains ORDER BY host", r => r.GetString(0), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> ListarHostsPendientesDeCertificadoAsync(DateTimeOffset renovarAntes, CancellationToken ct = default) =>
        await ConsultarAsync(
            """
            SELECT d.host
              FROM admin.transit_office_domains d
              JOIN catalogs.transit_offices o ON o.id = d.transit_office_id
             WHERE d.deleted_at IS NULL AND o.is_active
               AND ((d.status = 'verified' AND d.certificate_issued_at IS NULL)
                    OR (d.status = 'active' AND d.certificate_expires_at IS NOT NULL AND d.certificate_expires_at <= @antes))
             ORDER BY d.host
            """,
            r => r.GetString(0), ct, P("antes", renovarAntes)).ConfigureAwait(false);

    public async Task<IReadOnlyList<PortalDominioAprovisionamiento>> ListarAprovisionamientoAsync(CancellationToken ct = default) =>
        await ConsultarAsync(
            """
            SELECT d.host, d.status, d.certificate_issued_at, d.certificate_expires_at
              FROM admin.transit_office_domains d
              JOIN catalogs.transit_offices o ON o.id = d.transit_office_id
             WHERE d.deleted_at IS NULL AND o.is_active
               AND (d.status IN ('verified', 'active') OR d.certificate_issued_at IS NOT NULL)
             ORDER BY d.host
            """,
            r => new PortalDominioAprovisionamiento(r.GetString(0), r.GetString(1), Fecha(r, 2), Fecha(r, 3)), ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<PortalDominioReclamo>> ReclamarVencidosAsync(
        int lote, TimeSpan arriendo, DateTimeOffset ahora, CancellationToken ct = default)
    {
        if (lote <= 0)
            return [];

        // UPDATE ... RETURNING con SKIP LOCKED en una sola sentencia: el arriendo (adelantar next_check_at) es la reclamación.
        return await ConsultarAsync(
            """
            UPDATE admin.transit_office_domains
               SET next_check_at = @hasta
             WHERE id IN (
                   SELECT id FROM admin.transit_office_domains
                    WHERE next_check_at <= @ahora AND deleted_at IS NULL AND status IN ('pending', 'failed', 'active')
                    ORDER BY next_check_at
                    LIMIT @lote
                    FOR UPDATE SKIP LOCKED)
            RETURNING id, transit_office_id, host
            """,
            r => new PortalDominioReclamo(r.GetGuid(0), r.GetGuid(1), r.GetString(2)), ct,
            P("hasta", ahora + arriendo), P("ahora", ahora), P("lote", lote)).ConfigureAwait(false);
    }

    public async Task<DominioDePortal?> AplicarComprobacionAsync(
        Guid id, string nuevoEstado, string? motivo, DateTimeOffset? verificadoEn, DateTimeOffset? graciaHasta, int intentos,
        DateTimeOffset? proximaComprobacion, DateTimeOffset ahora, Guid? actor, string? trabajo, CancellationToken ct = default)
    {
        var anterior = await ObtenerPorIdAsync(id, ct).ConfigureAwait(false);
        if (anterior is null)
            return null;

        var actualizado = (await ConsultarAsync(
            $"""
            UPDATE admin.transit_office_domains
               SET status = @estado,
                   failure_reason = CASE WHEN @estado = 'failed' THEN @motivo ELSE NULL END,
                   failed_at = CASE WHEN @estado = 'failed' THEN @ahora ELSE failed_at END,
                   verified_at = COALESCE(@verificado, verified_at),
                   grace_until = @gracia, check_attempts = @intentos, last_checked_at = @ahora, next_check_at = @proxima,
                   is_primary = (is_primary AND @estado = 'active'), updated_at = @ahora, updated_by = @actor
             WHERE id = @id AND deleted_at IS NULL
            RETURNING {Columnas}
            """,
            Mapear, ct,
            P("id", id), P("estado", nuevoEstado), P("motivo", motivo), P("ahora", ahora), P("verificado", verificadoEn),
            P("gracia", graciaHasta), P("intentos", intentos), P("proxima", proximaComprobacion), P("actor", actor)).ConfigureAwait(false)).FirstOrDefault();

        if (actualizado is null)
            return null;

        await AuditarEstadoAsync(anterior.TransitOfficeId, anterior.Status, nuevoEstado, motivo, actor, trabajo, ahora, ct).ConfigureAwait(false);
        await AsegurarPrincipalAsync(anterior.TransitOfficeId, ct).ConfigureAwait(false);
        InvalidarSiCambiaActivo(actualizado.Host, anterior.Status, nuevoEstado);
        return actualizado;
    }

    public async Task<DominioDePortal?> AplicarCertificadoAsync(
        string host, string nuevoEstado, DateTimeOffset? activadoEn, DateTimeOffset emitidoEn, DateTimeOffset? venceEn, string trabajo,
        CancellationToken ct = default)
    {
        var anterior = await ObtenerPorHostAsync(host, ct).ConfigureAwait(false);
        if (anterior is null)
            return null;

        // Un dominio recién activo se vuelve a comprobar a su ritmo de revalidación (como en marca blanca).
        DateTimeOffset? proxima = activadoEn?.AddDays(1);
        var actualizado = (await ConsultarAsync(
            $"""
            UPDATE admin.transit_office_domains
               SET certificate_issued_at = @emitido, certificate_expires_at = @vence,
                   status = @estado, activated_at = COALESCE(@activado, activated_at), failure_reason = NULL,
                   next_check_at = CASE WHEN @estado = 'active' AND status <> 'active' THEN @proxima ELSE next_check_at END,
                   updated_at = now()
             WHERE host = @host AND deleted_at IS NULL
            RETURNING {Columnas}
            """,
            Mapear, ct,
            P("host", host), P("emitido", emitidoEn), P("vence", venceEn), P("estado", nuevoEstado), P("activado", activadoEn), P("proxima", proxima))
            .ConfigureAwait(false)).FirstOrDefault();

        if (actualizado is null)
            return null;

        if (!string.Equals(anterior.Status, nuevoEstado, StringComparison.Ordinal))
            await AuditarEstadoAsync(anterior.TransitOfficeId, anterior.Status, nuevoEstado, null, null, trabajo, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        await AsegurarPrincipalAsync(anterior.TransitOfficeId, ct).ConfigureAwait(false);
        InvalidarSiCambiaActivo(host, anterior.Status, nuevoEstado);
        return actualizado;
    }

    public async Task<DominioDePortal?> MarcarCertificadoFallidoAsync(
        string host, string motivo, DateTimeOffset ahora, DateTimeOffset proximaComprobacion, string trabajo, CancellationToken ct = default)
    {
        var fallido = (await ConsultarAsync(
            $"""
            UPDATE admin.transit_office_domains
               SET status = 'failed', failure_reason = @motivo, failed_at = @ahora, check_attempts = 1, next_check_at = @proxima, updated_at = @ahora
             WHERE host = @host AND deleted_at IS NULL AND status = 'verified'
            RETURNING {Columnas}
            """,
            Mapear, ct, P("host", host), P("motivo", motivo), P("ahora", ahora), P("proxima", proximaComprobacion)).ConfigureAwait(false)).FirstOrDefault();

        if (fallido is null)
            return null;

        await AuditarEstadoAsync(fallido.TransitOfficeId, TenantDomainStatus.Verified, TenantDomainStatus.Failed, motivo, null, trabajo, ahora, ct).ConfigureAwait(false);
        return fallido;
    }

    /// <summary>Rastro legible de cada cambio de estado, con el autor (usuario o trabajo) y el motivo si lo hay.</summary>
    private Task AuditarEstadoAsync(
        Guid transitOfficeId, string anterior, string nuevo, string? motivo, Guid? actor, string? trabajo, DateTimeOffset ahora, CancellationToken ct)
    {
        if (string.Equals(anterior, nuevo, StringComparison.Ordinal))
            return Task.CompletedTask;

        var autor = trabajo ?? actor?.ToString() ?? "system";
        return EjecutarAsync(
            """
            INSERT INTO admin.tenant_config_audit_logs
                (id, tenant_id, entity_name, field_name, old_value, new_value, changed_at, changed_by, operation, result, module,
                 target_entity_type, target_entity_id)
            VALUES (uuidv7(), NULL, 'PortalDominio', 'status', CAST(@anterior AS jsonb), CAST(@nuevo AS jsonb), @ahora, @actor, 'update', 'success',
                    'config', 'TRANSIT_OFFICE_PORTAL_DOMINIO', @office)
            """,
            ct,
            P("anterior", JsonSerializer.Serialize(new { status = anterior })),
            P("nuevo", JsonSerializer.Serialize(new { status = nuevo, reason = motivo, changedBy = autor })),
            P("ahora", ahora), P("actor", actor), P("office", transitOfficeId));
    }

    private void InvalidarSiCambiaActivo(string host, string anterior, string nuevo)
    {
        if ((anterior == TenantDomainStatus.Active) != (nuevo == TenantDomainStatus.Active))
            Invalidar(host);
    }

    /// <summary>Quita de la caché de 60 s del resolutor el host y la lista de activos, sin esperar el TTL.</summary>
    private void Invalidar(string host)
    {
        cache?.Remove(CachedTenantDomainResolver.ResolveCacheKey(host));
        cache?.Remove(CachedTenantDomainResolver.ActiveHostsCacheKey);
    }

    // ── ADO.NET sobre la conexión del contexto ───────────────────────────────────────────────────────────────────────

    private static NpgsqlParameter P(string nombre, object? valor) => valor switch
    {
        null => new NpgsqlParameter(nombre, DBNull.Value),
        Guid g => new NpgsqlParameter(nombre, NpgsqlDbType.Uuid) { Value = g },
        DateTimeOffset f => new NpgsqlParameter(nombre, NpgsqlDbType.TimestampTz) { Value = f.UtcDateTime },
        int i => new NpgsqlParameter(nombre, NpgsqlDbType.Integer) { Value = i },
        string s => new NpgsqlParameter(nombre, NpgsqlDbType.Text) { Value = s },
        _ => new NpgsqlParameter(nombre, valor),
    };

    private static DateTimeOffset? Fecha(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateTimeOffset>(i);

    private static DominioDePortal Mapear(DbDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5),
        Fecha(r, 6), Fecha(r, 7), Fecha(r, 8), Fecha(r, 9), Fecha(r, 10), Fecha(r, 11), r.GetInt32(12), Fecha(r, 13),
        r.GetFieldValue<DateTimeOffset>(14), r.GetInt64(15), r.GetBoolean(16));

    private async Task<List<T>> ConsultarAsync<T>(string sql, Func<DbDataReader, T> mapear, CancellationToken ct, params NpgsqlParameter[] parametros)
    {
        var conexion = db.Database.GetDbConnection();
        var abierta = conexion.State != ConnectionState.Open;
        if (abierta)
            await conexion.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var comando = conexion.CreateCommand();
            comando.CommandText = sql;
            comando.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var p in parametros)
                comando.Parameters.Add(p);

            var filas = new List<T>();
            await using var lector = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await lector.ReadAsync(ct).ConfigureAwait(false))
                filas.Add(mapear(lector));
            return filas;
        }
        finally
        {
            if (abierta)
                await conexion.CloseAsync().ConfigureAwait(false);
        }
    }

    private async Task EjecutarAsync(string sql, CancellationToken ct, params NpgsqlParameter[] parametros)
    {
        var conexion = db.Database.GetDbConnection();
        var abierta = conexion.State != ConnectionState.Open;
        if (abierta)
            await conexion.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using var comando = conexion.CreateCommand();
            comando.CommandText = sql;
            comando.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var p in parametros)
                comando.Parameters.Add(p);
            await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (abierta)
                await conexion.CloseAsync().ConfigureAwait(false);
        }
    }
}
