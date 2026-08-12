using Flit.Admin.Domain.OtWebhooks;
using Flit.Infrastructure.Ict;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.OtWebhooks;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Notifications;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Messaging;

/// <summary>
/// HU #11464 / #11465 — el fan-out de <see cref="IProcedureStateChangeNotifier"/> se registra en un solo
/// punto (<see cref="ProcedureStateChangeNotifierRegistration"/>).
/// </summary>
public sealed class ProcedureStateChangeNotifierRegistrationTests
{
    [Fact]
    public void SinIct_ElFanOutEsCompositeConOtYCorreo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ =>
            new OtWebhookProcedureStateChangeNotifier(Substitute.For<IOtWebhookDispatchService>()));
        services.AddScoped(_ => new FlitDbContext(
            new DbContextOptionsBuilder<FlitDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options));
        services.AddScoped<ITramiteNotificationRecipientResolver, TramiteNotificationRecipientResolver>();
        services.AddProcedureStateChangeNotifierFanOut(includeIctReflection: false);

        using var sp = services.BuildServiceProvider();
        var notifier = sp.GetRequiredService<IProcedureStateChangeNotifier>();

        notifier.Should().BeOfType<CompositeProcedureStateChangeNotifier>();
    }

    [Fact]
    public void NadieRegistraElFanOutFueraDelPuntoCentralizado()
    {
        var root = LocateRepoRoot();
        var infraSrc = Path.Combine(root, "services", "core-api", "src", "Flit.Infrastructure");
        Directory.Exists(infraSrc).Should().BeTrue();

        var offenders = Directory.EnumerateFiles(infraSrc, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                if (name.Equals("ProcedureStateChangeNotifierRegistration.cs", StringComparison.Ordinal))
                    return false;
                var text = File.ReadAllText(path);
                return text.Contains("AddScoped<IProcedureStateChangeNotifier>", StringComparison.Ordinal)
                       || text.Contains("AddScoped<IProcedureStateChangeNotifier>(", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToArray();

        offenders.Should().BeEmpty(
            "el único AddScoped<IProcedureStateChangeNotifier> debe vivir en ProcedureStateChangeNotifierRegistration");
    }

    [Fact]
    public void ElPuntoCentralizadoExisteEnElEnsamblado()
    {
        typeof(ProcedureStateChangeNotifierRegistration)
            .GetMethod(nameof(ProcedureStateChangeNotifierRegistration.AddProcedureStateChangeNotifierFanOut))
            .Should().NotBeNull();
    }

    [Fact]
    public async Task UnSinkQueFallaNoImpideQueLosDemasDespachen_PeroElOutboxReintenta()
    {
        var healthy = new RecordingNotifier();
        var failing = new FailingNotifier();
        var composite = new CompositeProcedureStateChangeNotifier(
            [failing, healthy],
            Substitute.For<ILogger<CompositeProcedureStateChangeNotifier>>());

        var act = async () => await composite.NotifyAsync(SampleEvent(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AggregateException>();
        healthy.Calls.Should().Be(1);
        ex.Which.InnerExceptions.Should().ContainSingle();
    }

    private static ProcedureStateChangeEvent SampleEvent() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        null,
        "aprobado",
        DateTimeOffset.UtcNow);

    private static string LocateRepoRoot()
    {
        // No anclar en CLAUDE.md: está gitignored y no existe en CI.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "services", "core-api", "src")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("No se encontró la raíz del monorepo FLIT.");
    }

    private sealed class RecordingNotifier : IProcedureStateChangeNotifier
    {
        public int Calls { get; private set; }

        public Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingNotifier : IProcedureStateChangeNotifier
    {
        public Task NotifyAsync(ProcedureStateChangeEvent change, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("sink roto");
    }
}
