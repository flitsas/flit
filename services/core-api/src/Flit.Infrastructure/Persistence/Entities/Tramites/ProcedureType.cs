namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Tipo de trámite del catálogo — <c>tramites.procedure_types</c> (DDL HU #10151,
/// Feature #10116). En la HU #10195 se usa <b>solo en lectura</b> para validar la
/// existencia del trámite al asociar documentos; su CRUD pertenece a otra HU. Solo se
/// mapean las columnas necesarias para esa comprobación.
/// </summary>
public sealed class ProcedureType
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
