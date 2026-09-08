using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class ImprintSignatureValidationRepository(FlitDbContext db) : IImprintSignatureValidationRepository
{
    public void Add(ImprintSignatureValidation row) => db.ImprintSignatureValidations.Add(row);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
