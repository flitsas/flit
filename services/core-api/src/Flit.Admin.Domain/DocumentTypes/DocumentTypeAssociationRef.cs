namespace Flit.Admin.Domain.DocumentTypes;

/// <summary>
/// Referencia mínima a un tipo de trámite que usa un documento. Enriquece el 409 del
/// soft-delete (AC6) para que el mensaje indique en qué trámites está en uso el documento.
/// </summary>
/// <param name="Codigo">Código del tipo de trámite (<c>tramites.procedure_types.code</c>).</param>
/// <param name="Nombre">Nombre del tipo de trámite (<c>tramites.procedure_types.name</c>).</param>
public sealed record DocumentTypeAssociationRef(string Codigo, string Nombre);
