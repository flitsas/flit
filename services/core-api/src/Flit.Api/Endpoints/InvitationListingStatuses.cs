namespace Flit.Api.Endpoints;

/// <summary>
/// Estados de invitación visibles en los listados de usuarios — HU #11552 / ADR-0048. Antes solo
/// se proyectaban invitaciones "pending"; una cancelada desaparecía del módulo Usuarios sin
/// dejar rastro. Ahora "cancelled" también se ve (con su estado real, no el literal "pending"
/// hardcodeado que tenían las cuatro copias) para que el usuario final pueda reactivarla.
///
/// <c>Visible</c> se usa como <c>.Contains(x.Status)</c> dentro de queries LINQ — EF Core lo
/// traduce a <c>IN (...)</c>, así que sigue siendo una sola query SQL por listado.
///
/// Las cuatro copias que consumen esta constante: <c>SecurityEndpoints.GET /users</c>
/// (SuperAdmin-otros-tenants, SuperAdmin-tenant-FLIT, AdminCompany) y
/// <c>AdminOtEndpoints.ListUsersAsync</c>. La rama <c>onlyDeleted=true</c> de
/// <c>SecurityEndpoints</c> NO consume esto — consulta solo <c>db.Users</c> soft-deleted, no
/// invitaciones.
/// </summary>
internal static class InvitationListingStatuses
{
    public static readonly string[] Visible = ["pending", "cancelled"];
}
