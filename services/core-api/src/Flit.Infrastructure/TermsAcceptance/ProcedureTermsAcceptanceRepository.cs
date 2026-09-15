using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.TermsAcceptance;
using Flit.Tramites.Domain.TermsAcceptance;

namespace Flit.Infrastructure.TermsAcceptance;

/// <summary>
/// Inserta la aceptación en <c>tramites.procedure_terms_acceptances</c> con el DbContext de la
/// petición. Sin try/catch a propósito: un fallo aquí tiene que llegar al endpoint como 500 para
/// que el frontend no habilite el formulario (RN-03).
/// </summary>
internal sealed class ProcedureTermsAcceptanceRepository(FlitDbContext context) : IProcedureTermsAcceptanceRepository
{
    public async Task AddAsync(ProcedureTermsAcceptance acceptance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        context.ProcedureTermsAcceptances.Add(acceptance);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
