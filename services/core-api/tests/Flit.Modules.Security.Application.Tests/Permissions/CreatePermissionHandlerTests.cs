using Flit.Modules.Security.Application.Permissions;
using Flit.Modules.Security.Domain.Permissions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Permissions;

public sealed class CreatePermissionHandlerTests
{
    private readonly IPermissionRepository _repo = Substitute.For<IPermissionRepository>();
    private readonly CreatePermissionHandler _handler;

    private static readonly Guid ModuleId = Guid.NewGuid();
    private static readonly Guid PermissionId = Guid.NewGuid();

    private const string Slug = "vehicles.read";
    private const string Name = "Leer vehículos";
    private const string Action = "READ";
    private const string HttpMethod = "GET";
    private const string RoutePattern = "/api/v1/vehicles";

    public CreatePermissionHandlerTests()
    {
        _handler = new CreatePermissionHandler(_repo);
        _repo.CreateAsync(Arg.Any<CreatePermissionData>(), Arg.Any<CancellationToken>())
            .Returns(PermissionId);
    }

    // AC1 — datos válidos, slug único, módulo activo → crea permiso y retorna Guid
    [Fact]
    public async Task HandleAsync_ValidData_CreatesPermission()
    {
        _repo.ModuleExistsAndIsActiveAsync(ModuleId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.SlugExistsAsync(Slug, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreatePermissionCommand(ModuleId, Slug, Name, Action, HttpMethod, RoutePattern, null),
            CancellationToken.None);

        result.Should().Be(PermissionId);
        await _repo.Received(1).CreateAsync(
            Arg.Is<CreatePermissionData>(d =>
                d.ModuleId == ModuleId &&
                d.Slug == Slug &&
                d.Name == Name &&
                d.Action == Action &&
                d.HttpMethod == HttpMethod &&
                d.RoutePattern == RoutePattern),
            Arg.Any<CancellationToken>());
    }

    // AC2 — slug duplicado → PermissionSlugDuplicateException, sin llamar a CreateAsync
    [Fact]
    public async Task HandleAsync_DuplicateSlug_ThrowsPermissionSlugDuplicate()
    {
        _repo.ModuleExistsAndIsActiveAsync(ModuleId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.SlugExistsAsync(Slug, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreatePermissionCommand(ModuleId, Slug, Name, Action, HttpMethod, RoutePattern, null),
                CancellationToken.None))
            .Should().ThrowAsync<PermissionSlugDuplicateException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<CreatePermissionData>(),
            Arg.Any<CancellationToken>());
    }

    // AC3 — módulo inactivo o inexistente → ModuleInactiveException
    [Fact]
    public async Task HandleAsync_InactiveModule_ThrowsModuleInactive()
    {
        _repo.ModuleExistsAndIsActiveAsync(ModuleId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreatePermissionCommand(ModuleId, Slug, Name, Action, HttpMethod, RoutePattern, null),
                CancellationToken.None))
            .Should().ThrowAsync<ModuleInactiveException>();

        await _repo.DidNotReceiveWithAnyArgs().SlugExistsAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<CreatePermissionData>(),
            Arg.Any<CancellationToken>());
    }

    // AC4 — action="CUSTOM" válido → crea permiso correctamente
    [Fact]
    public async Task HandleAsync_CustomAction_CreatesPermission()
    {
        const string customAction = "CUSTOM";
        _repo.ModuleExistsAndIsActiveAsync(ModuleId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.SlugExistsAsync(Slug, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreatePermissionCommand(ModuleId, Slug, Name, customAction, HttpMethod, RoutePattern, "Descripción opcional"),
            CancellationToken.None);

        result.Should().Be(PermissionId);
        await _repo.Received(1).CreateAsync(
            Arg.Is<CreatePermissionData>(d =>
                d.Action == customAction &&
                d.Description == "Descripción opcional"),
            Arg.Any<CancellationToken>());
    }
}
