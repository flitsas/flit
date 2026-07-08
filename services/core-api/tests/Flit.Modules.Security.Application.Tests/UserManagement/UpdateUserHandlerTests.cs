using Flit.Modules.Security.Application.UserManagement.UpdateUser;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserManagement;

/// <summary>Tests de <see cref="UpdateUserHandler"/> — HU #10621 (editar nombre/correo de un usuario).</summary>
public sealed class UpdateUserHandlerTests
{
    private readonly IUserManagementRepository _repo = Substitute.For<IUserManagementRepository>();
    private readonly UpdateUserHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    private static readonly UserManagementTarget DefaultTarget = new(
        UserId, TenantId, "old.email@flit.co", "Nombre Viejo", DeletedAt: null, RowVersion: 3);

    public UpdateUserHandlerTests()
    {
        _handler = new UpdateUserHandler(_repo);
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>()).Returns(DefaultTarget);
    }

    private static UpdateUserCommand MakeCommand(
        string? displayName = "Nombre Nuevo",
        string? email = "nuevo.email@flit.co",
        long rowVersion = 3,
        bool callerIsSuperAdmin = false) =>
        new(TenantId, UserId, displayName, email, rowVersion, CallerId, callerIsSuperAdmin);

    // AC1 — nombre y correo válidos y distintos del actual → se persisten.
    [Fact]
    public async Task HandleAsync_WithValidNameAndEmail_PersistsChanges()
    {
        _repo.FindByEmailIncludingDeletedAsync("nuevo.email@flit.co", Arg.Any<CancellationToken>())
            .Returns((ExistingUserByEmail?)null);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).UpdateProfileAsync(
            UserId, "Nombre Nuevo", "nuevo.email@flit.co", 3, Arg.Any<DateTimeOffset>(), CallerId,
            Arg.Any<CancellationToken>());
    }

    // AC1 — solo cambia el nombre, el correo se omite: no se valida unicidad y se envía email=null
    // (no tocar ese campo).
    [Fact]
    public async Task HandleAsync_WithOnlyDisplayName_DoesNotCheckEmailUniqueness()
    {
        var command = MakeCommand(displayName: "Solo Nombre", email: null);

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.DidNotReceiveWithAnyArgs().FindByEmailIncludingDeletedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).UpdateProfileAsync(
            UserId, "Solo Nombre", null, 3, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>());
    }

    // Reenviar el mismo correo actual (distinto casing) no dispara la validación de unicidad ni
    // se marca como cambio.
    [Fact]
    public async Task HandleAsync_WhenEmailUnchangedIgnoringCase_DoesNotCheckUniqueness()
    {
        var command = MakeCommand(displayName: null, email: "OLD.EMAIL@FLIT.CO");

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.DidNotReceiveWithAnyArgs().FindByEmailIncludingDeletedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).UpdateProfileAsync(
            UserId, null, null, 3, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>());
    }

    // AC2 — el correo ya pertenece a otra cuenta ACTIVA → USER_ALREADY_EXISTS (409 en el endpoint).
    [Fact]
    public async Task HandleAsync_WhenEmailBelongsToAnotherActiveUser_ThrowsUserAlreadyExists()
    {
        _repo.FindByEmailIncludingDeletedAsync("nuevo.email@flit.co", Arg.Any<CancellationToken>())
            .Returns(new ExistingUserByEmail(Guid.NewGuid(), IsDeleted: false));

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<UserAlreadyExistsException>();

        await _repo.DidNotReceiveWithAnyArgs().UpdateProfileAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // AC3 — el correo pertenece a una cuenta soft-deleted → error específico, no el genérico de "ya existe".
    [Fact]
    public async Task HandleAsync_WhenEmailBelongsToDeletedAccount_ThrowsEmailBelongsToDeletedAccount()
    {
        _repo.FindByEmailIncludingDeletedAsync("nuevo.email@flit.co", Arg.Any<CancellationToken>())
            .Returns(new ExistingUserByEmail(Guid.NewGuid(), IsDeleted: true));

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<UserEmailBelongsToDeletedAccountException>();

        await _repo.DidNotReceiveWithAnyArgs().UpdateProfileAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // AC4 — rowVersion desactualizado: el repositorio detecta la concurrencia optimista y lanza
    // UserProfileConcurrencyException; el handler la propaga TAL CUAL (el endpoint la mapea a 409).
    [Fact]
    public async Task HandleAsync_WhenRowVersionIsStale_PropagatesConcurrencyException()
    {
        _repo.FindByEmailIncludingDeletedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExistingUserByEmail?)null);
        _repo.UpdateProfileAsync(
                UserId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserProfileConcurrencyException());

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(rowVersion: 1), CancellationToken.None))
            .Should().ThrowAsync<UserProfileConcurrencyException>();
    }

    // AC5 — el handler no invalida sesiones activas: su ÚNICA dependencia es
    // IUserManagementRepository (no hay repositorio/servicio de sesiones/tokens involucrado), así
    // que un guardado exitoso no puede tener ese efecto secundario. Documenta la decisión de
    // diseño explícita del alcance (solo afecta el próximo login).
    [Fact]
    public async Task HandleAsync_OnSuccessfulEmailChange_HasNoSessionInvalidationSideEffect()
    {
        _repo.FindByEmailIncludingDeletedAsync("nuevo.email@flit.co", Arg.Any<CancellationToken>())
            .Returns((ExistingUserByEmail?)null);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        // Única interacción posterior a la resolución del target: FindByEmail + UpdateProfile.
        await _repo.Received(1).FindByEmailIncludingDeletedAsync("nuevo.email@flit.co", Arg.Any<CancellationToken>());
        await _repo.Received(1).UpdateProfileAsync(
            UserId, "Nombre Nuevo", "nuevo.email@flit.co", 3, Arg.Any<DateTimeOffset>(), CallerId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTargetNotFound_ThrowsTargetUserNotFound()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>()).Returns((UserManagementTarget?)null);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<TargetUserNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().FindByEmailIncludingDeletedAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTargetBelongsToAnotherTenantAndCallerIsNotSuperAdmin_ThrowsOutOfScope()
    {
        var otherTenantTarget = DefaultTarget with { TenantId = OtherTenantId };
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>()).Returns(otherTenantTarget);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(callerIsSuperAdmin: false), CancellationToken.None))
            .Should().ThrowAsync<UserOutOfScopeException>();

        await _repo.DidNotReceiveWithAnyArgs().UpdateProfileAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTargetBelongsToAnotherTenantButCallerIsSuperAdmin_Succeeds()
    {
        var otherTenantTarget = DefaultTarget with { TenantId = OtherTenantId };
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>()).Returns(otherTenantTarget);
        _repo.FindByEmailIncludingDeletedAsync("nuevo.email@flit.co", Arg.Any<CancellationToken>())
            .Returns((ExistingUserByEmail?)null);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(callerIsSuperAdmin: true), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).UpdateProfileAsync(
            UserId, "Nombre Nuevo", "nuevo.email@flit.co", 3, Arg.Any<DateTimeOffset>(), CallerId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDisplayNameIsBlank_TreatsItAsNoChange()
    {
        var command = MakeCommand(displayName: "   ", email: null);

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).UpdateProfileAsync(
            UserId, null, null, 3, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>());
    }
}
