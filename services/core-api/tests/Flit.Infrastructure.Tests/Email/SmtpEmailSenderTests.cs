using Flit.Infrastructure.Email;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Infrastructure.Tests.Email;

/// <summary>
/// Cierra el ciego de <see cref="SmtpEmailSender"/>: antes de este cambio la clase no tenía
/// <see cref="ILogger"/> y descartaba en silencio toda excepción de MailKit. Cubre el retorno
/// temprano por configuración incompleta (sin tocar la red) y, con un puerto local sin listener,
/// la rama <c>catch (SocketException)</c> real — sin depender de conectividad a Internet.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var logger = new CapturingLogger&lt;SmtpEmailSender&gt;();
/// var sender = new SmtpEmailSender(settings, logger);
/// var result = await sender.SendAsync(message, CancellationToken.None);
/// </code>
/// </remarks>
public sealed class SmtpEmailSenderTests
{
    private static readonly EmailMessage Message = new(
        TenantId: Guid.NewGuid(),
        TemplateKey: "security.forgot-password",
        ToEmail: "destinatario@flit.test",
        ToName: "Destinatario",
        Subject: "Asunto",
        HtmlBody: "<html>cuerpo</html>");

    // ── Configuración incompleta — no toca la red, siempre determinístico ────────────────────────

    [Fact]
    public async Task HostVacio_DevuelveConfiguracionIncompletaYRegistraLogDeError()
    {
        var settings = new EmailSettings
        {
            Host = string.Empty,
            Port = 587,
            DefaultSenderEmail = "no-reply@flit.test",
            DefaultSenderPassword = "cualquier-cosa",
        };
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(settings, logger);

        var result = await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        result.Should().Be(EmailSendResult.Failed(EmailSendOutcome.ConfigurationIncomplete));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error
            && e.Message.Contains("configuración incompleta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RemitenteVacio_DevuelveConfiguracionIncompletaYRegistraLogDeError()
    {
        var settings = new EmailSettings
        {
            Host = "smtp.flit.test",
            Port = 587,
            DefaultSenderEmail = string.Empty,
            DefaultSenderPassword = "cualquier-cosa",
        };
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(settings, logger);

        var result = await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        result.Should().Be(EmailSendResult.Failed(EmailSendOutcome.ConfigurationIncomplete));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error
            && e.Message.Contains("configuración incompleta", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("smtp.flit.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PasswordVacioConAutenticacionActiva_DevuelveConfiguracionIncompletaYRegistraLogDeError()
    {
        var settings = new EmailSettings
        {
            Host = "smtp.flit.test",
            Port = 587,
            DefaultSenderEmail = "no-reply@flit.test",
            DefaultSenderPassword = string.Empty,
            DisableAuthentication = false,
        };
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(settings, logger);

        var result = await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        result.Should().Be(EmailSendResult.Failed(EmailSendOutcome.ConfigurationIncomplete));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    // ── La contraseña NUNCA aparece en ningún log — la prueba importante ─────────────────────────

    [Fact]
    public async Task Password_NuncaApareceEnNingunLog_ConConfiguracionIncompletaPorFaltaDeHost()
    {
        const string secreto = "SuperSecretPassword-No-Debe-Salir-123!";
        var settings = new EmailSettings
        {
            Host = string.Empty,
            Port = 587,
            DefaultSenderEmail = "no-reply@flit.test",
            DefaultSenderPassword = secreto,
        };
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(settings, logger);

        await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        logger.Entries.Should().NotBeEmpty();
        foreach (var entry in logger.Entries)
        {
            entry.Message.Should().NotContain(secreto);
            (entry.Exception?.ToString() ?? string.Empty).Should().NotContain(secreto);
        }
    }

    [Fact]
    public async Task Password_NuncaApareceEnNingunLog_ConFalloDeTransporteReal()
    {
        // Puerto local sin listener (127.0.0.1) — SocketException real (ECONNREFUSED), sin
        // depender de conectividad a Internet. Ejercita la rama catch (SocketException) real.
        const string secreto = "OtroSecretoQueTampocoDebeSalir-456!";
        var settings = new EmailSettings
        {
            Host = "127.0.0.1",
            Port = 1,
            DefaultSenderEmail = "no-reply@flit.test",
            DefaultSenderPassword = secreto,
            DisableAuthentication = true,
        };
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(settings, logger);

        var result = await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Exception != null);

        foreach (var entry in logger.Entries)
        {
            entry.Message.Should().NotContain(secreto);
            (entry.Exception?.ToString() ?? string.Empty).Should().NotContain(secreto);
        }
    }

    // ── Fallo de transporte real — se registra con la excepción original (antes se descartaba) ──

    [Fact]
    public async Task FalloDeConexion_RegistraLaExcepcionOriginalConNivelError()
    {
        var settings = new EmailSettings
        {
            Host = "127.0.0.1",
            Port = 1,
            DefaultSenderEmail = "no-reply@flit.test",
            DefaultSenderPassword = "irrelevante",
            DisableAuthentication = true,
        };
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(settings, logger);

        var result = await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error
            && e.Exception != null
            && e.Message.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    /// <summary>Captura cada llamada a <see cref="ILogger.Log"/> tal como la ve el consumidor —
    /// funciona igual para logging manual y para métodos generados por <c>[LoggerMessage]</c>,
    /// porque ambos terminan invocando este método.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
