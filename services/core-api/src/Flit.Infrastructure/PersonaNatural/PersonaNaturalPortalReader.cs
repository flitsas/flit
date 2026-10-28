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
public sealed class PersonaNaturalPortalReader(FlitDbContext db, TimeProvider? reloj = null)
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
        return filas.Select(o => new OrganismoHabilitadoPn(o.Id, o.Code, o.Name, o.CityName ?? string.Empty, o.DepartmentName ?? string.Empty)).ToList();
    }

    public async Task<bool> OrganismoHabilitadoAsync(Guid organismoId, CancellationToken ct) =>
        await db.TenantTransitOfficeGrants.AsNoTracking()
            .AnyAsync(g => g.TenantId == PlatformTenants.PersonaNatural && g.TransitOfficeId == organismoId && g.IsEnabled, ct)
            .ConfigureAwait(false)
        && await db.TransitOffices.AsNoTracking().AnyAsync(o => o.Id == organismoId && o.IsActive, ct).ConfigureAwait(false);

    /// <summary>Tarifas vigentes en <paramref name="fecha"/> (inicio ≤ fecha ≤ fin o sin fin). La tabla es pequeña: se lee entera.</summary>
    public async Task<IReadOnlyList<TarifaVigente>> TarifasVigentesAsync(DateOnly fecha, CancellationToken ct)
    {
        var filas = await db.Database.SqlQuery<TarifaFila>($"""
            SELECT id, concept, procedure_type_id,
                   transit_office_id, amount_cop
              FROM admin.persona_natural_tariffs
             WHERE valid_from <= {fecha} AND (valid_to IS NULL OR valid_to >= {fecha})
            """).ToListAsync(ct).ConfigureAwait(false);
        return filas.Select(f => new TarifaVigente(f.Concept, f.ProcedureTypeId, f.TransitOfficeId, f.AmountCop, f.Id)).ToList();
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
