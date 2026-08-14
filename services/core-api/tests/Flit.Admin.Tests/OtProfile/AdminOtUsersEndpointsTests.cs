using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Flit.Admin.Tests.OtProfile;

/// <summary>
/// Self-service de usuarios OT (refactor adminOT) — <c>POST /api/v1/admin/ot/users/invite</c>,
/// <c>GET /api/v1/admin/ot/users</c>, <c>POST</c>/<c>DELETE /api/v1/admin/ot/users/{userId}/suspend</c>.
/// Integración real contra la BD de desarrollo (WebApplicationFactory + FlitDbContext real —
/// mismo patrón que <see cref="AdminOtAuthorizationTests"/>): seeda su propio tenant OT,
/// oficina de catálogo, rol <c>ot_admin</c> y usuarios, con GUIDs aleatorios por ejecución
/// para no chocar con datos existentes, y limpia lo creado al finalizar.
/// </summary>
public sealed class AdminOtUsersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly SymmetricSecurityKey DummyKey =
        new(Encoding.UTF8.GetBytes(new string('k', 64)));

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _transitOfficeId = Guid.NewGuid();
    private readonly Guid _otTenantId = Guid.NewGuid();

    // HU #10505: security.roles es un catálogo GLOBAL — "ot_admin" ya no se crea por tenant en
    // el seed de este test; se resuelve por Code contra la fila global sembrada por
    // DevelopmentAuthSeeder al levantar la WebApplicationFactory.
    private Guid _roleId;

    private readonly Guid _superAdminUserId = Guid.NewGuid();
    private readonly Guid _otAdminUserId = Guid.NewGuid();
    private readonly Guid _collaboratorUserId = Guid.NewGuid();

    public AdminOtUsersEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_TargetingTransitOfficeId_ResolvesOtAdminRole_Returns201()
    {
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"superadmin-invite-{Guid.NewGuid():N}@flit.local";
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/invite?transitOfficeId={_transitOfficeId}",
            new { email, fullName = "Primer AdminOT" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_otTenantId);
        invitation.RoleId.Should().Be(_roleId);
    }

    [Fact]
    public async Task Invite_AsOtAdmin_WithoutQueryParam_UsesOwnTenant_Returns201()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"ot-admin-invite-{Guid.NewGuid():N}@flit.local";
        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email, fullName = "Colaborador OT" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_otTenantId);
        invitation.RoleId.Should().Be(_roleId);
    }

    [Fact]
    public async Task Invite_AsOtAdmin_WithForeignTransitOfficeId_IgnoresSpoof_UsesJwtTenant()
    {
        // HU #11229 — ot_admin no puede escalar tenant vía query; el backend ignora transitOfficeId.
        var foreignOfficeId = Guid.NewGuid();
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"ot-spoof-invite-{Guid.NewGuid():N}@flit.local";
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/invite?transitOfficeId={foreignOfficeId}",
            new { email, fullName = "Colaborador propio" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
        invitation.TenantId.Should().Be(_otTenantId);
    }

    [Fact]
    public async Task Invite_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email = "sin-scope@flit.local" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListUsers_AsOtAdmin_ReturnsCollaboratorInOwnTenant()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/admin/ot/users", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ListUsersBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Data.Should().Contain(u => u.Id == _collaboratorUserId.ToString());
    }

    // HU #10624 AC3 — con ?onlyDeleted=true, SuperAdmin ve al colaborador soft-deleted del
    // tenant OT resuelto (mismo criterio de scope que el listado normal — transitOfficeId).
    [Fact]
    public async Task ListUsers_OnlyDeleted_AsSuperAdmin_ReturnsSoftDeletedCollaborator()
    {
        // Eliminar es EXCLUSIVO de SuperAdmin: el paso de preparación (borrar al colaborador)
        // ya no puede hacerlo ot_admin.
        var superAdminToken = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        long rowVersion;
        await using (var db = CreateDbContext())
        {
            rowVersion = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => u.RowVersion)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var deleteResponse = await _client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/admin/ot/users/{_collaboratorUserId}?transitOfficeId={_transitOfficeId}")
            {
                Content = JsonContent.Create(new { rowVersion }),
            },
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.GetAsync(
            $"/api/v1/admin/ot/users?transitOfficeId={_transitOfficeId}&onlyDeleted=true",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DeletedUsersBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        var deletedCollaborator = body!.Data.Should()
            .ContainSingle(u => u.Id == _collaboratorUserId.ToString()).Subject;
        deletedCollaborator.DeletedAt.Should().NotBeNull();
    }

    // HU #10624 AC4 — un ot_admin (no SuperAdmin) que intenta usar onlyDeleted=true recibe 403:
    // solo SuperAdmin puede ver/restaurar usuarios eliminados.
    [Fact]
    public async Task ListUsers_OnlyDeleted_AsOtAdmin_Returns403()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync(
            "/api/v1/admin/ot/users?onlyDeleted=true", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // HU #10624 — regresión: sin onlyDeleted, el listado sigue excluyendo usuarios eliminados
    // exactamente igual que antes de agregar el parámetro.
    [Fact]
    public async Task ListUsers_WithoutOnlyDeleted_StillExcludesSoftDeletedUser()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        long rowVersion;
        await using (var db = CreateDbContext())
        {
            rowVersion = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => u.RowVersion)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        // Eliminar es EXCLUSIVO de SuperAdmin: el paso de preparación (borrar al colaborador)
        // ya no puede hacerlo ot_admin.
        var superAdminToken = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var deleteResponse = await _client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/admin/ot/users/{_collaboratorUserId}?transitOfficeId={_transitOfficeId}")
            {
                Content = JsonContent.Create(new { rowVersion }),
            },
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // El listado (sin onlyDeleted) sigue siendo accesible para ot_admin en su propio tenant.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/admin/ot/users", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ListUsersBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Data.Should().NotContain(u => u.Id == _collaboratorUserId.ToString());
    }

    // HU #10624 AC3 — con ?onlyDeleted=true en GET /api/v1/security/users, SuperAdmin ve al
    // colaborador soft-deleted de CUALQUIER tenant (aquí, el tenant OT del fixture).
    [Fact]
    public async Task SecurityListUsers_OnlyDeleted_AsSuperAdmin_ReturnsSoftDeletedCollaboratorFromAnyTenant()
    {
        // Eliminar es EXCLUSIVO de SuperAdmin: el paso de preparación (borrar al colaborador)
        // ya no puede hacerlo ot_admin.
        var superAdminToken = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        long rowVersion;
        await using (var db = CreateDbContext())
        {
            rowVersion = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => u.RowVersion)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var deleteResponse = await _client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/admin/ot/users/{_collaboratorUserId}?transitOfficeId={_transitOfficeId}")
            {
                Content = JsonContent.Create(new { rowVersion }),
            },
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.GetAsync(
            "/api/v1/security/users?onlyDeleted=true", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<List<SecurityDeletedUserItem>>(
            cancellationToken: TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        var deletedCollaborator = body!.Should()
            .ContainSingle(u => u.Id == _collaboratorUserId.ToString()).Subject;
        deletedCollaborator.TenantId.Should().Be(_otTenantId.ToString());
        deletedCollaborator.DeletedAt.Should().NotBeNull();
    }

    // HU #10624 AC4 — un ot_admin (no SuperAdmin) que intenta usar onlyDeleted=true en
    // GET /api/v1/security/users recibe 403.
    [Fact]
    public async Task SecurityListUsers_OnlyDeleted_AsOtAdmin_Returns403()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync(
            "/api/v1/security/users?onlyDeleted=true", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Suspend_ThenUnsuspend_AsSuperAdmin_TogglesActiveSuspension()
    {
        // Bloquear/desactivar y reactivar son EXCLUSIVOS de SuperAdmin.
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var suspendResponse = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}/suspend?transitOfficeId={_transitOfficeId}",
            new { reason = "Prueba automatizada", endsAt = DateTimeOffset.UtcNow.AddDays(1) },
            TestContext.Current.CancellationToken);
        suspendResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await using (var db = CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var hasActiveSuspension = await db.UserTempSuspensions.AsNoTracking().AnyAsync(
                s => s.UserId == _collaboratorUserId && s.TenantId == _otTenantId
                     && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now,
                TestContext.Current.CancellationToken);
            hasActiveSuspension.Should().BeTrue();
        }

        var unsuspendResponse = await _client.DeleteAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}/suspend?transitOfficeId={_transitOfficeId}",
            TestContext.Current.CancellationToken);
        unsuspendResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var hasActiveSuspension = await db.UserTempSuspensions.AsNoTracking().AnyAsync(
                s => s.UserId == _collaboratorUserId && s.TenantId == _otTenantId
                     && s.DeletedAt == null && s.StartsAt <= now && s.EndsAt >= now,
                TestContext.Current.CancellationToken);
            hasActiveSuspension.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Suspend_AsSuperAdmin_TargetingUnknownUserId_Returns404()
    {
        // Bloquear/desactivar es EXCLUSIVO de SuperAdmin; ot_admin ya no puede hacer esta
        // llamada (ver Suspend_AsOtAdmin_Returns403). Un userId inexistente dentro del tenant
        // OT resuelto por transitOfficeId sigue devolviendo 404 (TargetUserNotFound).
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/{Guid.NewGuid()}/suspend?transitOfficeId={_transitOfficeId}",
            new { reason = "Usuario inexistente", endsAt = DateTimeOffset.UtcNow.AddDays(1) },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Bloquear/desactivar es ahora EXCLUSIVO de SuperAdmin: ot_admin recibe 403 (antes podía
    // suspender colaboradores de su propio tenant).
    [Fact]
    public async Task Suspend_AsOtAdmin_Returns403()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}/suspend",
            new { reason = "ot_admin ya no puede suspender", endsAt = DateTimeOffset.UtcNow.AddDays(1) },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Eliminar es ahora EXCLUSIVO de SuperAdmin: ot_admin recibe 403 (antes podía eliminar
    // colaboradores de su propio tenant).
    [Fact]
    public async Task DeleteUser_AsOtAdmin_Returns403()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/ot/users/{_collaboratorUserId}")
            {
                Content = JsonContent.Create(new { rowVersion = 0L }),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // HU #10621 AC1 — nombre y correo válidos y distintos → se persisten, y row_version avanza
    // (trigger tr_users_row_version).
    [Fact]
    public async Task UpdateUser_AsOtAdmin_WithValidData_Returns200AndPersistsChanges()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        long rowVersion;
        await using (var db = CreateDbContext())
        {
            rowVersion = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => u.RowVersion)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var newEmail = $"colaborador-editado-{Guid.NewGuid():N}@flit.local";
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}",
            new { displayName = "Colaborador Editado", email = newEmail, rowVersion },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db2 = CreateDbContext();
        var user = await db2.Users.AsNoTracking()
            .SingleAsync(u => u.Id == _collaboratorUserId, TestContext.Current.CancellationToken);
        user.DisplayName.Should().Be("Colaborador Editado");
        user.Email.Should().Be(newEmail);
        user.RowVersion.Should().BeGreaterThan(rowVersion);
    }

    // HU #10625 AC1 — reenvío exitoso: sin envío previo, regenera el token y responde 200
    [Fact]
    public async Task ResendInvitation_AsOtAdmin_PendingNeverSent_Returns200()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (invitationId, email) = await SeedPendingInvitationAsync(lastSentAt: null);

        var response = await _client.PostAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var invitation = await db.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Id == invitationId, TestContext.Current.CancellationToken);
        invitation.Email.Should().Be(email);
        invitation.LastSentAt.Should().NotBeNull();
    }
    // HU #10621 AC4 — rowVersion desactualizado (otro admin ya guardó cambios) → 409, sin
    // sobrescribir nada.
    [Fact]
    public async Task UpdateUser_AsOtAdmin_WithStaleRowVersion_Returns409AndDoesNotPersist()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}",
            new { displayName = "No debería guardarse", rowVersion = -999L },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = CreateDbContext();
        var user = await db.Users.AsNoTracking()
            .SingleAsync(u => u.Id == _collaboratorUserId, TestContext.Current.CancellationToken);
        user.DisplayName.Should().NotBe("No debería guardarse");
    }

    // HU #10625 AC2 — cooldown activo: reenviada hace menos de 2 minutos → 429 + Retry-After
    [Fact]
    public async Task ResendInvitation_AsOtAdmin_WithinCooldown_Returns429WithRetryAfterHeader()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (invitationId, _) = await SeedPendingInvitationAsync(lastSentAt: DateTimeOffset.UtcNow.AddSeconds(-30));

        var response = await _client.PostAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }
    // HU #10621 AC2 — el correo ya pertenece a otra cuenta ACTIVA (el propio ot_admin) → 409
    // USER_ALREADY_EXISTS.
    [Fact]
    public async Task UpdateUser_WhenEmailBelongsToAnotherActiveUser_Returns409UserAlreadyExists()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        long rowVersion;
        string otAdminEmail;
        await using (var db = CreateDbContext())
        {
            var target = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => new { u.RowVersion })
                .SingleAsync(TestContext.Current.CancellationToken);
            rowVersion = target.RowVersion;
            otAdminEmail = await db.Users.AsNoTracking()
                .Where(u => u.Id == _otAdminUserId)
                .Select(u => u.Email)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}",
            new { email = otAdminEmail, rowVersion },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Error.Should().Be("USER_ALREADY_EXISTS");
        // HU #11550 — mensaje visible unificado con los otros dos conflictos de correo.
        body.Message.Should().Be("El correo utilizado ya se encuentra asociado a otra cuenta");
    }

    // HU #10625 AC3 — invitación ya no pendiente (aceptada) → 409
    [Fact]
    public async Task ResendInvitation_AsOtAdmin_InvitationAlreadyAccepted_Returns409()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var (invitationId, _) = await SeedPendingInvitationAsync(lastSentAt: null, status: "accepted");

        var response = await _client.PostAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
    // Invitación inexistente / fuera del alcance del tenant OT resuelto → 404
    [Fact]
    public async Task ResendInvitation_AsOtAdmin_InvitationNotFound_Returns404()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync(
            $"/api/v1/admin/ot/invitations/{Guid.NewGuid()}/resend", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // AC1 (HU #10627) — ot_admin cancela una invitación pendiente de su propio tenant: el
    // enlace de activación deja de funcionar y el email queda disponible para una nueva invitación.
    [Fact]
    public async Task CancelInvitation_AsOtAdmin_PendingInvitation_CancelsAndAllowsNewInvitation()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"ot-cancel-invite-{Guid.NewGuid():N}@flit.local";
        var inviteResponse = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email, fullName = "Colaborador a cancelar" },
            TestContext.Current.CancellationToken);
        inviteResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        Guid invitationId;
        await using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.AsNoTracking()
                .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
            invitationId = invitation.Id;
        }

        var cancelResponse = await _client.DeleteAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}", TestContext.Current.CancellationToken);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var cancelled = await db.UserInvitations.AsNoTracking()
                .SingleAsync(i => i.Id == invitationId, TestContext.Current.CancellationToken);
            cancelled.Status.Should().Be("cancelled");
            // ADR-0048: "cancelled" es un estado vivo y reversible, NO un soft-delete.
            cancelled.DeletedAt.Should().BeNull();
        }

        // El email queda disponible para una nueva invitación (la cancelada no cuenta como pending).
        var reInviteResponse = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email, fullName = "Colaborador a cancelar" },
            TestContext.Current.CancellationToken);
        reInviteResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // AC2 (HU #10627) — cancelar una invitación ya aceptada/cancelada previamente → 409.
    [Fact]
    public async Task CancelInvitation_AsOtAdmin_AlreadyCancelled_Returns409()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"ot-cancel-twice-{Guid.NewGuid():N}@flit.local";
        await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email, fullName = "Cancelar Dos Veces OT" },
            TestContext.Current.CancellationToken);

        Guid invitationId;
        await using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.AsNoTracking()
                .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
            invitationId = invitation.Id;
        }

        var firstCancel = await _client.DeleteAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}", TestContext.Current.CancellationToken);
        firstCancel.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var secondCancel = await _client.DeleteAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}", TestContext.Current.CancellationToken);
        secondCancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ot_admin no puede cancelar una invitación fuera de su tenant → 404 (mismo alcance que suspender).
    [Fact]
    public async Task CancelInvitation_AsOtAdmin_TargetingInvitationOutsideTenant_Returns404()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.DeleteAsync(
            $"/api/v1/admin/ot/invitations/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // HU #11552 / ADR-0048 — GET /admin/ot/users muestra la invitación cancelada con su estado
    // real (no desaparece del listado del tenant OT).
    [Fact]
    public async Task GetUsers_AsOtAdmin_ShowsCancelledInvitationWithRealStatus()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"ot-list-cancelled-{Guid.NewGuid():N}@flit.local";
        await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email, fullName = "Cancelada en el listado" },
            TestContext.Current.CancellationToken);

        Guid invitationId;
        await using (var db = CreateDbContext())
        {
            invitationId = await db.UserInvitations.AsNoTracking()
                .Where(i => i.Email == email).Select(i => i.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        (await _client.DeleteAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await _client.GetFromJsonAsync<OtUsersListResponse>(
            "/api/v1/admin/ot/users", TestContext.Current.CancellationToken);

        var row = listResponse!.Data.Single(u => u.Email == email);
        row.Status.Should().Be("cancelled");
    }

    // AC1 (HU #11552 / ADR-0048) — ot_admin reactiva una invitación cancelada de su propio
    // tenant: vuelve a "pending" con un token nuevo (el token viejo ya no resuelve nada).
    [Fact]
    public async Task ReactivateInvitation_AsOtAdmin_CancelledInvitation_ReturnsPendingWithNewToken()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"ot-reactivate-{Guid.NewGuid():N}@flit.local";
        await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email, fullName = "Reactivar OT" },
            TestContext.Current.CancellationToken);

        Guid invitationId;
        string oldTokenHash;
        await using (var db = CreateDbContext())
        {
            var invitation = await db.UserInvitations.AsNoTracking()
                .SingleAsync(i => i.Email == email, TestContext.Current.CancellationToken);
            invitationId = invitation.Id;
            oldTokenHash = invitation.TokenHash;
        }

        (await _client.DeleteAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reactivateResponse = await _client.PostAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);
        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var dbAfter = CreateDbContext();
        var reactivated = await dbAfter.UserInvitations.AsNoTracking()
            .SingleAsync(i => i.Id == invitationId, TestContext.Current.CancellationToken);
        reactivated.Status.Should().Be("pending");
        reactivated.TokenHash.Should().NotBe(oldTokenHash);
        reactivated.DeletedAt.Should().BeNull();
    }

    // AC2 — no es idempotente: reactivar una invitación que sigue "pending" → 409.
    [Fact]
    public async Task ReactivateInvitation_AsOtAdmin_InvitationStillPending_Returns409()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var email = $"ot-reactivate-pending-{Guid.NewGuid():N}@flit.local";
        await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email, fullName = "Sigue pendiente" },
            TestContext.Current.CancellationToken);

        await using var db = CreateDbContext();
        var invitationId = await db.UserInvitations.AsNoTracking()
            .Where(i => i.Email == email).Select(i => i.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        var response = await _client.PostAsync(
            $"/api/v1/admin/ot/invitations/{invitationId}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Error.Should().Be("INVITATION_NOT_CANCELLED");
    }

    // ot_admin no puede reactivar una invitación cancelada fuera de su tenant → 404.
    [Fact]
    public async Task ReactivateInvitation_AsOtAdmin_TargetingInvitationOutsideTenant_Returns404()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync(
            $"/api/v1/admin/ot/invitations/{Guid.NewGuid()}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // AdminCompanyPolicy (ruta de SecurityEndpoints) NO incluye ot_admin: la ruta de
    // AdminOtEndpoints es la única forma de que un ot_admin reactive — un ot_admin sin acceso
    // a /api/v1/security/invitations/{id}/reactivate recibe 403 ahí (AdminCompanyPolicy exige
    // AdminCompany o SuperAdmin, no ot_admin).
    [Fact]
    public async Task ReactivateInvitation_AsOtAdmin_OnSecurityRoute_Returns403()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync(
            $"/api/v1/security/invitations/{Guid.NewGuid()}/reactivate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // HU #10619 AC1 — sin endsAt: desactivación indefinida (sin fecha de fin), hasta reactivación manual.
    [Fact]
    public async Task Suspend_WithoutEndsAt_CreatesIndefiniteSuspension()
    {
        // Bloquear/desactivar es EXCLUSIVO de SuperAdmin.
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}/suspend?transitOfficeId={_transitOfficeId}",
            new { reason = "Desactivación indefinida de prueba" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var suspension = await db.UserTempSuspensions.AsNoTracking()
            .Where(s => s.UserId == _collaboratorUserId && s.TenantId == _otTenantId && s.DeletedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync(TestContext.Current.CancellationToken);

        suspension.EndsAt.Should().BeNull();
    }

    // HU #10619 AC4 — suspender/desactivar al único ot_admin activo del tenant se rechaza (409),
    // para no dejar el tenant sin ningún administrador disponible.
    [Fact]
    public async Task Suspend_TargetIsLastActiveOtAdmin_Returns409()
    {
        var soloTenantId = Guid.NewGuid();
        var soloAdminUserId = Guid.NewGuid();
        var soloTransitOfficeId = Guid.NewGuid();

        await using (var db = CreateDbContext())
        {
            db.TransitOffices.Add(new TransitOffice
            {
                Id = soloTransitOfficeId,
                Code = $"T{Guid.NewGuid():N}"[..10],
                Name = "OT solo-admin tests",
                DepartmentCode = "99",
                CityCode = "99999",
                IsActive = true,
            });
            db.Tenants.Add(new Tenant
            {
                Id = soloTenantId,
                Code = $"OT-SOLO-{Guid.NewGuid():N}"[..20],
                LegalName = "OT Solo Admin Tests",
                TaxId = TestNit.Unique(),
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Users.Add(new User
            {
                Id = soloAdminUserId,
                Email = $"solo-admin-{soloAdminUserId:N}@flit.local",
                DisplayName = "Solo AdminOT",
                Status = "active",
                HomeTenantId = soloTenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = Guid.NewGuid(),
                TenantId = soloTenantId,
                TransitOfficeId = soloTransitOfficeId,
                OperationMode = "dashboard",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = soloTenantId,
                UserId = soloAdminUserId,
                RoleId = _roleId,
                AssignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            // SuperAdmin (no es el propio objetivo) intenta suspender al único ot_admin del tenant.
            var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.PostAsJsonAsync(
                $"/api/v1/admin/ot/users/{soloAdminUserId}/suspend?transitOfficeId={soloTransitOfficeId}",
                new { reason = "Intento de dejar el tenant sin administradores" },
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        finally
        {
            // Mismo orden de dependencia FK que Dispose(): hijos primero (su propio SaveChanges),
            // padres después.
            await using var db = CreateDbContext();
            db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => a.TenantId == soloTenantId));
            db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == soloTenantId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.Users.RemoveRange(db.Users.Where(u => u.Id == soloAdminUserId));
            db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == soloTenantId));
            db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == soloTransitOfficeId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    // HU #10619 AC5 — AuthUserRepository.FindByEmailAsync (usado por LoginHandler para rechazar
    // el login, ver LoginHandlerSuspensionTests) debe reportar IsTemporarilySuspended = true
    // cuando la suspensión activa tiene EndsAt nulo (desactivación indefinida, AC1), igual que
    // con una suspensión temporal vigente. Seed/cleanup local, sin depender del factory-wide
    // Dispose(), siguiendo el mismo patrón que Suspend_TargetIsLastActiveOtAdmin_Returns409.
    [Fact]
    public async Task FindByEmailAsync_WhenSuspensionHasNullEndsAt_ReportsTemporarilySuspended()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var email = $"indefinite-suspension-{userId:N}@flit.local";

        await using (var db = CreateDbContext())
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Code = $"IND-SUSP-{Guid.NewGuid():N}"[..20],
                LegalName = "Tenant suspensión indefinida (tests)",
                TaxId = TestNit.Unique(),
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Users.Add(new User
            {
                Id = userId,
                Email = email,
                DisplayName = "Usuario desactivado indefinidamente (tests)",
                Status = "active",
                HomeTenantId = tenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            // AuthUserRepository.FindByEmailAsync exige una fila de credenciales (join
            // obligatorio); el hash no se valida en este test, solo se necesita que exista.
            db.UserCredentials.Add(new UserCredential
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PasswordHash = "not-used-in-this-test",
                MustChangePassword = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.UserTempSuspensions.Add(new UserTempSuspension
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                StartsAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                EndsAt = null,
                Reason = "HU #10619 AC5 — desactivación indefinida (test)",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            using var scope = _factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAuthUserRepository>();

            var snapshot = await repository.FindByEmailAsync(email, TestContext.Current.CancellationToken);

            snapshot.Should().NotBeNull();
            snapshot!.IsTemporarilySuspended.Should().BeTrue(
                "una suspensión sin fecha de fin (desactivación indefinida, HU #10619 AC1) debe " +
                "bloquear el login igual que una suspensión temporal vigente (AC5)");
        }
        finally
        {
            await using var db = CreateDbContext();
            db.UserTempSuspensions.RemoveRange(db.UserTempSuspensions.Where(s => s.UserId == userId));
            db.UserCredentials.RemoveRange(db.UserCredentials.Where(c => c.UserId == userId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.Users.RemoveRange(db.Users.Where(u => u.Id == userId));
            db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == tenantId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    // HU #10623 AC1 — eliminar (soft-delete reversible) a un usuario del tenant OT: 204, se marca
    // DeletedAt/DeletedBy y desaparece del listado activo (GET /users ya filtra DeletedAt == null).
    [Fact]
    public async Task DeleteUser_AsSuperAdmin_WithinScope_Returns204AndSoftDeletes()
    {
        // Eliminar es EXCLUSIVO de SuperAdmin.
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        long rowVersion;
        await using (var db = CreateDbContext())
        {
            rowVersion = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => u.RowVersion)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var response = await _client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/admin/ot/users/{_collaboratorUserId}?transitOfficeId={_transitOfficeId}")
            {
                Content = JsonContent.Create(new { rowVersion }),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        var user = await db2.Users.AsNoTracking()
            .SingleAsync(u => u.Id == _collaboratorUserId, TestContext.Current.CancellationToken);
        user.DeletedAt.Should().NotBeNull();
        user.DeletedBy.Should().Be(_superAdminUserId);

        // El listado sigue siendo accesible para ot_admin en su propio tenant.
        var otAdminToken = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otAdminToken);

        var listResponse = await _client.GetAsync("/api/v1/admin/ot/users", TestContext.Current.CancellationToken);
        var body = await listResponse.Content.ReadFromJsonAsync<ListUsersBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Data.Should().NotContain(u => u.Id == _collaboratorUserId.ToString());
    }

    // HU #10623 AC2 — un usuario no puede eliminarse a sí mismo → 400 SELF_DELETE. Eliminar es
    // ahora EXCLUSIVO de SuperAdmin (ot_admin ya no puede llamar este endpoint, ver
    // Suspend_AsOtAdmin_Returns403 / DeleteUser_AsOtAdmin_Returns403), así que la auto-eliminación
    // se reproduce con el propio SuperAdmin como caller y target (DeleteUserHandler evalúa
    // CallerId == UserId sin importar el tenant/scope resuelto).
    [Fact]
    public async Task DeleteUser_SelfDeletion_AsSuperAdmin_Returns400()
    {
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        long rowVersion;
        await using (var db = CreateDbContext())
        {
            rowVersion = await db.Users.AsNoTracking()
                .Where(u => u.Id == _superAdminUserId)
                .Select(u => u.RowVersion)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var response = await _client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/admin/ot/users/{_superAdminUserId}?transitOfficeId={_transitOfficeId}")
            {
                Content = JsonContent.Create(new { rowVersion }),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Error.Should().Be("SELF_DELETE");
    }

    // HU #10623 AC2 — eliminar al único ot_admin activo del tenant se rechaza (409), para no dejar
    // el tenant sin ningún administrador disponible (mismo criterio que Suspend_TargetIsLastActiveOtAdmin_Returns409).
    [Fact]
    public async Task DeleteUser_TargetIsLastActiveOtAdmin_Returns409()
    {
        var soloTenantId = Guid.NewGuid();
        var soloAdminUserId = Guid.NewGuid();
        var soloTransitOfficeId = Guid.NewGuid();

        await using (var db = CreateDbContext())
        {
            db.TransitOffices.Add(new TransitOffice
            {
                Id = soloTransitOfficeId,
                Code = $"T{Guid.NewGuid():N}"[..10],
                Name = "OT solo-admin delete tests",
                DepartmentCode = "99",
                CityCode = "99999",
                IsActive = true,
            });
            db.Tenants.Add(new Tenant
            {
                Id = soloTenantId,
                Code = $"OT-SOLO-DEL-{Guid.NewGuid():N}"[..20],
                LegalName = "OT Solo Admin Delete Tests",
                TaxId = TestNit.Unique(),
                TenantType = "RENTING",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Users.Add(new User
            {
                Id = soloAdminUserId,
                Email = $"solo-admin-del-{soloAdminUserId:N}@flit.local",
                DisplayName = "Solo AdminOT Delete",
                Status = "active",
                HomeTenantId = soloTenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = Guid.NewGuid(),
                TenantId = soloTenantId,
                TransitOfficeId = soloTransitOfficeId,
                OperationMode = "dashboard",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.UserRoleAssignments.Add(new UserRoleAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = soloTenantId,
                UserId = soloAdminUserId,
                RoleId = _roleId,
                AssignedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            // SuperAdmin (no es el propio objetivo) intenta eliminar al único ot_admin del tenant.
            var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _client.SendAsync(
                new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"/api/v1/admin/ot/users/{soloAdminUserId}?transitOfficeId={soloTransitOfficeId}")
                {
                    Content = JsonContent.Create(new { rowVersion = 0L }),
                },
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(
                cancellationToken: TestContext.Current.CancellationToken);
            body!.Error.Should().Be("LAST_ACTIVE_ADMIN");
        }
        finally
        {
            await using var db = CreateDbContext();
            db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => a.TenantId == soloTenantId));
            db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == soloTenantId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            db.Users.RemoveRange(db.Users.Where(u => u.Id == soloAdminUserId));
            db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == soloTenantId));
            db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == soloTransitOfficeId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    // HU #10623 AC3 — un SuperAdmin restaura a un usuario eliminado y recupera EXACTAMENTE los
    // mismos roles y el mismo estado de suspensión que tenía al momento de eliminarse (porque
    // DeleteUserHandler nunca toca UserRoleAssignment ni UserTempSuspension).
    [Fact]
    public async Task Restore_AfterDelete_RecoversExactSameRolesAndSuspensionState()
    {
        // Suspender y eliminar son EXCLUSIVOS de SuperAdmin.
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Deja al colaborador con una suspensión activa antes de eliminarlo.
        var suspendResponse = await _client.PostAsJsonAsync(
            $"/api/v1/admin/ot/users/{_collaboratorUserId}/suspend?transitOfficeId={_transitOfficeId}",
            new { reason = "HU #10623 AC3 — estado previo a eliminar", endsAt = DateTimeOffset.UtcNow.AddDays(1) },
            TestContext.Current.CancellationToken);
        suspendResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        long rowVersion;
        await using (var db = CreateDbContext())
        {
            rowVersion = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => u.RowVersion)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var deleteResponse = await _client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/admin/ot/users/{_collaboratorUserId}?transitOfficeId={_transitOfficeId}")
            {
                Content = JsonContent.Create(new { rowVersion }),
            },
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Snapshot ANTES de restaurar: roles activos + suspensión activa deben seguir intactos.
        List<Guid> roleAssignmentIdsBefore;
        List<Guid> suspensionIdsBefore;
        await using (var db = CreateDbContext())
        {
            roleAssignmentIdsBefore = await db.UserRoleAssignments.AsNoTracking()
                .Where(a => a.UserId == _collaboratorUserId && a.DeletedAt == null)
                .Select(a => a.Id)
                .ToListAsync(TestContext.Current.CancellationToken);
            suspensionIdsBefore = await db.UserTempSuspensions.AsNoTracking()
                .Where(s => s.UserId == _collaboratorUserId && s.DeletedAt == null)
                .Select(s => s.Id)
                .ToListAsync(TestContext.Current.CancellationToken);
        }
        roleAssignmentIdsBefore.Should().NotBeEmpty();
        suspensionIdsBefore.Should().NotBeEmpty();

        var superAdminToken = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var restoreResponse = await _client.PostAsync(
            $"/api/v1/superadmin/users/{_collaboratorUserId}/restore",
            content: null,
            TestContext.Current.CancellationToken);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db2 = CreateDbContext();
        var restoredUser = await db2.Users.AsNoTracking()
            .SingleAsync(u => u.Id == _collaboratorUserId, TestContext.Current.CancellationToken);
        restoredUser.DeletedAt.Should().BeNull();
        restoredUser.DeletedBy.Should().BeNull();

        var roleAssignmentIdsAfter = await db2.UserRoleAssignments.AsNoTracking()
            .Where(a => a.UserId == _collaboratorUserId && a.DeletedAt == null)
            .Select(a => a.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        var suspensionIdsAfter = await db2.UserTempSuspensions.AsNoTracking()
            .Where(s => s.UserId == _collaboratorUserId && s.DeletedAt == null)
            .Select(s => s.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        roleAssignmentIdsAfter.Should().BeEquivalentTo(roleAssignmentIdsBefore,
            "AC3: eliminar/restaurar nunca toca UserRoleAssignment");
        suspensionIdsAfter.Should().BeEquivalentTo(suspensionIdsBefore,
            "AC3: eliminar/restaurar nunca toca UserTempSuspension");
    }

    // HU #10623 AC5 — restaurar un usuario que NO está eliminado se rechaza explícitamente (409),
    // no es un no-op silencioso.
    [Fact]
    public async Task Restore_WhenUserIsNotDeleted_Returns409()
    {
        var token = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync(
            $"/api/v1/superadmin/users/{_collaboratorUserId}/restore",
            content: null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<SuperAdminErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Code.Should().Be("USER_NOT_DELETED");
    }

    // HU #10623 AC4 — invitar de nuevo con el correo de un usuario eliminado recibe un mensaje
    // claro (no un error crudo de constraint de BD).
    [Fact]
    public async Task InviteUser_WithEmailOfDeletedAccount_Returns409EmailBelongsToDeletedUser()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        string collaboratorEmail;
        long rowVersion;
        await using (var db = CreateDbContext())
        {
            var target = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => new { u.Email, u.RowVersion })
                .SingleAsync(TestContext.Current.CancellationToken);
            collaboratorEmail = target.Email;
            rowVersion = target.RowVersion;
        }

        // Eliminar es EXCLUSIVO de SuperAdmin: el paso de preparación (borrar al colaborador)
        // ya no puede hacerlo ot_admin.
        var superAdminToken = MintToken("SuperAdmin", Guid.NewGuid(), _superAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var deleteResponse = await _client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/admin/ot/users/{_collaboratorUserId}?transitOfficeId={_transitOfficeId}")
            {
                Content = JsonContent.Create(new { rowVersion }),
            },
            TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Reinvitar sigue siendo tarea de ot_admin en su propio tenant.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var inviteResponse = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email = collaboratorEmail, fullName = "Reintento de invitación" },
            TestContext.Current.CancellationToken);

        inviteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await inviteResponse.Content.ReadFromJsonAsync<ErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Error.Should().Be("EMAIL_BELONGS_TO_DELETED_USER");
        // HU #11550 AC3/AC4 — mensaje visible unificado con los otros dos conflictos de correo.
        body.Message.Should().Be("El correo utilizado ya se encuentra asociado a otra cuenta");
    }

    // HU #11550 AC1/AC4 — invitar por la ruta del OT con un correo que YA tiene una invitación
    // pendiente en el tenant debe mostrar el mismo mensaje unificado que los otros dos
    // conflictos de correo, conservando su propio código de error.
    [Fact]
    public async Task InviteUser_AsOtAdmin_EmailWithPendingInvitation_Returns409WithUnifiedMessage()
    {
        var (_, pendingEmail) = await SeedPendingInvitationAsync(lastSentAt: null);

        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var inviteResponse = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email = pendingEmail, fullName = "Invitación duplicada" },
            TestContext.Current.CancellationToken);

        inviteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await inviteResponse.Content.ReadFromJsonAsync<ErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Error.Should().Be("INVITATION_ALREADY_PENDING");
        body.Message.Should().Be("El correo utilizado ya se encuentra asociado a otra cuenta");
    }

    // HU #11550 AC2/AC4 — invitar por la ruta del OT con el correo de una cuenta ACTIVA debe
    // mostrar el mismo mensaje unificado, conservando su propio código de error.
    [Fact]
    public async Task InviteUser_AsOtAdmin_EmailOfActiveAccount_Returns409WithUnifiedMessage()
    {
        var token = MintToken("ot_admin", _otTenantId, _otAdminUserId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        string collaboratorEmail;
        await using (var db = CreateDbContext())
        {
            collaboratorEmail = await db.Users.AsNoTracking()
                .Where(u => u.Id == _collaboratorUserId)
                .Select(u => u.Email)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var inviteResponse = await _client.PostAsJsonAsync(
            "/api/v1/admin/ot/users/invite",
            new { email = collaboratorEmail, fullName = "Ya tiene cuenta activa" },
            TestContext.Current.CancellationToken);

        inviteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await inviteResponse.Content.ReadFromJsonAsync<ErrorBody>(
            cancellationToken: TestContext.Current.CancellationToken);
        body!.Error.Should().Be("USER_ALREADY_EXISTS");
        body.Message.Should().Be("El correo utilizado ya se encuentra asociado a otra cuenta");
    }

    private async Task<(Guid InvitationId, string Email)> SeedPendingInvitationAsync(
        DateTimeOffset? lastSentAt, string status = "pending")
    {
        await using var db = CreateDbContext();

        var invitationId = Guid.NewGuid();
        var email = $"resend-{Guid.NewGuid():N}@flit.local";

        db.UserInvitations.Add(new UserInvitation
        {
            Id = invitationId,
            TenantId = _otTenantId,
            Email = email,
            FullName = "Invitado Reenvío",
            RoleId = _roleId,
            TokenHash = $"hash-{Guid.NewGuid():N}",
            Status = status,
            InvitedBy = _otAdminUserId,
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

        db.TransitOffices.Add(new TransitOffice
        {
            Id = _transitOfficeId,
            Code = $"T{Guid.NewGuid():N}"[..10],
            Name = "OT self-service tests",
            DepartmentCode = "99",
            CityCode = "99999",
            IsActive = true,
        });

        db.Tenants.Add(new Tenant
        {
            Id = _otTenantId,
            Code = $"OT-TEST-{Guid.NewGuid():N}"[..20],
            LegalName = "OT Self-Service Tests",
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
            // HomeTenantId != null: UserManagementRepository.FindTargetAsync (compartida por
            // Suspend/Unsuspend/DeleteUser) exige un tenant para resolver al usuario objetivo —
            // necesario para reproducir la auto-eliminación del propio SuperAdmin (ver
            // DeleteUser_SelfDeletion_AsSuperAdmin_Returns400). El rol SuperAdmin viaja por el
            // claim del JWT, no por este HomeTenantId; el resto de tests solo lo usan como caller.
            HomeTenantId = _otTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Tenant + Users primero (sin FK entre sí): el perfil OT depende del tenant recién
        // insertado, y no hay navegación EF entre estos agregados que le permita a
        // SaveChanges inferir el orden de inserción por sí solo.
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // HU #10505 / ADR-0023: security.roles es un catálogo GLOBAL (sin tenant_id) — se
        // reutiliza la fila "ot_admin" si ya existe (p.ej. sembrada por DevelopmentAuthSeeder)
        // o se crea aquí mismo si no (BD limpia, como la que usa CI): el test no puede depender
        // de que el seeder de desarrollo haya corrido antes, solo de que exista una única fila
        // global (violaría UNIQUE(code, target_entity_type) crear una por tenant de prueba).
        var existingOtAdminRole = await db.Roles.AsNoTracking()
            .Where(r => r.Code == "ot_admin" && r.TargetEntityType == "TRANSIT_OFFICE" && r.DeletedAt == null)
            .Select(r => r.Id)
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);

        if (existingOtAdminRole == Guid.Empty)
        {
            var newRole = new Role
            {
                Id = Guid.NewGuid(),
                Code = "ot_admin",
                Name = "Administrador OT",
                TargetEntityType = "TRANSIT_OFFICE",
                IsSystem = true,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Roles.Add(newRole);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            existingOtAdminRole = newRole.Id;
        }

        _roleId = existingOtAdminRole;

        db.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            TransitOfficeId = _transitOfficeId,
            OperationMode = "dashboard",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _otAdminUserId,
            Email = $"otadmin-{_otAdminUserId:N}@flit.local",
            DisplayName = "AdminOT de prueba",
            Status = "active",
            HomeTenantId = _otTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Users.Add(new User
        {
            Id = _collaboratorUserId,
            Email = $"colaborador-{_collaboratorUserId:N}@flit.local",
            DisplayName = "Colaborador de prueba",
            Status = "active",
            HomeTenantId = _otTenantId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // HU #10619 AC4 — la guarda de "último administrador activo" exige que exista un rol
        // ot_admin ACTIVO persistido (no solo el claim del JWT) para que suspender al
        // colaborador no lo deje como único ot_admin del tenant.
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            UserId = _otAdminUserId,
            RoleId = _roleId,
            AssignedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = _otTenantId,
            UserId = _collaboratorUserId,
            RoleId = _roleId,
            AssignedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private FlitDbContext CreateDbContext() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

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
    /// Limpieza en orden de dependencia FK (RESTRICT en varias tablas de <c>security</c>
    /// hacia <c>identity.tenants</c>/<c>identity.users</c>/<c>security.roles</c>): cada
    /// etapa hace su propio <c>SaveChanges</c> porque, sin navegaciones EF entre estos
    /// agregados, el batching no puede inferir el orden correcto de borrado.
    /// </summary>
    public void Dispose()
    {
        using var db = CreateDbContext();

        db.UserRoleAssignments.RemoveRange(db.UserRoleAssignments.Where(a => a.TenantId == _otTenantId));
        db.UserTempSuspensions.RemoveRange(db.UserTempSuspensions.Where(s => s.TenantId == _otTenantId));
        db.UserInvitations.RemoveRange(db.UserInvitations.Where(i => i.TenantId == _otTenantId));
        db.TransitOfficeProfiles.RemoveRange(db.TransitOfficeProfiles.Where(p => p.TenantId == _otTenantId));
        db.SaveChanges();

        // HU #10505: "ot_admin" es un rol del catálogo GLOBAL, sembrado una sola vez por
        // DevelopmentAuthSeeder — este test NO lo crea ni lo borra, solo lo resuelve.

        db.Users.RemoveRange(db.Users.Where(u =>
            u.Id == _superAdminUserId || u.Id == _otAdminUserId || u.Id == _collaboratorUserId));
        db.Tenants.RemoveRange(db.Tenants.Where(t => t.Id == _otTenantId));
        db.TransitOffices.RemoveRange(db.TransitOffices.Where(o => o.Id == _transitOfficeId));
        db.SaveChanges();
    }

    private sealed record ListUsersBody(List<ListUserItem> Data);

    private sealed record ListUserItem(string Id, string FullName, string Email, string Status);

    // HU #10624 — respuesta de GET /api/v1/admin/ot/users?onlyDeleted=true (OtUserDto con deletedAt).
    private sealed record DeletedUsersBody(List<DeletedUserItem> Data);

    private sealed record DeletedUserItem(string Id, string FullName, string Email, string Status, DateTimeOffset? DeletedAt);

    // HU #10624 — respuesta de GET /api/v1/security/users?onlyDeleted=true (TenantUserDto con
    // tenantId/deletedAt); ese endpoint devuelve el arreglo directamente (sin envolver en "data").
    private sealed record SecurityDeletedUserItem(string Id, string FullName, string Email, string? TenantId, DateTimeOffset? DeletedAt);

    private sealed record ErrorBody(string Error, string? Message);

    // SecurityUsersEndpoints (SuperAdmin) usa "code" en vez de "error" para el campo de código.
    private sealed record SuperAdminErrorBody(string Code, string? Message);

    // GET /admin/ot/users envuelve el arreglo en "data" — proyección mínima para leer Status
    // real de invitaciones (HU #11552).
    private sealed record OtUsersListResponse(List<OtUserRow> Data);

    private sealed record OtUserRow(string Id, string FullName, string Email, string Status);
}
