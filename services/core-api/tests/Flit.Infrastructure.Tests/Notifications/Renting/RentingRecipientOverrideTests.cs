using System.Reflection;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Renting;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11364 / ADR-0044 — desvío al buzón de control (<see cref="RentingRecipientOverride"/>), con
/// interruptor afirmativo propio del despliegue
/// (<see cref="RentingChannelOptions.SendRealRecipientsEnabled"/>). El default seguro del <c>bool</c>
/// sin inicializar (<c>false</c>) significa "desviar" — por eso los ejemplos de abajo que quieren
/// desviar dejan la propiedad SIN tocar, y solo la declaran explícitamente en <c>true</c> cuando el
/// escenario bajo prueba es el envío real.
/// <para>
/// Uso de ejemplo del componente bajo prueba:
/// <code>
/// var overrideImpl = new RentingRecipientOverride(
///     Options.Create(new RentingChannelOptions { SendRealRecipientsEnabled = false, ... }),
///     logger);
/// var effective = overrideImpl.Apply(request);
/// </code>
/// </para>
/// <para>
/// AC3/AC4 (validación de arranque) se prueban aparte, en
/// <c>RentingRecipientOverrideStartupGateTests</c> (invocan
/// <c>InfrastructureExtensions.AddRentingChannel</c>, que es donde vive esa validación).
/// </para>
/// </summary>
public sealed class RentingRecipientOverrideTests
{
    private static RentingSendEmailRequest SampleRequest() => new(
        Subject: "Asunto de prueba",
        Body: "Cuerpo de prueba",
        Sender: new RentingEmailAddress("remitente@example.test", "Remitente FLIT"),
        Recipients: [new RentingEmailAddress("cliente-real@example.test", "Cliente Real")],
        BccRecipients: ["bcc-real@example.test"],
        Attachments: []);

    /// <param name="divertEnabled">
    /// <c>true</c> ⇒ desvía (equivalente al default seguro del despliegue); <c>false</c> ⇒ envío
    /// real (<see cref="RentingChannelOptions.SendRealRecipientsEnabled"/> = <c>true</c>).
    /// </param>
    private static RentingRecipientOverride NewOverride(
        bool divertEnabled, string? recipientEmail = "desvio@example.test", string? recipientUsername = "Desvío QA",
        ILogger<RentingRecipientOverride>? logger = null) =>
        new(
            Options.Create(new RentingChannelOptions
            {
                SendRealRecipientsEnabled = !divertEnabled,
                SendEmailDevelopmentRecipientEmail = recipientEmail ?? string.Empty,
                SendEmailDevelopmentRecipientUsername = recipientUsername ?? string.Empty,
            }),
            logger ?? NullLogger<RentingRecipientOverride>.Instance);

    // ── AC1 — el desvío solo actúa con su interruptor afirmativo ───────────────────

    [Fact]
    public void Apply_ConInterruptorActivoYDestinatarioConfigurado_LlevaElDestinatarioDeDesvioYNoElOriginal()
    {
        var overrideImpl = NewOverride(divertEnabled: true);

        var result = overrideImpl.Apply(SampleRequest());

        // El desvío queda marcado en el resultado (HU #11364 AC2 — es lo que sube hasta
        // EmailSendResult.RecipientDiverted y de ahí a la bitácora).
        result.Diverted.Should().BeTrue();

        // El destinatario de desvío SÍ aparece.
        var effective = result.Request;
        effective.Recipients.Should().ContainSingle();
        effective.Recipients[0].Email.Should().Be("desvio@example.test");
        effective.Recipients[0].UserName.Should().Be("Desvío QA");

        // El destinatario original NO aparece en NINGÚN campo de destinatario de la petición.
        effective.Recipients.Should().NotContain(r => r.Email == "cliente-real@example.test");
        effective.BccRecipients.Should().NotContain("bcc-real@example.test");
    }

    [Fact]
    public void Apply_ConInterruptorApagado_NoModificaLaPeticionYElDestinatarioOriginalSePreserva()
    {
        // Edge case — sin el interruptor propio en true, NO hay desvío, sin importar qué otra
        // configuración exista (AC5: solo decide el interruptor).
        var overrideImpl = NewOverride(divertEnabled: false);
        var request = SampleRequest();

        var result = overrideImpl.Apply(request);

        result.Diverted.Should().BeFalse();
        result.Request.Should().BeSameAs(request, "sin el interruptor activo la petición no se toca en absoluto");
        result.Request.Recipients[0].Email.Should().Be("cliente-real@example.test");
    }

    // ── L2 (revisión de seguridad, ADR-0044) — el default del CLR es el estado seguro ──

    [Fact]
    public void RentingChannelOptions_ConstruidoSinTocarNada_DejaSendRealRecipientsEnabledEnFalse()
    {
        // Regresión de polaridad: un `bool` sin inicializar en C# vale `false` por default del CLR.
        // Con SendRealRecipientsEnabled esa condición significa "NO enviar real", es decir, desviar
        // — el estado seguro. (El derogado DivertRecipientsEnabled tenía la polaridad opuesta: su
        // `false` por defecto significaba "envía real".)
        var options = new RentingChannelOptions();

        options.SendRealRecipientsEnabled.Should().BeFalse(
            "un RentingChannelOptions recién construido sin tocar debe significar «desviar»");
    }

    [Fact]
    public void Apply_ConOptionsRecienConstruidoSinTocar_DesviaPorDefecto()
    {
        // Mismo criterio que el test anterior, pero ejercitado de punta a punta contra el
        // componente real (no solo inspeccionando la propiedad): construir RentingChannelOptions
        // sin asignar SendRealRecipientsEnabled y sin configurar el buzón de control igual debe
        // producir un desvío (aunque el destinatario de desvío quede vacío, Diverted es true).
        var overrideImpl = new RentingRecipientOverride(
            Options.Create(new RentingChannelOptions()), NullLogger<RentingRecipientOverride>.Instance);

        var result = overrideImpl.Apply(SampleRequest());

        result.Diverted.Should().BeTrue(
            "un RentingChannelOptions() sin tocar debe significar desvío activo, no envío real");
    }

    // ── AC2 — el desvío queda registrado ────────────────────────────────────────────

    [Fact]
    public void Apply_ConInterruptorActivo_LaBitacoraRegistraElDestinatarioOriginalYUnaMarcaDeEnvioDesviado()
    {
        var logger = new CapturingLogger();
        var overrideImpl = NewOverride(divertEnabled: true, logger: logger);

        overrideImpl.Apply(SampleRequest());

        logger.Entries.Should().ContainSingle("un envío desviado debe dejar EXACTAMENTE un registro en la bitácora");
        var entry = logger.Entries[0];

        // Marca explícita de envío desviado (no un log genérico cualquiera).
        entry.Message.Should().Contain("DESVIADO");
        // El destinatario ORIGINAL (el real, antes de sustituirlo) queda en la bitácora.
        entry.Message.Should().Contain("cliente-real@example.test");
        entry.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void Apply_ConInterruptorApagado_NoRegistraNadaEnLaBitacora()
    {
        // Contrato — sin desvío, no hay nada de "envío desviado" que registrar.
        var logger = new CapturingLogger();
        var overrideImpl = NewOverride(divertEnabled: false, logger: logger);

        overrideImpl.Apply(SampleRequest());

        logger.Entries.Should().BeEmpty();
    }

    // ── AC5 — el desvío no se deduce del nombre del ambiente ───────────────────────

    [Fact]
    public void RentingRecipientOverride_NoTieneNingunaDependenciaDeIHostEnvironment()
    {
        // Regresión estructural: si alguien intentara decidir el desvío por environment.IsDevelopment()
        // tendría que inyectar IHostEnvironment en este tipo. Que el constructor JAMÁS lo reciba es
        // la prueba de que la única señal posible es el interruptor propio (AC5).
        var constructors = typeof(RentingRecipientOverride).GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

        constructors.Should().NotBeEmpty();
        foreach (var ctor in constructors)
        {
            ctor.GetParameters().Should().NotContain(
                p => typeof(IHostEnvironment).IsAssignableFrom(p.ParameterType),
                "el desvío se decide SOLO por RentingChannelOptions.SendRealRecipientsEnabled (ADR-0044)");
        }
    }

    // ── AC6 — el desvío no afecta al transporte SMTP ────────────────────────────────

    [Fact]
    public void SmtpEmailSender_NoDependeDeIRentingRecipientOverride()
    {
        // AC6/estructural — el desvío vive DENTRO del adaptador Renting (consumido únicamente por
        // RentingEmailApiSender.SendAsync). SmtpEmailSender no conoce IRentingRecipientOverride ni
        // RentingChannelOptions: no hay una condición que alguien pueda desactivar por error, es que
        // el flujo SMTP NUNCA pasa por ahí. Un envío SMTP con el interruptor activo va al
        // destinatario real porque este tipo ni siquiera podría invocar el desvío.
        var constructors = typeof(SmtpEmailSender).GetConstructors(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

        constructors.Should().NotBeEmpty();
        foreach (var ctor in constructors)
        {
            ctor.GetParameters().Should().NotContain(p => p.ParameterType == typeof(IRentingRecipientOverride));
            ctor.GetParameters().Should().NotContain(p => p.ParameterType == typeof(RentingChannelOptions));
            ctor.GetParameters().Should().NotContain(
                p => p.ParameterType == typeof(IOptions<RentingChannelOptions>));
        }
    }

    private sealed class CapturingLogger : ILogger<RentingRecipientOverride>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
