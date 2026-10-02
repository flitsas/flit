using Flit.Tramites.Application.BulkTramites.Processing;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// Bug #13194 (punto 4) — propagación de la identidad/firma:
/// <list type="bullet">
///   <item><see cref="EnsureIdentityAndNotifyHandler"/>: asegura la identidad y dispara el correo; los fallos
///   del proveedor ya no se tragan (antes el ICT los ignoraba).</item>
///   <item><see cref="RepresentanteLegalDesdeDirectorio"/>: completa el RL de una PJ desde el directorio
///   del MISMO tenant.</item>
///   <item><see cref="TramiteFirmaPendiente"/>: el conjunto de estados que se firman al aprobar la identidad.</item>
/// </list>
/// <para>Uso de ejemplo:
/// <c>var (r, e) = await notifier.HandleAsync(instanceId, tenantId, "comprador");</c> →
/// <c>r.Notificacion == "enviada" | "no_requerida" | "ya_en_curso" | "fallida"</c>.</para>
/// </summary>
public sealed class Bug13194PropagacionIdentidadTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private const string Nit = "900123456";
    private const string RlDoc = "7000001";

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly ISignatureVaultPolicy _vault = Substitute.For<ISignatureVaultPolicy>();

    public Bug13194PropagacionIdentidadTests()
    {
        _repo.ListInFlightByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProcedureInstanceBiometricValidation>());
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        _vault.ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SignatureVaultMatch?)null);
    }

    private EnsureIdentityAndNotifyHandler Notifier(bool kyverum = true) =>
        new(
            new EnsureIdentityHandler(_repo, _vault),
            new IniciarKyverumVerifyHandler(
                _repo, _kyverum, new FakeWebhookSecretProtector(),
                Substitute.For<IIdentityValidationEventPublisher>(), Substitute.For<IIdentityValidationAuditLog>(), _vault),
            new SimularBiometriaHandler(_repo),
            new BiometricsProviderOptions { Provider = kyverum ? BiometricProviders.Kyverum : BiometricProviders.Mock });

    private ProcedureInstance Matricula(string status = TramiteEstado.Borrador, bool juridica = false)
    {
        var id = Guid.NewGuid();
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.For("matricula_inicial"),
            Id = id,
            TenantId = TenantA,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-013194",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        instance.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            ProcedureInstanceId = id,
            ActorType = "comprador",
            FullName = juridica ? "Empresa de Prueba SAS" : "Persona de Prueba",
            DocumentType = juridica ? "NIT" : "CC",
            DocumentNumber = juridica ? Nit : RlDoc,
            PersonType = juridica ? "juridical" : "natural",
            Email = "contacto@prueba.test",
            Metadata = juridica
                ? "{\"representanteLegal\":{\"tipoDocumento\":\"CC\",\"numeroDocumento\":\"" + RlDoc
                  + "\",\"nombreCompleto\":\"Representante de Prueba\",\"email\":\"rl@prueba.test\"}}"
                : "{}",
        });
        _repo.GetByIdWithBiometricsAndActorsAsync(id, TenantA, Arg.Any<CancellationToken>()).Returns(instance);
        return instance;
    }

    private void StubKyverumOk() =>
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult("kyv_13194", "https://capture/kyv_13194", "whsec", "pending", "{}", null));

    // ── EnsureIdentityAndNotifyHandler ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Notify_RequiereValidacion_KyverumFalla_DevuelveFallidaConElCodigo()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Matricula();
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("rechazado", transient: false));

        var (result, error) = await Notifier().HandleAsync(instance.Id, TenantA, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.RequiereValidacion);
        result.Notificacion.Should().Be(IdentityNotificationOutcomes.Fallida);
        result.NotificacionError.Should().Be("proveedor_error", "el fallo del proveedor ya no se descarta");
    }

    [Fact]
    public async Task Notify_RequiereValidacion_EnTramiteAsignado_EnviaElCorreo()
    {
        // La identidad venció entre la radicación y el envío al organismo: se renueva sin volver a borrador.
        var ct = TestContext.Current.CancellationToken;
        var instance = Matricula(TramiteEstado.Asignado);
        StubKyverumOk();

        var (result, error) = await Notifier().HandleAsync(instance.Id, TenantA, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Notificacion.Should().Be(IdentityNotificationOutcomes.Enviada);
        result.ValidationId.Should().NotBeNull();
        await _kyverum.Received(1).StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), ct);
    }

    [Fact]
    public async Task Notify_TramiteEntregado_NoSeIniciaYDevuelveNotDraft()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Matricula(TramiteEstado.Entregado);
        StubKyverumOk();

        var (result, _) = await Notifier().HandleAsync(instance.Id, TenantA, "comprador", ct: ct);

        result!.Notificacion.Should().Be(IdentityNotificationOutcomes.Fallida);
        result.NotificacionError.Should().Be("not_draft");
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Notify_PersonaJuridicaConBaulVigente_NoEnviaCorreo()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Matricula(juridica: true);
        _vault.ResolveAsync(TenantA, "CC", RlDoc, Arg.Any<CancellationToken>())
            .Returns(new SignatureVaultMatch(
                Guid.NewGuid(), "Representante de Prueba", "hash", "path", "sha",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), RlDoc));

        var (result, error) = await Notifier().HandleAsync(instance.Id, TenantA, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Outcome.Should().Be(EnsureIdentityOutcomes.FirmaBaul);
        result.Notificacion.Should().Be(IdentityNotificationOutcomes.NoRequerida);
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Notify_ValidacionEnCurso_NoDuplicaLaSesion()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Matricula();
        instance.BiometricValidations.Add(new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            ProcedureInstanceId = instance.Id,
            PartyRole = "comprador",
            Name = "Persona de Prueba",
            DocumentType = "CC",
            DocumentNumber = RlDoc,
            Email = "contacto@prueba.test",
            Status = BiometricEstados.EnProceso,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var (first, _) = await Notifier().HandleAsync(instance.Id, TenantA, "comprador", ct: ct);
        var (second, _) = await Notifier().HandleAsync(instance.Id, TenantA, "comprador", ct: ct);

        first!.Notificacion.Should().Be(IdentityNotificationOutcomes.YaEnCurso);
        second!.Notificacion.Should().Be(IdentityNotificationOutcomes.YaEnCurso);
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Notify_ProveedorMock_SimulaLaValidacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Matricula();

        var (result, error) = await Notifier(kyverum: false).HandleAsync(instance.Id, TenantA, "comprador", ct: ct);

        error.Should().BeNull();
        result!.Notificacion.Should().Be(IdentityNotificationOutcomes.Enviada);
        instance.BiometricValidations.Should().ContainSingle(v => v.Status == BiometricEstados.Aprobado);
    }

    [Fact]
    public async Task KyverumDelGestor_SinBandera_ConservaLaRegla_SoloEditables()
    {
        // Contrato del endpoint del gestor: sin la bandera, un asignado sigue respondiendo not_draft.
        var ct = TestContext.Current.CancellationToken;
        var instance = Matricula(TramiteEstado.Asignado);
        var handler = new IniciarKyverumVerifyHandler(
            _repo, _kyverum, new FakeWebhookSecretProtector(),
            Substitute.For<IIdentityValidationEventPublisher>(), Substitute.For<IIdentityValidationAuditLog>());

        var (_, error, _) = await handler.HandleAsync(
            instance.Id, TenantA, new IniciarBiometriaInput("comprador", "", "", "", ""), ct);

        error.Should().Be("not_draft");
    }

    // ── RepresentanteLegalDesdeDirectorio ────────────────────────────────────────────────────────

    private static ActorInput ActorPj(ActorRepresentanteLegal? rl, string nit = Nit, string email = "") =>
        new(
            Rol: "comprador",
            TipoDocumento: "NIT",
            NumeroDocumento: nit,
            NombreCompleto: "Empresa de Prueba SAS",
            Email: email,
            Telefono: null,
            PersonType: "juridical",
            RepresentanteLegal: rl);

    private static BulkTramitesCompanyDirectoryEntry Directorio(string? rlEmail = "rl@prueba.test") =>
        new(
            "empresa@prueba.test", "Calle 1", "11001", "6010000",
            [new BulkTramitesLegalRepresentative("CC", RlDoc, "Representante de Prueba", rlEmail, "3000000000")]);

    [Fact]
    public async Task Rl_MismoTenant_CompletaDocumentoYCorreoDesdeElDirectorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Substitute.For<IBulkTramitesLegalRepresentativeDirectory>();
        directory.FindByNitAsync(TenantA, Nit, Arg.Any<CancellationToken>()).Returns(Directorio());

        var r = await new RepresentanteLegalDesdeDirectorio(directory).CompletarAsync(TenantA, [ActorPj(null)], ct);

        r.Avisos.Should().BeEmpty();
        var actor = r.Actores.Should().ContainSingle().Subject;
        actor.Email.Should().Be("empresa@prueba.test", "el correo de la compañía completa al actor");
        actor.RepresentanteLegal!.TipoDocumento.Should().Be("CC");
        actor.RepresentanteLegal.NumeroDocumento.Should().Be(RlDoc);
        actor.RepresentanteLegal.Email.Should().Be("rl@prueba.test");
        actor.RepresentanteLegal.MecanismoFirma.Should().BeNull("sin elección aplica la precedencia del baúl");
    }

    [Fact]
    public async Task Rl_OtroTenant_NoSeCompletaYAvisa()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Substitute.For<IBulkTramitesLegalRepresentativeDirectory>();
        // El directorio solo conoce la compañía en el tenant A; la consulta va con el tenant B.
        directory.FindByNitAsync(TenantA, Nit, Arg.Any<CancellationToken>()).Returns(Directorio());

        var original = ActorPj(null, email: "empresa@prueba.test");
        var r = await new RepresentanteLegalDesdeDirectorio(directory).CompletarAsync(TenantB, [original], ct);

        r.Actores.Should().ContainSingle().Which.Should().Be(original);
        r.Avisos.Should().Contain(["rl_no_registrado:comprador", "rl_sin_correo:comprador"]);
        await directory.DidNotReceive().FindByNitAsync(TenantA, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rl_DirectorioSinCorreoDelRepresentante_AvisaSinCorreo()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Substitute.For<IBulkTramitesLegalRepresentativeDirectory>();
        directory.FindByNitAsync(TenantA, Nit, Arg.Any<CancellationToken>()).Returns(Directorio(rlEmail: null));

        var r = await new RepresentanteLegalDesdeDirectorio(directory).CompletarAsync(TenantA, [ActorPj(null)], ct);

        r.Actores[0].RepresentanteLegal!.NumeroDocumento.Should().Be(RlDoc);
        r.Avisos.Should().ContainSingle().Which.Should().Be("rl_sin_correo:comprador");
    }

    [Fact]
    public async Task Rl_Declarado_SeEmpataPorDigitosYNoSeSustituyePorOtraPersona()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Substitute.For<IBulkTramitesLegalRepresentativeDirectory>();
        directory.FindByNitAsync(TenantA, Nit, Arg.Any<CancellationToken>()).Returns(Directorio());
        var sut = new RepresentanteLegalDesdeDirectorio(directory);

        var conPuntos = await sut.CompletarAsync(
            TenantA, [ActorPj(new ActorRepresentanteLegal("C.C.", "7.000.001", null, null, null))], ct);
        var otro = await sut.CompletarAsync(
            TenantA, [ActorPj(new ActorRepresentanteLegal("CC", "8000002", "Otra Persona", null, null))], ct);

        conPuntos.Actores[0].RepresentanteLegal!.NumeroDocumento.Should().Be(RlDoc, "se adopta el documento canónico del directorio");
        conPuntos.Actores[0].RepresentanteLegal!.TipoDocumento.Should().Be("CC");
        otro.Actores[0].RepresentanteLegal!.NumeroDocumento.Should().Be("8000002", "nunca se cambia la persona declarada");
        otro.Avisos.Should().Contain("rl_no_registrado:comprador");
    }

    [Fact]
    public async Task Rl_NitConDigitoDeVerificacion_BuscaPorLaFormaBase()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Substitute.For<IBulkTramitesLegalRepresentativeDirectory>();
        directory.FindByNitAsync(TenantA, Nit, Arg.Any<CancellationToken>()).Returns(Directorio());

        var r = await new RepresentanteLegalDesdeDirectorio(directory)
            .CompletarAsync(TenantA, [ActorPj(null, nit: "900.123.456-7")], ct);

        r.Actores[0].RepresentanteLegal!.NumeroDocumento.Should().Be(RlDoc);
    }

    [Fact]
    public async Task Rl_PersonaNatural_NoConsultaElDirectorio()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Substitute.For<IBulkTramitesLegalRepresentativeDirectory>();
        var pn = new ActorInput("comprador", "CC", RlDoc, "Persona de Prueba", "pn@prueba.test", null);

        var r = await new RepresentanteLegalDesdeDirectorio(directory).CompletarAsync(TenantA, [pn], ct);

        r.Actores.Should().ContainSingle().Which.Should().Be(pn);
        r.Avisos.Should().BeEmpty();
        await directory.DidNotReceiveWithAnyArgs().FindByNitAsync(default, default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("NIT", true)]
    [InlineData(" nit ", true)]
    [InlineData("N.I.T.", true)]
    [InlineData("CC", false)]
    public void Rl_EsJuridica_ToleraVariantesDelNit(string tipo, bool esperado) =>
        RepresentanteLegalDesdeDirectorio.EsJuridica(new ActorInput("comprador", tipo, "1", "X", "x@x.test", null))
            .Should().Be(esperado);

    // ── TramiteFirmaPendiente ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TramiteEstado.Borrador, false, true, true)]
    [InlineData(TramiteEstado.Borrador, false, false, false)]
    [InlineData(TramiteEstado.Preparado, false, false, true)]
    [InlineData(TramiteEstado.Preasignacion, false, false, true)]
    [InlineData(TramiteEstado.Asignado, false, false, true)]
    [InlineData(TramiteEstado.Rechazado, true, false, true)]
    [InlineData(TramiteEstado.Rechazado, false, false, false)]
    [InlineData(TramiteEstado.Entregado, false, false, false)]
    [InlineData(TramiteEstado.Aprobado, false, false, false)]
    [InlineData(TramiteEstado.Anulado, false, false, false)]
    [InlineData(TramiteEstado.Revocado, false, false, false)]
    public void FirmaPendiente_EntraEnLote_SoloLosNoEntregadosNiFinales(
        string status, bool subsanacion, bool finalizado, bool esperado) =>
        TramiteFirmaPendiente.EntraEnLoteDeFirma(status, subsanacion, finalizado).Should().Be(esperado);
}
