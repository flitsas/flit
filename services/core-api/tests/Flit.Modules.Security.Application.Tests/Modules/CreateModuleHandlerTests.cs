using Flit.Modules.Security.Application.Modules;
using Flit.Modules.Security.Domain.Modules;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Modules;

public sealed class CreateModuleHandlerTests
{
    private readonly ISecurityModuleRepository _repo = Substitute.For<ISecurityModuleRepository>();
    private readonly CreateModuleHandler _handler;

    private static readonly Guid ModuleId = Guid.NewGuid();
    private const string Code = "VEHICLES";
    private const string Name = "Vehículos";

    public CreateModuleHandlerTests()
    {
        _handler = new CreateModuleHandler(_repo);
        _repo.CreateAsync(Arg.Any<SecurityModuleData>(), Arg.Any<CancellationToken>())
            .Returns(ModuleId);
    }

    // AC1 — código único → crea módulo y retorna Guid
    [Fact]
    public async Task HandleAsync_UniqueCode_CreatesModuleAndReturnsId()
    {
        _repo.CodeExistsAsync(Code, null, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateModuleCommand(Code, Name, "Módulo de vehículos", (short)1),
            CancellationToken.None);

        result.Should().Be(ModuleId);
        await _repo.Received(1).CreateAsync(
            Arg.Is<SecurityModuleData>(d =>
                d.Code == Code &&
                d.Name == Name &&
                d.Description == "Módulo de vehículos" &&
                d.SortOrder == 1),
            Arg.Any<CancellationToken>());
    }

    // AC1 — sin descripción → crea módulo con Description null
    [Fact]
    public async Task HandleAsync_NullDescription_CreatesModuleWithNullDescription()
    {
        _repo.CodeExistsAsync(Code, null, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(
            new CreateModuleCommand(Code, Name, null, (short)1),
            CancellationToken.None);

        result.Should().Be(ModuleId);
        await _repo.Received(1).CreateAsync(
            Arg.Is<SecurityModuleData>(d => d.Description == null),
            Arg.Any<CancellationToken>());
    }

    // AC2 — código duplicado → ModuleCodeDuplicateException, sin llamar a CreateAsync
    [Fact]
    public async Task HandleAsync_DuplicateCode_ThrowsModuleCodeDuplicate()
    {
        _repo.CodeExistsAsync(Code, null, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(
                new CreateModuleCommand(Code, Name, null, (short)1),
                CancellationToken.None))
            .Should().ThrowAsync<ModuleCodeDuplicateException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<SecurityModuleData>(),
            Arg.Any<CancellationToken>());
    }
}
