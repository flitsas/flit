using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13298 (Feature #13282 C3, Épica #13202) — <see cref="AprobarValidacionManualHandler"/>: aprueba solo desde
/// <c>pendiente_revision_manual</c> por el mismo camino que Kyverum (<see cref="IdentityValidationResultApplier"/>: Approve con
/// 30 días + evento <see cref="IdentityValidationCompleted"/>), sella origen <c>manual</c> y revisor, audita
/// <c>manual_aprobado</c> sin PII, y responde 409 en cualquier otro estado.
/// <para>Uso: <c>await handler.HandleAsync(new AprobarValidacionManualCommand(validationId, userId), ct)</c>.</para>
/// </summary>
public sealed class AprobarValidacionManualHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IIdentityValidationAuditLog _audit = Substitute.For<IIdentityValidationAuditLog>();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();

    private AprobarValidacionManualHandler Handler() =>
        new(_repo, new IdentityValidationResultApplier(_events), _audit, new FixedTime(Now));

    private ProcedureInstanceBiometricValidation Fila(
        string status = BiometricEstados.PendienteRevisionManual, string provider = BiometricProviders.Manual)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            PartyRole = "comprador",
            Name = "Persona de prueba",
            DocumentType = "CC",
            DocumentNumber = "900123456",
            Email = "persona@example.test",
            Status = status,
            Provider = provider,
            TokenHash = new string('c', 64),
            FacePhotoPath = "ruta-opaca-rostro",
            SignatureImagePath = "ruta-opaca-firma",
        };
        _repo.GetBiometricByIdAsync(v.Id, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    [Fact]
    public async Task AC1_Aprueba_con_30_dias_origen_manual_y_revisor()
    {
        var v = Fila();

        var (result, error) = await Handler().HandleAsync(new AprobarValidacionManualCommand(v.Id, User), Ct);

        error.Should().BeNull();
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidatedAt.Should().Be(Now);
        v.ValidUntil.Should().Be(BiometricRules.FechaFinVigencia(Now));
        v.ApprovalOrigin.Should().Be("manual");
        v.ReviewedBy.Should().Be(User);
        v.ReviewedAt.Should().Be(Now);
        result!.Status.Should().Be("aprobado");
        result.ApprovalOrigin.Should().Be("manual");
        result.ValidUntil.Should().Be(v.ValidUntil!.Value);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publica_el_mismo_evento_de_completado_que_una_aprobacion_de_Kyverum()
    {
        var v = Fila();
        IdentityValidationEvent? publicado = null;
        await _events.PublishAsync(Arg.Do<IdentityValidationEvent>(e => publicado = e), Arg.Any<CancellationToken>());

        await Handler().HandleAsync(new AprobarValidacionManualCommand(v.Id, User), Ct);

        var completado = publicado.Should().BeOfType<IdentityValidationCompleted>().Subject;
        completado.ValidationId.Should().Be(v.Id);
        completado.TenantId.Should().Be(v.TenantId);
        completado.Estado.Should().Be(BiometricEstados.Aprobado);
        completado.Provider.Should().Be("manual");
        completado.Parte.Should().Be("comprador");
        await _events.Received(1).PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Audita_manual_aprobado_con_el_usuario_sin_PII_ni_rutas()
    {
        var v = Fila();
        var entradas = new List<IdentityValidationAuditEntry>();
        await _audit.LogAsync(Arg.Do<IdentityValidationAuditEntry>(entradas.Add), Arg.Any<CancellationToken>());

        await Handler().HandleAsync(new AprobarValidacionManualCommand(v.Id, User), Ct);

        var e = entradas.Should().ContainSingle().Subject;
        e.Stage.Should().Be(IdentityValidationAuditStages.ManualAprobado).And.Be("manual_aprobado");
        e.TenantId.Should().Be(v.TenantId);
        e.ValidationId.Should().Be(v.Id);
        e.Detail.Should().Contain(User.ToString());
        (e.Message + e.Detail).Should()
            .NotContain("persona@example.test").And.NotContain("900123456")
            .And.NotContain("ruta-opaca-rostro").And.NotContain("ruta-opaca-firma").And.NotContain("Persona de prueba");
    }

    [Theory]
    [InlineData(BiometricEstados.ManualActivo, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Aprobado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Rechazado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.Expirado, BiometricProviders.Manual)]
    [InlineData(BiometricEstados.PendienteRevisionManual, BiometricProviders.Kyverum)]
    [InlineData(BiometricEstados.EnProceso, BiometricProviders.Kyverum)]
    public async Task AC4_Fuera_de_pendiente_de_revision_responde_estado_invalido_y_no_cambia_nada(string status, string provider)
    {
        var v = Fila(status, provider);

        var (result, error) = await Handler().HandleAsync(new AprobarValidacionManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be(AprobarValidacionManualHandler.EstadoInvalido).And.Be("estado_invalido");
        v.ApprovalOrigin.Should().BeNull();
        v.ReviewedBy.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<IdentityValidationAuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validacion_inexistente_devuelve_not_found()
    {
        var (result, error) = await Handler().HandleAsync(new AprobarValidacionManualCommand(Guid.NewGuid(), User), Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Tramite_anulado_conserva_la_validacion_y_responde_tramite_inactivo()
    {
        var v = Fila();
        v.ProcedureInstance = new ProcedureInstance { Status = "anulado" };

        var (result, error) = await Handler().HandleAsync(new AprobarValidacionManualCommand(v.Id, User), Ct);

        result.Should().BeNull();
        error.Should().Be("tramite_inactivo");
        v.Status.Should().Be(BiometricEstados.PendienteRevisionManual);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
