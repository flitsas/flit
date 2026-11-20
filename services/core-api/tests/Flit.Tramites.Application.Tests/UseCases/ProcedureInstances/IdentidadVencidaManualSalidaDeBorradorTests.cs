using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #13824 (regresión FT9-0000001) — una identidad vencida a mano solo se conserva si el trámite salió de borrador ANTES
/// del vencimiento (<see cref="BiometricRules.ConservaFirmaTrasVencimientoManual"/>). Un trámite que salió DESPUÉS (p. ej. una
/// matrícula sin placa exenta de firma) nunca tuvo esa firma: ni el gate, ni el listado, ni el FUR pueden darla por buena.
/// El caso legítimo —salió firmado y luego le vencieron la identidad— sigue firmado. Sin fila de salida en el historial
/// (legado) se conserva el comportamiento previo.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>await IdentityApprovalResolver.ResolveApprovedPartiesAsync(repo, instance, Now, ct)</c> ⇒ sin
/// <c>comprador</c> si <c>GetSalidaDeBorradorAtAsync</c> devuelve una fecha posterior al <c>expired_manually_at</c>.
/// Datos ficticios (documento 9000000xxx).
/// </remarks>
public sealed class IdentidadVencidaManualSalidaDeBorradorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset VencidaA = new(2026, 10, 22, 18, 13, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidadaA = new(2026, 10, 8, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SalioAntesDeAprobar = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SalioAntes = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SalioDespues = new(2026, 11, 6, 12, 33, 20, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string TipoDoc = "CC";
    private const string Documento = "9000000915";

    private static readonly string Clave = BiometricRules.IdentidadKey(Tenant, TipoDoc, Documento);

    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();

    /// <summary>Identidad aprobada el 8-oct, vencida a mano el 22-oct y con fin de vigencia previo el 7-nov (FT9-0000002).</summary>
    private static ProcedureInstanceBiometricValidation IdentidadVencida(Guid? tramiteId = null, string? parte = null)
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = tramiteId,
            PartyRole = parte,
            Status = BiometricEstados.Aprobado,
            Name = "Persona de prueba",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            Email = "persona@example.test",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero),
            ValidatedAt = ValidadaA,
            CreatedAt = new DateTimeOffset(2026, 10, 8, 13, 0, 0, TimeSpan.Zero),
            ValidUntil = new DateTimeOffset(2026, 11, 7, 5, 0, 0, TimeSpan.Zero),
        };
        v.VencerManualmente(VencidaA);
        return v;
    }

    private static ProcedureInstance Tramite(string status, DateTimeOffset? creado = null)
    {
        var i = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FT9-0000001",
            Status = status,
            CreatedAt = creado ?? new DateTimeOffset(2026, 11, 6, 12, 32, 0, TimeSpan.Zero),
        };
        i.Actors.Add(new ProcedureInstanceActor
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ProcedureInstanceId = i.Id,
            ActorType = "comprador",
            DocumentType = TipoDoc,
            DocumentNumber = Documento,
            FullName = "Persona de prueba",
            PersonType = "natural",
        });
        return i;
    }

    /// <summary>
    /// Simula el repositorio real: la identidad vencida de OTRO trámite solo se devuelve si el trámite que pregunta
    /// "salió con esa firma" (misma regla que <c>ProcedureInstanceRepository.FindProtegidaPorVencimientoManualAsync</c>).
    /// </summary>
    private void RepoConProtegida(ProcedureInstance instance, ProcedureInstanceBiometricValidation vencida, DateTimeOffset? salida)
    {
        _repo.FindVigenteApprovedByDocumentAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        _repo.GetSalidaDeBorradorAtAsync(instance.Id, instance.TenantId, Arg.Any<CancellationToken>())
            .Returns(salida);
        _repo.FindProtegidaPorVencimientoManualAsync(
                Tenant, TipoDoc, Documento, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call => BiometricRules.EsAprobadaVigenteParaTramite(
                vencida, TramiteEstado.Entregado, call.ArgAt<DateTimeOffset>(3), call.ArgAt<DateTimeOffset?>(4))
                ? vencida
                : null);
    }

    // ── Gate (ResolveApprovedPartiesAsync) ──

    [Fact]
    public async Task CasoSinPlacaSinFirma_IdentidadVencidaAntesDeCrearElTramite_NoAprueba()
    {
        var i = Tramite(TramiteEstado.Preasignacion);
        RepoConProtegida(i, IdentidadVencida(), SalioDespues);

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);

        aprobadas.Should().NotContain(BiometricRules.ParteComprador);
    }

    [Fact]
    public async Task TramiteCreadoAntesDelVencimiento_PeroSalioDeBorradorDespues_NoAprueba()
    {
        var i = Tramite(TramiteEstado.Preasignacion, creado: new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        RepoConProtegida(i, IdentidadVencida(), SalioDespues);

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);

        aprobadas.Should().NotContain(BiometricRules.ParteComprador);
    }

    [Fact]
    public async Task SalioFirmadoYDespuesVencieronLaIdentidad_SigueAprobado_HU13824()
    {
        var i = Tramite(TramiteEstado.Entregado, creado: new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
        RepoConProtegida(i, IdentidadVencida(), SalioAntes);

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);

        aprobadas.Should().Contain(BiometricRules.ParteComprador);
    }

    [Fact]
    public async Task SinHistorialDeSalida_ConservaElComportamientoActual_Protegida()
    {
        var i = Tramite(TramiteEstado.Entregado);
        RepoConProtegida(i, IdentidadVencida(), salida: null);

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);

        aprobadas.Should().Contain(BiometricRules.ParteComprador);
    }

    [Fact]
    public async Task Borrador_NuncaConservaLaIdentidadVencida()
    {
        var i = Tramite(TramiteEstado.Borrador);
        RepoConProtegida(i, IdentidadVencida(), salida: null);

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);

        aprobadas.Should().NotContain(BiometricRules.ParteComprador);
    }

    [Theory]
    [InlineData(true, true)]   // salió ANTES de vencerla: la fila propia sigue acreditando
    [InlineData(false, false)] // salió DESPUÉS: la fila propia vencida a mano no acredita
    public async Task FilaPropiaVencidaAMano_SoloAcreditaSiSalioAntesDeVencerla(bool salioAntes, bool aprobada)
    {
        var i = Tramite(TramiteEstado.Preasignacion);
        i.BiometricValidations.Add(IdentidadVencida(i.Id, BiometricRules.ParteComprador));
        RepoConProtegida(i, IdentidadVencida(), salioAntes ? SalioAntes : SalioDespues);
        _repo.FindProtegidaPorVencimientoManualAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null); // aísla la fila propia

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);

        aprobadas.Contains(BiometricRules.ParteComprador).Should().Be(aprobada);
    }

    [Fact]
    public async Task EnviarAlOt_AsignadoConIdentidadVencidaQueNuncaTuvo_RespondeFirmaPendiente()
    {
        var i = Tramite(TramiteEstado.Asignado);
        i.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = "ABC123", Source = "user" });
        RepoConProtegida(i, IdentidadVencida(), SalioDespues);
        _repo.GetByIdWithWizardGraphAsync(i.Id, i.TenantId, Arg.Any<CancellationToken>()).Returns(i);
        _repo.SaveChangesWithConcurrencyGuardAsync(Arg.Any<CancellationToken>()).Returns(true);

        // FirmaGate (lo que consume EnviarAlOtHandler) ve la parte sin firma...
        var faltantes = await new TramiteFirmaGate(_repo).PartesSinFirmaAsync(i.Id, i.TenantId, Ct);
        faltantes.Should().Equal("comprador");

        // ...y la transición Asignado → Entregado responde firma_pendiente.
        var grant = Substitute.For<ITransitOfficeGrantGate>();
        grant.IsEnabledForTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var operable = Substitute.For<IOtOperabilityGate>();
        operable.IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var typeRepo = Substitute.For<IProcedureTypeRepository>();
        var lifecycle = new TramiteLifecycleService(
            _repo, typeRepo, grant, operable, NullOtRuleGate.Instance,
            new Estados.RecordingTransitionRecorder(), new Estados.RecordingTransitionPublisher());

        var outcome = await lifecycle.TransitionAsync(
            new TramiteTransitionCommand(i.Id, i.TenantId, TramiteEstado.Entregado, null, null), Ct);

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be(TramiteEstadoErrores.FirmaPendiente);
    }

    // ── Listado (ApprovedPartiesFromKeys / ListProcedureInstancesHandler) ──

    public static TheoryData<string, bool, bool, bool> CasosDeSalida => new()
    {
        { TramiteEstado.Preasignacion, true, false, false },   // caso real: salió después de vencerla
        { TramiteEstado.Entregado, true, true, true },         // HU #13824 legítimo
        { TramiteEstado.Entregado, false, false, true },       // sin historial: comportamiento actual
        { TramiteEstado.Borrador, false, false, false },       // borrador no la conserva
    };

    [Theory]
    [MemberData(nameof(CasosDeSalida))]
    public void ApprovedPartiesFromKeys_AplicaLaMismaRegla(string status, bool conSalida, bool salioAntes, bool aprobada)
    {
        var i = Tramite(status);
        DateTimeOffset? salida = conSalida ? (salioAntes ? SalioAntes : SalioDespues) : null;
        var protegidas = Protegidas(new IdentidadVencidaManual(ValidadaA, VencidaA));

        var aprobadas = IdentityApprovalResolver.ApprovedPartiesFromKeys(
            i, new HashSet<string>(), Now, null, protegidas, salida);

        aprobadas.Contains(BiometricRules.ParteComprador).Should().Be(aprobada);
    }

    private static Dictionary<string, IReadOnlyList<IdentidadVencidaManual>> Protegidas(params IdentidadVencidaManual[] identidades) =>
        new() { [Clave] = identidades };

    private void PrepararListado(IReadOnlyList<ProcedureInstance> instancias, IReadOnlyDictionary<Guid, DateTimeOffset> salidas)
    {
        _repo.ListWithSummaryGraphAsync(Tenant, ListProcedureInstancesHandler.MaxItems, Arg.Any<CancellationToken>())
            .Returns(instancias);
        _repo.GetTenantNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.GetUserDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string>());
        _repo.ListVigenteApprovedIdentityKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        _repo.ListProtegidasPorVencimientoManualKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Protegidas(new IdentidadVencidaManual(ValidadaA, VencidaA)));
        _repo.ListFirmaBaulVigenciaKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>(StringComparer.Ordinal));
        _repo.ListSalidasDeBorradorAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(salidas);
    }

    [Fact]
    public async Task Listado_CasoSinPlacaSinFirma_FilaPendiente_YElGateCoincide()
    {
        var i = Tramite(TramiteEstado.Preasignacion);
        PrepararListado([i], new Dictionary<Guid, DateTimeOffset> { [i.Id] = SalioDespues });

        var fila = (await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct)).Should().ContainSingle().Subject;

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
        fila.IdentityValidationStatus.Should().NotBe(BiometricEstados.Aprobado);

        // El gate (por instancia) dice lo mismo que el listado.
        RepoConProtegida(i, IdentidadVencida(), SalioDespues);
        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);
        aprobadas.Contains(BiometricRules.ParteComprador).Should().BeFalse();
    }

    [Fact]
    public async Task Listado_SalioFirmadoYDespuesVencieronLaIdentidad_SigueFirmado()
    {
        var i = Tramite(TramiteEstado.Entregado);
        PrepararListado([i], new Dictionary<Guid, DateTimeOffset> { [i.Id] = SalioAntes });

        var fila = (await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct)).Should().ContainSingle().Subject;

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    [Fact]
    public async Task Listado_ResuelveLasSalidasEnUnaSolaConsulta_SinNMasUno()
    {
        var a = Tramite(TramiteEstado.Preasignacion);
        var b = Tramite(TramiteEstado.Entregado);
        var c = Tramite(TramiteEstado.Preasignacion);
        var borrador = Tramite(TramiteEstado.Borrador);
        PrepararListado(
            [a, b, c, borrador],
            new Dictionary<Guid, DateTimeOffset> { [a.Id] = SalioDespues, [b.Id] = SalioAntes, [c.Id] = SalioDespues });

        var filas = await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct);

        filas.Should().HaveCount(4);
        await _repo.Received(1).ListSalidasDeBorradorAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(tenants => tenants.Count == 1 && tenants.Contains(Tenant)),
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 3 && !ids.Contains(borrador.Id)),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetSalidaDeBorradorAtAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listado_SinIdentidadesVencidasAMano_NoConsultaLasSalidas()
    {
        var i = Tramite(TramiteEstado.Preasignacion);
        PrepararListado([i], new Dictionary<Guid, DateTimeOffset>());
        _repo.ListProtegidasPorVencimientoManualKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<IdentidadVencidaManual>>());

        await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct);

        await _repo.DidNotReceive().ListSalidasDeBorradorAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Listado_TramiteSinFilaDeSalida_ConservaElComportamientoActual_Firmado()
    {
        // Legado/migrados: la instancia no aparece en el diccionario de salidas (no se trata como «salió en el año 1»).
        var i = Tramite(TramiteEstado.Entregado);
        PrepararListado([i], new Dictionary<Guid, DateTimeOffset>());

        var fila = (await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct)).Should().ContainSingle().Subject;

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    [Fact]
    public async Task T1_T4_Listado_MuestraPendiente_YCoincideConElGate()
    {
        // T1 salida (5-oct) < T2 aprobación (8-oct) < T3 vencimiento (22-oct) < T4 «Enviar al OT».
        var i = Tramite(TramiteEstado.Asignado);
        PrepararListado([i], new Dictionary<Guid, DateTimeOffset> { [i.Id] = SalioAntesDeAprobar });

        var fila = (await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct)).Should().ContainSingle().Subject;

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Pendiente);
    }

    [Fact]
    public async Task T1_T4_Listado_ConVariasIdentidadesVencidas_CuentaSiCualquieraCumple()
    {
        // Dos identidades vencidas de la persona; el trámite salió el 10-oct, dentro de la primera (8 → 22 oct).
        var i = Tramite(TramiteEstado.Entregado);
        PrepararListado([i], new Dictionary<Guid, DateTimeOffset> { [i.Id] = SalioAntes });
        _repo.ListProtegidasPorVencimientoManualKeysAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Protegidas(
                new IdentidadVencidaManual(
                    new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)),
                new IdentidadVencidaManual(ValidadaA, VencidaA)));

        var fila = (await new ListProcedureInstancesHandler(_repo).HandleAsync(Tenant, Ct)).Should().ContainSingle().Subject;

        fila.FirmaCompradorEstado.Should().Be(FirmaParteEstados.Firmado);
    }

    [Fact]
    public async Task T1_T4_SalioSinFirma_LaValidaronDespues_YLaVencieron_NoCuentaComoFirmaEnElGate()
    {
        var i = Tramite(TramiteEstado.Asignado);
        RepoConProtegida(i, IdentidadVencida(), SalioAntesDeAprobar);

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);

        aprobadas.Should().NotContain(BiometricRules.ParteComprador);
    }

    [Fact]
    public async Task T1_T4_FilaPropiaAprobadaDespuesDeSalir_YVencida_NoAcredita()
    {
        var i = Tramite(TramiteEstado.Asignado);
        i.BiometricValidations.Add(IdentidadVencida(i.Id, BiometricRules.ParteComprador));
        RepoConProtegida(i, IdentidadVencida(), SalioAntesDeAprobar);
        _repo.FindProtegidaPorVencimientoManualAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var aprobadas = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(_repo, i, Now, Ct);

        aprobadas.Should().NotContain(BiometricRules.ParteComprador);
    }

    [Fact]
    public void ConservaFirmaTrasVencimientoManual_ConVariasIdentidades_CuentaSiCualquieraCumple()
    {
        // Dos identidades vencidas a mano: 8-oct → 22-oct y 1-nov → 3-nov.
        IReadOnlyList<IdentidadVencidaManual> identidades =
        [
            new(ValidadaA, VencidaA),
            new(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)),
        ];

        BiometricRules.ConservaFirmaTrasVencimientoManual(
            TramiteEstado.Preasignacion, new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), identidades).Should().BeTrue();
        BiometricRules.ConservaFirmaTrasVencimientoManual(
            TramiteEstado.Preasignacion, SalioAntes, identidades).Should().BeTrue();
        BiometricRules.ConservaFirmaTrasVencimientoManual(
            TramiteEstado.Preasignacion, new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero), identidades).Should().BeFalse();
    }

    // ── Regla de dominio ──

    [Theory]
    [InlineData("preasignacion", "2026-10-10", "2026-10-08", "2026-10-22", true)]
    [InlineData("preasignacion", "2026-10-22", "2026-10-08", "2026-10-22", true)]  // el mismo instante cuenta como «salió con esa firma»
    [InlineData("preasignacion", "2026-10-08", "2026-10-08", "2026-10-22", true)]  // salió al aprobarse: ya estaba aprobada
    [InlineData("preasignacion", "2026-11-06", "2026-10-08", "2026-10-22", false)] // salió después de vencerla
    [InlineData("preasignacion", "2026-10-05", "2026-10-08", "2026-10-22", false)] // salió ANTES de que se aprobara (T1 < T2)
    [InlineData("preasignacion", null, "2026-10-08", "2026-10-22", true)]          // sin historial: legado
    [InlineData("preasignacion", "2026-10-05", null, "2026-10-22", true)]          // sin fecha de aprobación (fixtures): solo el vencimiento
    [InlineData("borrador", "2026-10-10", "2026-10-08", "2026-10-22", false)]
    public void ConservaFirmaTrasVencimientoManual_ReglaUnica(
        string status, string? salida, string? validada, string vencida, bool esperado) =>
        BiometricRules.ConservaFirmaTrasVencimientoManual(
                status,
                salida is null ? null : DateTimeOffset.Parse(salida + "T00:00:00Z"),
                validada is null ? null : DateTimeOffset.Parse(validada + "T00:00:00Z"),
                DateTimeOffset.Parse(vencida + "T00:00:00Z"))
            .Should().Be(esperado);
}
