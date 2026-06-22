using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Catalogs;

public sealed record ConsultationTemplateDto(
    Guid Id,
    string Code,
    string Name,
    string EntityScope,
    string? PersonType,
    string RequiredFieldKeys,
    bool IsActive);

public sealed class ListConsultationTemplatesHandler(ICatalogRepository repository)
{
    public async Task<List<ConsultationTemplateDto>> HandleAsync(CancellationToken ct = default)
    {
        var items = await repository.ListConsultationTemplatesAsync(ct);
        return items.Select(t => new ConsultationTemplateDto(
            t.Id, t.Code, t.Name, t.EntityScope, t.PersonType, t.RequiredFieldKeys, t.IsActive)).ToList();
    }
}
