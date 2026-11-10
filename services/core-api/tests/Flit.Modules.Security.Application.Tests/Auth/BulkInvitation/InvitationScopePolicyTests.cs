using Flit.Modules.Security.Application.Auth.BulkInvitation;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth.BulkInvitation;

/// <summary>HU #14421 — política única de alcance (fail-closed) para invitación masiva.</summary>
public sealed class InvitationScopePolicyTests
{
    private readonly IInvitationTargetDirectory _directory = Substitute.For<IInvitationTargetDirectory>();
    private readonly InvitationScopePolicy _sut;

    private static readonly Guid InviterUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtTenant = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RoleRadicador = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RoleSuperAdmin = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid RoleOtAdmin = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public InvitationScopePolicyTests()
    {
        _sut = new InvitationScopePolicy(_directory);
    }

    [Fact]
    public async Task ResolveAsync_AdminCompany_OwnCompany_ExcludesSuperAdminAndMarksAssignableRadicador()
    {
        _directory.GetTargetAsync(CompanyTenant, Arg.Any<CancellationToken>())
            .Returns(new InvitationTargetInfo(true, InvitationTargetKind.Company));
        _directory.ListVisibleRolesAsync(CompanyTenant, Arg.Any<CancellationToken>())
            .Returns([
                new InvitationRoleInfo(RoleRadicador, "Radicador", "Radicador", "COMPANY", null),
                new InvitationRoleInfo(RoleSuperAdmin, "SuperAdmin", "Super Administrador", "COMPANY", null),
            ]);

        var decision = await _sut.ResolveAsync(
            new InvitationScopeContext(InviterUser, CompanyTenant, ["AdminCompany"], CompanyTenant, InvitationChannel.Security),
            CancellationToken.None);

        decision.Allowed.Should().BeTrue();
        decision.AssignableRoles.Should().ContainSingle(r => r.Code == "Radicador");
        decision.Roles.Should().Contain(r =>
            r.Code == "SuperAdmin" && !r.Assignable && r.DenyReason == InvitationScopeReasons.SuperAdminRoleNotAssignable);
    }

    [Fact]
    public async Task ResolveAsync_AdminCompany_OtherTenant_DeniedOutOfScope()
    {
        var decision = await _sut.ResolveAsync(
            new InvitationScopeContext(InviterUser, CompanyTenant, ["AdminCompany"], OtherTenant, InvitationChannel.Security),
            CancellationToken.None);

        decision.Allowed.Should().BeFalse();
        decision.DenyReason.Should().Be(InvitationScopeReasons.TargetTenantOutOfScope);
        await _directory.DidNotReceive().GetTargetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_SuperAdmin_OwnTenant_DeniedFlitProfileOutOfV1()
    {
        var decision = await _sut.ResolveAsync(
            new InvitationScopeContext(InviterUser, CompanyTenant, ["SuperAdmin"], CompanyTenant, InvitationChannel.Security),
            CancellationToken.None);

        decision.Allowed.Should().BeFalse();
        decision.DenyReason.Should().Be(InvitationScopeReasons.CannotInviteToOwnTenant);
    }

    [Fact]
    public async Task ResolveAsync_GestorOt_CannotAssignOtAdmin()
    {
        _directory.GetTargetAsync(OtTenant, Arg.Any<CancellationToken>())
            .Returns(new InvitationTargetInfo(true, InvitationTargetKind.TransitOffice));
        _directory.ListVisibleRolesAsync(OtTenant, Arg.Any<CancellationToken>())
            .Returns([
                new InvitationRoleInfo(RoleOtAdmin, "ot_admin", "Administrador OT", "TRANSIT_OFFICE", null),
                new InvitationRoleInfo(RoleRadicador, "gestor_tramites_ot", "Gestor OT", "TRANSIT_OFFICE", null),
            ]);

        var decision = await _sut.ResolveAsync(
            new InvitationScopeContext(InviterUser, OtTenant, ["gestor_tramites_ot"], OtTenant, InvitationChannel.Ot),
            CancellationToken.None);

        decision.Allowed.Should().BeTrue();
        decision.AssignableRoles.Should().ContainSingle(r => r.Code == "gestor_tramites_ot");
        decision.Roles.Should().Contain(r =>
            r.Code == "ot_admin" && !r.Assignable && r.DenyReason == InvitationScopeReasons.AdminRoleNotAssignable);
    }

    [Fact]
    public async Task ResolveAsync_RadicadorOnSecurityChannel_DeniedInviterNotAuthorized()
    {
        var decision = await _sut.ResolveAsync(
            new InvitationScopeContext(InviterUser, CompanyTenant, ["Radicador"], CompanyTenant, InvitationChannel.Security),
            CancellationToken.None);

        decision.Allowed.Should().BeFalse();
        decision.DenyReason.Should().Be(InvitationScopeReasons.InviterNotAuthorized);
    }

    [Fact]
    public async Task FindRoles_AmbiguousNameAndCode_ReturnsBoth()
    {
        _directory.GetTargetAsync(CompanyTenant, Arg.Any<CancellationToken>())
            .Returns(new InvitationTargetInfo(true, InvitationTargetKind.Company));
        var globalId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _directory.ListVisibleRolesAsync(CompanyTenant, Arg.Any<CancellationToken>())
            .Returns([
                new InvitationRoleInfo(globalId, "Radicador", "Radicador", "COMPANY", null),
                new InvitationRoleInfo(tenantId, "custom_rad", "Radicador", "COMPANY", CompanyTenant),
            ]);

        var decision = await _sut.ResolveAsync(
            new InvitationScopeContext(InviterUser, CompanyTenant, ["AdminCompany"], CompanyTenant, InvitationChannel.Security),
            CancellationToken.None);

        decision.FindRoles("Radicador").Should().HaveCount(2);
    }
}
