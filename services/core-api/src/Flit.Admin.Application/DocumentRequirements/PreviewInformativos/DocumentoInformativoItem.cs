namespace Flit.Admin.Application.DocumentRequirements.PreviewInformativos;

/// <summary>Ítem informativo de documento requerido (solo lectura / preview).</summary>
public sealed record DocumentoInformativoItem(
    Guid DocumentTypeId,
    string Codigo,
    string Nombre,
    bool Obligatorio,
    short Orden,
    string? Descripcion = null,
    // HU #12066 — instrucción de cargue del catálogo, la misma que ve el gestor en Requisitos.
    string? InstruccionCargue = null);
