using Flit.Modules.Security.Application.Modules;
using Flit.Modules.Security.Domain.Modules;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Modules;

public sealed class DeleteModuleHandlerTests
{
    private readonly ISecurityModuleRepository _repo = Substitute.For<ISecurityModuleRepository>();
    private readonly DeleteModuleHandler _handler;

    private static readonly Guid ModuleId = Guid.NewGuid();
    private static readonly SecurityModuleDetail ExistingModule =
        new(ModuleId, "VEHICLES", "Vehículos", null, 1, true, null);

    public DeleteModuleHandlerTests()
    {
        _handler = new DeleteModuleHandler(_repo);
    }

    // AC1 — módulo sin permisos activos → soft delete exitoso
    [Fact]
    public async Task HandleAsync_ModuleWithoutActivePermissions_SoftDeletesSuccessfully()
    {
        _repo.GetByIdAsync(ModuleId, Arg.Any<CancellationToken>()).Returns(ExistingModule);
        _repo.HasActivePermissionsAsync(ModuleId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler.Invoking(h => h.HandleAsync(ModuleId, CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteAsync(ModuleId, Arg.Any<CancellationToken>());
    }

    // AC2 — módulo con permisos activos → ModuleHasActivePermissionsException
    [Fact]
    public async Task HandleAsync_ModuleWithActivePermissions_ThrowsModuleHasActivePermissions()
    {
        _repo.GetByIdAsync(ModuleId, Arg.Any<CancellationToken>()).Returns(ExistingModule);
        _repo.HasActivePermissionsAsync(ModuleId, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(ModuleId, CancellationToken.None))
            .Should().ThrowAsync<ModuleHasActivePermissionsException>();

        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // AC3 — módulo no encontrado → ModuleNotFoundException
    [Fact]
    public async Task HandleAsync_ModuleNotFound_ThrowsModuleNotFoundException()
    {
        _repo.GetByIdAsync(ModuleId, Arg.Any<CancellationToken>()).Returns((SecurityModuleDetail?)null);

        await _handler
            .Invoking(h => h.HandleAsync(ModuleId, CancellationToken.None))
            .Should().ThrowAsync<ModuleNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().HasActivePermissionsAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
