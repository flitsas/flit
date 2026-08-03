namespace Flit.Admin.Application.DocumentRequirements.PreviewInformativos;

/// <summary>Ítem informativo de documento requerido (solo lectura / preview).</summary>
public sealed record DocumentoInformativoItem(
    Guid DocumentTypeId,
    string Codigo,
    string Nombre,
    bool Obligatorio,
    short Orden,
    string? Descripcion = null);
