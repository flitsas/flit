using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>
/// HU #14071 (Feature J, Épica #14021) — administración de las tarifas del portal sobre <c>admin.persona_natural_tariffs</c>
/// (DDL 153). SQL parametrizado: la tabla no es entidad EF (el modelo del DbContext no cambia). Todo el versionado pasa por
/// <c>admin.persona_natural_tariff_new_version</c> y el trigger de la tabla: aquí no se actualiza ningún valor.
/// </summary>
internal sealed class PersonaNaturalTarifaRepository(FlitDbContext db, PersonaNaturalPortalReader portal) : IPersonaNaturalTarifaRepository
{
    public DateOnly HoyColombia() => portal.HoyColombia();

    public async Task<IReadOnlyList<TarifaPnView>> ListarAsync(TarifaPnFiltro filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var fecha = filtro.Fecha ?? HoyColombia();
        var filas = await db.Database.SqlQuery<TarifaFila>($"""
            SELECT t.id, t.concept, t.procedure_type_id, p.code AS procedure_type_code, p.name AS procedure_type_name,
                   t.transit_office_id, o.code AS transit_office_code, o.name AS transit_office_name,
                   t.amount_cop, t.valid_from, t.valid_to, t.created_at, t.created_by, t.updated_at, t.updated_by
              FROM admin.persona_natural_tariffs t
              LEFT JOIN tramites.procedure_types p ON p.id = t.procedure_type_id
              LEFT JOIN catalogs.transit_offices o ON o.id = t.transit_office_id
             WHERE ({filtro.TipoTramiteId}::uuid IS NULL OR t.procedure_type_id = {filtro.TipoTramiteId}::uuid)
               AND ({filtro.OrganismoId}::uuid IS NULL OR t.transit_office_id = {filtro.OrganismoId}::uuid)
               AND ({filtro.Concepto}::text IS NULL OR t.concept = {filtro.Concepto}::text)
               AND (NOT {filtro.SoloVigentes} OR (t.valid_from <= {fecha} AND (t.valid_to IS NULL OR t.valid_to >= {fecha})))
            """).ToListAsync(ct).ConfigureAwait(false);
        return filas
            .OrderBy(f => f.TransitOfficeName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.ProcedureTypeName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.Concept, StringComparer.Ordinal)
            .ThenByDescending(f => f.ValidFrom)
            .Select(f => ToView(f, fecha))
            .ToList();
    }

    public async Task<TarifaPnView?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var fila = await UnaAsync(id, ct).ConfigureAwait(false);
        return fila is null ? null : ToView(fila, HoyColombia());
    }

    public async Task<IReadOnlyList<TarifaPnView>?> HistorialAsync(Guid id, CancellationToken ct = default)
    {
        var fila = await UnaAsync(id, ct).ConfigureAwait(false);
        if (fila is null)
            return null;
        var hoy = HoyColombia();
        var versiones = await db.Database.SqlQuery<TarifaFila>($"""
            SELECT t.id, t.concept, t.procedure_type_id, p.code AS procedure_type_code, p.name AS procedure_type_name,
                   t.transit_office_id, o.code AS transit_office_code, o.name AS transit_office_name,
                   t.amount_cop, t.valid_from, t.valid_to, t.created_at, t.created_by, t.updated_at, t.updated_by
              FROM admin.persona_natural_tariffs t
              LEFT JOIN tramites.procedure_types p ON p.id = t.procedure_type_id
              LEFT JOIN catalogs.transit_offices o ON o.id = t.transit_office_id
             WHERE t.concept = {fila.Concept}
               AND t.procedure_type_id IS NOT DISTINCT FROM {fila.ProcedureTypeId}::uuid
               AND t.transit_office_id IS NOT DISTINCT FROM {fila.TransitOfficeId}::uuid
            """).ToListAsync(ct).ConfigureAwait(false);
        return versiones.OrderByDescending(v => v.ValidFrom).Select(v => ToView(v, hoy)).ToList();
    }

    public Task<bool> TipoExisteAsync(Guid tipoId, CancellationToken ct = default) =>
        db.ProcedureTypes.AsNoTracking().AnyAsync(t => t.Id == tipoId, ct);

    public Task<bool> OrganismoExisteAsync(Guid organismoId, CancellationToken ct = default) =>
        db.TransitOffices.AsNoTracking().AnyAsync(o => o.Id == organismoId, ct);

    public async Task<TarifaPnVersionadaResult> VersionarAsync(
        Guid? tipoId, Guid? organismoId, IReadOnlyList<TarifaPnValor> valores, DateOnly vigenteDesde, Guid actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(valores);
        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var anteriores = new List<TarifaPnView>();
            var nuevas = new List<TarifaPnView>();
            var hoy = HoyColombia();
            try
            {
                foreach (var v in valores)
                {
                    // Lo que estaba abierto antes (valor anterior de la auditoría). La función lo cierra el día anterior al inicio.
                    var abierta = await db.Database.SqlQuery<Guid>($"""
                        SELECT id AS "Value" FROM admin.persona_natural_tariffs
                         WHERE concept = {v.Concepto}
                           AND procedure_type_id IS NOT DISTINCT FROM {tipoId}::uuid
                           AND transit_office_id IS NOT DISTINCT FROM {organismoId}::uuid
                           AND valid_to IS NULL AND valid_from < {vigenteDesde}
                        """).ToListAsync(ct).ConfigureAwait(false);
                    foreach (var idAbierta in abierta)
                    {
                        var fila = await UnaAsync(idAbierta, ct).ConfigureAwait(false);
                        if (fila is not null)
                            anteriores.Add(ToView(fila, hoy));
                    }

                    var nuevoId = (await db.Database.SqlQuery<Guid>($"""
                        SELECT admin.persona_natural_tariff_new_version({v.Concepto}, {tipoId}::uuid, {organismoId}::uuid, {v.ValorCop}, {vigenteDesde}, {actor}) AS "Value"
                        """).ToListAsync(ct).ConfigureAwait(false)).Single();
                    var creada = await UnaAsync(nuevoId, ct).ConfigureAwait(false);
                    nuevas.Add(ToView(creada!, hoy));
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.RestrictViolation)
            {
                // El trigger (cruce de vigencias) o el índice único (misma fecha de inicio): se deshace todo el lote.
                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw new TarifaPnSolapadaException(ex.MessageText);
            }

            return new TarifaPnVersionadaResult(nuevas, anteriores);
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TarifaPnView>> VigentesDelTipoEnOrganismoAsync(Guid tipoId, Guid organismoId, DateOnly fecha, CancellationToken ct = default)
    {
        var vigentes = await portal.TarifasVigentesAsync(fecha, ct).ConfigureAwait(false);
        var resultado = new List<TarifaPnView>();
        foreach (var concepto in ConceptosTarifaPn.Todos)
        {
            // Misma precedencia que la cotización: un único cálculo (CotizadorPersonaNatural.Resolver).
            var elegida = CotizadorPersonaNatural.Resolver(concepto, tipoId, organismoId, vigentes);
            if (elegida is null)
                continue;
            var fila = await UnaAsync(elegida.VersionId, ct).ConfigureAwait(false);
            if (fila is not null)
                resultado.Add(ToView(fila, fecha));
        }

        return resultado;
    }

    private async Task<TarifaFila?> UnaAsync(Guid id, CancellationToken ct) =>
        (await db.Database.SqlQuery<TarifaFila>($"""
            SELECT t.id, t.concept, t.procedure_type_id, p.code AS procedure_type_code, p.name AS procedure_type_name,
                   t.transit_office_id, o.code AS transit_office_code, o.name AS transit_office_name,
                   t.amount_cop, t.valid_from, t.valid_to, t.created_at, t.created_by, t.updated_at, t.updated_by
              FROM admin.persona_natural_tariffs t
              LEFT JOIN tramites.procedure_types p ON p.id = t.procedure_type_id
              LEFT JOIN catalogs.transit_offices o ON o.id = t.transit_office_id
             WHERE t.id = {id}
            """).ToListAsync(ct).ConfigureAwait(false)).FirstOrDefault();

    private static TarifaPnView ToView(TarifaFila f, DateOnly fecha)
    {
        var estado = f.ValidFrom > fecha
            ? EstadosTarifaPn.Futura
            : f.ValidTo is { } fin && fin < fecha ? EstadosTarifaPn.Cerrada : EstadosTarifaPn.Vigente;
        return new TarifaPnView(
            f.Id, f.Concept, f.ProcedureTypeId, f.ProcedureTypeCode, f.ProcedureTypeName, f.TransitOfficeId, f.TransitOfficeCode,
            f.TransitOfficeName, f.AmountCop, f.ValidFrom, f.ValidTo, estado, f.CreatedAt, f.CreatedBy, f.UpdatedAt, f.UpdatedBy);
    }

    /// <summary>Forma de la fila leída por SQL (snake_case del DbContext = nombre de propiedad).</summary>
    private sealed class TarifaFila
    {
        public Guid Id { get; set; }
        public string Concept { get; set; } = string.Empty;
        public Guid? ProcedureTypeId { get; set; }
        public string? ProcedureTypeCode { get; set; }
        public string? ProcedureTypeName { get; set; }
        public Guid? TransitOfficeId { get; set; }
        public string? TransitOfficeCode { get; set; }
        public string? TransitOfficeName { get; set; }
        public long AmountCop { get; set; }
        public DateOnly ValidFrom { get; set; }
        public DateOnly? ValidTo { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
    }
}
