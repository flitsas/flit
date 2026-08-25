using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Metrics;
using Flit.Analytics.Application.Scheduling;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Queries.Domain;
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
            .Returns(Task.FromResult(EmailSendResult.Sent));
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
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
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
            .Returns(Task.FromResult(EmailSendResult.Sent));
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
        emailSender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>());

        await processor.ProcessSchedulesAsync(NowUtc, Ct);
        // Segundo ciclo un minuto después, misma ventana (mismo día local) → sin re-envío.
        await processor.ProcessSchedulesAsync(NowUtc.AddMinutes(1), Ct);

        await emailSender.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Adjuntos reales (Reportes 2.0 — cierra la limitación histórica de IEmailSender)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Informe_tipo_resumen_formato_pdf_adjunta_el_pdf_del_resumen_ejecutivo()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName, reportType: "resumen", format: "pdf");
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var pdfGenerator = Substitute.For<IExecutiveSummaryPdfGenerator>();
        pdfGenerator.Generate(Arg.Any<ExecutiveSummaryData>()).Returns([1, 2, 3, 4]);
        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            executiveSummaryPdfGenerator: pdfGenerator);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].Attachments.Should().ContainSingle();
        sent[0].Attachments[0].ContentType.Should().Be("application/pdf");
        sent[0].Attachments[0].Content.Should().Equal((byte)1, (byte)2, (byte)3, (byte)4);
        sent[0].Attachments[0].FileName.Should().StartWith("informe-resumen-").And.EndWith(".pdf");
    }

    [Fact]
    public async Task Informe_tipo_operacion_formato_excel_adjunta_el_excel_de_detalle()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName, reportType: "operacion", format: "excel");
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var excelExporter = Substitute.For<IProcedureExcelExporter>();
        excelExporter.ExportAsync(Arg.Any<Stream>(), Arg.Any<ProcedureExportFilter>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Stream>().WriteAsync(new byte[] { 9, 9 }, CancellationToken.None).AsTask());
        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            procedureExcelExporter: excelExporter);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].Attachments.Should().ContainSingle();
        sent[0].Attachments[0].ContentType.Should()
            .Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        sent[0].Attachments[0].FileName.Should().StartWith("informe-operacion-").And.EndWith(".xlsx");
    }

    [Fact]
    public async Task Informe_tipo_uso_adjunta_el_excel_de_telemetria_de_uso_no_el_de_tramites()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName, reportType: "uso", format: "excel");
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var usage = Substitute.For<IUsageMetricsReadRepository>();
        usage.GetWizardStepMetricsAsync(TenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<WizardStepMetricDto> { new("datos_vehiculo", 10, 8, 20.0, 5000, 4500) });
        usage.GetModuleUsageAsync(TenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<ModuleUsageDto>());
        usage.GetPeakHoursAsync(TenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<PeakHourDto>());
        usage.GetWizardDurationAsync(TenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new WizardDurationDto(5000, 4500));
        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            usageMetricsReadRepository: usage);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].Attachments.Should().ContainSingle();
        sent[0].Attachments[0].Content.Length.Should().BeGreaterThan(0);
        sent[0].HtmlBody.Should().Contain("archivo adjunto").And.Contain("Uso del aplicativo");
    }

    [Fact]
    public async Task Informe_tipo_ot_adjunta_el_excel_de_metricas_ot_no_el_de_tramites()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName, reportType: "ot", format: "excel");
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var metrics = Substitute.For<IAnalyticsMetricsReadRepository>();
        metrics.GetOtMetricsAsync(Arg.Any<MetricsFilter>(), Arg.Any<CancellationToken>()).Returns(new OtMetricsDto(
            new OtMetricsSummaryDto(10, 7, 3, 30.0, 12, 10, 20, 5.0, 2),
            [], [], [], [], [],
            new ReincidenceDto(3, 1, 1.2, 2),
            new StuckDto(0, []),
            [],
            1.1,
            new InternalCycleDto(4, 3, 6)));
        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            analyticsMetricsReadRepository: metrics);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].Attachments.Should().ContainSingle();
        sent[0].Attachments[0].Content.Length.Should().BeGreaterThan(0);
        sent[0].HtmlBody.Should().Contain("Organismo de Tr").And.Contain("archivo adjunto");
    }

    [Fact]
    public async Task Fallo_generando_el_adjunto_no_impide_el_envio_del_correo()
    {
        var dbName = NewDbName();
        await SeedScheduleAsync(dbName, reportType: "resumen", format: "pdf");
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
        var pdfGenerator = Substitute.For<IExecutiveSummaryPdfGenerator>();
        pdfGenerator.Generate(Arg.Any<ExecutiveSummaryData>())
            .Returns(_ => throw new InvalidOperationException("QuestPDF caído (simulado)"));
        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            executiveSummaryPdfGenerator: pdfGenerator);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle("el fallo al generar el adjunto no debe cancelar el envío");
        sent[0].Attachments.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Informe tipo "consulta" (Reportes 2.0, HU-D, segunda ola)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Informe_tipo_consulta_ejecuta_la_savedQuery_y_adjunta_el_excel()
    {
        var dbName = NewDbName();
        var savedQueryId = Guid.NewGuid();
        await SeedScheduleAsync(
            dbName, reportType: "consulta", format: "excel", savedQueryId: savedQueryId, savedQueryScope: "empresa");
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));

        var savedQuery = new SavedQueryDto(
            savedQueryId, "Pendientes de hoy", null, DeFabrica: false,
            new QueryDefinition(new QueryDateFilter("creacion", QueryRangePreset.Ultimos7), [], ["referencia", "placa"]),
            DateTimeOffset.UtcNow.AddDays(-1), null);
        var companyQueries = Substitute.For<ICompanyQueryRepository>();
        companyQueries.GetSavedByIdAsync(TenantId, savedQueryId, Arg.Any<CancellationToken>())
            .Returns(savedQuery);
        companyQueries.ExecuteAsync(TenantId, Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompanyQueryResultDto(
                1, 1, 200, DateOnly.FromDateTime(NowUtc.Date), DateOnly.FromDateTime(NowUtc.Date), 0,
                [Row(savedQueryId)], []));

        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            companyQueryRepository: companyQueries);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].Subject.Should().Contain("Informe diario");
        sent[0].HtmlBody.Should().Contain("Pendientes de hoy").And.Contain("Resultados:</strong> 1");
        sent[0].Attachments.Should().ContainSingle();
        sent[0].Attachments[0].FileName.Should().EndWith(".xlsx");
        sent[0].Attachments[0].Content.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Informe_tipo_consulta_alcance_superadmin_ejecuta_sobre_todas_las_companias()
    {
        var dbName = NewDbName();
        var savedQueryId = Guid.NewGuid();
        // TenantId null: único caso legítimo (§75 del DDL), alcance "superadmin".
        await SeedScheduleAsync(
            dbName, reportType: "consulta", format: "excel", savedQueryId: savedQueryId,
            savedQueryScope: "superadmin", superAdminScope: true);
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));

        var savedQuery = new SavedQueryDto(
            savedQueryId, "Aprobados de todas las compañías", null, DeFabrica: false,
            new QueryDefinition(new QueryDateFilter("creacion", QueryRangePreset.Ultimos7), [], ["referencia", "compania"]),
            DateTimeOffset.UtcNow.AddDays(-1), null);
        var superAdminSavedQueries = Substitute.For<ISuperAdminSavedQueryRepository>();
        superAdminSavedQueries.GetByIdAsync(savedQueryId, Arg.Any<CancellationToken>()).Returns(savedQuery);
        var companyQueries = Substitute.For<ICompanyQueryRepository>();
        companyQueries.ExecuteForSuperAdminAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompanyQueryResultDto(
                2, 1, 200, DateOnly.FromDateTime(NowUtc.Date), DateOnly.FromDateTime(NowUtc.Date), 0,
                [Row(savedQueryId), Row(Guid.NewGuid())], []));

        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            companyQueryRepository: companyQueries, superAdminSavedQueryRepository: superAdminSavedQueries);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].HtmlBody.Should().Contain("Aprobados de todas las").And.Contain("Resultados:</strong> 2");
        sent[0].Attachments.Should().ContainSingle();
        await companyQueries.Received(1).ExecuteForSuperAdminAsync(Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>());
        await companyQueries.DidNotReceive().ExecuteAsync(Arg.Any<Guid>(), Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Informe_tipo_consulta_con_savedQuery_borrada_avisa_sin_adjunto()
    {
        var dbName = NewDbName();
        var savedQueryId = Guid.NewGuid();
        await SeedScheduleAsync(
            dbName, reportType: "consulta", format: "excel", savedQueryId: savedQueryId, savedQueryScope: "empresa");
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));

        var companyQueries = Substitute.For<ICompanyQueryRepository>();
        companyQueries.GetSavedByIdAsync(TenantId, savedQueryId, Arg.Any<CancellationToken>())
            .Returns((SavedQueryDto?)null);

        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            companyQueryRepository: companyQueries);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].Attachments.Should().BeEmpty();
        sent[0].Subject.Should().Contain("consulta no disponible");
        sent[0].HtmlBody.Should().Contain("ya no existe");
    }

    [Fact]
    public async Task Informe_tipo_consulta_alcance_ict_ejecuta_la_savedQuery_y_adjunta_el_excel()
    {
        var dbName = NewDbName();
        var savedQueryId = Guid.NewGuid();
        await SeedScheduleAsync(
            dbName, reportType: "consulta", format: "excel", savedQueryId: savedQueryId, savedQueryScope: "ict");
        var emailSender = Substitute.For<IEmailSender>();
        var sent = new List<EmailMessage>();
        emailSender.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));

        var savedQuery = new SavedQueryDto(
            savedQueryId, "Novedades de hoy", null, DeFabrica: false,
            new QueryDefinition(new QueryDateFilter("registro", QueryRangePreset.Ultimos7), [], ["radicado", "placa"]),
            DateTimeOffset.UtcNow.AddDays(-1), null);
        var ictQueries = Substitute.For<Flit.Analytics.Application.IctQueries.IIctQueryRepository>();
        ictQueries.GetSavedByIdAsync(TenantId, savedQueryId, Arg.Any<CancellationToken>())
            .Returns(savedQuery);
        ictQueries.ExecuteAsync(TenantId, Arg.Any<QueryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Flit.Analytics.Application.IctQueries.IctQueryResultDto(
                1, 1, 200, DateOnly.FromDateTime(NowUtc.Date), DateOnly.FromDateTime(NowUtc.Date), 0,
                [IctRow(savedQueryId)], []));

        var processor = NewProcessor(dbName, emailSender, Substitute.For<IAlertMetricsReadRepository>(),
            ictQueryRepository: ictQueries);

        await processor.ProcessSchedulesAsync(NowUtc, Ct);

        sent.Should().ContainSingle();
        sent[0].Subject.Should().Contain("Informe diario");
        sent[0].HtmlBody.Should().Contain("Novedades de hoy").And.Contain("Resultados:</strong> 1");
        sent[0].Attachments.Should().ContainSingle();
        sent[0].Attachments[0].FileName.Should().EndWith(".xlsx");
        sent[0].Attachments[0].Content.Length.Should().BeGreaterThan(0);
    }

    private static Flit.Analytics.Application.IctQueries.IctQueryRowDto IctRow(Guid seed) => new(
        Guid.NewGuid(), 1000, $"REF-{seed:N}", "ABC123", null,
        Guid.NewGuid(), "Empresa Demo", "Matrícula", "recibido", false, false, false,
        null, null, null, null, NowUtc.AddDays(-1), null, null);

    private static CompanyQueryRowDto Row(Guid seed) => new(
        Guid.NewGuid(), $"REF-{seed:N}", "ABC123", null, null, null,
        Guid.NewGuid(), "Empresa Demo", Guid.NewGuid(), "Traspaso", "aprobado",
        false, false, 0, null, null, false, null, false, [], false, null, "bilateral",
        "Usuario Uno", NowUtc.AddDays(-2), NowUtc.AddDays(-1), NowUtc, NowUtc, NowUtc,
        1.0, 0.5, 0);

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

    private static async Task<Guid> SeedScheduleAsync(
        string dbName, string reportType = "resumen", string format = "pdf",
        Guid? savedQueryId = null, string? savedQueryScope = null,
        // Alcance superadmin (§75 del DDL): único caso con TenantId nulo a propósito.
        bool superAdminScope = false)
    {
        var id = Guid.NewGuid();
        await using var db = NewContext(dbName);
        db.Set<ReportSchedule>().Add(new ReportSchedule
        {
            Id = id,
            TenantId = superAdminScope ? null : TenantId,
            Name = "Informe diario",
            ReportType = reportType,
            Frequency = "daily",
            SendHour = 7, // NowUtc = 07:30 en Bogotá → vencido
            Format = format,
            Recipients = ["gerencia@empresa.co"],
            IsActive = true,
            CreatedAt = NowUtc.AddDays(-1),
            SavedQueryId = savedQueryId,
            SavedQueryScope = savedQueryScope,
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private static AnalyticsSchedulerProcessor NewProcessor(
        string dbName,
        IEmailSender emailSender,
        IAlertMetricsReadRepository metrics,
        IExecutiveSummaryPdfGenerator? executiveSummaryPdfGenerator = null,
        IProcedureExcelExporter? procedureExcelExporter = null,
        IUsageMetricsReadRepository? usageMetricsReadRepository = null,
        IAnalyticsMetricsReadRepository? analyticsMetricsReadRepository = null,
        ICompanyQueryRepository? companyQueryRepository = null,
        ISuperAdminSavedQueryRepository? superAdminSavedQueryRepository = null,
        Flit.Analytics.Application.IctQueries.IIctQueryRepository? ictQueryRepository = null)
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

        // Solo se registran cuando el test los necesita: así los tests que no los pasan ejercitan
        // el camino "no se pudo generar el adjunto" (best-effort) sin tener que simularlo aparte.
        if (executiveSummaryPdfGenerator is not null)
        {
            services.AddSingleton(executiveSummaryPdfGenerator);
            services.AddScoped<ExportExecutivePdfHandler>();
        }

        if (procedureExcelExporter is not null)
            services.AddSingleton(procedureExcelExporter);

        if (usageMetricsReadRepository is not null)
        {
            services.AddSingleton(usageMetricsReadRepository);
            services.AddScoped<UsageReportDocumentBuilder>();
        }

        if (analyticsMetricsReadRepository is not null)
        {
            services.AddSingleton(analyticsMetricsReadRepository);
            services.AddScoped<OtReportDocumentBuilder>();
        }

        if (companyQueryRepository is not null)
        {
            services.AddSingleton(companyQueryRepository);
            // CompanyQueryReportDocumentBuilder también resuelve ISuperAdminSavedQueryRepository en su
            // constructor (alcance "superadmin") — sin registrar AL MENOS un doble, la resolución del
            // builder fallaría y el fallback "SavedQuery no disponible" (best-effort) enmascararía el
            // test real que se quiere cubrir en los que solo ejercitan "empresa".
            services.AddSingleton(superAdminSavedQueryRepository ?? Substitute.For<ISuperAdminSavedQueryRepository>());
            services.AddScoped<CompanyQueryReportDocumentBuilder>();
        }

        if (ictQueryRepository is not null)
        {
            services.AddSingleton(ictQueryRepository);
            services.AddScoped<IctQueryReportDocumentBuilder>();
        }

        var provider = services.BuildServiceProvider();

        return new AnalyticsSchedulerProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AnalyticsSchedulerProcessor>.Instance);
    }
}
