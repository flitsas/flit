using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Infrastructure.OtClientProcedures;

/// <summary>
/// HU #14547 — matriz de precedencia del OT para el consolidado maestro, con la misma regla que
/// <see cref="OtClientProcedureConsolidadoContext"/> (orden resuelto de <see cref="IResolvedDocumentMatrixResolver"/>).
/// Vive aquí porque <c>Flit.Tramites.Application</c> no referencia Admin. Un fallo del resolver devuelve la lista
/// vacía: el maestro cae al orden por modalidad.
/// <para>Uso de ejemplo: <c>await provider.ResolverAsync(instance.ProcedureTypeId, instance.TransitOfficeId, ct)</c>.</para>
/// </summary>
public sealed class ConsolidadoMaestroPrecedenciaProvider(IResolvedDocumentMatrixResolver matrixResolver)
    : IConsolidadoMaestroPrecedenciaProvider
{
    private readonly IResolvedDocumentMatrixResolver _matrixResolver =
        matrixResolver ?? throw new ArgumentNullException(nameof(matrixResolver));

    public async Task<IReadOnlyList<string>> ResolverAsync(
        Guid procedureTypeId, Guid? transitOfficeId, CancellationToken ct = default)
    {
        try
        {
            var matriz = await _matrixResolver.ResolveAsync(procedureTypeId, transitOfficeId, ct).ConfigureAwait(false);
            return matriz.Select(m => m.Codigo).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
