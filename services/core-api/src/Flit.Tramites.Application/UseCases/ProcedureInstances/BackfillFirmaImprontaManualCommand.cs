using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Resultado del backfill administrativo de firma automática de impronta manual (HU #12116).</summary>
public sealed record BackfillFirmaImprontaManualResult(
    int Total,
    int Firmadas,
    int YaFirmadas,
    int SinImprontaManual,
    IReadOnlyDictionary<string, int> NoListas,
    int Fallidas,
    int Limit);

/// <summary>
/// HU #12116 — backfill administrativo (SuperAdmin) de trámites que quedaron sin la firma
/// automática de su impronta manual (p. ej. radicados antes de esta HU, o cuya asignación de placa
/// no disparó la firma). Idempotente y re-ejecutable: cada trámite pasa por el mismo
/// <see cref="FirmarImprontaManualSiListaHandler"/> que usan los disparadores en línea, así que
/// procesarlo dos veces no duplica nada (idempotencia por trámite + hash del PDF base).
/// </summary>
public sealed class BackfillFirmaImprontaManualHandler(
    IProcedureInstanceRepository repo,
    FirmarImprontaManualSiListaHandler firmaImpronta)
{
    public async Task<BackfillFirmaImprontaManualResult> HandleAsync(int limit, CancellationToken ct = default)
    {
        var candidatos = await repo
            .ListImprontaManualBackfillCandidatesAsync(limit, ct)
            .ConfigureAwait(false);

        var firmadas = 0;
        var yaFirmadas = 0;
        var sinImprontaManual = 0;
        var fallidas = 0;
        var noListas = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (instanceId, tenantId) in candidatos)
        {
            var resultado = await firmaImpronta
                .HandleAsync(instanceId, tenantId, FirmaImprontaAutomaticaOrigen.Backfill, ct)
                .ConfigureAwait(false);

            switch (resultado.Estado)
            {
                case FirmaImprontaAutomaticaEstado.Firmada:
                    firmadas++;
                    break;
                case FirmaImprontaAutomaticaEstado.YaFirmada:
                    yaFirmadas++;
                    break;
                case FirmaImprontaAutomaticaEstado.SinImprontaManual:
                    sinImprontaManual++;
                    break;
                case FirmaImprontaAutomaticaEstado.NoLista:
                    var motivo = resultado.Motivo ?? "desconocido";
                    noListas[motivo] = noListas.GetValueOrDefault(motivo) + 1;
                    break;
                case FirmaImprontaAutomaticaEstado.Fallo:
                default:
                    fallidas++;
                    break;
            }
        }

        return new BackfillFirmaImprontaManualResult(
            Total: candidatos.Count,
            Firmadas: firmadas,
            YaFirmadas: yaFirmadas,
            SinImprontaManual: sinImprontaManual,
            NoListas: noListas,
            Fallidas: fallidas,
            Limit: limit);
    }
}
