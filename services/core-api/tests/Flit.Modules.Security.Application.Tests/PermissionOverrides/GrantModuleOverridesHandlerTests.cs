using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.PermissionOverrides;

/// <summary>HU #13642 AC2 — otorgar un módulo completo es una FOTO de sus acciones actuales.</summary>
public sealed class GrantModuleOverridesHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private readonly IUserPermissionOverrideRepository _repo = Substitute.For<IUserPermissionOverrideRepository>();

    private static ModuleActionsSnapshot Module(params string[] slugs) =>
        new("ict", "tramites", slugs.Select(s => new ModuleActionRef(Guid.NewGuid(), s)).ToList());

    private GrantModuleOverridesHandler Handler(ModuleActionsSnapshot? module, params UserPermissionOverride[] existing)
    {
        _repo.GetModuleActionsAsync("ict", Arg.Any<CancellationToken>()).Returns(module);
        _repo.ListActiveAsync(Tenant, User, "tramites", Arg.Any<CancellationToken>()).Returns(existing);
        return new GrantModuleOverridesHandler(_repo);
    }

    [Fact]
    public async Task Un_modulo_con_3_acciones_crea_3_permisos_individuales_y_3_filas_de_bitacora()
    {
        var module = Module("ict.a", "ict.b", "ict.c");
        var result = await Handler(module).HandleAsync(new GrantModuleOverridesCommand(Tenant, User, Actor, "ict", "allow"), TestContext.Current.CancellationToken);

        result.Created.Should().Be(3);
        await _repo.Received(1).AddAsync(
            Tenant,
            Arg.Is<IReadOnlyList<UserPermissionOverride>>(o => o.Count == 3
                && o.All(x => x.Effect == "allow" && x.Source == "MODULE_SNAPSHOT" && x.BatchId == result.BatchId
                    && x.ProductCode == "tramites" && x.GrantedBy == Actor)
                && o.Select(x => x.PermissionId).ToHashSet().SetEquals(module.Actions.Select(a => a.PermissionId))),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(a => a.Count == 3 && a.All(x => x.Action == "MODULE_SNAPSHOT" && x.Outcome == "OK" && x.ValueAfter == "allow")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Las_acciones_que_ya_tienen_override_activo_se_conservan_y_se_cuentan_como_omitidas()
    {
        var module = Module("ict.a", "ict.b");
        var existente = new UserPermissionOverride(Guid.NewGuid(), Tenant, User, "tramites", module.Actions[0].PermissionId, "deny", "INDIVIDUAL", null, Actor);

        var result = await Handler(module, existente).HandleAsync(new GrantModuleOverridesCommand(Tenant, User, Actor, "ict", "allow"), TestContext.Current.CancellationToken);

        result.Created.Should().Be(1);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task Modulo_inexistente_o_efecto_invalido_se_rechazan_sin_escribir()
    {
        var sinModulo = async () => await Handler(null).HandleAsync(new GrantModuleOverridesCommand(Tenant, User, Actor, "ict", "allow"), TestContext.Current.CancellationToken);
        await sinModulo.Should().ThrowAsync<ModuleNotFoundForOverrideException>();

        var efecto = async () => await Handler(Module("ict.a")).HandleAsync(new GrantModuleOverridesCommand(Tenant, User, Actor, "ict", "maybe"), TestContext.Current.CancellationToken);
        await efecto.Should().ThrowAsync<ArgumentException>();

        await _repo.DidNotReceiveWithAnyArgs().AddAsync(default, default!, default!, TestContext.Current.CancellationToken);
    }
}
