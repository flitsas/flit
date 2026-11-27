using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Identity.Rounds;
using Flit.Tramites.Application.UseCases.IdentityRounds;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

/// <summary>
/// Configurador GLOBAL de rondas de identidad: validación de la configuración (dominio), bloqueo del reenvío al máximo en los
/// caminos (standalone, edición de correo, admin y portal), disparador <c>al_maximo</c> vs <c>cada_ronda</c> y alerta idempotente.
/// El conteo es POR FILA: <c>1 + ResendCount</c>.
/// </summary>
public sealed class IdentityRoundsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

    // ── Validación de la configuración (dominio) ─────────────────────────────────

    [Fact]
    public void SinLimite_NoBloqueaNingunaRonda_AunqueSuperenElMaximo()
    {
        var config = IdentityRoundsConfig.Create(3, IdentityRoundsAlertTriggers.AlMaximo, [], limiteActivo: false);

        config.AllowsNewRound(3).Should().BeTrue();
        config.AllowsNewRound(50).Should().BeTrue();
    }

    [Fact]
    public void SinLimite_AvisaASoporteCadaUmbralDeValidacionesDeLaPersona()
    {
        var config = IdentityRoundsConfig.Create(3, IdentityRoundsAlertTriggers.AlMaximo, [], limiteActivo: false, umbralAlertaSoporte: 3);

        config.ShouldAlert(1).Should().BeFalse();
        config.ShouldAlert(2).Should().BeFalse();
        config.ShouldAlert(3).Should().BeTrue();
        config.ShouldAlert(4).Should().BeFalse();
        config.ShouldAlert(6).Should().BeTrue();
    }

    [Fact]
    public void ConLimite_LaRegla_NoCambia()
    {
        var config = IdentityRoundsConfig.Create(3, IdentityRoundsAlertTriggers.AlMaximo, []);

        config.LimiteActivo.Should().BeTrue();
        config.AllowsNewRound(2).Should().BeTrue();
        config.AllowsNewRound(3).Should().BeFalse();
        config.ShouldAlert(2).Should().BeFalse();
        config.ShouldAlert(3).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Create_RechazaUmbralFueraDeRango(int umbral)
    {
        var act = () => IdentityRoundsConfig.Create(3, IdentityRoundsAlertTriggers.AlMaximo, [], umbralAlertaSoporte: umbral);

        act.Should().Throw<IdentityRoundsConfigException>()
            .Which.Errors.Should().ContainKey("umbralAlertaSoporte");
    }

    [Fact]
    public void Default_TrayectoriaDeFabricaConLimiteActivoYUmbralTres()
    {
        IdentityRoundsConfig.Default.LimiteActivo.Should().BeTrue();
        IdentityRoundsConfig.Default.UmbralAlertaSoporte.Should().Be(3);
    }


    [Fact]
    public void Default_Es3RondasAlMaximoSinCorreos()
    {
        var d = IdentityRoundsConfig.Default;
        d.RondasMaximas.Should().Be(3);
        d.DisparadorAlerta.Should().Be("al_maximo");
        d.CorreosAlerta.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void Create_AceptaElRango1a10(int rondas) =>
        IdentityRoundsConfig.Create(rondas, "cada_ronda", null).RondasMaximas.Should().Be(rondas);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Create_RechazaRondasFueraDeRango(int rondas)
    {
        var act = () => IdentityRoundsConfig.Create(rondas, "al_maximo", null);
        act.Should().Throw<IdentityRoundsConfigException>().Which.Errors.Should().ContainKey("rondasMaximas");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("siempre")]
    [InlineData("AL_MAXIMO")]
    public void Create_RechazaDisparadorDesconocido(string? disparador)
    {
        var act = () => IdentityRoundsConfig.Create(3, disparador, null);
        act.Should().Throw<IdentityRoundsConfigException>().Which.Errors.Should().ContainKey("disparadorAlerta");
    }

    [Fact]
    public void Create_NormalizaCorreosAMinusculaYRecorta()
    {
        var c = IdentityRoundsConfig.Create(3, "al_maximo", ["  Ops@Flit.CO ", "otro@flit.co"]);
        c.CorreosAlerta.Should().Equal("ops@flit.co", "otro@flit.co");
    }

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("a@b")]
    [InlineData("Nombre <a@b.co>")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RechazaCorreosInvalidos(string correo)
    {
        var act = () => IdentityRoundsConfig.Create(3, "al_maximo", [correo]);
        act.Should().Throw<IdentityRoundsConfigException>().Which.Errors.Should().ContainKey("correosAlerta");
    }

    [Fact]
    public void Create_RechazaCorreosRepetidosSinImportarMayusculas()
    {
        var act = () => IdentityRoundsConfig.Create(3, "al_maximo", ["a@flit.co", "A@FLIT.CO"]);
        act.Should().Throw<IdentityRoundsConfigException>().Which.Errors["correosAlerta"].Should().Contain("Hay correos repetidos.");
    }

    [Fact]
    public void Create_AceptaHasta20Correos_YRechazaMas()
    {
        var veinte = Enumerable.Range(1, 20).Select(i => $"u{i}@flit.co").ToList();
        IdentityRoundsConfig.Create(3, "al_maximo", veinte).CorreosAlerta.Should().HaveCount(20);

        veinte.Add("u21@flit.co");
        var act = () => IdentityRoundsConfig.Create(3, "al_maximo", veinte);
        act.Should().Throw<IdentityRoundsConfigException>().Which.Errors.Should().ContainKey("correosAlerta");
    }

    [Fact]
    public void Create_DevuelveTodosLosErroresJuntos()
    {
        var act = () => IdentityRoundsConfig.Create(0, "x", ["malo"]);
        act.Should().Throw<IdentityRoundsConfigException>().Which.Errors.Keys
            .Should().BeEquivalentTo("rondasMaximas", "disparadorAlerta", "correosAlerta");
    }

    // ── Conteo por fila ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(2, 3, false)]
    [InlineData(5, 6, false)]
    public void RondasUsadas_EsUnoMasResendCount_YElReenvioSeBloqueaAlMaximo(int resendCount, int usadas, bool permite)
    {
        var v = Validation(resendCount);
        IdentityRoundsConfig.RoundsUsed(v).Should().Be(usadas);
        IdentityRoundsConfig.Default.AllowsNewRound(v).Should().Be(permite);
    }

    [Theory]
    [InlineData("al_maximo", 0, false)]
    [InlineData("al_maximo", 1, false)]
    [InlineData("al_maximo", 2, true)]
    [InlineData("cada_ronda", 0, true)]
    [InlineData("cada_ronda", 1, true)]
    [InlineData("cada_ronda", 2, true)]
    public void ShouldAlert_SegunElDisparador(string disparador, int resendCount, bool alerta) =>
        IdentityRoundsConfig.Create(3, disparador, null).ShouldAlert(Validation(resendCount)).Should().Be(alerta);

    // ── Bloqueo del reenvío en los caminos ───────────────────────────────────────

    [Fact]
    public async Task Reenvio_Standalone_AlMaximo_ResponderLimite_SinTocarLaFilaNiElProveedor()
    {
        var env = new Env(rondas: 3);
        var validation = env.SeedStandalone(resendCount: 2);
        var handler = new ReenviarPrevalidacionHandler(env.Repo, env.Kyverum, env.Mock, env.Protector, env.Events, env.Audit, env.Policy);

        var (result, error, _) = await handler.HandleAsync(env.TenantId, validation.Id, TestContext.Current.CancellationToken);

        error.Should().Be("limite_rondas_alcanzado");
        result.Should().BeNull();
        validation.ResendCount.Should().Be(2);
        await env.Events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationRequested>(), Arg.Any<CancellationToken>());
        await env.Repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reenvio_Standalone_ConCupo_Reenvia()
    {
        var env = new Env(rondas: 3);
        var validation = env.SeedStandalone(resendCount: 1);
        var handler = new ReenviarPrevalidacionHandler(env.Repo, env.Kyverum, env.Mock, env.Protector, env.Events, env.Audit, env.Policy);

        var (result, error, _) = await handler.HandleAsync(env.TenantId, validation.Id, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        validation.ResendCount.Should().Be(2);
    }

    [Fact]
    public async Task Reenvio_ElTopeEsConfigurable_ConUnaRondaNoSePuedeReenviarNunca()
    {
        var env = new Env(rondas: 1);
        var validation = env.SeedStandalone(resendCount: 0);
        var handler = new ReenviarPrevalidacionHandler(env.Repo, env.Kyverum, env.Mock, env.Protector, env.Events, env.Audit, env.Policy);

        var (_, error, _) = await handler.HandleAsync(env.TenantId, validation.Id, TestContext.Current.CancellationToken);

        error.Should().Be("limite_rondas_alcanzado");
    }

    [Fact]
    public async Task EdicionDeCorreo_AlMaximo_BloqueaElReenvioAutomatico_YNoCambiaNada()
    {
        var env = new Env(rondas: 3);
        var validation = env.SeedStandalone(resendCount: 2);
        var handler = new EditarPrevalidacionHandler(env.Repo, env.Kyverum, env.Mock, env.Protector, env.Events, env.Audit, env.Policy);

        var (result, error, _) = await handler.HandleAsync(
            env.TenantId, validation.Id, new EditarPrevalidacionRequest(Email: "nuevo@correo.com"), TestContext.Current.CancellationToken);

        error.Should().Be("limite_rondas_alcanzado");
        result.Should().BeNull();
        validation.Person!.Email.Should().Be("ana@old.com");
    }

    [Fact]
    public async Task Reenvio_Admin_AlMaximo_ResponderLimite()
    {
        var env = new Env(rondas: 3);
        var instance = new ProcedureInstance
        {
            Id = Guid.NewGuid(), TenantId = env.TenantId, ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "FT1-1", Status = TramiteEstado.Entregado, CreatedAt = Now,
        };
        var validation = env.SeedTramite(instance.Id, resendCount: 2);
        env.Repo.GetByIdAsync(instance.Id, env.TenantId, Arg.Any<CancellationToken>()).Returns(instance);
        var kyverumVerify = new IniciarKyverumVerifyHandler(env.Repo, env.Kyverum, env.Protector, env.Events, env.Audit);
        var handler = new AdminReenviarValidacionIdentidadHandler(
            env.Repo, env.Kyverum, env.Mock, env.Protector, env.Events, env.Audit, kyverumVerify, env.Policy);

        var (result, error, _, _) = await handler.HandleAsync(
            new AdminReenviarValidacionIdentidadCommand(instance.Id, validation.Id, null, null, env.TenantId, null, Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        error.Should().Be("limite_rondas_alcanzado");
        result.Should().BeNull();
        validation.ResendCount.Should().Be(2);
    }

    [Fact]
    public async Task Reenvio_Portal_AlMaximo_ResponderLimite()
    {
        var env = new Env(rondas: 3);
        var validation = env.SeedTramite(null, resendCount: 2);
        env.Repo.GetPortalValidationByIdAsync(env.TenantId, validation.Id, Arg.Any<CancellationToken>()).Returns(validation);
        var handler = new ReenviarValidacionPortalHandler(env.Repo, env.Kyverum, env.Mock, env.Protector, env.Events, env.Audit, env.Policy);

        var (result, error) = await handler.HandleAsync(env.TenantId, validation.Id, null, TestContext.Current.CancellationToken);

        error.Should().Be("limite_rondas_alcanzado");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Reenvio_Portal_ConCupo_Reenvia()
    {
        var env = new Env(rondas: 3);
        var validation = env.SeedTramite(null, resendCount: 1);
        env.Repo.GetPortalValidationByIdAsync(env.TenantId, validation.Id, Arg.Any<CancellationToken>()).Returns(validation);
        var handler = new ReenviarValidacionPortalHandler(env.Repo, env.Kyverum, env.Mock, env.Protector, env.Events, env.Audit, env.Policy);

        var (result, error) = await handler.HandleAsync(env.TenantId, validation.Id, null, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        validation.ResendCount.Should().Be(2);
    }

    // ── Alerta: disparador e idempotencia ────────────────────────────────────────

    [Fact]
    public async Task Alerta_AlMaximo_SoloEnLaUltimaRondaPermitida()
    {
        var env = new Env(rondas: 3, disparador: "al_maximo", correos: ["ops@flit.co"]);

        await env.NotifyAsync(Rechazada(resendCount: 0), Now, TestContext.Current.CancellationToken);
        await env.NotifyAsync(Rechazada(resendCount: 1), Now, TestContext.Current.CancellationToken);
        env.Sender.Sent.Should().BeEmpty();
        env.Store.Alerts.Should().BeEmpty();

        var ultima = Rechazada(resendCount: 2);
        await env.NotifyAsync(ultima, Now, TestContext.Current.CancellationToken);

        env.Sender.Sent.Should().ContainSingle();
        var (mensaje, destinatarios) = env.Sender.Sent.Single();
        destinatarios.Should().Equal("ops@flit.co");
        mensaje.RondasUsadas.Should().Be(3);
        mensaje.RondasMaximas.Should().Be(3);
        mensaje.Nombre.Should().Be("Ana Ríos");
        mensaje.NumeroDocumento.Should().Be("1020304050");
        env.Store.Alerts.Single().Resultado.Should().Be("enviado");
    }

    [Fact]
    public async Task Persona_RondasDeTodasSusFilas_BloqueanUnaFilaNuevaAunqueSuFilaTengaCeroReenvios()
    {
        // La persona lleva 3 rondas repartidas entre dos filas (p. ej. 2 en la fila A y 1 en la fila B). Con máximo 3, una fila nueva
        // con 0 reenvíos NO debe abrir otra ronda: el límite es por persona en la empresa, no por fila.
        var ct = TestContext.Current.CancellationToken;
        var env = new Env(rondas: 3);
        env.Store.PersonRounds = 3;

        (await env.Policy.IsPersonBlockedAsync(env.TenantId, "CC", "1020304050", ct)).Should().BeTrue();
        (await env.Policy.IsResendBlockedAsync(Rechazada(resendCount: 0), ct)).Should().BeTrue();

        env.Store.PersonRounds = 2;
        (await env.Policy.IsPersonBlockedAsync(env.TenantId, "CC", "1020304050", ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Alerta_CadaRonda_EnCualquierRondaRechazada()
    {
        var env = new Env(rondas: 3, disparador: "cada_ronda", correos: ["ops@flit.co"]);

        await env.NotifyAsync(Rechazada(resendCount: 0), Now, TestContext.Current.CancellationToken);
        await env.NotifyAsync(Rechazada(resendCount: 1), Now, TestContext.Current.CancellationToken);
        await env.NotifyAsync(Rechazada(resendCount: 2), Now, TestContext.Current.CancellationToken);

        env.Sender.Sent.Should().HaveCount(3);
        env.Store.Alerts.Select(a => a.Ronda).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Alerta_EsIdempotentePorRonda_ElMismoRechazoDosVecesMandaUnSoloCorreo()
    {
        var env = new Env(rondas: 3, disparador: "al_maximo", correos: ["ops@flit.co", "jefe@flit.co"]);
        var v = Rechazada(resendCount: 2);

        await env.NotifyAsync(v, Now, TestContext.Current.CancellationToken);
        await env.NotifyAsync(v, Now.AddMinutes(1), TestContext.Current.CancellationToken); // reconciliación / webhook repetido

        env.Sender.Sent.Should().ContainSingle();
        env.Store.Alerts.Should().ContainSingle();
    }

    [Fact]
    public async Task Alerta_SinDestinatarios_SoloAudita_NoEnvia()
    {
        var env = new Env(rondas: 3, disparador: "cada_ronda", correos: []);

        await env.NotifyAsync(Rechazada(resendCount: 0), Now, TestContext.Current.CancellationToken);

        env.Sender.Sent.Should().BeEmpty();
        env.Store.Alerts.Single().Resultado.Should().Be("sin_destinatarios");
    }

    [Fact]
    public async Task Alerta_NoAplicaSiLaFilaNoEstaRechazadaOAunTieneIntentos()
    {
        var env = new Env(rondas: 1, disparador: "cada_ronda", correos: ["ops@flit.co"]);

        var enProceso = Rechazada(0);
        enProceso.Status = BiometricEstados.EnProceso;
        await env.NotifyAsync(enProceso, Now, TestContext.Current.CancellationToken);

        var prematuro = Rechazada(0);
        prematuro.Provider = BiometricProviders.Kyverum;
        prematuro.KyverumVerificationId = "kyv_1";
        prematuro.Attempts = 1; // rechazo con intentos disponibles: aún puede aprobarse
        prematuro.MaxAttempts = 3;
        await env.NotifyAsync(prematuro, Now, TestContext.Current.CancellationToken);

        env.Sender.Sent.Should().BeEmpty();
        env.Store.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Alerta_ConFalloDelCorreo_QuedaFallido_YNoLanza()
    {
        var env = new Env(rondas: 3, disparador: "cada_ronda", correos: ["ops@flit.co"]) { };
        env.Sender.Fallidos = 1;

        var act = () => env.NotifyAsync(Rechazada(0), Now, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        env.Store.Alerts.Single().Resultado.Should().Be("fallido");
    }

    [Fact]
    public async Task Alerta_ConExcepcionDelEnvio_NoRompeLaAplicacionDelVeredicto()
    {
        var env = new Env(rondas: 3, disparador: "cada_ronda", correos: ["ops@flit.co"]);
        env.Sender.Throw = true;

        var act = () => env.NotifyAsync(Rechazada(0), Now, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    // ── Punto único de aplicación del veredicto ──────────────────────────────────

    [Fact]
    public async Task Applier_RechazoTerminal_NotificaUnaVez_YLaAprobacionNo()
    {
        var env = new Env(rondas: 3, disparador: "cada_ronda", correos: ["ops@flit.co"]);
        var applier = new IdentityValidationResultApplier(env.Events, rounds: env.Policy);

        var rechazada = Validation(0);
        rechazada.Provider = BiometricProviders.Kyverum;
        rechazada.Status = BiometricEstados.EnProceso;
        rechazada.Attempts = 3;
        rechazada.MaxAttempts = 3;
        await applier.ApplyAsync(rechazada, new IdentityValidationTerminalResult(false, "rechazado", "{}", 10), Now, TestContext.Current.CancellationToken);
        await applier.ApplyAsync(rechazada, new IdentityValidationTerminalResult(false, "rechazado", "{}", 10), Now, TestContext.Current.CancellationToken);

        var aprobada = Validation(0);
        aprobada.Status = BiometricEstados.EnProceso;
        await applier.ApplyAsync(aprobada, new IdentityValidationTerminalResult(true, "aprobado", "{}", 99), Now, TestContext.Current.CancellationToken);

        env.Sender.Sent.Should().ContainSingle();
        env.Sender.Sent.Single().Message.ValidationId.Should().Be(rechazada.Id);
    }

    // ── Configuración: handlers ──────────────────────────────────────────────────

    [Fact]
    public async Task Update_Invalida_LanzaYNoGuardaNiAudita()
    {
        var env = new Env(rondas: 3);
        var handler = new UpdateIdentityRoundsConfigHandler(env.Store, env.Auditor);

        var act = () => handler.HandleAsync(new UpdateIdentityRoundsConfigCommand(11, "al_maximo", [], Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<IdentityRoundsConfigException>();
        env.Store.Saves.Should().Be(0);
        env.Auditor.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_Valida_GuardaYAuditaConAnteriorNuevoYActor()
    {
        var env = new Env(rondas: 3);
        var actor = Guid.NewGuid();
        var handler = new UpdateIdentityRoundsConfigHandler(env.Store, env.Auditor);

        var saved = await handler.HandleAsync(
            new UpdateIdentityRoundsConfigCommand(5, "cada_ronda", ["Ops@Flit.co"], actor), TestContext.Current.CancellationToken);

        saved.RondasMaximas.Should().Be(5);
        saved.CorreosAlerta.Should().Equal("ops@flit.co");
        env.Store.Saves.Should().Be(1);
        env.Store.Config.RondasMaximas.Should().Be(5);
        var (quien, antes, despues) = env.Auditor.Entries.Single();
        quien.Should().Be(actor);
        antes.RondasMaximas.Should().Be(3);
        despues.RondasMaximas.Should().Be(5);
    }

    [Fact]
    public async Task Update_SinRondasMaximas_EsError()
    {
        var env = new Env(rondas: 3);
        var handler = new UpdateIdentityRoundsConfigHandler(env.Store, env.Auditor);

        var act = () => handler.HandleAsync(new UpdateIdentityRoundsConfigCommand(null, "al_maximo", [], null), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<IdentityRoundsConfigException>()).Which.Errors.Should().ContainKey("rondasMaximas");
    }

    // ── Infra de prueba ──────────────────────────────────────────────────────────

    private static ProcedureInstanceBiometricValidation Validation(int resendCount) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        Name = "Ana Ríos",
        DocumentType = "CC",
        DocumentNumber = "1020304050",
        Email = "ana@old.com",
        Status = BiometricEstados.Enviado,
        Provider = BiometricProviders.Mock,
        ResendCount = resendCount,
        CreatedAt = Now,
    };

    private static ProcedureInstanceBiometricValidation Rechazada(int resendCount)
    {
        var v = Validation(resendCount);
        v.Status = BiometricEstados.Rechazado;
        return v;
    }

    private sealed class FakeStore(IdentityRoundsConfig config) : IIdentityRoundsStore
    {
        public IdentityRoundsConfig Config { get; private set; } = config;

        public int Saves { get; private set; }

        public List<IdentityRoundsAlertRecord> Alerts { get; } = [];

        public Task<IdentityRoundsConfig> GetConfigAsync(CancellationToken ct = default) => Task.FromResult(Config);

        /// <summary>Rondas que la persona lleva en la empresa (suma de sus filas). Lo fija cada prueba.</summary>
        public int PersonRounds { get; set; } = 1;

        public Task<int> CountRoundsForPersonAsync(Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default) =>
            Task.FromResult(PersonRounds);

        public Task SaveConfigAsync(IdentityRoundsConfig newConfig, Guid? updatedBy, DateTimeOffset now, CancellationToken ct = default)
        {
            Config = newConfig;
            Saves++;
            return Task.CompletedTask;
        }

        public Task<bool> TryRegisterAlertAsync(IdentityRoundsAlertRecord alert, CancellationToken ct = default)
        {
            if (Alerts.Any(a => a.ValidationId == alert.ValidationId && a.Ronda == alert.Ronda))
                return Task.FromResult(false);
            Alerts.Add(alert);
            return Task.FromResult(true);
        }

        public Task CompleteAlertAsync(Guid alertId, string resultado, CancellationToken ct = default)
        {
            var i = Alerts.FindIndex(a => a.Id == alertId);
            Alerts[i] = Alerts[i] with { Resultado = resultado };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender : IIdentityRoundsAlertSender
    {
        public List<(IdentityRoundsAlertMessage Message, IReadOnlyList<string> Recipients)> Sent { get; } = [];

        public int Fallidos { get; set; }

        public bool Throw { get; set; }

        public Task<IdentityRoundsAlertDelivery> SendAsync(
            IdentityRoundsAlertMessage message, IReadOnlyList<string> recipients, CancellationToken ct = default)
        {
            if (Throw)
                throw new InvalidOperationException("smtp caído");
            Sent.Add((message, recipients));
            return Task.FromResult(new IdentityRoundsAlertDelivery(Fallidos > 0 ? 0 : recipients.Count, Fallidos));
        }
    }

    private sealed class FakeAuditor : IIdentityRoundsConfigAuditor
    {
        public List<(Guid? Actor, IdentityRoundsConfig Before, IdentityRoundsConfig After)> Entries { get; } = [];

        public Task WriteAsync(Guid? actorUserId, IdentityRoundsConfig before, IdentityRoundsConfig after, CancellationToken ct = default)
        {
            Entries.Add((actorUserId, before, after));
            return Task.CompletedTask;
        }
    }

    private sealed class Env
    {
        public Guid TenantId { get; } = Guid.NewGuid();

        public IProcedureInstanceRepository Repo { get; } = Substitute.For<IProcedureInstanceRepository>();

        public IKyverumVerifyClient Kyverum { get; } = Substitute.For<IKyverumVerifyClient>();

        public FakeWebhookSecretProtector Protector { get; } = new();

        public IIdentityValidationEventPublisher Events { get; } = Substitute.For<IIdentityValidationEventPublisher>();

        public IIdentityValidationAuditLog Audit { get; } = Substitute.For<IIdentityValidationAuditLog>();

        public BiometricsProviderOptions Mock { get; } = new() { Provider = BiometricProviders.Mock };

        public FakeStore Store { get; }

        public FakeSender Sender { get; } = new();

        public FakeAuditor Auditor { get; } = new();

        public IdentityRoundsPolicy Policy { get; }

        public Env(int rondas, string disparador = "al_maximo", string[]? correos = null)
        {
            Store = new FakeStore(IdentityRoundsConfig.Create(rondas, disparador, correos));
            Policy = new IdentityRoundsPolicy(Store, Sender, NullLogger<IdentityRoundsPolicy>.Instance);
        }

        /// <summary>Notifica el rechazo de la ronda; fija antes las rondas de la persona (en estas pruebas, la de la fila).</summary>
        public Task NotifyAsync(ProcedureInstanceBiometricValidation v, DateTimeOffset now, CancellationToken ct)
        {
            Store.PersonRounds = IdentityRoundsConfig.RoundsUsed(v);
            return Policy.NotifyRoundRejectedAsync(v, now, ct);
        }

        public ProcedureInstanceBiometricValidation SeedStandalone(int resendCount)
        {
            var person = new Person
            {
                Id = Guid.NewGuid(), TenantId = TenantId, DocumentType = "CC", DocumentNumber = "1020304050",
                FullName = "Ana Ríos", Email = "ana@old.com", PersonType = PersonTypes.Natural, CreatedAt = Now,
            };
            var v = new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(), TenantId = TenantId, PersonId = person.Id, Person = person, ProcedureInstanceId = null,
                Name = person.FullName, DocumentType = person.DocumentType, DocumentNumber = person.DocumentNumber, Email = person.Email,
                Status = BiometricEstados.Rechazado, Provider = BiometricProviders.Mock, TokenHash = "h",
                ExpiresAt = Now.AddHours(-1), ResendCount = resendCount, CreatedAt = Now,
            };
            Repo.GetBiometricByIdWithPersonAsync(v.Id, TenantId, Arg.Any<CancellationToken>()).Returns(v);
            Store.PersonRounds = 1 + resendCount;
            return v;
        }

        public ProcedureInstanceBiometricValidation SeedTramite(Guid? instanceId, int resendCount)
        {
            var v = new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ProcedureInstanceId = instanceId, PartyRole = BiometricRules.ParteComprador,
                Name = "Ana Ríos", DocumentType = "CC", DocumentNumber = "1020304050", Email = "ana@old.com", RegisteredEmail = "ana@old.com",
                Status = BiometricEstados.Rechazado, Provider = BiometricProviders.Mock, TokenHash = "h",
                ExpiresAt = Now.AddHours(-1), ResendCount = resendCount, CreatedAt = Now,
            };
            Repo.GetBiometricByIdWithPersonAsync(v.Id, TenantId, Arg.Any<CancellationToken>()).Returns(v);
            Store.PersonRounds = 1 + resendCount;
            return v;
        }
    }
}
