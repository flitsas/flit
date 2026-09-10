using Flit.Tramites.Application.Biometrics;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Cierre del hueco de identidad (ADR-0053, ronda 2) — con dos actores JURÍDICOS del mismo rol, cada
/// uno debe poder validar su propia identidad, obtener su propia marca de firma diferida, y verse por
/// separado en la grilla del gestor. Antes de este cierre, la idempotencia de creación de la validación
/// biométrica y la búsqueda de la marca diferida estaban clave por <c>(instancia, PartyRole)</c> —sin
/// documento—, así que el segundo actor SIEMPRE chocaba contra el registro del primero.
///
/// <para>Cubre los 3 puntos P0: (1) <see cref="IniciarBiometriaHandler"/>/<see cref="IniciarKyverumVerifyHandler"/>
/// — cada actor obtiene su propia validación; (2) <see cref="MarcarFirmaPosteriorHandler"/> — cada actor
/// obtiene su propia marca; (3) <see cref="ListBiometriaHandler"/> — la grilla muestra a ambos, con
/// <c>Ordinal</c> y cobertura de baúl por actor. También cubre <see cref="EnsureIdentityHandler"/> (el
/// bug de expiración cruzada encontrado al generalizar el "cambio de persona").</para>
/// </summary>
public sealed class MultiplePropietarioIdentityLifecycleTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    public MultiplePropietarioIdentityLifecycleTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
    }

    private static ProcedureInstance Instance(Guid id, Guid tenant) => new()
    {
        ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
        Id = id,
        TenantId = tenant,
        ProcedureTypeId = Guid.NewGuid(),
        ReferenceNumber = "TRM-2026-000009",
        Status = TramiteEstado.Borrador,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureInstanceActor ActorJuridico(
        Guid tenant, int ordinal, string nit, string rlDoc, string rlNombre, string rlEmail) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant,
        ProcedureEntityId = Guid.NewGuid(),
        ActorType = "comprador",
        DocumentType = "NIT",
        DocumentNumber = nit,
        FullName = $"Empresa {nit} SAS",
        Email = $"contacto{nit}@x.com",
        PersonType = "juridical",
        Ordinal = ordinal,
        Metadata = ActorMetadataReader.Serialize(
            null, null, new ActorRepresentanteLegal("CC", rlDoc, rlNombre, rlEmail, null)),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // ── 1a) IniciarBiometriaHandler (mock) ──────────────────────────────────────

    [Fact]
    public async Task IniciarBiometrica_DosActoresJuridicosMismoRol_CadaUnoObtieneSuPropiaValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(ActorJuridico(tenant, 1, "900111111", "1000000001", "RL Uno", "rl1@x.com"));
        instance.Actors.Add(ActorJuridico(tenant, 2, "900222222", "1000000002", "RL Dos", "rl2@x.com"));
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        var handler = new IniciarBiometriaHandler(_repo);

        var (_, error1, _) = await handler.HandleAsync(
            id, tenant, new IniciarBiometriaInput("comprador", "RL Uno", "CC", "1000000001", "rl1@x.com"), ct);
        error1.Should().BeNull();

        // Antes del fix: esto devolvía "biometria_activa" porque la idempotencia solo miraba el rol.
        var (result2, error2, _) = await handler.HandleAsync(
            id, tenant, new IniciarBiometriaInput("comprador", "RL Dos", "CC", "1000000002", "rl2@x.com"), ct);

        error2.Should().BeNull();
        result2.Should().NotBeNull();
        instance.BiometricValidations.Should().HaveCount(2);
        instance.BiometricValidations.Select(v => v.DocumentNumber)
            .Should().BeEquivalentTo(["1000000001", "1000000002"]);
    }

    [Fact]
    public async Task IniciarBiometrica_UnSoloActor_SegundoIntentoSigueBloqueadoPorRol()
    {
        // Regresión cero: con 1 solo actor por rol, la idempotencia sigue siendo por rol (no exige
        // documento) — el comportamiento anterior a ADR-0053 se conserva íntegro.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(ActorJuridico(tenant, 1, "900111111", "1000000001", "RL Uno", "rl1@x.com"));
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        var handler = new IniciarBiometriaHandler(_repo);

        var (_, error1, _) = await handler.HandleAsync(
            id, tenant, new IniciarBiometriaInput("comprador", "RL Uno", "CC", "1000000001", "rl1@x.com"), ct);
        error1.Should().BeNull();

        var (_, error2, _) = await handler.HandleAsync(
            id, tenant, new IniciarBiometriaInput("comprador", "RL Uno", "CC", "1000000001", "rl1@x.com"), ct);

        error2.Should().Be("biometria_activa");
        instance.BiometricValidations.Should().ContainSingle();
    }

    // ── 1b) IniciarKyverumVerifyHandler (proveedor real) ────────────────────────

    [Fact]
    public async Task IniciarKyverum_DosActoresJuridicosMismoRol_CadaUnoObtieneSuPropiaValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(ActorJuridico(tenant, 1, "900111111", "1000000001", "RL Uno", "rl1@x.com"));
        instance.Actors.Add(ActorJuridico(tenant, 2, "900222222", "1000000002", "RL Dos", "rl2@x.com"));
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var kyverum = Substitute.For<IKyverumVerifyClient>();
        kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var req = call.Arg<KyverumVerifyStartRequest>();
                return new KyverumVerifyStartResult(
                    "kyv_" + req.Documento, "https://capture/" + req.Documento, "whsec", "pending", "{}", null);
            });
        var handler = new IniciarKyverumVerifyHandler(
            _repo, kyverum, new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(), Substitute.For<IIdentityValidationAuditLog>());

        var (_, error1, _) = await handler.HandleAsync(
            id, tenant, new IniciarBiometriaInput("comprador", "RL Uno", "CC", "1000000001", "rl1@x.com"), ct);
        error1.Should().BeNull();

        var (result2, error2, _) = await handler.HandleAsync(
            id, tenant, new IniciarBiometriaInput("comprador", "RL Dos", "CC", "1000000002", "rl2@x.com"), ct);

        error2.Should().BeNull();
        result2.Should().NotBeNull();
        instance.BiometricValidations.Should().HaveCount(2);
        instance.BiometricValidations.Select(v => v.KyverumVerificationId)
            .Should().BeEquivalentTo(["kyv_1000000001", "kyv_1000000002"]);
    }

    // ── 1c) EnsureIdentityHandler — no se pisan entre actores del mismo rol ─────

    [Fact]
    public async Task EnsureIdentity_ProcesarUnActor_NoExpiraLaValidacionDelOtroActorDelMismoRol()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(ActorJuridico(tenant, 1, "900111111", "1000000001", "RL Uno", "rl1@x.com"));
        instance.Actors.Add(ActorJuridico(tenant, 2, "900222222", "1000000002", "RL Dos", "rl2@x.com"));
        var validacionActor1 = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.Enviado,
            Name = "RL Uno",
            DocumentType = "CC",
            DocumentNumber = "1000000001",
            Email = "rl1@x.com",
            TokenHash = "h1",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.BiometricValidations.Add(validacionActor1);
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        var handler = new EnsureIdentityHandler(_repo);

        // Se pregunta por el actor 2 (documento distinto al de la validación existente, que es del
        // actor 1). Antes del fix, el "paso 0" (expira validaciones de OTRA persona) comparaba contra
        // UN solo documento resuelto y expiraba la del actor 1 en cada llamada sobre el actor 2.
        var (result, error) = await handler.HandleAsync(
            id, tenant, "comprador", documento: "1000000002", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
        validacionActor1.Status.Should().Be(BiometricEstados.Enviado); // sigue viva, no se expiró
    }

    // ── 2) MarcarFirmaPosteriorHandler — cada actor obtiene su propia marca ─────

    [Fact]
    public async Task MarcarFirmaPosterior_DosActoresJuridicosMismoRol_CadaUnoObtieneSuPropiaMarca()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(ActorJuridico(tenant, 1, "900111111", "1000000001", "RL Uno", "rl1@x.com"));
        instance.Actors.Add(ActorJuridico(tenant, 2, "900222222", "1000000002", "RL Dos", "rl2@x.com"));
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var marks = Substitute.For<IDeferredSignatureMarkRepository>();
        // Ninguno de los dos representantes tiene todavía una marca pendiente.
        marks.FindPendienteAsync(tenant, id, "comprador", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((DeferredSignatureMark?)null);
        var creadas = new List<DeferredSignatureMark>();
        marks.When(m => m.Add(Arg.Any<DeferredSignatureMark>())).Do(call => creadas.Add(call.Arg<DeferredSignatureMark>()));

        var handler = new MarcarFirmaPosteriorHandler(_repo, marks);

        var (r1, e1) = await handler.HandleAsync(id, tenant, "comprador", documento: "1000000001", ct: ct);
        var (r2, e2) = await handler.HandleAsync(id, tenant, "comprador", documento: "1000000002", ct: ct);

        e1.Should().BeNull();
        e2.Should().BeNull();
        r1!.Marcado.Should().BeTrue();
        r2!.Marcado.Should().BeTrue();
        creadas.Should().HaveCount(2);
        creadas.Select(m => m.RepresentativeDocumentNumber).Should().BeEquivalentTo(["1000000001", "1000000002"]);

        // La consulta a FindPendienteAsync se hizo POR DOCUMENTO, no solo por rol — la corrección real.
        await marks.Received(1).FindPendienteAsync(tenant, id, "comprador", "1000000001", Arg.Any<CancellationToken>());
        await marks.Received(1).FindPendienteAsync(tenant, id, "comprador", "1000000002", Arg.Any<CancellationToken>());
    }

    // ── 3) ListBiometriaHandler — la grilla muestra a ambos actores ─────────────

    [Fact]
    public async Task ListBiometrica_DosActores_LaGrillaMuestraAAmbosConSuOrdinal()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(ActorJuridico(tenant, 1, "900111111", "1000000001", "RL Uno", "rl1@x.com"));
        instance.Actors.Add(ActorJuridico(tenant, 2, "900222222", "1000000002", "RL Dos", "rl2@x.com"));
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.Enviado,
            Name = "RL Uno",
            DocumentType = "CC",
            DocumentNumber = "1000000001",
            Email = "rl1@x.com",
            TokenHash = "h1",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            PartyRole = "comprador",
            Status = BiometricEstados.Rechazado,
            Name = "RL Dos",
            DocumentType = "CC",
            DocumentNumber = "1000000002",
            Email = "rl2@x.com",
            TokenHash = "h2",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);
        var handler = new ListBiometriaHandler(_repo, new BiometricsProviderOptions());

        var (result, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // Antes del fix, la grilla resolvía "el" actor del rol (FirstOrDefault): el segundo copropietario
        // (rechazado) nunca aparecía diferenciado del primero.
        result!.Validations.Should().HaveCount(2);
        result.Validations.Should().Contain(v => v.DocumentNumber == "1000000001" && v.Ordinal == 1);
        result.Validations.Should().Contain(v => v.DocumentNumber == "1000000002" && v.Ordinal == 2);
    }

    [Fact]
    public async Task ListBiometrica_CoberturaDeBaul_SoloElActorCubiertoSeReportaEnFirmaBaulActores()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var instance = Instance(id, tenant);
        instance.Actors.Add(ActorJuridico(tenant, 1, "900111111", "1000000001", "RL Uno", "rl1@x.com"));
        instance.Actors.Add(ActorJuridico(tenant, 2, "900222222", "1000000002", "RL Dos", "rl2@x.com"));
        _repo.GetByIdWithBiometricsAndActorsAsync(id, tenant, ct).Returns(instance);

        var vault = Substitute.For<ISignatureVaultPolicy>();
        // Firma vigente SOLO para el RL del actor 1 (ordinal 1).
        vault.ResolveAsync(tenant, "CC", "1000000001", Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "RL Uno", "hash", "vault/f.png", "sha",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "1000000001"));
        vault.ResolveAsync(tenant, "CC", "1000000002", Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);

        var handler = new ListBiometriaHandler(_repo, new BiometricsProviderOptions(), vault);

        var (result, error) = await handler.HandleAsync(id, tenant, ct);

        error.Should().BeNull();
        // firmaBaulPartes (a nivel de rol) sigue existiendo, imprecisa a propósito.
        result!.FirmaBaulPartes.Should().Contain("comprador");
        // firmaBaulActores (nuevo, por actor) discrimina: solo el actor 1 está cubierto.
        result.FirmaBaulActores.Should().ContainSingle();
        result.FirmaBaulActores!.Single().DocumentNumber.Should().Be("1000000001");
        result.FirmaBaulActores!.Single().Ordinal.Should().Be(1);
    }
}
