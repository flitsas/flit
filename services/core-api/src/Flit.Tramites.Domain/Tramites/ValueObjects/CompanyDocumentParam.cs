namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Estado documental parametrizado por la compañía gestora (HU #10521, RF31).</summary>
public enum CompanyDocumentParamState
{
    /// <summary>El documento no aplica para esta gestora (se oculta del checklist).</summary>
    Oculto,

    /// <summary>El documento es obligatorio para esta gestora.</summary>
    Obligatorio,

    /// <summary>El documento es opcional para esta gestora.</summary>
    Opcional,
}

/// <summary>
/// Parámetro documental de una compañía gestora: fija el estado (oculto/obligatorio/opcional)
/// de un tipo de documento. Equivalente a los switches de v1.0
/// (<c>registrationRequiresAttachedCepdField</c>, <c>transferRequiresAttachedSellerIdField</c>,
/// <c>soatAttachmentEnabled</c>, <c>registrationRequiresAttachedOwnerIdField</c>).
/// </summary>
/// <param name="DocTipo">Código del tipo de documento (coincide con <c>ChecklistItem.DocTipo</c>/<c>Id</c>).</param>
/// <param name="State">Estado a forzar para esta gestora.</param>
public sealed record CompanyDocumentParam(string DocTipo, CompanyDocumentParamState State);
