using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

public sealed class CreateRoleHandlerTests
{
    private readonly IRoleRepository _repo = Substitute.For<IRoleRepository>();
    private readonly CreateRoleHandler _handler;

    private const string TargetEntityType = "COMPANY";
    private static readonly Guid RoleId = Guid.NewGuid();

    private const string Code = "ADMIN";
    private const string Name = "Administrador";

    public CreateRoleHandlerTests()
    {
        _handler = new CreateRoleHandler(_repo);
        _repo.CreateAsync(Arg.Any<CreateRoleData>(), Arg.Any<CancellationToken>())
            .Returns(RoleId);
    }

    // AC1 — datos válidos, code único en el targetEntityType → crea rol y retorna Guid
    [Fact]
    public async Task HandleAsync_ValidData_CreatesRole()
    {
        _repo.CodeExistsAsync(TargetEntityType, Code, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateRoleCommand(TargetEntityType, Code, Name, null),
            CancellationToken.None);

        result.Should().Be(RoleId);
        await _repo.Received(1).CreateAsync(
            Arg.Is<CreateRoleData>(d =>
                d.TargetEntityType == TargetEntityType &&
                d.Code == Code &&
                d.Name == Name &&
                d.Description == null),
            Arg.Any<CancellationToken>());
    }

    // AC6 — code duplicado en el mismo targetEntityType → RoleCodeDuplicateException, sin llamar a CreateAsync
    [Fact]
    public async Task HandleAsync_DuplicateCode_ThrowsRoleCodeDuplicate()
    {
        _repo.CodeExistsAsync(TargetEntityType, Code, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreateRoleCommand(TargetEntityType, Code, Name, null),
                CancellationToken.None))
            .Should().ThrowAsync<RoleCodeDuplicateException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<CreateRoleData>(),
            Arg.Any<CancellationToken>());
    }

    // HU #10505 — mismo code en un targetEntityType distinto → no es duplicado, crea el rol
    [Fact]
    public async Task HandleAsync_SameCode_DifferentTargetEntityType_CreatesRole()
    {
        const string otherTargetEntityType = "TRANSIT_OFFICE";
        _repo.CodeExistsAsync(TargetEntityType, Code, Arg.Any<CancellationToken>()).Returns(true);
        _repo.CodeExistsAsync(otherTargetEntityType, Code, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateRoleCommand(otherTargetEntityType, Code, Name, null),
            CancellationToken.None);

        result.Should().Be(RoleId);
        await _repo.Received(1).CreateAsync(
            Arg.Is<CreateRoleData>(d => d.TargetEntityType == otherTargetEntityType && d.Code == Code),
            Arg.Any<CancellationToken>());
    }
}
