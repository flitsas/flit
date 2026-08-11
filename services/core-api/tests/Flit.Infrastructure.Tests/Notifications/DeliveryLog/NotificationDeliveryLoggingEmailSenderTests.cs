using Flit.Infrastructure.Notifications.DeliveryLog;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.DeliveryLog;

/// <summary>
/// HU #11363 — decorador de <see cref="IEmailSender"/> que escribe la bitácora
/// <c>admin.notification_delivery_logs</c>. Usa EF Core InMemory (mismo patrón que
/// <c>CompanyQueryRepositoryTests</c>): <c>TenantRlsScope</c> detecta un proveedor no relacional
/// (<c>IsRelational() == false</c>) y ejecuta directo, sin transacción ni <c>set_config</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);
/// var result = await sender.SendAsync(message, CancellationToken.None);
/// </code>
/// </remarks>
public sealed class NotificationDeliveryLoggingEmailSenderTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ── AC1 — todo intento deja fila con plantilla, canal, destinatario, resultado, causa y duración ──

    [Fact]
    public async Task AC1_EnvioExitoso_DejaFilaConPlantillaCanalDestinatarioResultadoYDuracionMedida()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var provider = BuildProvider(dbName);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = new CapturingLogger<NotificationDeliveryLoggingEmailSender>();
        var inner = new FakeEmailSender(EmailSendResult.Sent, delay: TimeSpan.FromMilliseconds(20));
        var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);

        var message = new EmailMessage(
            TenantA, "security.forgot-password", "usuario@flit.test", "Usuario Demo",
            "Asunto", "<html>cuerpo</html>");

        var result = await sender.SendAsync(message, TestContext.Current.CancellationToken);

        result.Should().Be(EmailSendResult.Sent);

        await using var ctx = NewContext(dbName);
        var row = await ctx.NotificationDeliveryLogs.SingleAsync(TestContext.Current.CancellationToken);
        row.TenantId.Should().Be(TenantA);
        row.TemplateKey.Should().Be("security.forgot-password");
        row.Channel.Should().Be("flit_smtp");
        row.Recipient.Should().Be("usuario@flit.test");
        row.Result.Should().Be("enviado");
        row.FailureReason.Should().BeNull();
        // La duración se mide de verdad (Stopwatch alrededor del envío simulado de 20ms), no un
        // valor fijo o cero puesto a mano.
        row.DurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AC1_EnvioFallido_DejaFilaConResultadoFallidoCausaYDuracionMedida()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var provider = BuildProvider(dbName);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = new CapturingLogger<NotificationDeliveryLoggingEmailSender>();
        var failure = EmailSendResult.Failed(EmailSendOutcome.ProviderUnavailable);
        var inner = new FakeEmailSender(failure, delay: TimeSpan.FromMilliseconds(15));
        var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);

        var message = new EmailMessage(
            TenantA, "security.admin-reset-password", "objetivo@flit.test", "Objetivo",
            "Asunto", "<html>cuerpo</html>");

        var result = await sender.SendAsync(message, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();

        await using var ctx = NewContext(dbName);
        var row = await ctx.NotificationDeliveryLogs.SingleAsync(TestContext.Current.CancellationToken);
        row.Result.Should().Be("fallido");
        row.FailureReason.Should().Be(failure.Message);
        row.DurationMs.Should().BeGreaterThan(0);
    }

    // ── AC1 (caso tenant nulo) — Decisión A: no se escribe fila; sí queda el aviso en la traza ──────

    [Fact]
    public async Task AC1_TenantNulo_NoEscribeFilaYDejaAvisoEnLaTraza()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var provider = BuildProvider(dbName);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = new CapturingLogger<NotificationDeliveryLoggingEmailSender>();
        var inner = new FakeEmailSender(EmailSendResult.Sent);
        var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);

        // Caso real: ForgotPasswordHandler puede resolver un usuario sin ninguna asignación de rol
        // activa — el TenantId del mensaje llega null "a propósito", no por error.
        var message = new EmailMessage(
            TenantId: null, "security.forgot-password", "sin-tenant@flit.test", "Sin Tenant",
            "Asunto", "<html>cuerpo</html>");

        var result = await sender.SendAsync(message, TestContext.Current.CancellationToken);

        result.Should().Be(EmailSendResult.Sent, "el envío en sí no depende de si hay o no tenant");

        await using var ctx = NewContext(dbName);
        (await ctx.NotificationDeliveryLogs.CountAsync(TestContext.Current.CancellationToken)).Should().Be(
            0, "AC1 NO se cumple literalmente para un envío sin tenant resoluble — Decisión A");

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("sin tenant", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("security.forgot-password", StringComparison.Ordinal));
    }

    // ── AC2 — el vocabulario de resultado nunca afirma "entregado" ───────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AC2_ResultadoPersistidoNuncaAfirmaEntregaSoloEnviadoOFallido(bool exitoso)
    {
        var dbName = Guid.NewGuid().ToString();
        await using var provider = BuildProvider(dbName);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = new CapturingLogger<NotificationDeliveryLoggingEmailSender>();
        var sendResult = exitoso
            ? EmailSendResult.Sent
            : EmailSendResult.Failed(EmailSendOutcome.RecipientRejected);
        var inner = new FakeEmailSender(sendResult);
        var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);

        var message = new EmailMessage(
            TenantA, "analytics.alert", "destino@flit.test", "Destino", "Asunto", "<html/>");

        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        await using var ctx = NewContext(dbName);
        var row = await ctx.NotificationDeliveryLogs.SingleAsync(TestContext.Current.CancellationToken);

        // El vocabulario de BD (CHECK del DDL 64) solo admite estos dos valores; esta prueba fija
        // en código que "entregado" nunca es uno de ellos — ni aquí ni en la causa del fallo.
        row.Result.Should().BeOneOf("enviado", "fallido");
        row.Result.Should().NotBe("entregado");
        row.Result.Should().NotContain("entregado", "un canal de API que solo acusa recibo no puede presentarse como entrega");
        (row.FailureReason ?? string.Empty).Should().NotContain("entregado");
    }

    // ── AC4 — ningún campo persistido lleva cuerpo HTML ni secretos ──────────────────────────────

    [Fact]
    public async Task AC4_NingunCampoPersistidoContieneElCuerpoHtmlNiSecretos()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var provider = BuildProvider(dbName);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = new CapturingLogger<NotificationDeliveryLoggingEmailSender>();
        const string htmlSecreto =
            "<html>Tu token es TOKEN-XYZ-999, tu contraseña temporal es P4ssw0rd!, "
            + "usa el api-key sk_live_ABC123</html>";
        var inner = new FakeEmailSender(EmailSendResult.Sent);
        var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);

        var message = new EmailMessage(
            TenantA, "security.admin-reset-password", "objetivo@flit.test", "Objetivo",
            "Asunto con datos: P4ssw0rd!", htmlSecreto);

        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        await using var ctx = NewContext(dbName);
        var row = await ctx.NotificationDeliveryLogs.SingleAsync(TestContext.Current.CancellationToken);

        // La entidad de persistencia NO TIENE columna para asunto ni cuerpo (ver
        // NotificationDeliveryLogEntity): solo se puede afirmar aquí que ninguno de los campos que
        // SÍ existen contiene el secreto/HTML del mensaje.
        var camposPersistidos = new[]
        {
            row.TemplateKey, row.Channel, row.Recipient, row.Result, row.FailureReason ?? string.Empty,
        };

        foreach (var campo in camposPersistidos)
        {
            campo.Should().NotContain("P4ssw0rd!");
            campo.Should().NotContain("TOKEN-XYZ-999");
            campo.Should().NotContain("sk_live_ABC123");
            campo.Should().NotContain("<html>");
        }
    }

    // ── AC6 — un fallo al escribir la bitácora NUNCA cambia el resultado del envío ───────────────

    [Fact]
    public async Task AC6_FalloAlEscribirLaBitacora_NoCambiaElResultadoYQuedaRegistradoEnElLog()
    {
        var thrown = new InvalidOperationException("boom: la escritura de bitácora falló a propósito");
        var services = new ServiceCollection();
        services.AddSingleton<INotificationDeliveryLogWriter>(new ThrowingWriter(thrown));
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var logger = new CapturingLogger<NotificationDeliveryLoggingEmailSender>();
        var inner = new FakeEmailSender(EmailSendResult.Sent);
        var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);

        var message = new EmailMessage(
            TenantA, "security.invitation", "invitado@flit.test", "Invitado", "Asunto", "<html/>");

        var result = await sender.SendAsync(message, TestContext.Current.CancellationToken);

        // 1) El resultado que ve el llamador es el del ENVÍO, intacto — no lo tocó el fallo de bitácora.
        result.Should().Be(EmailSendResult.Sent);

        // 2) El fallo NO se descarta en silencio (el defecto de DbSignatureVaultReader): queda en la
        //    traza de aplicación, con la excepción real adjunta.
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error
            && e.Exception == thrown
            && e.Message.Contains("bitácora", StringComparison.OrdinalIgnoreCase));
    }

    // ── HU #11364 AC2 — la marca de envío desviado llega a la fila junto al destinatario original ──

    [Fact]
    public async Task HU11364AC2_EnvioDesviado_DejaLaFilaConLaMarcaEnVerdaderoYElDestinatarioOriginal()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var provider = BuildProvider(dbName);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = new CapturingLogger<NotificationDeliveryLoggingEmailSender>();
        // El adaptador de canal (RentingEmailApiSender) es quien enciende RecipientDiverted en el
        // EmailSendResult — aquí se simula directamente ese resultado, que es lo único que atraviesa
        // el decorador (no hace falta un HTTP real del canal Renting para probar el decorador).
        var diverted = EmailSendResult.Sent with { RecipientDiverted = true };
        var inner = new FakeEmailSender(diverted);
        var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);

        // El destinatario ORIGINAL es el que trae el EmailMessage — el decorador lo lee ANTES de que
        // el adaptador de canal lo sustituya por el de desvío (el decorador nunca ve ese cambio).
        var message = new EmailMessage(
            TenantA, "analytics.alert", "cliente-real@flit.test", "Cliente Real", "Asunto", "<html/>");

        var result = await sender.SendAsync(message, TestContext.Current.CancellationToken);

        result.RecipientDiverted.Should().BeTrue();

        await using var ctx = NewContext(dbName);
        var row = await ctx.NotificationDeliveryLogs.SingleAsync(TestContext.Current.CancellationToken);

        row.RecipientDiverted.Should().BeTrue();
        row.Recipient.Should().Be("cliente-real@flit.test", "la fila conserva el destinatario ORIGINAL, no el de desvío");
    }

    [Fact]
    public async Task HU11364AC2_EnvioNoDesviado_DejaLaFilaConLaMarcaEnFalso()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var provider = BuildProvider(dbName);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = new CapturingLogger<NotificationDeliveryLoggingEmailSender>();
        var inner = new FakeEmailSender(EmailSendResult.Sent);
        var sender = new NotificationDeliveryLoggingEmailSender(inner, scopeFactory, logger);

        var message = new EmailMessage(
            TenantA, "analytics.alert", "cliente-real@flit.test", "Cliente Real", "Asunto", "<html/>");

        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        await using var ctx = NewContext(dbName);
        var row = await ctx.NotificationDeliveryLogs.SingleAsync(TestContext.Current.CancellationToken);

        row.RecipientDiverted.Should().BeFalse();
    }

    // ── Infraestructura de prueba ─────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FlitDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<INotificationDeliveryLogWriter, NotificationDeliveryLogWriter>();
        return services.BuildServiceProvider();
    }

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class FakeEmailSender(EmailSendResult result, TimeSpan delay = default) : IEmailSender
    {
        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, CancellationToken.None);

            return result;
        }
    }

    private sealed class ThrowingWriter(Exception toThrow) : INotificationDeliveryLogWriter
    {
        public Task WriteAsync(NotificationDeliveryLogEntry entry, CancellationToken cancellationToken) =>
            throw toThrow;
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
