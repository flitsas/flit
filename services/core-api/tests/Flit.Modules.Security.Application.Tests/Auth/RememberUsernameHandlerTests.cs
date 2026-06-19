using Flit.Modules.Security.Application.Auth.RememberUsername;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class RememberUsernameHandlerTests
{
    private readonly IUserAccountRepository _repo = Substitute.For<IUserAccountRepository>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly RememberUsernameHandler _handler;

    public RememberUsernameHandlerTests()
    {
        _handler = new RememberUsernameHandler(_repo, _email);
    }

    [Fact]
    public async Task HandleAsync_DocumentMatchesAccount_SendsUsernameReminder()
    {
        _repo.FindActiveByDocumentAsync("1020304050", Arg.Any<CancellationToken>())
            .Returns(new PasswordRecoveryUser(Guid.NewGuid(), "demo@flit.local", "Demo"));

        await _handler.HandleAsync(new RememberUsernameCommand("1020304050"), CancellationToken.None);

        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.ToEmail == "demo@flit.local" && m.HtmlBody.Contains("demo@flit.local")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UnknownDocument_DoesNotSendNorThrow()
    {
        _repo.FindActiveByDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PasswordRecoveryUser?)null);

        await _handler.Invoking(h => h.HandleAsync(new RememberUsernameCommand("0000"), CancellationToken.None))
            .Should().NotThrowAsync();
        await _email.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_BlankDocument_IsNoOp()
    {
        await _handler.HandleAsync(new RememberUsernameCommand("  "), CancellationToken.None);

        await _repo.DidNotReceiveWithAnyArgs().FindActiveByDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }
}
