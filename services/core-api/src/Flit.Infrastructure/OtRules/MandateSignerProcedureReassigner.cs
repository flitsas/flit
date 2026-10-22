using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// HU #13137 (Feature #13115, ADR-0066 P8) — al dar de baja a un mandatario (inactivar o eliminar), los trámites
/// RADICADOS SIN APROBAR (<see cref="TramiteEstado.PendientesDelOrganismo"/>: preasignación, asignado y entregado)
/// que apuntaban a él se reasignan con la prelación del OT, usando el MISMO evaluador que la pantalla, el PDF y el
/// gate de radicación (<see cref="MandateSignerEvaluator"/>: OT para la compañía, propio de la compañía, asociado,
/// default del OT). Si nadie resuelve —o los candidatos válidos son varios sin desempate— el firmante queda en
/// nulo y el OT decide al aprobar.
///
/// <para>No toca borradores (el FUR se recalcula solo) ni los trámites aprobados, revocados o anulados: conservan
/// quién firmó. Corre DENTRO de la transacción de la baja (comparte el <see cref="FlitDbContext"/>): si algo
/// falla, el llamante revierte todo y el mandatario sigue activo.</para>
///
/// <para>Sin datos personales: el resultado lleva solo identificadores.</para>
/// </summary>
internal sealed class MandateSignerProcedureReassigner : IMandateSignerProcedureReassigner
{
    private readonly FlitDbContext _context;
    private readonly IProcedureInstanceRepository _instances;
    private readonly MandateSignerEvaluator _evaluator;

    public MandateSignerProcedureReassigner(
        FlitDbContext context,
        IProcedureInstanceRepository instances,
        MandateSignerEvaluator evaluator)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    public async Task<MandateSignerReassignmentResult> ReassignAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default)
    {
        // Los trámites son de las compañías gestoras (otro tenant que el del OT): lectura y escritura
        // cross-tenant dentro de la transacción de la baja.
        if (_context.Database.IsRelational())
        {
            await _context.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                .ConfigureAwait(false);
        }

        var pendientes = TramiteEstado.PendientesDelOrganismo.ToArray();
        var afectados = await _context.ProcedureInstances
            .AsNoTracking()
            .Where(p => p.MandateSignerId == mandateSignerId
                && p.DeletedAt == null
                && pendientes.Contains(p.Status))
            .Select(p => new { p.Id, p.TenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (afectados.Count == 0)
        {
            return MandateSignerReassignmentResult.None;
        }

        var moves = new List<MandateSignerProcedureMove>(afectados.Count);
        var now = DateTimeOffset.UtcNow;

        foreach (var afectado in afectados)
        {
            var instance = await _instances
                .GetByIdWithDetailsAsync(afectado.Id, afectado.TenantId, cancellationToken)
                .ConfigureAwait(false);
            if (instance is null || instance.MandateSignerId != mandateSignerId)
            {
                continue;
            }

            // Sin elección del OT: el guardado (el mandatario dado de baja) ya no es válido y se ignora, así que
            // manda la prelación. Solo un firmante único y válido se asigna; el resto queda para el OT.
            var evaluacion = await _evaluator.EvaluateAsync(instance, eleccionOt: null, cancellationToken)
                .ConfigureAwait(false);
            Guid? nuevo = evaluacion.Estado == MandateSignerEstado.Valido ? evaluacion.Signer?.Id : null;

            instance.MandateSignerId = nuevo;
            instance.UpdatedAt = now;
            moves.Add(new MandateSignerProcedureMove(instance.Id, mandateSignerId, nuevo));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new MandateSignerReassignmentResult(moves);
    }
}
