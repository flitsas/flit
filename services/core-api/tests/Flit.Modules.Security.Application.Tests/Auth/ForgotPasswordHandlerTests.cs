using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class ForgotPasswordHandlerTests
{
    private readonly IUserAccountRepository _userAccountRepository = Substitute.For<IUserAccountRepository>();
    private readonly IPasswordResetTokenRepository _tokenRepository = Substitute.For<IPasswordResetTokenRepository>();
    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly PasswordRecoveryOptions _options = new();
    private readonly IAdminAuditWriter _auditWriter = Substitute.For<IAdminAuditWriter>();
    private readonly IAuditContextAccessor _auditContext = NullAuditContextAccessor.Instance;
    private readonly ForgotPasswordHandler _handler;

    public ForgotPasswordHandlerTests()
    {
        _handler = new ForgotPasswordHandler(
            _userAccountRepository, _tokenRepository, _tokenGenerator, _emailSender, _options,
            _auditWriter, _auditContext, NullLogger<ForgotPasswordHandler>.Instance);
        // HU #11358 — el puerto ya no es "void": por defecto el sender simula éxito, igual que
        // antes lo hacía implícitamente un Task no configurado.
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
    }

    [Fact]
    public async Task HandleAsync_RegisteredActiveEmail_GeneratesTokenAndSendsEmail()
    {
        var userId = Guid.NewGuid();
        _userAccountRepository.FindActiveByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new PasswordRecoveryUser(userId, "demo@flit.local", "Demo User", Guid.NewGuid()));
        _tokenGenerator.Generate().Returns(new GeneratedToken("raw-token", "hash-token"));

        await _handler.HandleAsync(new ForgotPasswordCommand("demo@flit.local"), CancellationToken.None);

        await _tokenRepository.Received(1).CreateAsync(
            userId, "hash-token", "password_reset", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToEmail == "demo@flit.local" && m.HtmlBody.Contains("raw-token")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UnknownEmail_DoesNotCreateTokenNorSendEmailNorThrow()
    {
        _userAccountRepository.FindActiveByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PasswordRecoveryUser?)null);

        var act = () => _handler.HandleAsync(new ForgotPasswordCommand("missing@flit.local"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _tokenRepository.DidNotReceiveWithAnyArgs().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlankEmail_IsNoOp()
    {
        await _handler.HandleAsync(new ForgotPasswordCommand("   "), CancellationToken.None);

        await _userAccountRepository.DidNotReceiveWithAnyArgs()
            .FindActiveByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // HU #11358 AC1 — el tenant del usuario (resuelto por el repositorio) viaja explícito en la
    // solicitud de envío.
    [Fact]
    public async Task HandleAsync_SendsMessageWithUserTenantId()
    {
        var tenantId = Guid.NewGuid();
        _userAccountRepository.FindActiveByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new PasswordRecoveryUser(Guid.NewGuid(), "demo@flit.local", "Demo User", tenantId));
        _tokenGenerator.Generate().Returns(new GeneratedToken("raw-token", "hash-token"));

        await _handler.HandleAsync(new ForgotPasswordCommand("demo@flit.local"), CancellationToken.None);

        await _emailSender.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.TenantId == tenantId), Arg.Any<CancellationToken>());
    }

    // HU #11358 AC3 — un fallo tipado del transporte NO se propaga como excepción: el token ya
    // quedó persistido y el handler termina con normalidad (mismo 202 genérico del endpoint).
    [Fact]
    public async Task HandleAsync_EmailTransportFails_DoesNotThrowAndTokenStaysPersisted()
    {
        var userId = Guid.NewGuid();
        _userAccountRepository.FindActiveByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new PasswordRecoveryUser(userId, "demo@flit.local", "Demo User", Guid.NewGuid()));
        _tokenGenerator.Generate().Returns(new GeneratedToken("raw-token", "hash-token"));
        _emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable)));

        var act = () => _handler.HandleAsync(new ForgotPasswordCommand("demo@flit.local"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _tokenRepository.Received(1).CreateAsync(
            userId, "hash-token", "password_reset", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
