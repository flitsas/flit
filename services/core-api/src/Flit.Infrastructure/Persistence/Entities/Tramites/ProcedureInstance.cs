namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Instancia de trámite — <c>tramites.procedure_instances</c> (DDL HU #10150). En la
/// HU #10197 se crea el registro mínimo en estado <c>draft</c> al iniciar un trámite, para
/// anclar el snapshot documental inmutable (<see cref="ProcedureDocumentSnapshot"/>). El
/// runtime completo de radicación (#10128) pertenece a otra HU.
/// </summary>
public sealed class ProcedureInstance
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureTypeId { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public string Status { get; set; } = "draft";

    public Guid? TransitOfficeId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
