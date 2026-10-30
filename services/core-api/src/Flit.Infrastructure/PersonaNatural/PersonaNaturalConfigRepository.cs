using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Platform.Sdk;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>
/// HU #14072 (Feature J, Épica #14021) — configuración del portal: lista blanca de tipos y parámetros generales (DDL 164) y
/// habilitación de organismos con el grant que YA existe (<c>admin.tenant_transit_office_grants</c> de la empresa Persona
/// Natural). SQL parametrizado (las tablas nuevas no son entidades EF). Los grants tienen RLS por empresa: se leen y escriben
/// bajo <see cref="TenantRlsScope"/> con la empresa de Persona Natural, así el superadministrador no depende de un
/// <c>X-Tenant-Id</c>.
/// </summary>
internal sealed class PersonaNaturalConfigRepository(
    FlitDbContext db, PersonaNaturalTiposReader tiposReader, PersonaNaturalPortalReader portal) : IPersonaNaturalConfigRepository
{
    // ── Parámetros ────────────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<ParametrosPn> ObtenerParametrosAsync(CancellationToken ct = default)
    {
        var fila = (await db.Database.SqlQuery<ParametrosFila>($"""
            SELECT session_validity_minutes, draft_validity_days, queries_per_citizen_per_day, queries_global_per_day,
                   captcha_enabled, support_email, support_phone, support_hours, row_version, updated_at, updated_by
              FROM admin.persona_natural_settings
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        return fila is null ? new ParametrosPn(null, null, null, null, null, null, null, null, 0, null, null) : ToView(fila);
    }

    public async Task<GuardarParametrosPnResultado> GuardarParametrosAsync(
        ParametrosPn nuevos, long rowVersionEsperada, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nuevos);
        // La fila única nace con el DDL; si faltara (base restaurada a mano), se recrea con los predeterminados.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO admin.persona_natural_settings (singleton) VALUES (true) ON CONFLICT (singleton) DO NOTHING", ct).ConfigureAwait(false);

        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE admin.persona_natural_settings
               SET session_validity_minutes = {nuevos.SesionVigenciaMinutos}::int,
                   draft_validity_days = {nuevos.BorradorVigenciaDias}::int,
                   queries_per_citizen_per_day = {nuevos.TopeConsultasCiudadanoDia}::int,
                   queries_global_per_day = {nuevos.TopeConsultasGlobalDia}::int,
                   captcha_enabled = {nuevos.CaptchaActivo}::boolean,
                   support_email = {nuevos.SoporteCorreo}::text,
                   support_phone = {nuevos.SoporteTelefono}::text,
                   support_hours = {nuevos.SoporteHorario}::text,
                   updated_at = now(),
                   updated_by = {actor}
             WHERE singleton AND row_version = {rowVersionEsperada}
            """, ct).ConfigureAwait(false);

        var actual = await ObtenerParametrosAsync(ct).ConfigureAwait(false);
        return filas == 0 ? new GuardarParametrosPnResultado(actual, true) : new GuardarParametrosPnResultado(actual, false);
    }

    // ── Organismos (grant de la empresa Persona Natural) ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrganismoPnView>> ListarOrganismosAsync(bool soloHabilitados, CancellationToken ct = default)
    {
        var habilitados = await GrantsHabilitadosAsync(ct).ConfigureAwait(false);
        var canales = await CanalesPuntoEncendidosAsync(ct).ConfigureAwait(false);
        // Solo los «activos por FLIT» (operables); un habilitado que dejó de serlo sigue en la lista para poder apagarlo.
        var operables = await portal.OrganismosOperablesAsync(ct).ConfigureAwait(false);
        var visibles = operables.Concat(habilitados).ToList();
        var oficinas = await db.TransitOffices.AsNoTracking().Where(o => o.IsActive && visibles.Contains(o.Id)).OrderBy(o => o.Name)
            .Select(o => new { o.Id, o.Code, o.Name, o.CityName, o.DepartmentName })
            .ToListAsync(ct).ConfigureAwait(false);
        var ofrecibles = (await TiposAsync(ct).ConfigureAwait(false)).Where(t => t.Ofrecible).Select(t => t.Id).ToList();
        var tarifas = await portal.TarifasVigentesAsync(portal.HoyColombia(), ct).ConfigureAwait(false);

        return oficinas
            .Where(o => !soloHabilitados || habilitados.Contains(o.Id))
            .Select(o => new OrganismoPnView(
                o.Id, o.Code, o.Name, o.CityName ?? string.Empty, o.DepartmentName ?? string.Empty, habilitados.Contains(o.Id),
                ofrecibles.Count(t => TieneTarifaBase(t, o.Id, tarifas)), operables.Contains(o.Id), canales.Contains(o.Id)))
            .ToList();
    }

    public async Task<CambioHabilitacionPn> EstablecerCanalPuntoAsync(Guid organismoId, bool encendido, Guid actor, CancellationToken ct = default)
    {
        if (!await db.TransitOffices.AsNoTracking().AnyAsync(o => o.Id == organismoId && o.IsActive, ct).ConfigureAwait(false))
            return new CambioHabilitacionPn(false, false);

        var previo = (await db.Database.SqlQuery<bool>($"""
            SELECT is_enabled AS "Value" FROM admin.persona_natural_point_channel WHERE transit_office_id = {organismoId}
            """).ToListAsync(ct).ConfigureAwait(false)).Select(v => (bool?)v).FirstOrDefault();
        var estadoPrevio = previo ?? false;
        if (previo == encendido || (previo is null && !encendido))
            return new CambioHabilitacionPn(true, estadoPrevio);

        // Solo esta tabla. El grant del portal (tenant_transit_office_grants) no se lee ni se escribe aquí.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.persona_natural_point_channel (transit_office_id, is_enabled, updated_at, updated_by)
            VALUES ({organismoId}, {encendido}, now(), {actor})
            ON CONFLICT (transit_office_id) DO UPDATE
                SET is_enabled = EXCLUDED.is_enabled, updated_at = now(), updated_by = EXCLUDED.updated_by
            """, ct).ConfigureAwait(false);
        return new CambioHabilitacionPn(true, estadoPrevio);
    }

    public async Task<CanalPuntoResolucion> ResolverCanalPuntoAsync(string codigo, CancellationToken ct = default)
    {
        var buscado = (codigo ?? string.Empty).Trim().ToUpperInvariant();
        if (buscado.Length == 0)
            return new CanalPuntoResolucion(CanalPuntoResolucion.CodigoDesconocido, Guid.Empty, string.Empty, string.Empty);

        var oficina = await db.TransitOffices.AsNoTracking()
            .Where(o => o.IsActive && o.Code.ToUpper() == buscado)
            .Select(o => new { o.Id, o.Code, o.Name })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (oficina is null)
            return new CanalPuntoResolucion(CanalPuntoResolucion.CodigoDesconocido, Guid.Empty, string.Empty, string.Empty);
        if (!await OrganismoEsOperableAsync(oficina.Id, ct).ConfigureAwait(false))
            return new CanalPuntoResolucion(CanalPuntoResolucion.OrganismoNoOperable, oficina.Id, oficina.Code, oficina.Name);

        var encendido = (await db.Database.SqlQuery<bool>($"""
            SELECT is_enabled AS "Value" FROM admin.persona_natural_point_channel WHERE transit_office_id = {oficina.Id}
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();
        if (!encendido)
            return new CanalPuntoResolucion(CanalPuntoResolucion.CanalApagado, oficina.Id, oficina.Code, oficina.Name);

        return new CanalPuntoResolucion(CanalPuntoResolucion.Ok, oficina.Id, oficina.Code, oficina.Name);
    }

    public async Task<bool> OrganismoEsOperableAsync(Guid organismoId, CancellationToken ct = default) =>
        (await portal.OrganismosOperablesAsync(ct).ConfigureAwait(false)).Contains(organismoId);

    public Task<bool> ExisteOrganismoAsync(Guid organismoId, CancellationToken ct = default) =>
        db.TransitOffices.AsNoTracking().AnyAsync(o => o.Id == organismoId && o.IsActive, ct);

    public async Task<CambioHabilitacionPn> EstablecerOrganismoAsync(Guid organismoId, bool habilitado, Guid actor, CancellationToken ct = default)
    {
        if (!await db.TransitOffices.AsNoTracking().AnyAsync(o => o.Id == organismoId && o.IsActive, ct).ConfigureAwait(false))
            return new CambioHabilitacionPn(false, false);

        var empresa = PlatformTenants.PersonaNatural;
        return await TenantRlsScope.ExecuteAsync(db, empresa, async () =>
        {
            var previo = (await db.Database.SqlQuery<bool>($"""
                SELECT is_enabled AS "Value" FROM admin.tenant_transit_office_grants
                 WHERE tenant_id = {empresa} AND transit_office_id = {organismoId}
                """).ToListAsync(ct).ConfigureAwait(false)).Select(v => (bool?)v).FirstOrDefault();
            var estadoPrevio = previo ?? false;
            if (previo is null && !habilitado)
                return new CambioHabilitacionPn(true, false);
            if (previo == habilitado)
                return new CambioHabilitacionPn(true, estadoPrevio);

            // Origen SYSTEM: el grant lo gobierna la plataforma (HU #12346), no la compañía.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO admin.tenant_transit_office_grants (tenant_id, transit_office_id, is_enabled, source, created_by)
                VALUES ({empresa}, {organismoId}, {habilitado}, 'SYSTEM', {actor})
                ON CONFLICT (tenant_id, transit_office_id) DO UPDATE SET is_enabled = EXCLUDED.is_enabled
                """, ct).ConfigureAwait(false);
            return new CambioHabilitacionPn(true, estadoPrevio);
        }, ct).ConfigureAwait(false);
    }

    // ── Tipos (lista blanca) ──────────────────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TipoPnView>> ListarTiposAsync(CancellationToken ct = default)
    {
        var tipos = await TiposAsync(ct).ConfigureAwait(false);
        var habilitados = await GrantsHabilitadosAsync(ct).ConfigureAwait(false);
        var tarifas = await portal.TarifasVigentesAsync(portal.HoyColombia(), ct).ConfigureAwait(false);
        return tipos
            .Select(t => t with { OrganismosConTarifaVigente = habilitados.Count(o => TieneTarifaBase(t.Id, o, tarifas)) })
            .ToList();
    }

    public async Task<CambioHabilitacionPn> EstablecerTipoAsync(Guid tipoId, bool habilitado, Guid actor, CancellationToken ct = default)
    {
        if (!await db.ProcedureTypes.AsNoTracking().AnyAsync(t => t.Id == tipoId, ct).ConfigureAwait(false))
            return new CambioHabilitacionPn(false, false);

        var previo = (await db.Database.SqlQuery<bool>($"""
            SELECT is_enabled AS "Value" FROM admin.persona_natural_enabled_types WHERE procedure_type_id = {tipoId}
            """).ToListAsync(ct).ConfigureAwait(false)).Select(v => (bool?)v).FirstOrDefault();
        if (previo is null && !habilitado)
            return new CambioHabilitacionPn(true, false);
        if (previo == habilitado)
            return new CambioHabilitacionPn(true, habilitado);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.persona_natural_enabled_types (procedure_type_id, is_enabled, created_by)
            VALUES ({tipoId}, {habilitado}, {actor})
            ON CONFLICT (procedure_type_id) DO UPDATE SET is_enabled = EXCLUDED.is_enabled, updated_at = now(), updated_by = {actor}
            """, ct).ConfigureAwait(false);
        return new CambioHabilitacionPn(true, previo ?? false);
    }

    public async Task<bool> TipoEsCandidatoAsync(Guid tipoId, CancellationToken ct = default) =>
        (await tiposReader.ListarAsync(ct).ConfigureAwait(false)).Any(t => t.Id == tipoId && t.Elegibilidad.Elegible);

    // ── Soporte ───────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Candidatos (elegibles) y los habilitados que dejaron de serlo. El conteo de organismos con tarifa lo llena quien llama
    /// (<c>0</c> aquí).
    /// </summary>
    private async Task<List<TipoPnView>> TiposAsync(CancellationToken ct)
    {
        var habilitados = await portal.TiposHabilitadosAsync(ct).ConfigureAwait(false);
        var tipos = await tiposReader.ListarParaAdministracionAsync(habilitados, ct).ConfigureAwait(false);
        // Un no elegible solo sale Habilitado si conserva una fila vieja en la lista blanca (para poder apagarlo); nunca es ofrecible.
        return tipos
            .Select(t => new TipoPnView(
                t.Id, t.Codigo, t.Nombre, t.Familia, t.Elegibilidad.Elegible, t.Elegibilidad.Motivo,
                habilitados.Contains(t.Id),
                t.Elegibilidad.Elegible && habilitados.Contains(t.Id), 0, t.ActivoEnFlit))
            .ToList();
    }

    /// <summary>Organismos con el canal del punto encendido. Tabla global, sin RLS y sin el grant del portal.</summary>
    private async Task<HashSet<Guid>> CanalesPuntoEncendidosAsync(CancellationToken ct)
    {
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT transit_office_id AS "Value" FROM admin.persona_natural_point_channel WHERE is_enabled
            """).ToListAsync(ct).ConfigureAwait(false);
        return [.. ids];
    }

    /// <summary>Organismos con grant habilitado de la empresa Persona Natural (activos o no: el filtro de activos lo hace quien lista).</summary>
    private async Task<HashSet<Guid>> GrantsHabilitadosAsync(CancellationToken ct)
    {
        var empresa = PlatformTenants.PersonaNatural;
        var ids = await TenantRlsScope.ExecuteAsync(db, empresa, async () =>
            await db.TenantTransitOfficeGrants.AsNoTracking()
                .Where(g => g.TenantId == empresa && g.IsEnabled)
                .Select(g => g.TransitOfficeId).ToListAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return [.. ids];
    }

    private static bool TieneTarifaBase(Guid tipoId, Guid organismoId, IReadOnlyCollection<TarifaVigente> tarifas) =>
        CotizadorPersonaNatural.Resolver(ConceptosTarifa.DerechosTransito, tipoId, organismoId, tarifas) is not null
        && CotizadorPersonaNatural.Resolver(ConceptosTarifa.ServicioFlit, tipoId, organismoId, tarifas) is not null;

    private static ParametrosPn ToView(ParametrosFila f) => new(
        f.SessionValidityMinutes, f.DraftValidityDays, f.QueriesPerCitizenPerDay, f.QueriesGlobalPerDay, f.CaptchaEnabled,
        f.SupportEmail, f.SupportPhone, f.SupportHours, f.RowVersion, f.UpdatedAt, f.UpdatedBy);

    /// <summary>Forma de la fila leída por SQL (snake_case del DbContext = nombre de propiedad).</summary>
    private sealed class ParametrosFila
    {
        public int? SessionValidityMinutes { get; set; }
        public int? DraftValidityDays { get; set; }
        public int? QueriesPerCitizenPerDay { get; set; }
        public int? QueriesGlobalPerDay { get; set; }
        public bool? CaptchaEnabled { get; set; }
        public string? SupportEmail { get; set; }
        public string? SupportPhone { get; set; }
        public string? SupportHours { get; set; }
        public long RowVersion { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }
}
