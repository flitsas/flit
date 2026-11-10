using Flit.Modules.Security.Application.Auth.BulkInvitation;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth.BulkInvitation;

/// <summary>HU #14423 — dryRun no crea invitaciones; filas inválidas no tumban el lote.</summary>
public sealed class BulkInvitationServiceTests
{
    private readonly IInvitationScopePolicy _scope = Substitute.For<IInvitationScopePolicy>();
    private readonly IBulkInvitationXlsxParser _parser = Substitute.For<IBulkInvitationXlsxParser>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly IServiceScopeFactory _scopes = Substitute.For<IServiceScopeFactory>();
    private readonly BulkInvitationOptions _options = new();
    private readonly ILogger<BulkInvitationService> _logger = Substitute.For<ILogger<BulkInvitationService>>();
    private readonly BulkInvitationService _sut;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    public BulkInvitationServiceTests()
    {
        _sut = new BulkInvitationService(_scope, _parser, _invitations, _scopes, _options, _logger);
    }

    [Fact]
    public async Task ProcessAsync_DryRun_ValidAndInvalidRows_DoesNotCreateInvitations()
    {
        var decision = InvitationScopeDecision.Permitted(
            InvitationTargetKind.Company,
            [
                new InvitationRoleOption(RoleId, "Radicador", "Radicador", true, null),
                new InvitationRoleOption(
                    Guid.NewGuid(),
                    "SuperAdmin",
                    "SuperAdmin",
                    false,
                    InvitationScopeReasons.SuperAdminRoleNotAssignable),
            ]);
        _scope.ResolveAsync(Arg.Any<InvitationScopeContext>(), Arg.Any<CancellationToken>()).Returns(decision);

        _parser.Parse(Arg.Any<Stream>()).Returns(new BulkInvitationParseResult(
            null,
            [
                new BulkInvitationParsedRow(2, "Ana Pérez", "ana@flit.local", "Radicador", false),
                new BulkInvitationParsedRow(3, "Bob", "bob@flit.local", "SuperAdmin", false),
                new BulkInvitationParsedRow(4, "", "carol@flit.local", "Radicador", false),
            ]));

        _invitations.ExistsPendingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _invitations.UserExistsWithEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.ProcessAsync(
            new InvitationScopeContext(UserId, TenantId, ["AdminCompany"], TenantId, InvitationChannel.Security),
            new MemoryStream([1, 2, 3]),
            dryRun: true,
            CancellationToken.None);

        result.FileError.Should().BeNull();
        result.Report.Should().NotBeNull();
        result.Report!.DryRun.Should().BeTrue();
        result.Report.Total.Should().Be(3);
        result.Report.Valid.Should().Be(1);
        result.Report.Created.Should().Be(0);
        result.Report.Rejected.Should().Be(2);
        result.Report.Rows.Should().Contain(r => r.Status == BulkInvitationRowStatus.Valid && r.Email == "ana@flit.local");
        result.Report.Rows.Should().Contain(r =>
            r.Email == "bob@flit.local" && r.ErrorCode == InvitationScopeReasons.SuperAdminRoleNotAssignable);
        result.Report.Rows.Should().Contain(r =>
            r.Email == "carol@flit.local" && r.ErrorCode == BulkInvitationRowErrors.FullNameRequired);

        // dryRun: nunca abre scopes de CreateInvitationHandler
        _scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public async Task ProcessAsync_ScopeDenied_ThrowsBeforeParsing()
    {
        _scope.ResolveAsync(Arg.Any<InvitationScopeContext>(), Arg.Any<CancellationToken>())
            .Returns(InvitationScopeDecision.Denied(InvitationScopeReasons.InviterNotAuthorized));

        var act = () => _sut.ProcessAsync(
            new InvitationScopeContext(UserId, TenantId, ["Radicador"], TenantId, InvitationChannel.Security),
            new MemoryStream([1]),
            dryRun: true,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvitationScopeDeniedException>()
            .Where(ex => ex.Reason == InvitationScopeReasons.InviterNotAuthorized);
        _parser.DidNotReceive().Parse(Arg.Any<Stream>());
    }

    [Fact]
    public async Task ProcessAsync_FileError_ReturnsWithoutRows()
    {
        _scope.ResolveAsync(Arg.Any<InvitationScopeContext>(), Arg.Any<CancellationToken>())
            .Returns(InvitationScopeDecision.Permitted(InvitationTargetKind.Company, []));
        _parser.Parse(Arg.Any<Stream>())
            .Returns(new BulkInvitationParseResult(BulkInvitationFileErrors.TooManyRows, []));

        var result = await _sut.ProcessAsync(
            new InvitationScopeContext(UserId, TenantId, ["AdminCompany"], TenantId, InvitationChannel.Security),
            new MemoryStream([1]),
            dryRun: true,
            CancellationToken.None);

        result.FileError.Should().Be(BulkInvitationFileErrors.TooManyRows);
        result.Report.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_DryRun_EmailAlreadyInUse_RejectsWithStableCode()
    {
        var decision = InvitationScopeDecision.Permitted(
            InvitationTargetKind.Company,
            [new InvitationRoleOption(RoleId, "Radicador", "Radicador", true, null)]);
        _scope.ResolveAsync(Arg.Any<InvitationScopeContext>(), Arg.Any<CancellationToken>()).Returns(decision);
        _parser.Parse(Arg.Any<Stream>()).Returns(new BulkInvitationParseResult(
            null,
            [new BulkInvitationParsedRow(2, "Ana", "ana@flit.local", "Radicador", false)]));
        _invitations.ExistsPendingAsync("ana@flit.local", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.ProcessAsync(
            new InvitationScopeContext(UserId, TenantId, ["AdminCompany"], TenantId, InvitationChannel.Security),
            new MemoryStream([1]),
            dryRun: true,
            CancellationToken.None);

        result.Report!.Rejected.Should().Be(1);
        result.Report.Rows.Should().ContainSingle(r =>
            r.ErrorCode == BulkInvitationRowErrors.EmailAlreadyInUse);
        _scopes.DidNotReceive().CreateScope();
    }
}
