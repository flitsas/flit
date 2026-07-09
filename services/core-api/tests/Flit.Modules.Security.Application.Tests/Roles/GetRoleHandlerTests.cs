using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

public sealed class GetRoleHandlerTests
{
    private readonly IRoleRepository _repo = Substitute.For<IRoleRepository>();
    private readonly GetRoleHandler _handler;

    private static readonly Guid RoleId = Guid.NewGuid();

    public GetRoleHandlerTests()
    {
        _handler = new GetRoleHandler(_repo);
    }

    [Fact]
    public async Task HandleAsync_ExistingRole_ReturnsDetail()
    {
        var detail = new RoleDetail(RoleId, "COMPANY", "ADMIN", "Administrador", null, false, true, []);
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await _handler.HandleAsync(RoleId, CancellationToken.None);

        result.Should().Be(detail);
    }

    [Fact]
    public async Task HandleAsync_RoleNotFound_ThrowsRoleNotFound()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns((RoleDetail?)null);

        await _handler
            .Invoking(h => h.HandleAsync(RoleId, CancellationToken.None))
            .Should().ThrowAsync<RoleNotFoundException>();
    }
}
