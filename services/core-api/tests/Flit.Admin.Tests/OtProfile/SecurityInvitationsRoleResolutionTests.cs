using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.OtProfile;

/// <summary>
/// <c>POST /api/v1/security/invitations</c> cuando el caller es SuperAdmin (refactor
/// adminOT): el rol de sistema a asignar ya no se fuerza siempre a <c>AdminCompany</c> —
/// se resuelve según el tipo de tenant destino (OT con <see cref="TransitOfficeProfile"/>
/// asociado → <c>ot_admin</c>; compañía → <c>AdminCompany</c>, comportamiento sin cambios).
/// Integración real contra la BD de desarrollo (WebApplicationFactory + FlitDbContext real,
/// mismo patrón que <see cref="AdminOtUsersEndpointsTests"/>): seeda un tenant compañía y un
/// tenant OT con GUIDs aleatorios por ejecución, y limpia lo creado al finalizar.
/// </summary>
public sealed class SecurityInvitationsRoleResolutionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _superAdminTenantId = Guid.NewGuid();

    private readonly Guid _companyTenantId = Guid.NewGuid();

    // HU #10505: security.roles es un catálogo GLOBAL — "AdminCompany"/"ot_admin" ya no se
    // crean por tenant en el seed de este test; se resuelven por Code contra las filas
    // globales sembradas por DevelopmentAuthSeeder al levantar la WebApplicationFactory.
    private Guid _companyAdminRoleId;

    private readonly Guid _transitOfficeId = Guid.NewGuid();
    private readonly Guid _otTenantId = Guid.NewGuid();
    private Guid _otAdminRoleId;

    public SecurityInvitationsRoleResolutionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("SuperAdmin", _superAdminTenantId, _superAdminUserId));

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_TargetingCompanyTenant_ResolvesAdminCompanyRole()
    {
        var email = $"admin-company-invite-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Admin Compañía", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_companyTenantId);
        invitation.RoleId.Should().Be(_companyAdminRoleId);
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_TargetingOtTenant_ResolvesOtAdminRole()
    {
        var email = $"admin-ot-invite-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Admin OT", targetTenantId = _otTenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_otTenantId);
        invitation.RoleId.Should().Be(_otAdminRoleId);
    }

    // HU #11550 AC1 / HU #11580 AC1-AC2 — invitar por la ruta de Security (SuperAdmin) con un
    // correo que YA tiene una invitación pendiente en el tenant destino debe mostrar el mensaje
    // Y el código unificados (indistinguibilidad — ya no conserva su propio código de error).
    [Fact]
    public async Task Invite_AsSuperAdmin_EmailWithPendingInvitation_Returns409WithUnifiedMessage()
    {
        var (_, pendingEmail) = await SeedPendingInvitationAsync(_companyTenantId, lastSentAt: null);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email = pendingEmail, fullName = "Invitación duplicada", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<SecurityErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Code.Should().Be("EMAIL_ALREADY_IN_USE");
        body.Message.Should().Be("El correo utilizado ya se encuentra asociado a otra cuenta");
    }

    // HU #11550 AC2 / HU #11580 AC1-AC2 — invitar por la ruta de Security con el correo de una
    // cuenta ACTIVA debe mostrar el mismo mensaje Y el mismo código unificados.
    [Fact]
    public async Task Invite_AsSuperAdmin_EmailOfActiveAccount_Returns409WithUnifiedMessage()
    {
        var activeEmail = $"superadmin-{_superAdminUserId:N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email = activeEmail, fullName = "Ya tiene cuenta activa", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<SecurityErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Code.Should().Be("EMAIL_ALREADY_IN_USE");
        body.Message.Should().Be("El correo utilizado ya se encuentra asociado a otra cuenta");
    }

    // HU #10625 AC1 — SuperAdmin puede reenviar CUALQUIER invitación del sistema (sin
    // restricción de tenant), a diferencia de AdminCompany que solo puede su propio tenant.
    [Fact]
    public async Task Resend_AsSuperAdmin_InvitationOfAnotherTenant_Returns200()
    {
        var (invitationId, email) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null);

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Id == invitationId, TestContext.Current.CancellationToken);
        invitation.Email.Should().Be(email);
        invitation.LastSentAt.Should().NotBeNull();
    }

    // HU #10625 AC1 — AdminCompany reenvía una invitación pendiente de su propio tenant
    [Fact]
    public async Task Resend_AsAdminCompany_OwnTenantInvitation_Returns200()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(_companyTenantId, lastSentAt: null);

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // HU #10625 AC2 — cooldown activo: reenviada hace menos de 2 minutos → 429 + Retry-After
    [Fact]
    public async Task Resend_AsAdminCompany_WithinCooldown_Returns429WithRetryAfterHeader()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(
            _companyTenantId, lastSentAt: DateTimeOffset.UtcNow.AddSeconds(-30));

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    // HU #10625 AC3 — invitación ya cancelada → 409
    [Fact]
    public async Task Resend_AsAdminCompany_InvitationCancelled_Returns409()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(
            _companyTenantId, lastSentAt: null, status: "cancelled");

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // AdminCompany no puede reenviar una invitación de OTRO tenant → 404 (fuera de alcance)
    [Fact]
    public async Task Resend_AsAdminCompany_InvitationOfAnotherTenant_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null);

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // AC1 (HU #10627) — cancelar una invitación pendiente de mi alcance: el enlace de
    // activación deja de funcionar (Status ya no es "pending") y el email queda disponible
    // para una nueva invitación.
    [Fact]
    public async Task CancelInvitation_AsSuperAdmin_PendingInvitation_CancelsAndAllowsNewInvitation()
    {
        var email = $"cancel-invite-{Guid.NewGuid():N}@flit.local";

        var createResponse = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Cancelar Invitación", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.AsNoTracking()
                .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);

            var cancelResponse = await _client.DeleteAsync(
                $"/api/v1/security/invitations/{invitation.Id}", TestContext.Current.CancellationToken);
            cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        await using (var db = CreateDbContext())
        {
            var cancelled = await db.UserInvitations.AsNoTracking()
                .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
            cancelled.Status.Should().Be("cancelled");
            // ADR-0048: "cancelled" es un estado vivo y reversible, NO un soft-delete.
            cancelled.DeletedAt.Should().BeNull();
        }

        // El email queda disponible para una nueva invitación (la cancelada no cuenta como pending).
        var reInviteResponse = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Cancelar Invitación", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);
        reInviteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // AC2 (HU #10627) — cancelar una invitación ya cancelada previamente → 409, error explícito.
    [Fact]
    public async Task CancelInvitation_AlreadyCancelled_Returns409()
    {
        var email = $"cancel-twice-{Guid.NewGuid():N}@flit.local";

        await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Cancelar Dos Veces", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);

        var firstCancel = await _client.DeleteAsync(
            $"/api/v1/security/invitations/{invitation.Id}", TestContext.Current.CancellationToken);
        firstCancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondCancel = await _client.DeleteAsync(
            $"/api/v1/security/invitations/{invitation.Id}", TestContext.Current.CancellationToken);
        secondCancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // Cancelar una invitación inexistente → 404.
    [Fact]
    public async Task CancelInvitation_NotFound_Returns404()
    {
        var response = await _client.DeleteAsync(
            $"/api/v1/security/invitations/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // HU #11552 / ADR-0048 — el listado GET /users muestra la invitación cancelada con su
    // estado real (no desaparece, no queda en "pending" hardcodeado).
    [Fact]
    public async Task GetUsers_AsSuperAdmin_ShowsCancelledInvitationWithRealStatus()
    {
        var (invitationId, email) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null);

        var cancelResponse = await _client.DeleteAsync(
            $"/api/v1/security/invitations/{invitationId}", TestContext.Current.CancellationToken);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await _client.GetFromJsonAsync<List<TenantUserListDto>>(
            "/api/v1/security/users", TestContext.Current.CancellationToken);

        var row = listResponse!.Single(u => u.Email == email);
        row.Status.Should().Be("cancelled");
    }

    // ADR-0048, ciclo completo — cancelar→reactivar: vuelve a "pending" con un token nuevo (el
    // token viejo ya no resuelve ninguna invitación pendiente) y reenvía el correo.
    [Fact]
    public async Task ReactivateInvitation_AsSuperAdmin_CancelledInvitation_ReturnsPendingWithNewToken()
    {
        var (invitationId, email) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null);

        string oldTokenHash;
        await using (var db = CreateDbContext())
        {
            oldTokenHash = await db.UserInvitations.AsNoTracking()
                .Where(i => i.Id == invitationId).Select(i => i.TokenHash)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        (await _client.DeleteAsync(
            $"/api/v1/security/invitations/{invitationId}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reactivateResponse = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);
        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await reactivateResponse.Content.ReadFromJsonAsync<InvitationCreatedBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.InvitationId.Should().Be(invitationId);
        body.Email.Should().Be(email);

        await using var dbAfter = CreateDbContext();
        var invitation = await dbAfter.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Id == invitationId, TestContext.Current.CancellationToken);
        invitation.Status.Should().Be("pending");
        // Token viejo muerto: el token regenerado nunca reutiliza el hash anterior.
        invitation.TokenHash.Should().NotBe(oldTokenHash);
        // "cancelled" no fue nunca un soft-delete (ADR-0048) — la reactivación tampoco toca DeletedAt.
        invitation.DeletedAt.Should().BeNull();
    }

    // No es idempotente a propósito: reactivar una invitación que ya está "pending" (nunca se
    // canceló) → 409 INVITATION_NOT_CANCELLED, distinto del 409 INVITATION_NOT_PENDING de cancelar/reenviar.
    [Fact]
    public async Task ReactivateInvitation_AsSuperAdmin_InvitationStillPending_Returns409()
    {
        var (invitationId, _) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null);

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<SecurityErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Code.Should().Be("INVITATION_NOT_CANCELLED");
    }

    // Ciclo completo cancelar→reactivar→(activada) — una segunda reactivación sobre la misma
    // invitación, ya "accepted", sigue rechazándose con el mismo código que "pending".
    [Fact]
    public async Task ReactivateInvitation_FullCycle_CancelReactivateThenAccepted_SecondReactivateReturns409()
    {
        var (invitationId, _) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null, status: "cancelled");

        var firstReactivate = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);
        firstReactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        // Simula la activación (consumo del token por ActivateAccountHandler, cubierto por sus
        // propios tests unitarios) marcando la invitación como aceptada directamente en BD.
        await using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.SingleAsync(
                i => i.Id == invitationId, TestContext.Current.CancellationToken);
            invitation.Status = "accepted";
            invitation.AcceptedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var secondReactivate = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);
        secondReactivate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await secondReactivate.Content.ReadFromJsonAsync<SecurityErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Code.Should().Be("INVITATION_NOT_CANCELLED");
    }

    // Reactivar una invitación cancelada cuando ya existe OTRA pending con el mismo correo en el
    // tenant → 409 EMAIL_ALREADY_IN_USE (pre-validado antes de tocar la fila; HU #11580: código
    // único, ya no INVITATION_ALREADY_PENDING).
    [Fact]
    public async Task ReactivateInvitation_AsSuperAdmin_CollidesWithAnotherPendingSameEmail_Returns409()
    {
        var (cancelledId, email) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null, status: "cancelled");

        await using (var db = CreateDbContext())
        {
            db.UserInvitations.Add(new UserInvitation
            {
                Id = Guid.NewGuid(),
                TenantId = _otTenantId,
                Email = email,
                FullName = "Invitado Colisión",
                TokenHash = $"hash-{Guid.NewGuid():N}",
                Status = "pending",
                InvitedBy = _superAdminUserId,
                CreatedAt = DateTimeOffset.UtcNow,
                RowVersion = 0,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{cancelledId}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<SecurityErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Code.Should().Be("EMAIL_ALREADY_IN_USE");
    }

    // Cooldown antiabuso compartido con /resend: cancelada hace menos de 2 minutos desde el
    // último envío → 429 + Retry-After (evita el bypass cancel→reactivate en bucle).
    [Fact]
    public async Task ReactivateInvitation_AsSuperAdmin_WithinCooldown_Returns429WithRetryAfterHeader()
    {
        var (invitationId, _) = await SeedPendingInvitationAsync(
            _otTenantId, lastSentAt: DateTimeOffset.UtcNow.AddSeconds(-30), status: "cancelled");

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    // AdminCompany reactiva una invitación cancelada de su propio tenant.
    [Fact]
    public async Task ReactivateInvitation_AsAdminCompany_OwnTenantInvitation_Returns200()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(
            _companyTenantId, lastSentAt: null, status: "cancelled");

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // AdminCompany no puede reactivar una invitación cancelada de OTRO tenant → 404 (mismo
    // alcance/mismo error que cancelar).
    [Fact]
    public async Task ReactivateInvitation_AsAdminCompany_InvitationOfAnotherTenant_Returns404()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, Guid.NewGuid()));

        var (invitationId, _) = await SeedPendingInvitationAsync(_otTenantId, lastSentAt: null, status: "cancelled");

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Reactivar una invitación inexistente → 404.
    [Fact]
    public async Task ReactivateInvitation_NotFound_Returns404()
    {
        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{Guid.NewGuid()}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Bug #11581 AC1 — un caller autenticado pero SIN rol AdminCompany/SuperAdmin (p.ej.
    // "Radicador", rol real sembrado por DevelopmentAuthSeeder) no puede crear invitaciones:
    // antes del fix el grupo solo exigía .RequireAuthorization() sin policy, así que
    // cualquier usuario autenticado del tenant podía invitar (escalada de privilegios).
    [Fact]
    public async Task Invite_AsRadicador_Returns403AndDoesNotCreateInvitation()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("Radicador", _companyTenantId, Guid.NewGuid()));

        var email = $"radicador-invite-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Invitado por Radicador", targetTenantId = _companyTenantId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = CreateDbContext();
        var created = await db.UserInvitations.AsNoTracking()
            .AnyAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        created.Should().BeFalse("un caller sin rol AdminCompany/SuperAdmin no debe poder crear invitaciones");
    }

    // Bug #11581 AC2 — mismo defecto en /resend: un caller "Radicador" no puede reenviar una
    // invitación pendiente, aunque sea de su propio tenant.
    [Fact]
    public async Task Resend_AsRadicador_Returns403()
    {
        var (invitationId, _) = await SeedPendingInvitationAsync(_companyTenantId, lastSentAt: null);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("Radicador", _companyTenantId, Guid.NewGuid()));

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Bug #11581 AC3 (no regresión) — con AdminCompany, crear invitación sigue funcionando
    // igual que antes del fix (Resend_AsAdminCompany_OwnTenantInvitation_Returns200 ya cubre
    // la no-regresión de /resend; aquí se cubre /invitations, que no tenía cobertura explícita
    // con AdminCompany, solo con SuperAdmin).
    [Fact]
    public async Task Invite_AsAdminCompany_OwnTenant_Returns201()
    {
        // invited_by es FK a users.id (user_invitations_invited_by_fkey): a diferencia de
        // /resend (que no inserta una fila nueva), /invitations SÍ necesita un usuario real
        // — se reutiliza _superAdminUserId (ya sembrado) solo para satisfacer la FK; el claim
        // "role" del token sigue siendo AdminCompany, que es lo que decide la autorización.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, _superAdminUserId));

        var email = $"admincompany-invite-{Guid.NewGuid():N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email, fullName = "Invitado por AdminCompany", roleIds = new[] { _companyAdminRoleId } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_companyTenantId);
    }

    // HU #11580 AC1/AC2 — indistinguibilidad: las tres causas de correo ya ocupado
    // (invitación pendiente, cuenta activa, cuenta eliminada) deben producir EXACTAMENTE la
    // misma respuesta (status + code + message), para que un atacante no pueda distinguir cuál
    // de las tres es la causa real. Se comparan las tres respuestas ENTRE SÍ, no solo contra un
    // valor fijo, para que el test falle si alguien reintroduce cualquier diferencia.
    [Fact]
    public async Task Invite_ThreeConflictCauses_ReturnIndistinguishableResponses()
    {
        // Causa 1 — invitación pendiente en el mismo tenant destino.
        var (_, pendingEmail) = await SeedPendingInvitationAsync(_companyTenantId, lastSentAt: null);

        // Causa 2 — cuenta activa (reutiliza el correo del SuperAdmin sembrado en SeedAsync).
        var activeEmail = $"superadmin-{_superAdminUserId:N}@flit.local";

        // Causa 3 — cuenta eliminada (soft-delete): se siembra un usuario ya con DeletedAt
        // fijado, sin pasar por el endpoint de borrado (más simple y no depende de otro flujo).
        var deletedUserId = Guid.NewGuid();
        var deletedEmail = $"deleted-{deletedUserId:N}@flit.local";
        await using (var db = CreateDbContext())
        {
            db.Users.Add(new User
            {
                Id = deletedUserId,
                Email = deletedEmail,
                DisplayName = "Cuenta eliminada de prueba",
                Status = "active",
                CreatedAt = DateTimeOffset.UtcNow,
                DeletedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            var responsePending = await _client.PostAsJsonAsync(
                "/api/v1/security/invitations",
                new { email = pendingEmail, fullName = "Causa 1", targetTenantId = _companyTenantId },
                TestContext.Current.CancellationToken);
            var responseActive = await _client.PostAsJsonAsync(
                "/api/v1/security/invitations",
                new { email = activeEmail, fullName = "Causa 2", targetTenantId = _companyTenantId },
                TestContext.Current.CancellationToken);
            var responseDeleted = await _client.PostAsJsonAsync(
                "/api/v1/security/invitations",
                new { email = deletedEmail, fullName = "Causa 3", targetTenantId = _companyTenantId },
                TestContext.Current.CancellationToken);

            responsePending.StatusCode.Should().Be(HttpStatusCode.Conflict);
            responseActive.StatusCode.Should().Be(HttpStatusCode.Conflict);
            responseDeleted.StatusCode.Should().Be(HttpStatusCode.Conflict);

            var bodyPending = await responsePending.Content.ReadFromJsonAsync<SecurityErrorBody>(
                cancellationToken: TestContext.Current.CancellationToken);
            var bodyActive = await responseActive.Content.ReadFromJsonAsync<SecurityErrorBody>(
                cancellationToken: TestContext.Current.CancellationToken);
            var bodyDeleted = await responseDeleted.Content.ReadFromJsonAsync<SecurityErrorBody>(
                cancellationToken: TestContext.Current.CancellationToken);

            // Las tres respuestas deben ser IDÉNTICAS entre sí — ni un atacante que dispare las
            // tres causas puede diferenciarlas por el cuerpo de la respuesta.
            bodyActive.Should().BeEquivalentTo(bodyPending);
            bodyDeleted.Should().BeEquivalentTo(bodyPending);

            bodyPending!.Code.Should().Be("EMAIL_ALREADY_IN_USE");
            bodyPending.Message.Should().Be("El correo utilizado ya se encuentra asociado a otra cuenta");
        }
        finally
        {
            await using var db = CreateDbContext();
            db.TenantConfigAuditLogs.RemoveRange(
                db.TenantConfigAuditLogs.Where(a => a.ChangedBy == _superAdminUserId || a.TargetEntityId == deletedUserId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            db.Users.RemoveRange(db.Users.Where(u => u.Id == deletedUserId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    // HU #11580 AC3 — la causa CONCRETA del conflicto (no el código único de la respuesta) debe
    // quedar registrada en auditoría vía ConfigAuditFailureContext, para que el rastro conserve
    // la trazabilidad que antes vivía (falsamente, según el hallazgo de la HU) en el código de
    // la respuesta. Antes de este fix, AdminAuditFilter.ResolveErrorCode caía al fallback por
    // status HTTP y registraba literalmente "conflict" para cualquier 409.
    //
    // Usa el caller AdminCompany (tenant real _companyTenantId) en vez de SuperAdmin: el tenant
    // del token SuperAdmin de este archivo (_superAdminTenantId) es un GUID que NUNCA se
    // persiste como fila `Tenants` (solo vive en el claim del JWT) — AdminAuditWriter intenta
    // `SELECT set_config('app.current_tenant_id', ...)` y el INSERT posterior viola la FK
    // `tenant_config_audit_logs.tenant_id → identity.tenants(id)`, y como el writer es
    // best-effort (nunca propaga excepciones), la fila simplemente no se escribe — sin este
    // dato el AC3 daría un falso negativo por un defecto de seed del propio test, no del código
    // bajo prueba.
    [Fact]
    public async Task Invite_AsAdminCompany_EmailOfActiveAccount_AuditLogsConcreteCause_NotGenericConflict()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MintToken("AdminCompany", _companyTenantId, _superAdminUserId));

        var activeEmail = $"superadmin-{_superAdminUserId:N}@flit.local";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/security/invitations",
            new { email = activeEmail, fullName = "Auditoría causa concreta", roleIds = new[] { _companyAdminRoleId } },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = CreateDbContext();
        var auditRow = await db.TenantConfigAuditLogs.AsNoTracking()
            .Where(a => a.ChangedBy == _superAdminUserId
                && a.Module == AuditVocabulary.Modules.Users
                && a.Operation == AuditVocabulary.Operations.Invite
                && a.Result == AuditVocabulary.Results.Failure)
            .OrderByDescending(a => a.ChangedAt)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        auditRow.Should().NotBeNull("el endpoint tiene AdminAuditFilter enganchado (invite) y debe dejar traza");
        auditRow!.ErrorCode.Should().Be("user_already_exists");
        auditRow.ErrorCode.Should().NotBe("conflict");

        // Limpieza: esta fila de auditoría bloquearía el borrado de _superAdminUserId en
        // Dispose() (FK tenant_config_audit_logs_changed_by_fkey) si no se retira aquí.
        db.TenantConfigAuditLogs.Remove(auditRow);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(Guid InvitationId, string Email)> SeedPendingInvitationAsync(
        Guid tenantId, DateTimeOffset? lastSentAt, string status = "pending")
    {
        await using var db = CreateDbContext();

        var invitationId = Guid.NewGuid();
        var email = $"resend-{Guid.NewGuid():N}@flit.local";

        db.UserInvitations.Add(new UserInvitation
        {
            Id = invitationId,
            TenantId = tenantId,
            Email = email,
            FullName = "Invitado Reenvío",
            TokenHash = $"hash-{Guid.NewGuid():N}",
            Status = status,
            InvitedBy = _superAdminUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            LastSentAt = lastSentAt,
            RowVersion = 0,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (invitationId, email);
    }

    private async Task SeedAsync()
    {
        await using var db = CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = _companyTenantId,
            Code = $"CO-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "Compañía de prueba",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.TransitOffices.Add(new TransitOffice
        {
            Id = _transitOfficeId,
            Code = $"T{Guid.NewGuid():N}"[..10],
            Name = "OT invitations role-resolution tests",
            DepartmentCode = "99",
            CityCode = "99998",
            IsActive = true,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _otTenantId,
            Code = $"OT-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "OT de prueba",
            TaxId = TestNit.Unique(),
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _superAdminUserId,
            Email = $"superadmin-{_superAdminUserId:N}@flit.local",
            DisplayName = "SuperAdmin de prueba",
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Tenants + Users primero (el perfil OT depende del tenant recién insertado; sin
        // navegaciones EF entre estos agregados, mismo motivo que en
        // AdminOtUsersEndpointsTests.SeedAsync).
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id) — se
        // reutiliza la fila si ya existe (p.ej. sembrada por DevelopmentAuthSeeder) o se crea
        // aquí mismo si no (BD limpia, como la que usa CI): el test no puede depender de que
        // el seeder de desarrollo haya corrido antes que este, solo de que exista una única
        // fila global (violaría UNIQUE(code, target_entity_type) crear una por tenant de prueba).
        _companyAdminRoleId = await GetOrCreateGlobalRoleAsync(
            db, "AdminCompany", "Administrador de Compañía", "COMPANY", TestContext.Current.CancellationToken);

        _otAdminRoleId = await GetOrCreateGlobalRoleAsync(
            db, "ot_admin", "Administrador OT", "TRANSIT_OFFICE", TestContext.Current.CancellationToken);

        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            TransitOfficeId = _transitOfficeId,
            OperationMode = "dashboard",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

    /// <summary>
    /// El catálogo de roles es GLOBAL y varias clases de test lo siembran. xUnit las corre en
    /// paralelo, así que entre el SELECT y el INSERT otra clase puede haber creado la misma fila
    /// y el índice único parcial <c>uq_roles_code_target_entity_type</c> rechaza la segunda. Ese
    /// choque no es un fallo: significa que la fila ya existe, que es justo lo que se pedía.
    /// </summary>
    private static async Task<Guid> GetOrCreateGlobalRoleAsync(
        FlitDbContext db, string code, string name, string targetEntityType, CancellationToken ct)
    {
        var existingId = await FindGlobalRoleAsync(db, code, targetEntityType, ct);
        if (existingId != Guid.Empty)
            return existingId;

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            TargetEntityType = targetEntityType,
            IsSystem = true,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Roles.Add(role);

        try
        {
            await db.SaveChangesAsync(ct);
            return role.Id;
        }
        catch (DbUpdateException)
        {
            db.Entry(role).State = EntityState.Detached;

            var winnerId = await FindGlobalRoleAsync(db, code, targetEntityType, ct);
            if (winnerId == Guid.Empty)
                throw; // No fue la carrera: el INSERT falló por otro motivo.

            return winnerId;
        }
    }

    private static Task<Guid> FindGlobalRoleAsync(
        FlitDbContext db, string code, string targetEntityType, CancellationToken ct) =>
        db.Roles.AsNoTracking()
            .Where(r => r.Code == code && r.TargetEntityType == targetEntityType && r.DeletedAt == null)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

    private static string MintToken(string role, Guid tenantId, Guid userId)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("role", role),
                new Claim("tenant_id", tenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(DummyKey, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>
    /// Limpieza en orden de dependencia FK: cada etapa hace su propio <c>SaveChanges</c>
    /// porque, sin navegaciones EF entre estos agregados, el batching no puede inferir el
    /// orden correcto de borrado (mismo patrón que <see cref="AdminOtUsersEndpointsTests"/>).
    /// </summary>
    public void Dispose()
    {
        using var db = CreateDbContext();

        db.UserInvitations.RemoveRange(
            db.UserInvitations.Where(i => i.TenantId == _companyTenantId || i.TenantId == _otTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantId));
        db.SaveChanges();

        // HU #10505: "AdminCompany"/"ot_admin" son roles del catálogo GLOBAL, sembrados una sola
        // vez por DevelopmentAuthSeeder — este test NO los crea ni los borra, solo los resuelve.

        db.Users.RemoveRange(db.Users.Where(u => u.Id == _superAdminUserId));
        db.Tenants.RemoveRange(
            db.Tenants.Where(t => t.Id == _companyTenantId || t.Id == _otTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _transitOfficeId));
        db.SaveChanges();
    }

    // SecurityEndpoints (SuperAdmin/AdminCompany) usa "code" en vez de "error" — mismo
    // convenio documentado en AdminOtUsersEndpointsTests.SuperAdminErrorBody.
    private sealed record SecurityErrorBody(string Code, string? Message);

    // Respuesta de POST /invitations y POST /invitations/{id}/reactivate — mismo shape
    // (InvitationCreatedResponse) que crear/reenviar.
    private sealed record InvitationCreatedBody(Guid InvitationId, string Email, bool EmailSent);

    // Proyección mínima de GET /security/users usada solo para leer el Status real (HU #11552).
    private sealed record TenantUserListDto(string Id, string FullName, string Email, string Status);
}
