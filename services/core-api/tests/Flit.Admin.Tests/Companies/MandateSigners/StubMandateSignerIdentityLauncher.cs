using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #13246 — doble del puerto <see cref="IMandateSignerIdentityLauncher"/> para los tests de los handlers: registra cada
/// lanzamiento y responde con el desenlace configurado (o lanza la excepción indicada).
/// </summary>
internal sealed class StubMandateSignerIdentityLauncher : IMandateSignerIdentityLauncher
{
    public List<MandateSignerIdentityLaunchRequest> Calls { get; } = [];

    public MandateSignerIdentityLaunchOutcome Outcome { get; set; } = MandateSignerIdentityLaunchOutcome.Sent;

    public Exception? Throw { get; set; }

    public Task<MandateSignerIdentityLaunchResult> LaunchAsync(
        MandateSignerIdentityLaunchRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(request);
        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(new MandateSignerIdentityLaunchResult(Outcome, Guid.NewGuid()));
    }
}
