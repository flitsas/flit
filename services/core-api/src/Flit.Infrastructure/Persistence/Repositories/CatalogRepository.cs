using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

internal sealed class CatalogRepository(FlitDbContext db) : ICatalogRepository
{
    public Task<List<ProcedureEntity>> ListProcedureEntitiesAsync(CancellationToken ct) =>
        db.ProcedureEntities.OrderBy(x => x.SortOrder).ToListAsync(ct);

    public Task<List<ExternalDataSource>> ListExternalDataSourcesAsync(CancellationToken ct) =>
        db.ExternalDataSources.OrderBy(x => x.Code).ToListAsync(ct);

    public Task<List<ConsultationTemplate>> ListConsultationTemplatesAsync(CancellationToken ct) =>
        db.ConsultationTemplates
            .Include(t => t.ExternalDataSource)
            .OrderBy(t => t.Code)
            .ToListAsync(ct);

    public Task<ConsultationTemplate?> GetConsultationTemplateByIdAsync(Guid id, CancellationToken ct) =>
        db.ConsultationTemplates
            .Include(t => t.ExternalDataSource)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<ConsultationTemplate?> GetConsultationTemplateByCodeAsync(string code, CancellationToken ct) =>
        db.ConsultationTemplates
            .Include(t => t.ExternalDataSource)
            .FirstOrDefaultAsync(t => t.Code == code, ct);

    public Task<ProcedureEntity?> GetProcedureEntityByCodeAsync(string code, CancellationToken ct) =>
        db.ProcedureEntities.FirstOrDefaultAsync(e => e.Code == code, ct);
}
