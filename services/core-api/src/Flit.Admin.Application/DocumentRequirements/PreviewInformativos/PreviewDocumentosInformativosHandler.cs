using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.DocumentRequirements;

namespace Flit.Admin.Application.DocumentRequirements.PreviewInformativos;

/// <summary>
/// Resuelve la lista INFORMATIVA de documentos para el paso 1 del wizard (matrícula/traspaso).
/// Mapea modalidad del wizard → código canónico de <c>procedure_types</c>
/// (<c>MATRICULA_NUEVA</c> / <c>TRASPASO_STANDARD</c>, mismo cableado que
/// <c>CreateProcedureInstanceHandler</c>) → matriz resuelta (OT &gt; Default).
/// No crea snapshot ni instancia.
/// </summary>
public sealed class PreviewDocumentosInformativosHandler
{
    // Códigos canónicos en tramites.procedure_types (NO confundir con tipologia_codigo runtime).
    private const string CodigoProcedureMatricula = "MATRICULA_NUEVA";
    private const string CodigoProcedureTraspaso = "TRASPASO_STANDARD";

    private static readonly Dictionary<string, string> ModalidadToCanonicalCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["matricula_inicial"] = CodigoProcedureMatricula,
            ["matricula_nueva"] = CodigoProcedureMatricula,
            ["traspaso"] = CodigoProcedureTraspaso,
            ["traspaso_standard"] = CodigoProcedureTraspaso,
        };

    private readonly IProcedureTypeCatalog _procedureTypes;
    private readonly IResolvedDocumentMatrixResolver _resolver;

    public PreviewDocumentosInformativosHandler(
        IProcedureTypeCatalog procedureTypes,
        IResolvedDocumentMatrixResolver resolver)
    {
        _procedureTypes = procedureTypes ?? throw new ArgumentNullException(nameof(procedureTypes));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<PreviewDocumentosInformativosResult> HandleAsync(
        PreviewDocumentosInformativosQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Modalidad)
            || !ModalidadToCanonicalCode.TryGetValue(query.Modalidad.Trim(), out var procedureCode))
        {
            return PreviewDocumentosInformativosResult.ModalidadInvalida();
        }

        var catalog = await _procedureTypes
            .ListActivePublishedAsync(cancellationToken)
            .ConfigureAwait(false);

        var procedureType = catalog.FirstOrDefault(p =>
            string.Equals(p.Code, procedureCode, StringComparison.OrdinalIgnoreCase));

        if (procedureType is null)
        {
            return PreviewDocumentosInformativosResult.ProcedureTypeNotFound();
        }

        var matrix = await _resolver
            .ResolveAsync(procedureType.Id, query.TransitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        var items = matrix
            .OrderBy(m => m.OrdenResuelto)
            .Select(m => new DocumentoInformativoItem(
                m.DocumentTypeId,
                m.Codigo,
                m.Nombre,
                m.Obligatorio,
                m.OrdenResuelto,
                Descripcion: null))
            .ToList();

        return PreviewDocumentosInformativosResult.Resolved(procedureCode, procedureType.Id, items);
    }
}
