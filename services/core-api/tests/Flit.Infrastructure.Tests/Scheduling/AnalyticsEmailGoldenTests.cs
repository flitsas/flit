using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Scheduling;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>
/// HU #11350 — congela ASUNTO y CUERPO de las 2 plantillas de correo de Analítica, carácter a
/// carácter, ANTES de que la HU #11352 traslade la composición del asunto al composer.
/// <para>
/// La captura se hace en el <b>punto de envío</b> y no llamando al composer, y aquí eso no es una
/// preferencia sino la única forma correcta: hoy el cuerpo lo compone
/// <c>SchedulerEmailComposer</c> pero el asunto lo interpola <c>AnalyticsSchedulerProcessor</c>.
/// Un golden tomado del composer no vería el asunto y el traslado podría romperlo sin que nadie
/// se enterara.
/// </para>
/// <para>
/// El reloj se fija (<see cref="NowUtc"/>) porque el cuerpo de la alerta imprime la fecha del
/// disparo en hora de Bogotá y el asunto del informe lleva el periodo: sin fijarlo, el golden
/// caducaría al día siguiente.
/// </para>
/// </summary>
public sealed class AnalyticsEmailGoldenTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // Martes 2026-07-07 12:30 UTC = 07:30 en Bogotá (UTC-5).
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 7, 12, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Informe_programado_conserva_asunto_y_cuerpo()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName);
        var sent = new List<EmailMessage>();
        var processor = NewProcessor(dbName, CapturingSender(sent), ConActividad());

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        EmailGolden.Assert(Single(sent), "informe-programado");
    }

    /// <summary>
    /// El periodo sin actividad tiene textos propios ("Sin actividad registrada en el periodo." y
    /// "Sin radicaciones en el periodo.") que ninguna prueba cubría. Se congelan aquí porque
    /// congelarlos después del refactor no probaría nada.
    /// </summary>
    [Fact]
    public async Task Informe_programado_sin_actividad_conserva_asunto_y_cuerpo()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName);
        var sent = new List<EmailMessage>();
        var processor = NewProcessor(dbName, CapturingSender(sent), SinActividad());

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        EmailGolden.Assert(Single(sent), "informe-programado-sin-actividad");
    }

    [Fact]
    public async Task Alerta_conserva_asunto_y_cuerpo()
    {
        var dbName = NewDbName();
        await SeedRuleAsync(dbName);
        var sent = new List<EmailMessage>();
        var metrics = Substitute.For<IAlertMetricsReadRepository>();
        metrics.GetMetricValueAsync(TenantId, "rejection_rate_pct", 1440, Arg.Any<CancellationToken>())
            .Returns(31.2m);
        var processor = NewProcessor(dbName, CapturingSender(sent), ConActividad(), metrics);

        await processor.ProcessAlertRulesAsync(NowUtc, Ct);

        EmailGolden.Assert(Single(sent), "alerta");
    }

    // ------------------------------------------------------------------ helpers

    private static EmailMessage Single(List<EmailMessage> sent)
    {
        sent.Should().ContainSingle("cada semilla tiene un único destinatario");
        return sent[0];
    }

    private static IEmailSender CapturingSender(List<EmailMessage> sent)
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        return sender;
    }

    private static IAnalyticsReadRepository ConActividad()
    {
        var analytics = Substitute.For<IAnalyticsReadRepository>();
        analytics.GetOverviewAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<CategoryMetricsDto>
            {
                new("matriculas", 7, new List<StatusCountDto> { new("aprobado", 5), new("rechazado", 2) }),
                new("traspasos", 3, new List<StatusCountDto> { new("radicado", 3) }),
            });
        analytics.GetTopProducersAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<TopProducerDto>
            {
                new(Guid.Empty, "Radicador Uno", 6, 5, 1),
                new(Guid.Empty, "Radicador Dos", 4, 2, 2),
            });
        return analytics;
    }

    private static IAnalyticsReadRepository SinActividad()
    {
        var analytics = Substitute.For<IAnalyticsReadRepository>();
        analytics.GetOverviewAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<CategoryMetricsDto>());
        analytics.GetTopProducersAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<TopProducerDto>());
        return analytics;
    }

    private static string NewDbName() => $"flit-hu11350-golden-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static async Task SeedScheduleAsync(string dbName)
    {
        await using var db = NewContext(dbName);
        db.Set<ReportSchedule>().Add(new ReportSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "Informe diario",
            ReportType = "resumen",
            Frequency = "daily",
            SendHour = 7, // NowUtc = 07:30 en Bogotá → vencido
            Format = "pdf",
            Recipients = ["destinatario@flit.test"],
            IsActive = true,
            CreatedAt = NowUtc.AddDays(-1),
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedRuleAsync(string dbName)
    {
        await using var db = NewContext(dbName);
        db.Set<AlertRule>().Add(new AlertRule
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "Rechazo OT alto",
            Metric = "rejection_rate_pct",
            Operator = "gt",
            Threshold = 25m,
            WindowMinutes = 1440,
            CooldownMinutes = 240,
            Recipients = ["destinatario@flit.test"],
            IsActive = true,
            CreatedAt = NowUtc.AddDays(-1),
        });
        await db.SaveChangesAsync(Ct);
    }

    private static AnalyticsSchedulerProcessor NewProcessor(
        string dbName,
        IEmailSender emailSender,
        IAnalyticsReadRepository analytics,
        IAlertMetricsReadRepository? metrics = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        services.AddSingleton(emailSender);
        services.AddSingleton(metrics ?? Substitute.For<IAlertMetricsReadRepository>());
        services.AddSingleton(analytics);
        var provider = services.BuildServiceProvider();

        return new AnalyticsSchedulerProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AnalyticsSchedulerProcessor>.Instance);
    }
}
