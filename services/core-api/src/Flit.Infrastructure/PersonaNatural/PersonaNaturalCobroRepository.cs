using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Tramites.PersonaNatural;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.PersonaNatural;

/// <summary>
/// HU #14393 (Feature #14392, Épica #14297) — banderas de medios y switches de cobro del portal sobre
/// <c>admin.persona_natural_payment_methods</c> y <c>admin.persona_natural_charge_switches</c> (DDL 182). SQL parametrizado: las
/// tablas no son entidades EF. El ámbito nulo es la web. Sin caché: cada lectura va a la base.
/// </summary>
internal sealed class PersonaNaturalCobroRepository(FlitDbContext db, PersonaNaturalPortalReader portal) : IPersonaNaturalCobroRepository
{
    public Task<ConfigCobroPn> LeerAsync(Guid? ambitoOrganismoId, CancellationToken ct = default) =>
        portal.ConfigCobroAsync(ambitoOrganismoId, ct);

    public async Task<IReadOnlyDictionary<(Guid TipoId, string Cobro), long>> MontosAsync(Guid? ambitoOrganismoId, CancellationToken ct = default)
    {
        var tarifas = await portal.TarifasVigentesAsync(portal.HoyColombia(), ct).ConfigureAwait(false);
        // Web: solo valores generales. Un organismo inexistente (Guid.Empty) nunca coincide con una excepción propia.
        var organismo = ambitoOrganismoId ?? Guid.Empty;
        var montos = new Dictionary<(Guid TipoId, string Cobro), long>();
        foreach (var tipo in tarifas.Where(t => t.TipoId is not null).Select(t => t.TipoId!.Value).Distinct())
        {
            foreach (var cobro in CobrosPn.Todos)
            {
                if (CotizadorPersonaNatural.Resolver(CobrosPn.ConceptoBase(cobro), tipo, organismo, tarifas) is { } t)
                    montos[(tipo, cobro)] = t.ValorCop;
            }
        }

        return montos;
    }

    public Task<bool?> GuardarMedioAsync(Guid? ambitoOrganismoId, string medio, bool enabled, Guid actor, CancellationToken ct = default) =>
        UpsertAsync(async () =>
        {
            var anterior = await db.Database.SqlQuery<bool>($"""
                SELECT enabled AS "Value" FROM admin.persona_natural_payment_methods
                 WHERE method = {medio} AND transit_office_id IS NOT DISTINCT FROM {ambitoOrganismoId}::uuid
                """).ToListAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO admin.persona_natural_payment_methods (transit_office_id, method, enabled, created_by)
                VALUES ({ambitoOrganismoId}::uuid, {medio}, {enabled}, {actor})
                ON CONFLICT (method, (COALESCE(transit_office_id, '00000000-0000-0000-0000-000000000000'::uuid)))
                DO UPDATE SET enabled = EXCLUDED.enabled, updated_by = {actor}, updated_at = now()
                """, ct).ConfigureAwait(false);
            return anterior.Count == 0 ? (bool?)null : anterior[0];
        }, ct);

    public Task<bool?> GuardarSwitchAsync(
        Guid? ambitoOrganismoId, Guid tipoId, string medio, string cobro, bool enabled, Guid actor, CancellationToken ct = default) =>
        UpsertAsync(async () =>
        {
            var anterior = await db.Database.SqlQuery<bool>($"""
                SELECT enabled AS "Value" FROM admin.persona_natural_charge_switches
                 WHERE procedure_type_id = {tipoId} AND method = {medio} AND charge = {cobro}
                   AND transit_office_id IS NOT DISTINCT FROM {ambitoOrganismoId}::uuid
                """).ToListAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO admin.persona_natural_charge_switches (transit_office_id, procedure_type_id, method, charge, enabled, created_by)
                VALUES ({ambitoOrganismoId}::uuid, {tipoId}, {medio}, {cobro}, {enabled}, {actor})
                ON CONFLICT (COALESCE(transit_office_id, '00000000-0000-0000-0000-000000000000'::uuid), procedure_type_id, method, charge)
                DO UPDATE SET enabled = EXCLUDED.enabled, updated_by = {actor}, updated_at = now()
                """, ct).ConfigureAwait(false);
            return anterior.Count == 0 ? (bool?)null : anterior[0];
        }, ct);

    /// <summary>Lee el valor anterior y escribe en una misma transacción, dentro de la estrategia de reintentos del contexto.</summary>
    private Task<bool?> UpsertAsync(Func<Task<bool?>> trabajo, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var anterior = await trabajo().ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return anterior;
            }
        });
}
