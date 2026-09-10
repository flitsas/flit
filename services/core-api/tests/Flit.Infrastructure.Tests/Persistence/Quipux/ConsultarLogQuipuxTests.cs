using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Quipux.Application.UseCases.ConsultarLog;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Quipux;

/// <summary>
/// LOG QX (HU #10793): consulta de trazabilidad de la integración Quipux sobre el handler real
/// <see cref="ConsultarLogQuipuxHandler"/> + <see cref="DbQuipuxLogRepository"/> con EF InMemory.
/// Cubre los AC de la HU: búsqueda por trámite/placa/radicado con timeline (AC1/AC3), duración+origen
/// por evento (AC2), registro inexistente e histórico parcial (AC4) y que jamás se devuelve payload
/// crudo, solo el detail sanitizado ya persistido (AC5).
/// </summary>
public sealed class ConsultarLogQuipuxTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProcedureTypeId = Guid.NewGuid();
    private static readonly Guid TransitOfficeId = Guid.NewGuid();
    private static readonly DateTimeOffset Base = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);

    private static ConsultarLogQuipuxHandler NewHandler(FlitDbContext db) =>
        new(new DbQuipuxLogRepository(db));

    private static async Task SeedCatalogAsync(FlitDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = TenantId, LegalName = "Renting del Café S.A.S." });
        db.ProcedureTypes.Add(new ProcedureType {
        Family = "MATRICULAS", Id = ProcedureTypeId, Name = "Traspaso" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed record SeedOptions(
        string Status = QuipuxSubmissionEstado.Registrado,
        string Reference = "TRM-2026-000001",
        string? Plate = null,
        int? QxRegisterCode = null,
        int? QxProcedureCode = null,
        string? RejectionReason = null,
        DateTimeOffset? CreatedAt = null);

    /// <summary>Crea trámite + (opcional placa) + submission. Devuelve (submissionId, instanceId).</summary>
    private static async Task<(Guid SubmissionId, Guid InstanceId)> SeedSubmissionAsync(
        FlitDbContext db,
        SeedOptions options)
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = Guid.NewGuid();
        db.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = instanceId,
            TenantId = TenantId,
            ProcedureTypeId = ProcedureTypeId,
            ReferenceNumber = options.Reference,
            TransitOfficeId = TransitOfficeId,
            CreatedAt = options.CreatedAt ?? Base,
        });

        if (options.Plate is not null)
        {
            db.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProcedureInstanceId = instanceId,
                FieldKey = "plate",
                ValueText = options.Plate,
                CreatedAt = options.CreatedAt ?? Base,
            });
        }

        var submissionId = Guid.NewGuid();
        db.QuipuxSubmissions.Add(new QuipuxSubmission
        {
            Id = submissionId,
            TenantId = TenantId,
            ProcedureInstanceId = instanceId,
            DocumentName = $"FLIT_{options.Reference}",
            AttachmentId = Guid.NewGuid(),
            QuipuxProcedureType = 16,
            QuipuxRequirementType = 51,
            DivipoCode = "05001",
            Status = options.Status,
            QxRegisterCode = options.QxRegisterCode,
            QxProcedureCode = options.QxProcedureCode,
            RejectionReason = options.RejectionReason,
            CreatedAt = options.CreatedAt ?? Base,
        });

        await db.SaveChangesAsync(ct);
        return (submissionId, instanceId);
    }

    private static async Task SeedEventAsync(
        FlitDbContext db,
        Guid submissionId,
        string stage,
        string outcome,
        string? detail,
        DateTimeOffset occurredAt)
    {
        db.QuipuxSubmissionEvents.Add(new QuipuxSubmissionEvent
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SubmissionId = submissionId,
            Stage = stage,
            Outcome = outcome,
            Detail = detail,
            CorrelationId = Guid.NewGuid(),
            OccurredAt = occurredAt,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // ── AC1 — búsqueda por trámite devuelve estado final + códigos + timeline ordenado ──

    [Fact]
    public async Task SearchByInstanceId_ReturnsFinalStatusCodesAndOrderedTimeline_WithErrorsAndRetries()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(SearchByInstanceId_ReturnsFinalStatusCodesAndOrderedTimeline_WithErrorsAndRetries));
        await SeedCatalogAsync(db);
        var (submissionId, instanceId) = await SeedSubmissionAsync(db, new SeedOptions(
            Status: QuipuxSubmissionEstado.Aprobado, QxRegisterCode: 81, QxProcedureCode: 2));

        // Eventos sembrados fuera de orden: la línea de tiempo debe salir por OccurredAt ascendente,
        // e incluir el evento de error y el de reintento manual.
        await SeedEventAsync(db, submissionId, QuipuxStage.Aprobado, QuipuxOutcome.Ok, "{\"estado_tramite\":2}", Base.AddMinutes(30));
        await SeedEventAsync(db, submissionId, QuipuxStage.Claimed, QuipuxOutcome.Ok, "{\"intento\":0}", Base);
        await SeedEventAsync(db, submissionId, QuipuxStage.RegistroError, QuipuxOutcome.ErrorTransitorio, "{\"codigo\":500}", Base.AddMinutes(5));
        await SeedEventAsync(db, submissionId, QuipuxStage.ReintentoManual, QuipuxOutcome.Ok, null, Base.AddMinutes(10));

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, instanceId.ToString(), null, null, null), ct);

        result.TotalCount.Should().Be(1);
        var entry = result.Data.Should().ContainSingle().Subject;
        entry.ProcedureInstanceId.Should().Be(instanceId);
        entry.Status.Should().Be(QuipuxSubmissionEstado.Aprobado);
        entry.QxRegisterCode.Should().Be(81);
        entry.QxProcedureCode.Should().Be(2);
        entry.ProcedureTypeName.Should().Be("Traspaso");
        entry.ClientTenantName.Should().Be("Renting del Café S.A.S.");

        entry.Events.Select(e => e.Stage).Should().ContainInOrder(
            QuipuxStage.Claimed, QuipuxStage.RegistroError, QuipuxStage.ReintentoManual, QuipuxStage.Aprobado);
    }

    // ── AC2 — cada evento expone código, duración y origen tomados del detail instrumentado ──

    [Fact]
    public async Task SearchTimeline_ExposesResponseCodeDurationAndOrigin_FromDetail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(SearchTimeline_ExposesResponseCodeDurationAndOrigin_FromDetail));
        await SeedCatalogAsync(db);
        var (submissionId, instanceId) = await SeedSubmissionAsync(db, new SeedOptions());
        await SeedEventAsync(db, submissionId, QuipuxStage.RegistroRespuesta, QuipuxOutcome.Ok,
            "{\"codigo\":81,\"duration_ms\":1234,\"origen\":\"quipux_register\"}", Base);

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, instanceId.ToString(), null, null, null), ct);

        var ev = result.Data.Single().Events.Single();
        ev.ResponseCode.Should().Be(81);
        ev.DurationMs.Should().Be(1234);
        ev.Origin.Should().Be(QuipuxJobNames.Register);
    }

    [Fact]
    public async Task Timeline_DerivesOrigin_ForLegacyEventsWithoutOrigenInDetail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Timeline_DerivesOrigin_ForLegacyEventsWithoutOrigenInDetail));
        await SeedCatalogAsync(db);
        var (submissionId, instanceId) = await SeedSubmissionAsync(db, new SeedOptions());
        // Evento previo a la instrumentación: sin duration_ms ni origen en el detail.
        await SeedEventAsync(db, submissionId, QuipuxStage.ConsultaRespuesta, QuipuxOutcome.Ok, "{\"codigo\":81}", Base);

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, instanceId.ToString(), null, null, null), ct);

        var ev = result.Data.Single().Events.Single();
        ev.DurationMs.Should().BeNull();
        ev.Origin.Should().Be(QuipuxJobNames.StatusPoll); // derivado de la etapa consulta_*
    }

    // ── AC3 — búsqueda por placa y por radicado, con paginación ──

    [Fact]
    public async Task SearchByPlate_IsCaseInsensitiveAndTrimmed_AndProjectsThePlate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(SearchByPlate_IsCaseInsensitiveAndTrimmed_AndProjectsThePlate));
        await SeedCatalogAsync(db);
        await SeedSubmissionAsync(db, new SeedOptions(Reference: "TRM-A", Plate: "ABC123"));
        await SeedSubmissionAsync(db, new SeedOptions(Reference: "TRM-B", Plate: "ZZZ999"));

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery("  abc123 ", null, null, null, null), ct);

        result.TotalCount.Should().Be(1);
        var entry = result.Data.Single();
        entry.ReferenceNumber.Should().Be("TRM-A");
        entry.Plate.Should().Be("ABC123");
    }

    [Fact]
    public async Task SearchByRadicado_MatchesRegisterOrProcedureCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(SearchByRadicado_MatchesRegisterOrProcedureCode));
        await SeedCatalogAsync(db);
        await SeedSubmissionAsync(db, new SeedOptions(Reference: "TRM-REG", QxRegisterCode: 4567));
        await SeedSubmissionAsync(db, new SeedOptions(Reference: "TRM-PROC", QxProcedureCode: 8899));
        await SeedSubmissionAsync(db, new SeedOptions(Reference: "TRM-NONE"));

        var byRegister = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, null, "4567", null, null), ct);
        byRegister.Data.Should().ContainSingle().Which.ReferenceNumber.Should().Be("TRM-REG");

        var byProcedure = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, null, "8899", null, null), ct);
        byProcedure.Data.Should().ContainSingle().Which.ReferenceNumber.Should().Be("TRM-PROC");
    }

    [Fact]
    public async Task SearchByRadicado_NonNumeric_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(SearchByRadicado_NonNumeric_ReturnsEmpty));
        await SeedCatalogAsync(db);
        await SeedSubmissionAsync(db, new SeedOptions(QxRegisterCode: 81));

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, null, "no-numerico", null, null), ct);

        result.TotalCount.Should().Be(0);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_Paginates_AndEchoesNormalizedPaging()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Search_Paginates_AndEchoesNormalizedPaging));
        await SeedCatalogAsync(db);
        await SeedSubmissionAsync(db, new SeedOptions(Reference: "TRM-1", CreatedAt: Base));
        await SeedSubmissionAsync(db, new SeedOptions(Reference: "TRM-2", CreatedAt: Base.AddHours(1)));
        await SeedSubmissionAsync(db, new SeedOptions(Reference: "TRM-3", CreatedAt: Base.AddHours(2)));

        var page1 = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, null, null, 1, 2), ct);

        page1.TotalCount.Should().Be(3);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(2);
        page1.Data.Should().HaveCount(2);
        // Más nuevo primero.
        page1.Data[0].ReferenceNumber.Should().Be("TRM-3");
        page1.Data[1].ReferenceNumber.Should().Be("TRM-2");

        var page2 = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, null, null, 2, 2), ct);
        page2.Data.Should().ContainSingle().Which.ReferenceNumber.Should().Be("TRM-1");
    }

    // ── AC4 — inexistente e histórico parcial ──

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyPage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Search_NoMatch_ReturnsEmptyPage));
        await SeedCatalogAsync(db);
        await SeedSubmissionAsync(db, new SeedOptions(Plate: "ABC123"));

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, Guid.NewGuid().ToString(), null, null, null), ct);

        result.TotalCount.Should().Be(0);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchByInstanceId_NonUuid_ReturnsEmpty_WithoutError()
    {
        // Borde: un instanceId que no es UUID (p. ej. el usuario teclea un número con el eje "Trámite"
        // seleccionado) NO debe reventar; se resuelve como página vacía, igual que un radicado no numérico.
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(SearchByInstanceId_NonUuid_ReturnsEmpty_WithoutError));
        await SeedCatalogAsync(db);
        await SeedSubmissionAsync(db, new SeedOptions(Plate: "ABC123"));

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, "3", null, null, null), ct);

        result.TotalCount.Should().Be(0);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeline_EventWithoutDetail_IsExposedAsNull_WithoutBreaking()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Timeline_EventWithoutDetail_IsExposedAsNull_WithoutBreaking));
        await SeedCatalogAsync(db);
        var (submissionId, instanceId) = await SeedSubmissionAsync(db, new SeedOptions());
        await SeedEventAsync(db, submissionId, QuipuxStage.ReintentoManual, QuipuxOutcome.Ok, null, Base);

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, instanceId.ToString(), null, null, null), ct);

        var ev = result.Data.Single().Events.Single();
        ev.Detail.Should().BeNull(); // "sin payload disponible"
        ev.DurationMs.Should().BeNull();
        ev.ResponseCode.Should().BeNull();
    }

    // ── AC5 — solo el detail sanitizado ya persistido, nunca payload crudo ──

    [Fact]
    public async Task Timeline_PassesThroughSanitizedDetailVerbatim()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Timeline_PassesThroughSanitizedDetailVerbatim));
        await SeedCatalogAsync(db);
        var (submissionId, instanceId) = await SeedSubmissionAsync(db, new SeedOptions());
        // Detail sanitizado tal cual lo persiste la captura: solo códigos/ids/banderas.
        const string sanitized = "{\"codigo\":81,\"usa_placa\":true,\"intento\":1}";
        await SeedEventAsync(db, submissionId, QuipuxStage.RegistroRespuesta, QuipuxOutcome.Ok, sanitized, Base);

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, instanceId.ToString(), null, null, null), ct);

        var ev = result.Data.Single().Events.Single();
        ev.Detail.Should().NotBeNull();
        var detail = ev.Detail!.Value;
        detail.GetProperty("codigo").GetInt32().Should().Be(81);
        detail.GetProperty("usa_placa").GetBoolean().Should().BeTrue();
        // No hay claves de request/response crudos ni PII: el detail solo trae lo sembrado.
        detail.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("codigo", "usa_placa", "intento");
    }

    // ── HU #10794 · AC3 — enmascarado de datos sensibles (segunda barrera sobre el detail) ──

    [Fact]
    public async Task Masking_MasksSensitiveKeysInDetail_KeepingOnlyLastFourChars()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Masking_MasksSensitiveKeysInDetail_KeepingOnlyLastFourChars));
        await SeedCatalogAsync(db);
        var (submissionId, instanceId) = await SeedSubmissionAsync(db, new SeedOptions());
        // Un dato de propietario que se hubiera colado al detail pese a la sanitización de captura.
        await SeedEventAsync(db, submissionId, QuipuxStage.RegistroRespuesta, QuipuxOutcome.Ok,
            "{\"codigo\":81,\"documento_propietario\":\"1098765432\",\"usa_placa\":true}", Base);

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, instanceId.ToString(), null, null, null), ct);

        var detail = result.Data.Single().Events.Single().Detail!.Value;
        // Sensible → enmascarado, solo últimos 4 visibles.
        detail.GetProperty("documento_propietario").GetString().Should().Be("******5432");
        // Claves técnicas / no sensibles → intactas.
        detail.GetProperty("codigo").GetInt32().Should().Be(81);
        detail.GetProperty("usa_placa").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Masking_MasksNestedSensitiveValues_AndPreservesInstrumentationExtraction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext(nameof(Masking_MasksNestedSensitiveValues_AndPreservesInstrumentationExtraction));
        await SeedCatalogAsync(db);
        var (submissionId, instanceId) = await SeedSubmissionAsync(db, new SeedOptions());
        // Instrumentación (codigo/duration_ms/origen) + PII anidada bajo una clave sensible.
        await SeedEventAsync(db, submissionId, QuipuxStage.RegistroRespuesta, QuipuxOutcome.Ok,
            "{\"codigo\":81,\"duration_ms\":1234,\"origen\":\"quipux_register\","
            + "\"propietario\":{\"nombre\":\"Juan\",\"cedula\":\"123456789\"}}", Base);

        var result = await NewHandler(db).HandleAsync(new ConsultarLogQuipuxQuery(null, instanceId.ToString(), null, null, null), ct);

        var ev = result.Data.Single().Events.Single();
        // La extracción de instrumentación sigue funcionando (se lee del detail original, no del masker).
        ev.ResponseCode.Should().Be(81);
        ev.DurationMs.Should().Be(1234);
        ev.Origin.Should().Be(QuipuxJobNames.Register);
        // La PII anidada queda enmascarada; los ≤4 chars se ocultan por completo.
        var propietario = ev.Detail!.Value.GetProperty("propietario");
        propietario.GetProperty("nombre").GetString().Should().Be("****");        // "Juan" (4) → todo oculto
        propietario.GetProperty("cedula").GetString().Should().Be("*****6789");   // "123456789" (9) → últimos 4
    }
}
