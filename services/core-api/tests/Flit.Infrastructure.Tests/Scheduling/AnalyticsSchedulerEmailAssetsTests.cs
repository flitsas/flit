using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Scheduling;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>
/// Correos 2.0 — con el layout FLIT los correos del scheduler llevan banner y logo: el procesador
/// productivo debe entregarle al composer la base de assets del ambiente
/// (<c>Notifications:EmailAssets:BaseUrl</c>). Sin ese cableado las imágenes saldrían del respaldo
/// local (<c>localhost:3000</c>) y llegarían rotas en DEV/QA/PDN.
/// <para>Archivo aparte de <c>AnalyticsSchedulerProcessorTests</c> a propósito (lo toca otra sesión
/// del rediseño); el arnés es el mínimo para estos tres caminos.</para>
/// </summary>
public sealed class AnalyticsSchedulerEmailAssetsTests
{
    private const string QaAssets = "https://qa.flitsas.online/email-assets";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 7, 12, 30, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Informe_programado_usa_la_base_de_assets_del_ambiente()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName, reportType: "resumen");
        var sent = new List<EmailMessage>();
        var processor = NewProcessor(dbName, Sender(sent), Substitute.For<IAlertMetricsReadRepository>());

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        AssertAssets(sent[0].HtmlBody);
    }

    [Fact]
    public async Task Consulta_no_disponible_por_error_usa_la_base_de_assets_del_ambiente()
    {
        // Sin CompanyQueryReportDocumentBuilder registrado la generación falla y el procesador cae
        // al correo «consulta no disponible» del catch: ese camino también debe llevar la base.
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName, reportType: "consulta", savedQueryId: Guid.NewGuid(), savedQueryScope: "empresa");
        var sent = new List<EmailMessage>();
        var processor = NewProcessor(dbName, Sender(sent), Substitute.For<IAlertMetricsReadRepository>());

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].Subject.Should().EndWith("consulta no disponible");
        AssertAssets(sent[0].HtmlBody);
    }

    [Fact]
    public async Task Alerta_usa_la_base_de_assets_del_ambiente()
    {
        var dbName = NewDbName();
        await SeedRuleAsync(dbName);
        var metrics = Substitute.For<IAlertMetricsReadRepository>();
        metrics.GetMetricValueAsync(TenantId, "rejection_rate_pct", 1440, Arg.Any<CancellationToken>()).Returns(40m);
        var sent = new List<EmailMessage>();
        var processor = NewProcessor(dbName, Sender(sent), metrics);

        await processor.ProcessAlertRulesAsync(NowUtc, Ct);

        sent.Should().NotBeEmpty();
        AssertAssets(sent[0].HtmlBody);
    }

    private static void AssertAssets(string html)
    {
        html.Should().Contain($"{QaAssets}/flit-banner-correo.png")
            .And.Contain($"{QaAssets}/flit-logo-firma.png")
            .And.NotContain("localhost:3000", "en un ambiente desplegado las imágenes no pueden salir del respaldo local");
    }

    private static IEmailSender Sender(List<EmailMessage> sent)
    {
        var sender = Substitute.For<IEmailSender>();
        sender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);
        return sender;
    }

    private static string NewDbName() => $"flit-correos2-scheduler-{Guid.NewGuid()}";

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

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
            Recipients = ["ops@empresa.co"],
            IsActive = true,
            CreatedAt = NowUtc.AddDays(-1),
        });
        await db.SaveChangesAsync(Ct);
    }

    private static async Task SeedScheduleAsync(
        string dbName, string reportType, Guid? savedQueryId = null, string? savedQueryScope = null)
    {
        await using var db = NewContext(dbName);
        db.Set<ReportSchedule>().Add(new ReportSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "Informe diario",
            ReportType = reportType,
            Frequency = "daily",
            SendHour = 7, // NowUtc = 07:30 en Bogotá → vencido
            Format = "pdf",
            Recipients = ["gerencia@empresa.co"],
            IsActive = true,
            CreatedAt = NowUtc.AddDays(-1),
            SavedQueryId = savedQueryId,
            SavedQueryScope = savedQueryScope,
        });
        await db.SaveChangesAsync(Ct);
    }

    private static AnalyticsSchedulerProcessor NewProcessor(
        string dbName, IEmailSender emailSender, IAlertMetricsReadRepository metrics)
    {
        var analytics = Substitute.For<IAnalyticsReadRepository>();
        analytics.GetOverviewAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), null, Arg.Any<CancellationToken>())
            .Returns(new List<CategoryMetricsDto>());
        analytics.GetTopProducersAsync(Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), null, Arg.Any<CancellationToken>())
            .Returns(new List<TopProducerDto>());

        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(dbName));
        services.AddSingleton(emailSender);
        services.AddSingleton(metrics);
        services.AddSingleton(analytics);
        services.AddSingleton<IEmailThemeResolver>(NullEmailThemeResolver.Instance);
        services.AddSingleton(Options.Create(new NotificationEmailAssetsOptions { BaseUrl = QaAssets }));

        var provider = services.BuildServiceProvider();
        return new AnalyticsSchedulerProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AnalyticsSchedulerProcessor>.Instance);
    }
}
