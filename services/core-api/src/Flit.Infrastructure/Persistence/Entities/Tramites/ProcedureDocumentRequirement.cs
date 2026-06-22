namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Asociación trámite ↔ tipo de documento — <c>tramites.procedure_document_requirements</c>
/// (DDL HU #10155). En la HU #10193 se usa <b>solo en lectura</b> para el guard de
/// integridad del soft-delete (AC6): si existe alguna fila que referencie un tipo de
/// documento, no puede desactivarse.
/// </summary>
public sealed class ProcedureDocumentRequirement
{
    public Guid Id { get; set; }

    public Guid ProcedureTypeId { get; set; }

    public Guid DocumentTypeId { get; set; }

    public bool IsMandatory { get; set; }

    public short DefaultSortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
