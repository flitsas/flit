using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>Un organismo ofrecido a Persona Natural (con grant vigente para su empresa).</summary>
public sealed record OrganismoHabilitadoPn(Guid Id, string Codigo, string Nombre, string Municipio, string Departamento);

/// <summary>
/// HU #14043 (Épica #14021) — lecturas del catálogo del portal: organismos habilitados para la empresa «Persona Natural» y
/// tarifas vigentes. Solo lectura: las tarifas se administran desde Plataforma (Feature J).
/// </summary>
public sealed class PersonaNaturalPortalReader(FlitDbContext db, TimeProvider? reloj = null, Flit.Admin.Domain.Companies.TransitOffices.IEffectiveTransitOfficeListResolver? efectivos = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    /// <summary>Hoy en Colombia (UTC-5 todo el año, sin horario de verano): la vigencia de la tarifa se mide en días.</summary>
    public DateOnly HoyColombia() => DateOnly.FromDateTime(_reloj.GetUtcNow().UtcDateTime.AddHours(-5));

    /// <summary>
    /// Organismos con grant habilitado para <see cref="PlatformTenants.PersonaNatural"/> y activos. Sin grants devuelve
    /// lista vacía (no es un error): hoy la empresa no tiene organismos asignados hasta que se le concedan.
    /// </summary>
    public async Task<IReadOnlyList<OrganismoHabilitadoPn>> ListarOrganismosAsync(CancellationToken ct)
    {
        var filas = await (
                from g in db.TenantTransitOfficeGrants.AsNoTracking()
                join o in db.TransitOffices.AsNoTracking() on g.TransitOfficeId equals o.Id
                where g.TenantId == PlatformTenants.PersonaNatural && g.IsEnabled && o.IsActive
                orderby o.Name
                select new { o.Id, o.Code, o.Name, o.CityName, o.DepartmentName })
            .ToListAsync(ct).ConfigureAwait(false);
        // HU #14072: además del grant, el organismo debe estar operable en FLIT 2 (si no, core-api rechaza el trámite).
        var operables = await OrganismosOperablesAsync(ct).ConfigureAwait(false);
        return filas.Where(o => operables.Contains(o.Id))
            .Select(o => new OrganismoHabilitadoPn(o.Id, o.Code, o.Name, o.CityName ?? string.Empty, o.DepartmentName ?? string.Empty)).ToList();
    }

    public const int LimiteDestinoPorDefecto = 20;
    public const int LimiteDestinoMaximo = 50;

    /// <summary>
    /// HU 10 (D3 del PO) — organismos donde se puede trasladar una cuenta: los habilitados para la empresa Persona Natural
    /// (misma lista efectiva que valida FLIT 2 al radicar) y operables. Con <paramref name="id"/> devuelve solo ese si es
    /// operable; con <paramref name="texto"/> filtra por nombre, código, municipio o departamento. Ordenado por nombre y acotado.
    /// </summary>
    public async Task<IReadOnlyList<OrganismoHabilitadoPn>> ListarOrganismosDestinoAsync(string? texto, Guid? id, int limite, CancellationToken ct)
    {
        // D3 (PO, HU #14259): solo los organismos habilitados para la empresa Persona Natural, con el criterio de FLIT 2 al radicar
        // (ValidarOrganismoDestinoAsync -> ITransitOfficeGrantGate -> lista efectiva de la empresa) y además operables.
        var operables = await OrganismosOperablesAsync(ct).ConfigureAwait(false);
        var habilitados = efectivos is null
            ? [.. await db.TenantTransitOfficeGrants.AsNoTracking().Where(g => g.TenantId == PlatformTenants.PersonaNatural && g.IsEnabled).Select(g => g.TransitOfficeId).ToListAsync(ct).ConfigureAwait(false)]
            : (await efectivos.ListEffectiveOfficeIdsAsync(PlatformTenants.PersonaNatural, ct).ConfigureAwait(false)).ToHashSet();
        operables = operables.Where(habilitados.Contains).ToHashSet();
        if (operables.Count == 0 || (id is { } unico && !operables.Contains(unico)))
            return [];

        var consulta = db.TransitOffices.AsNoTracking().Where(o => o.IsActive && operables.Contains(o.Id));
        if (id is { } solo)
            consulta = consulta.Where(o => o.Id == solo);
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var patron = "%" + texto.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            consulta = consulta.Where(o => EF.Functions.ILike(o.Name, patron) || EF.Functions.ILike(o.Code, patron)
                || (o.CityName != null && EF.Functions.ILike(o.CityName, patron)) || (o.DepartmentName != null && EF.Functions.ILike(o.DepartmentName, patron)));
        }

        var filas = await consulta.OrderBy(o => o.Name).Take(Math.Clamp(limite, 1, LimiteDestinoMaximo))
            .Select(o => new { o.Id, o.Code, o.Name, o.CityName, o.DepartmentName }).ToListAsync(ct).ConfigureAwait(false);
        return filas.Select(o => new OrganismoHabilitadoPn(o.Id, o.Code, o.Name, o.CityName ?? string.Empty, o.DepartmentName ?? string.Empty)).ToList();
    }

    public async Task<bool> OrganismoHabilitadoAsync(Guid organismoId, CancellationToken ct) =>
        await db.TenantTransitOfficeGrants.AsNoTracking()
            .AnyAsync(g => g.TenantId == PlatformTenants.PersonaNatural && g.TransitOfficeId == organismoId && g.IsEnabled, ct)
            .ConfigureAwait(false)
        && await db.TransitOffices.AsNoTracking().AnyAsync(o => o.Id == organismoId && o.IsActive, ct).ConfigureAwait(false)
        && (await OrganismosOperablesAsync(ct).ConfigureAwait(false)).Contains(organismoId);

    /// <summary>
    /// Organismos «activos por FLIT»: el MISMO criterio de <c>OtOperabilityGate</c> — oficina activa del catálogo, con perfil
    /// OT (<c>admin.transit_office_profiles</c>) cuyo tenant OT está activo. El perfil y el tenant son de OTRO tenant, por eso
    /// la lectura desactiva RLS solo en su transacción (igual que el gate).
    /// </summary>
    public async Task<IReadOnlySet<Guid>> OrganismosOperablesAsync(CancellationToken ct)
    {
        async Task<HashSet<Guid>> Leer() => (await (
                from o in db.TransitOffices.AsNoTracking()
                join p in db.TransitOfficeProfiles.AsNoTracking() on o.Id equals p.TransitOfficeId
                join t in db.Tenants.AsNoTracking() on p.TenantId equals t.Id
                where o.IsActive && t.IsActive
                select o.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();

        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
            return await Leer().ConfigureAwait(false);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", ct).ConfigureAwait(false);
                var r = await Leer().ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return r;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Tarifas vigentes en <paramref name="fecha"/> (inicio ≤ fecha ≤ fin o sin fin). La tabla es pequeña: se lee entera.</summary>
    public async Task<IReadOnlyList<TarifaVigente>> TarifasVigentesAsync(DateOnly fecha, CancellationToken ct)
    {
        var filas = await db.Database.SqlQuery<TarifaFila>($"""
            SELECT id, concept, procedure_type_id,
                   transit_office_id, amount_cop
              FROM admin.persona_natural_tariffs
             WHERE superseded_at IS NULL AND valid_from <= {fecha} AND (valid_to IS NULL OR valid_to >= {fecha})
            """).ToListAsync(ct).ConfigureAwait(false);
        return filas.Select(f => new TarifaVigente(f.Concept, f.ProcedureTypeId, f.TransitOfficeId, f.AmountCop, f.Id)).ToList();
    }

    /// <summary>
    /// HU #14393/#14395 — banderas de medios y switches guardados para un ámbito (organismo, o la web con <c>null</c>). Sin
    /// caché: lo recién guardado se ve en la siguiente lectura. Sin filas, la configuración vacía (el portal cobra como antes).
    /// </summary>
    public async Task<ConfigCobroPn> ConfigCobroAsync(Guid? ambitoOrganismoId, CancellationToken ct)
    {
        var medios = await db.Database.SqlQuery<MedioCobroFila>($"""
            SELECT method, enabled FROM admin.persona_natural_payment_methods
             WHERE transit_office_id IS NOT DISTINCT FROM {ambitoOrganismoId}::uuid
            """).ToListAsync(ct).ConfigureAwait(false);
        var switches = await db.Database.SqlQuery<SwitchCobroFila>($"""
            SELECT procedure_type_id AS tipo_id, method, charge, enabled
              FROM admin.persona_natural_charge_switches
             WHERE transit_office_id IS NOT DISTINCT FROM {ambitoOrganismoId}::uuid
            """).ToListAsync(ct).ConfigureAwait(false);
        return new ConfigCobroPn(
            ambitoOrganismoId is null,
            medios.Select(m => new MedioPnFila(m.Method, m.Enabled)),
            switches.Select(s => new SwitchPnFila(s.TipoId, s.Method, s.Charge, s.Enabled)));
    }

    /// <summary>
    /// HU #14395 — decide qué conceptos de la cotización se cobran en un medio: un concepto entra si el switch de su grupo
    /// (trámite o gestión) está encendido para el tipo y el medio. Sin fila, el grupo vale encendido si su renglón base tiene
    /// monto mayor que cero.
    /// </summary>
    public static Func<string, bool> ConceptosCobrados(
        ConfigCobroPn config, Guid tipoId, Guid organismoId, string medio, IReadOnlyCollection<TarifaVigente> tarifas)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(tarifas);
        var encendidos = CobrosPn.Todos.ToDictionary(
            cobro => cobro,
            cobro => config.SwitchEncendido(
                tipoId, medio, cobro, CotizadorPersonaNatural.Resolver(CobrosPn.ConceptoBase(cobro), tipoId, organismoId, tarifas)?.ValorCop ?? 0));
        return concepto => encendidos[CobrosPn.DeConcepto(concepto)];
    }

    /// <summary>
    /// HU #14072 — ids de los tipos que el superadministrador habilitó para el portal (lista blanca explícita). La tabla nace
    /// vacía: mientras no se habilite ninguno, el portal no ofrece ningún tipo.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> TiposHabilitadosAsync(CancellationToken ct)
    {
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT procedure_type_id AS "Value" FROM admin.persona_natural_enabled_types WHERE is_enabled
            """).ToListAsync(ct).ConfigureAwait(false);
        return ids.ToHashSet();
    }

    /// <summary>Motivo de un tipo elegible que el superadministrador aún no habilitó.</summary>
    public const string MotivoNoHabilitado = "Aún no está habilitado para el portal.";

    /// <summary>
    /// «Habilitado = elegible + habilitado por el superadministrador + tarifa vigente» (ADR-0075 decisión 11): un tipo elegible
    /// que no está en la lista blanca pasa a no elegible, con su motivo. Es la ÚNICA aplicación de la lista blanca: la usan
    /// <c>ListarTiposDisponibles</c> y <c>Cotizar</c>.
    /// </summary>
    public static IReadOnlyList<TipoPersonaNatural> AplicarListaBlanca(IReadOnlyList<TipoPersonaNatural> tipos, IReadOnlySet<Guid> habilitados)
    {
        ArgumentNullException.ThrowIfNull(tipos);
        ArgumentNullException.ThrowIfNull(habilitados);
        return tipos
            .Select(t => t.Elegibilidad.Elegible && !habilitados.Contains(t.Id)
                ? t with { Elegibilidad = new ResultadoElegibilidad(false, [MotivoNoHabilitado]) }
                : t)
            .ToList();
    }

    /// <summary>Un tipo se ofrece en un organismo solo si los dos renglones base tienen tarifa vigente (propia o general).</summary>
    public static bool TieneTarifaBase(Guid tipoId, Guid organismoId, IReadOnlyCollection<TarifaVigente> tarifas)
    {
        ArgumentNullException.ThrowIfNull(tarifas);
        return CotizadorPersonaNatural.Resolver(ConceptosTarifa.DerechosTransito, tipoId, organismoId, tarifas) is not null
            && CotizadorPersonaNatural.Resolver(ConceptosTarifa.ServicioFlit, tipoId, organismoId, tarifas) is not null;
    }

    /// <summary>
    /// HU #14391 — de los organismos habilitados, deja los que ofrecen al menos un tipo (elegible, ya con la lista blanca
    /// aplicada) con precio base vigente. Es el mismo criterio con que se listan los tipos de un organismo, así que un
    /// organismo sin ningún tipo que ofrecer no aparece.
    /// </summary>
    public static IReadOnlyList<OrganismoHabilitadoPn> ConOferta(
        IReadOnlyList<OrganismoHabilitadoPn> organismos, IReadOnlyList<TipoPersonaNatural> tipos, IReadOnlyCollection<TarifaVigente> tarifas)
    {
        ArgumentNullException.ThrowIfNull(organismos);
        ArgumentNullException.ThrowIfNull(tipos);
        ArgumentNullException.ThrowIfNull(tarifas);
        var elegibles = tipos.Where(t => t.Elegibilidad.Elegible).ToList();
        return organismos.Where(o => elegibles.Any(t => TieneTarifaBase(t.Id, o.Id, tarifas))).ToList();
    }

    public sealed class MedioCobroFila
    {
        public string Method { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    public sealed class SwitchCobroFila
    {
        public Guid TipoId { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Charge { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    /// <summary>Forma de la fila leída por SQL (alias = nombre de propiedad).</summary>
    public sealed class TarifaFila
    {
        public Guid Id { get; set; }
        public string Concept { get; set; } = string.Empty;
        public Guid? ProcedureTypeId { get; set; }
        public Guid? TransitOfficeId { get; set; }
        public long AmountCop { get; set; }
    }
}
