using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Repositories;

public interface ICatalogRepository
{
    Task<List<ProcedureEntity>> ListProcedureEntitiesAsync(CancellationToken ct = default);
    Task<List<ExternalDataSource>> ListExternalDataSourcesAsync(CancellationToken ct = default);
    Task<List<ConsultationTemplate>> ListConsultationTemplatesAsync(CancellationToken ct = default);
    Task<ConsultationTemplate?> GetConsultationTemplateByIdAsync(Guid id, CancellationToken ct = default);
    Task<ConsultationTemplate?> GetConsultationTemplateByCodeAsync(string code, CancellationToken ct = default);
    Task<ProcedureEntity?> GetProcedureEntityByCodeAsync(string code, CancellationToken ct = default);
}
