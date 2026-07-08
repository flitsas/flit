using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Scheduling;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>
/// Reportes 2.0 HU-D — integración InMemory del scheduler: una regla que dispara crea el
/// alert_event, sella last_triggered_at y envía el correo (IEmailSender sustituido); un
/// segundo ciclo respeta el cooldown (no re-dispara). También cubre el sellado de
/// last_sent_at de los informes programados (no re-envía en la misma ventana).
/// </summary>
public sealed class AnalyticsSchedulerProcessorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Martes 2026-07-07 12:30 UTC = 07:30 en Bogotá (UTC-5).
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 7, 12, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------
    // Alertas
    // ------------------------------------------------------------------

    [Fact]
    public async Task Regla_que_dispara_crea_alert_event_sella_cooldown_y_envia_correo()
    {
        var dbName = NewDbName();
        var ruleId = await SeedRuleAsync(dbName, threshold: 25m); // gt 25
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var metrics = Substitute.For<IAlertMetricsReadRepository>();
        metrics.GetMetricValueAsync(TenantId, "rejection_rate_pct", 1440, Arg.Any<CancellationToken>())
            .Returns(31.2m);
        var processor = NewProcessor(dbName, emailSender, metrics);

        await processor.ProcessAlertRulesAsync(NowUtc, Ct);

        // Evento registrado con el valor y el umbral del disparo.
        await using var verify = NewContext(dbName);
        var evt = await verify.Set<AlertEvent>().SingleAsync(Ct);
        evt.AlertRuleId.Should().Be(ruleId);
        evt.TenantId.Should().Be(TenantId);
        evt.MetricValue.Should().Be(31.2m);
        evt.Threshold.Should().Be(25m);
        evt.Notified.Should().BeTrue("el correo se envió con éxito");
        evt.Message.Should().Contain("Tasa de rechazo");

        // Cooldown sellado.
        var rule = await verify.Set<AlertRule>().SingleAsync(Ct);
        rule.LastTriggeredAt.Should().Be(NowUtc);

        // Correo en español a los destinatarios de la regla.
        sent.Should().HaveCount(2);
        sent.Select(m => m.ToEmail).Should().BeEquivalentTo("ops@empresa.co", "gerencia@empresa.co");
        sent[0].Subject.Should().Be("[FLIT] Alerta: Rechazo OT alto");
        sent[0].HtmlBody.Should().Contain("Tasa de rechazo").And.Contain("31.2");
    }

    [Fact]
    public async Task Segundo_ciclo_dentro_del_cooldown_no_re_dispara()
    {
        var dbName = NewDbName();
        await SeedRuleAsync(dbName, threshold: 25m);
        var emailSender = Substitute.For<IEmailSender>();
        var metrics = Substitute.For<IAlertMetricsReadRepository>();
        metrics.GetMetricValueAsync(TenantId, "rejection_rate_pct", 1440, Arg.Any<CancellationToken>())
            .Returns(31.2m);
        var processor = NewProcessor(dbName, emailSender, metrics);

        await processor.ProcessAlertRulesAsync(NowUtc, Ct);
        // Segundo ciclo un minuto después: cooldown (240 min) vigente → NO re-dispara.
        await processor.ProcessAlertRulesAsync(NowUtc.AddMinutes(1), Ct);

        await using var verify = NewContext(dbName);
        (await verify.Set<AlertEvent>().CountAsync(Ct)).Should().Be(1);
        await emailSender.Received(2).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());

        // Tercer ciclo con el cooldown vencido → SÍ re-dispara.
        await processor.ProcessAlertRulesAsync(NowUtc.AddMinutes(241), Ct);
        await using var verify2 = NewContext(dbName);
        (await verify2.Set<AlertEvent>().CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Regla_bajo_el_umbral_no_dispara_ni_envia()
    {
        var dbName = NewDbName();
        await SeedRuleAsync(dbName, threshold: 25m);
        var emailSender = Substitute.For<IEmailSender>();
        var metrics = Substitute.For<IAlertMetricsReadRepository>();
        metrics.GetMetricValueAsync(TenantId, "rejection_rate_pct", 1440, Arg.Any<CancellationToken>())
            .Returns(10m);
        var processor = NewProcessor(dbName, emailSender, metrics);

        await processor.ProcessAlertRulesAsync(NowUtc, Ct);

        await using var verify = NewContext(dbName);
        (await verify.Set<AlertEvent>().CountAsync(Ct)).Should().Be(0);
        await emailSender.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallo_en_una_regla_no_tumba_la_evaluacion_de_las_demas()
    {
        var dbName = NewDbName();
        await SeedRuleAsync(dbName, threshold: 25m, name: "Regla que falla", metric: "stuck_count");
        await SeedRuleAsync(dbName, threshold: 25m);
        var emailSender = Substitute.For<IEmailSender>();
        var metrics = Substitute.For<IAlertMetricsReadRepository>();
        metrics.GetMetricValueAsync(TenantId, "stuck_count", 1440, Arg.Any<CancellationToken>())
            .Returns<decimal>(_ => throw new InvalidOperationException("SQL caído (simulado)"));
        metrics.GetMetricValueAsync(TenantId, "rejection_rate_pct", 1440, Arg.Any<CancellationToken>())
            .Returns(31.2m);
        var processor = NewProcessor(dbName, emailSender, metrics);

        await processor.ProcessAlertRulesAsync(NowUtc, Ct);

        // La regla sana disparó a pesar del fallo de la otra.
        await using var verify = NewContext(dbName);
        (await verify.Set<AlertEvent>().CountAsync(Ct)).Should().Be(1);
    }

    // ------------------------------------------------------------------
    // Informes programados
    // ------------------------------------------------------------------

    [Fact]
    public async Task Informe_vencido_sella_last_sent_at_y_envia_resumen_html()
    {
        var dbName = NewDbName();
        var scheduleId = await SeedScheduleAsync(dbName); // daily a las 7, hora Bogotá
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>());

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        await using var verify = NewContext(dbName);
        var schedule = await verify.Set<ReportSchedule>().SingleAsync(s => s.Id == scheduleId, Ct);
        schedule.LastSentAt.Should().Be(NowUtc, "se sella ANTES de enviar");

        sent.Should().ContainSingle();
        sent[0].ToEmail.Should().Be("gerencia@empresa.co");
        sent[0].Subject.Should().StartWith("[FLIT] Informe diario —");
        sent[0].HtmlBody.Should().Contain("Resumen general").And.Contain("Trámites por categoría");
    }

    [Fact]
    public async Task Informe_ya_enviado_en_la_ventana_no_se_reenvia()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName);
        var emailSender = Substitute.For<IEmailSender>();
        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>());

        await processor.ProcessSchedulesAsync(NowUtc, Ct);
        // Segundo ciclo un minuto después, misma ventana (mismo día local) → sin re-envío.
        await processor.ProcessSchedulesAsync(NowUtc.AddMinutes(1), Ct);

        await emailSender.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string NewDbName() => $"flit-hu-d-scheduler-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task<Guid> SeedRuleAsync(
        string dbName, decimal threshold, string name = "Rechazo OT alto", string metric = "rejection_rate_pct")
    {
        var id = Guid.NewGuid();
        await using var db = NewContext(dbName);
        db.Set<AlertRule>().Add(new AlertRule
        {
            Id = id,
            TenantId = TenantId,
            Name = name,
            Metric = metric,
            Operator = "gt",
            Threshold = threshold,
            WindowMinutes = 1440,
            CooldownMinutes = 240,
            Recipients = ["ops@empresa.co", "gerencia@empresa.co"],
            IsActive = true,
            CreatedAt = NowUtc.AddDays(-1),
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private static async Task<Guid> SeedScheduleAsync(string dbName)
    {
        var id = Guid.NewGuid();
        await using var db = NewContext(dbName);
        db.Set<ReportSchedule>().Add(new ReportSchedule
        {
            Id = id,
            TenantId = TenantId,
            Name = "Informe diario",
            ReportType = "resumen",
            Frequency = "daily",
            SendHour = 7, // NowUtc = 07:30 en Bogotá → vencido
            Format = "pdf",
            Recipients = ["gerencia@empresa.co"],
            IsActive = true,
            CreatedAt = NowUtc.AddDays(-1),
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private static AnalyticsSchedulerProcessor NewProcessor(
        string dbName, IEmailSender emailSender, IAlertMetricsReadRepository metrics)
    {
        var analytics = Substitute.For<IAnalyticsReadRepository>();
        analytics.GetOverviewAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<CategoryMetricsDto>
            {
                new("matriculas", 3, new List<StatusCountDto> { new("aprobado", 3) }),
            });
        analytics.GetTopProducersAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<TopProducerDto>());

        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        services.AddSingleton(emailSender);
        services.AddSingleton(metrics);
        services.AddSingleton(analytics);
        var provider = services.BuildServiceProvider();

        return new AnalyticsSchedulerProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AnalyticsSchedulerProcessor>.Instance);
    }
}
