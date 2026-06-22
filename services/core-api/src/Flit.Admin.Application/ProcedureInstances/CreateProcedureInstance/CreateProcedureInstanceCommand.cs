namespace Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;

/// <summary>
/// Comando de alta de un trámite con snapshot documental (HU #10197, AC1).
/// <see cref="ActorUserId"/> es el usuario autenticado (JWT <c>sub</c>): se usa como
/// <c>created_by_user_id</c>/<c>snapshot_by</c> cuando el cuerpo no trae
/// <c>createdByUserId</c>.
/// </summary>
public sealed class CreateProcedureInstanceCommand
{
    public required CreateProcedureInstanceRequest Request { get; init; }

    public Guid? ActorUserId { get; init; }
}
