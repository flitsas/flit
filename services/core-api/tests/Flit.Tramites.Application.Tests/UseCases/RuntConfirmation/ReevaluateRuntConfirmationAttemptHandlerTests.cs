using Flit.Tramites.Application.UseCases.RuntConfirmation;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.RuntConfirmation;

/// <summary>HU #12308 AC12 — re-evaluar desde el crudo guardado sin llamar al proveedor y sin tocar el intento original.</summary>
public sealed class ReevaluateRuntConfirmationAttemptHandlerTests
{
    private static readonly Guid Instance = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Tenant = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private const string RawConCambioColor = """
        {"ok":true,"data":{"vehiculo":{"placa":"QZU024","estadoAutomotor":"ACTIVO","color":"ROSADO","mostrarSolicitudes":"SI"},
        "solicitudes":[{"noSolicitud":"302345557","fechaSolicitud":"2026-09-08T15:49:49.000-05:00","estado":"AUTORIZADA","tramitesRealizados":"TRÁMITE CAMBIO COLOR, ","entidad":"STRIA TTEyTTO BELLO"}]}}
        """;

    [Fact]
    public async Task Reevaluar_ProduceIntentoNuevoDesdeElCrudo_YNoModificaElOriginal()
    {
        var store = new FakeRuntConfirmationStore();
        store.Candidates.Add(Candidate("CAMBIO_COLOR", ProcedureFamily.Otros, new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero)));
        var payloadId = Guid.CreateVersion7();
        store.Payloads[payloadId] = RawConCambioColor;

        // Intento original decidido con una regla anterior (hipotética) que dijo Pendiente.
        var original = new RuntConfirmationAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant,
            ProcedureInstanceId = Instance,
            AttemptNo = 2,
            QueriedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ProviderKey = "kyverum_runt",
            QueryKind = "plate",
            Verdict = "pending",
            ReasonText = "regla vieja",
            RuleVersion = "confirmacion-v0",
            RawPayloadId = payloadId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        store.Attempts.Add(original);
        var originalSnapshot = (original.Verdict, original.ReasonText, original.RuleVersion, original.AttemptNo);

        var result = await new ReevaluateRuntConfirmationAttemptHandler(store)
            .HandleAsync(new ReevaluateRuntConfirmationAttemptCommand(original.Id, null), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ReevaluateStatus.Ok);
        var nuevo = result.NewAttempt!;
        nuevo.Id.Should().NotBe(original.Id);
        nuevo.Verdict.Should().Be("confirmed");
        nuevo.RuleVersion.Should().Be(RuntConfirmationRules.Version);
        nuevo.QueryKind.Should().Be("reevaluation");
        nuevo.ReevaluatedFromAttemptId.Should().Be(original.Id);
        nuevo.AttemptNo.Should().Be(2, "una re-evaluación no consume número de intento");
        nuevo.RawPayloadId.Should().Be(payloadId, "reutiliza el crudo: no hubo llamada al proveedor");
        nuevo.ProviderKey.Should().Be("kyverum_runt");

        (original.Verdict, original.ReasonText, original.RuleVersion, original.AttemptNo).Should().Be(originalSnapshot);
        store.Attempts.Should().HaveCount(2);

        // Un Confirmado nuevo sí marca el trámite; el contador no se toca.
        var update = store.Updates.Single().Update;
        update.ConfirmedAt.Should().NotBeNull();
        update.IncrementAttempts.Should().BeFalse();
        update.ClearFlag.Should().BeTrue();
    }

    [Fact]
    public async Task Reevaluar_SinCrudoGuardado_NoHaceNada()
    {
        var store = new FakeRuntConfirmationStore();
        store.Candidates.Add(Candidate("CAMBIO_COLOR", ProcedureFamily.Otros, DateTimeOffset.UtcNow));
        var original = new RuntConfirmationAttempt { Id = Guid.CreateVersion7(), TenantId = Tenant, ProcedureInstanceId = Instance, AttemptNo = 1, ProviderKey = "verifik", QueryKind = "plate", Verdict = "error", ReasonText = "timeout", RuleVersion = "confirmacion-v1" };
        store.Attempts.Add(original);

        var result = await new ReevaluateRuntConfirmationAttemptHandler(store)
            .HandleAsync(new ReevaluateRuntConfirmationAttemptCommand(original.Id, null), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ReevaluateStatus.NoRawPayload);
        store.Attempts.Should().ContainSingle();
    }

    [Fact]
    public async Task Reevaluar_IntentoInexistente_DevuelveNotFound()
    {
        var result = await new ReevaluateRuntConfirmationAttemptHandler(new FakeRuntConfirmationStore())
            .HandleAsync(new ReevaluateRuntConfirmationAttemptCommand(Guid.NewGuid(), null), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ReevaluateStatus.AttemptNotFound);
    }

    internal static RuntConfirmationCandidate Candidate(string type, ProcedureFamily family, DateTimeOffset submittedAt) =>
        new(Instance, Tenant, "FLT-1", type, family, "XLRAEL2G0PL523530", "QZU024", submittedAt, submittedAt.AddDays(1), submittedAt.AddDays(-1),
            "STRIA TTEyTTO BELLO", new RuntDocument("NIT", "890903938"), null, null, 0, null, null);
}
