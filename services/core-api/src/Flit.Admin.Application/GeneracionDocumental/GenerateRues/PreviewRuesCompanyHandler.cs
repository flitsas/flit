using Flit.Admin.Application.GeneracionDocumental.Ports;

namespace Flit.Admin.Application.GeneracionDocumental.GenerateRues;

/// <summary>Un campo mercantil devuelto para revisión previa (clave del RUES + valor).</summary>
public sealed record StandaloneRuesPreviewField(string Key, string? Value);

/// <summary>
/// Resultado de la vista previa. <c>Error</c> es <c>invalid_request</c>, <c>provider_unavailable</c>
/// o <c>provider_not_found</c>; nunca la excepción cruda del proveedor.
/// </summary>
public sealed record PreviewRuesCompanyResult(
    bool Found,
    string Nit,
    IReadOnlyList<StandaloneRuesPreviewField> Campos,
    string? Error);

/// <summary>
/// Vista previa del Certificado RUES (CF-04): devuelve los campos mercantiles para que el usuario
/// los revise antes de emitir.
///
/// <para><b>No persiste NADA</b> — y eso es estructural, no una promesa: este handler ni siquiera
/// recibe <c>IStandaloneDocumentRepository</c> ni <c>IStandaloneDocumentStorage</c>, así que no
/// tiene con qué escribir una fila en <c>admin.standalone_documents</c> ni un archivo en storage.
/// Mismo criterio que <c>RuesPreviewHandler</c> en el wizard de trámites.</para>
/// </summary>
public sealed class PreviewRuesCompanyHandler
{
    private readonly IStandaloneRuesCompanyLookup _lookup;

    public PreviewRuesCompanyHandler(IStandaloneRuesCompanyLookup lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public async Task<PreviewRuesCompanyResult> HandleAsync(
        Guid tenantId,
        string? nit,
        CancellationToken cancellationToken = default)
    {
        var normalized = nit?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return new PreviewRuesCompanyResult(false, string.Empty, [], "invalid_request");
        }

        var result = await _lookup
            .ConsultAsync(tenantId, normalized, cancellationToken)
            .ConfigureAwait(false);

        if (result.Error is not null)
        {
            return new PreviewRuesCompanyResult(false, normalized, [], result.Error);
        }

        var campos = result.Found
            ? result.Fields.Select(f => new StandaloneRuesPreviewField(f.Key, f.Value)).ToList()
            : [];

        return new PreviewRuesCompanyResult(result.Found, normalized, campos, null);
    }
}
