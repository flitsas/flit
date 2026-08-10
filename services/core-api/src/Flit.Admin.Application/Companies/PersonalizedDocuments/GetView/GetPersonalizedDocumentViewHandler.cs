namespace Flit.Admin.Application.Companies.PersonalizedDocuments.GetView;

/// <summary>
/// Vista previa sin activar (HU #11314, AC3, ADR-0029-preview-presigned-get-inline). Resuelve el
/// ownership ANTES de firmar (<see cref="ICompanyPersonalizedDocumentRepository.GetByIdAsync"/> con
/// <c>WHERE tenant_id</c> explícito) y solo entonces pide la presigned GET inline — nunca cambia el
/// estado de la versión ni la activa. Un id de otro tenant es <see cref="GetPersonalizedDocumentViewOutcome.NotFound"/>
/// (negativo de aislamiento, AC6): la URL firmada de la compañía B nunca se emite a un usuario de A.
/// </summary>
public sealed class GetPersonalizedDocumentViewHandler
{
    private readonly ICompanyPersonalizedDocumentRepository _repository;
    private readonly ICompanyPersonalizedDocumentStorage _storage;

    public GetPersonalizedDocumentViewHandler(
        ICompanyPersonalizedDocumentRepository repository,
        ICompanyPersonalizedDocumentStorage storage)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<GetPersonalizedDocumentViewResult> HandleAsync(
        GetPersonalizedDocumentViewCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var record = await _repository
            .GetByIdAsync(command.TenantId, command.Id, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return GetPersonalizedDocumentViewResult.NotFound();
        }

        var view = await _storage
            .GetViewUrlAsync(record.StoragePath, cancellationToken)
            .ConfigureAwait(false);
        if (view is null)
        {
            return GetPersonalizedDocumentViewResult.NotFound();
        }

        return GetPersonalizedDocumentViewResult.Found(view.Url, view.ExpiresAt);
    }
}
