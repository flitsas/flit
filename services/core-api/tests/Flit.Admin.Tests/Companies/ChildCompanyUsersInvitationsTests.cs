using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Admin.Tests.Companies;

/// <summary>
/// Bug #14714 — la cabeza (AdminCompany) gestiona a su hijo desde la ficha de red. El listado
/// <c>GET /companies/{cabeza}/children/{hijo}/users</c> solo traía usuarios ya activos (con rol): una
/// invitación pendiente dejaba la tabla en «No hay usuarios en esta compañía». Ahora las invitaciones
/// pendientes y canceladas llegan con su estado real, como en el listado general de Usuarios.
/// </summary>
public sealed class ChildCompanyUsersInvitationsTests
    : IClassFixture<AdminCompanyReservedSectionsTests.HierarchyFactory>
{
    private static readonly Guid Cabeza = Guid.Parse("e0000000-0000-4000-8000-000000000100");
    private static readonly Guid Hija = Guid.Parse("e0000000-0000-4000-8000-000000001100");

    private readonly AdminCompanyReservedSectionsTests.HierarchyFactory _factory;

    public ChildCompanyUsersInvitationsTests(AdminCompanyReservedSectionsTests.HierarchyFactory factory)
        => _factory = factory;

    [Fact]
    public async Task La_cabeza_ve_las_invitaciones_pendientes_y_canceladas_de_su_hijo()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var emailPendiente = $"bug14714.pend.{sufijo}@flit.local";
        var emailCancelada = $"bug14714.canc.{sufijo}@flit.local";
        var emailAceptada = $"bug14714.acep.{sufijo}@flit.local";

        await SembrarAsync(
            (emailPendiente, "pending"),
            (emailCancelada, "cancelled"),
            (emailAceptada, "accepted"));
        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", TestTokenFactory.CreateAdminCompanyToken(Cabeza));

            var response = await client.GetAsync(
                $"/api/v1/admin/companies/{Cabeza}/children/{Hija}/users", TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var porEmail = json.RootElement.GetProperty("items").EnumerateArray()
                .ToDictionary(i => i.GetProperty("email").GetString()!, i => i.GetProperty("status").GetString());

            porEmail.Should().ContainKey(emailPendiente).WhoseValue.Should().Be("pending");
            porEmail.Should().ContainKey(emailCancelada).WhoseValue.Should().Be("cancelled");
            // Una invitación ya aceptada es un usuario con rol, no una fila de invitación.
            porEmail.Should().NotContainKey(emailAceptada);
        }
        finally
        {
            await LimpiarAsync(sufijo);
        }
    }

    private async Task SembrarAsync(params (string Email, string Status)[] filas)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var invitedBy = await db.Users.AsNoTracking().Select(u => u.Id).FirstAsync(TestContext.Current.CancellationToken);

        // La invitación tiene FK a identity.tenants: el id fijo de la «hija» del test debe existir.
        // El `code` lo asigna Postgres; el NIT sale del id para que sea único y estable entre corridas.
        var nit = "T14714-" + Hija.ToString("N")[^8..];
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
               VALUES ({Hija}, 'Hija Bug 14714 (test)', {nit}, 'CONCESIONARIO', true, now())
               ON CONFLICT (id) DO NOTHING",
            TestContext.Current.CancellationToken);

        foreach (var (email, status) in filas)
        {
            db.UserInvitations.Add(new UserInvitation
            {
                TenantId = Hija,
                Email = email,
                FullName = "Invitado Bug 14714",
                TokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                Status = status,
                InvitedBy = invitedBy,
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task LimpiarAsync(string sufijo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        await db.UserInvitations
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == Hija && i.Email.Contains(sufijo))
            .ExecuteDeleteAsync(CancellationToken.None);

        // Quita el tenant sembrado solo si quedó sin nada colgando; si otro test lo reutiliza, lo deja.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM identity.tenants t WHERE t.id = {Hija} AND t.legal_name = 'Hija Bug 14714 (test)'
               AND NOT EXISTS (SELECT 1 FROM security.user_invitations i WHERE i.tenant_id = t.id)",
            CancellationToken.None);
    }
}
