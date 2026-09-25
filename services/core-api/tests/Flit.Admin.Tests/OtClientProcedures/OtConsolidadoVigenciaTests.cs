using System.Text.Json;
using Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;
using Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Quipux.Domain.Envios;
using Flit.Queries.Domain.Documentos;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.OtClientProcedures;

/// <summary>
/// HU #12791 (Épica #12760) — la bandeja OT (también la vista SuperAdmin de trámites por organismo,
/// que consume el MISMO <c>GET /admin/ot/client-procedures</c>) y el detalle OT exponen la vigencia +
/// sello de los dos consolidados, proyectados en la misma consulta de la fila.
/// <para>Uso de ejemplo: <c>(await ListarAsync(db)).Data.Single(p =&gt; p.Id == id).ConsolidadoWizard!.Estado</c>
/// ⇒ <c>"vigente"</c>.</para>
/// </summary>
public sealed class OtConsolidadoVigenciaTests
{
    private static readonly Guid OtTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid ClientTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid TransitOffice = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee1");
    private static readonly Guid TipoMatricula = Guid.Parse("11111111-1111-1111-1111-111111111191");

    private static readonly Guid Entregado = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    private static readonly Guid Aprobado = Guid.Parse("c0000000-0000-4000-8000-000000000002");
    private static readonly Guid CargadoUser = Guid.Parse("c0000000-0000-4000-8000-000000000003");
    private static readonly Guid MigradoFinal = Guid.Parse("c0000000-0000-4000-8000-000000000004");

    private static readonly DateTimeOffset SelloWizard = new(2026, 9, 20, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SelloMaestro = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RadicadoViejo = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RadicadoNuevo = new(2026, 9, 21, 16, 45, 0, TimeSpan.Zero);
    private static readonly Guid MaestroViejo = Guid.Parse("d0000000-0000-4000-8000-000000000001");
    private static readonly Guid MaestroNuevo = Guid.Parse("d0000000-0000-4000-8000-000000000002");

    [Fact]
    public async Task AC1_DetalleOt_WizardVigenteYMaestroDesactualizado()
    {
        var db = await SembrarAsync();

        var result = await DetalleAsync(db, Entregado);

        result.Status.Should().Be(GetOtClientProcedureStatus.Found);
        result.Procedure!.ConsolidadoWizard.Should().Be(
            new ConsolidadoVigenciaDto("vigente", SelloWizard, "system", false, null));
        result.Procedure.ConsolidadoMaestro.Should().Be(
            new ConsolidadoVigenciaDto("desactualizado", SelloMaestro, "system", false, null));
    }

    [Fact]
    public async Task AC1_DetalleOt_SerializaLosCamposNuevosEnCamelCase()
    {
        var db = await SembrarAsync();

        var result = await DetalleAsync(db, Entregado);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            result.Procedure, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        json.RootElement.GetProperty("consolidadoWizard").GetProperty("estado").GetString().Should().Be("vigente");
        json.RootElement.GetProperty("consolidadoMaestro").GetProperty("estado").GetString().Should().Be("desactualizado");
    }

    [Fact]
    public async Task AC2_BandejaOt_SinConsolidadoMaestro_Inexistente_ConSelloNull()
    {
        var db = await SembrarAsync();

        var fila = (await ListarAsync(db)).Data.Single(p => p.Id == CargadoUser);

        fila.ConsolidadoMaestro.Should().Be(new ConsolidadoVigenciaDto("inexistente", null, null, false, null));
    }

    [Fact]
    public async Task AC3_BandejaOt_TodasLasFilasTraenLaVigencia_EnUnaSolaLecturaDelRepositorio()
    {
        var db = await SembrarAsync();

        var result = await ListarAsync(db);

        result.Data.Should().HaveCount(4).And.OnlyContain(p => p.ConsolidadoWizard != null && p.ConsolidadoMaestro != null);
        result.Data.Single(p => p.Id == Entregado).ConsolidadoWizard!.Estado.Should().Be("vigente");
    }

    [Fact]
    public async Task AC4_BandejaOt_EstadoFinal_SourceUser_YMigrado()
    {
        var db = await SembrarAsync();

        var data = (await ListarAsync(db)).Data;

        data.Single(p => p.Id == Aprobado).ConsolidadoWizard.Should().Be(
            new ConsolidadoVigenciaDto("vigente", SelloWizard, "system", true, "definitivo_estado_final"));
        data.Single(p => p.Id == CargadoUser).ConsolidadoWizard.Should().Be(
            new ConsolidadoVigenciaDto("desactualizado", SelloWizard, "user", false, "cargado_por_usuario"));
        data.Single(p => p.Id == MigradoFinal).ConsolidadoWizard.Should().Be(
            new ConsolidadoVigenciaDto("vigente", null, "system", true, "migrado_solo_lectura"));
        data.Single(p => p.Id == MigradoFinal).ConsolidadoMaestro.Should().Be(
            new ConsolidadoVigenciaDto("inexistente", null, null, false, "migrado_solo_lectura"));
    }

    [Fact]
    public async Task AC4_DetalleOt_ElAdjuntoMasRecienteDecideElOrigen()
    {
        // El adjunto más reciente del tipo decide el origen (mismo criterio que la ruta de entrega).
        var db = await SembrarAsync();
        await using (var ctx = NewContext(db))
        {
            ctx.ProcedureInstanceAttachments.Add(Adjunto(Entregado, "consolidado", "user", SelloWizard.AddHours(1)));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await DetalleAsync(db, Entregado);

        result.Procedure!.ConsolidadoWizard!.Origen.Should().Be("user");
        result.Procedure.ConsolidadoWizard.Modo.Should().Be("cargado_por_usuario");
    }

    // ── Ampliación #12787 AC2 — fecha de radicación Quipux ───────────────────────────────────

    [Fact]
    public async Task AC1_DetalleOt_Radicado_ExponeLaUltimaRadicacionExitosaYSuMaestro()
    {
        // Dos radicaciones exitosas (la vieja terminó rechazada por Quipux; la nueva sigue registrada):
        // gana la MÁS RECIENTE por RegisteredAt, con el adjunto que se envió en ella.
        var db = await SembrarAsync();

        var result = await DetalleAsync(db, Entregado);

        result.Procedure!.QuipuxRadicadoEn.Should().Be(RadicadoNuevo);
        result.Procedure.QuipuxMaestroAttachmentId.Should().Be(MaestroNuevo);
    }

    [Fact]
    public async Task AC1_DetalleOt_SinRadicar_Null()
    {
        var db = await SembrarAsync();

        var result = await DetalleAsync(db, CargadoUser);

        result.Procedure!.QuipuxRadicadoEn.Should().BeNull();
        result.Procedure.QuipuxMaestroAttachmentId.Should().BeNull();
    }

    [Fact]
    public async Task AC1_SubmissionFallida_NoCuentaComoRadicacion_NiEnDetalleNiEnBandeja()
    {
        // 'fallido' = agotó reintentos sin radicar. Aunque trajera un RegisteredAt espurio, no cuenta.
        var db = await SembrarAsync();

        var detalle = await DetalleAsync(db, Aprobado);
        var fila = (await ListarAsync(db)).Data.Single(p => p.Id == Aprobado);

        detalle.Procedure!.QuipuxRadicadoEn.Should().BeNull();
        detalle.Procedure.QuipuxMaestroAttachmentId.Should().BeNull();
        fila.QuipuxRadicadoEn.Should().BeNull();
    }

    [Fact]
    public async Task AC3_BandejaOt_ExponeLaRadicacionPorFila()
    {
        var db = await SembrarAsync();

        var data = (await ListarAsync(db)).Data;

        data.Single(p => p.Id == Entregado).QuipuxRadicadoEn.Should().Be(RadicadoNuevo);
        data.Single(p => p.Id == Entregado).QuipuxMaestroAttachmentId.Should().Be(MaestroNuevo);
        data.Single(p => p.Id == CargadoUser).QuipuxRadicadoEn.Should().BeNull();
    }

    [Fact]
    public async Task AC5_DetalleOt_SerializaQuipuxRadicadoEnComoIsoYNullSinRadicar()
    {
        var db = await SembrarAsync();
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        using var radicado = JsonDocument.Parse(JsonSerializer.Serialize((await DetalleAsync(db, Entregado)).Procedure, web));
        using var sinRadicar = JsonDocument.Parse(JsonSerializer.Serialize((await DetalleAsync(db, CargadoUser)).Procedure, web));

        radicado.RootElement.GetProperty("quipuxRadicadoEn").GetDateTimeOffset().Should().Be(RadicadoNuevo);
        radicado.RootElement.GetProperty("quipuxMaestroAttachmentId").GetGuid().Should().Be(MaestroNuevo);
        sinRadicar.RootElement.GetProperty("quipuxRadicadoEn").ValueKind.Should().Be(JsonValueKind.Null);
        sinRadicar.RootElement.GetProperty("quipuxMaestroAttachmentId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task<GetOtClientProcedureResult> DetalleAsync(string db, Guid id)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        return await new GetOtClientProcedureHandler(repo).HandleAsync(
            new GetOtClientProcedureQuery { OtTenantId = OtTenant, ProcedureInstanceId = id },
            TestContext.Current.CancellationToken);
    }

    private static async Task<ListOtClientProceduresResult> ListarAsync(string db)
    {
        await using var ctx = NewContext(db);
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());
        return await new ListOtClientProceduresHandler(repo).HandleAsync(
            new ListOtClientProceduresQuery { OtTenantId = OtTenant },
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> SembrarAsync()
    {
        var db = Guid.NewGuid().ToString();
        await using var ctx = NewContext(db);

        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = OtTenant,
            TransitOfficeId = TransitOffice,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = ClientTenant,
            TransitOfficeId = TransitOffice,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Tenants.Add(new Tenant
        {
            Id = ClientTenant,
            Code = "cli12791",
            LegalName = "Flota Andina S.A.S.",
            TaxId = "900000000",
            TenantType = "client",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = TipoMatricula,
            Code = "matricula_inicial",
            Name = "Matrícula inicial",
            Family = "MATRICULAS",
            IsActive = true,
            PublicationStatus = PublicationStatus.Published,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Entregado: wizard vigente + maestro desactualizado (ambos con PDF).
        ctx.ProcedureInstances.Add(Instancia(Entregado, 1, TramiteEstado.Entregado, wizardVigente: true, maestroVigente: false));
        ctx.ProcedureInstanceAttachments.Add(Adjunto(Entregado, "consolidado", "system", SelloWizard));
        ctx.ProcedureInstanceAttachments.Add(Adjunto(Entregado, "consolidado_maestro", "system", SelloMaestro));

        // Aprobado: la transición bajó la bandera, pero el PDF es el definitivo.
        ctx.ProcedureInstances.Add(Instancia(Aprobado, 2, TramiteEstado.Aprobado, wizardVigente: false, maestroVigente: false));
        ctx.ProcedureInstanceAttachments.Add(Adjunto(Aprobado, "consolidado", "system", SelloWizard));

        // Carga manual del SuperAdmin (Source=user), sin maestro.
        ctx.ProcedureInstances.Add(Instancia(CargadoUser, 3, TramiteEstado.Entregado, wizardVigente: false, maestroVigente: true));
        ctx.ProcedureInstanceAttachments.Add(Adjunto(CargadoUser, "consolidado", "user", SelloWizard));

        // Migrado V1 aprobado, sin sello (histórico) y sin maestro.
        var migrado = Instancia(MigradoFinal, 4, TramiteEstado.Aprobado, wizardVigente: false, maestroVigente: false);
        migrado.IsMigrated = true;
        migrado.ConsolidadoWizardGeneradoEn = null;
        migrado.ConsolidadoMaestroGeneradoEn = null;
        ctx.ProcedureInstances.Add(migrado);
        ctx.ProcedureInstanceAttachments.Add(Adjunto(MigradoFinal, "consolidado", "system", SelloWizard));

        // Quipux: Entregado radicado dos veces (vieja rechazada, nueva registrada); Aprobado con un
        // envío fallido (con RegisteredAt espurio a propósito); CargadoUser nunca enviado.
        ctx.QuipuxSubmissions.AddRange(
            Submission(Entregado, QuipuxSubmissionEstado.Rechazado, RadicadoViejo, MaestroViejo),
            Submission(Entregado, QuipuxSubmissionEstado.Registrado, RadicadoNuevo, MaestroNuevo),
            Submission(Entregado, QuipuxSubmissionEstado.Pendiente, null, Guid.NewGuid()),
            Submission(Aprobado, QuipuxSubmissionEstado.Fallido, RadicadoNuevo, Guid.NewGuid()));

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    private static QuipuxSubmission Submission(Guid instanceId, string status, DateTimeOffset? registeredAt, Guid attachmentId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = ClientTenant,
        ProcedureInstanceId = instanceId,
        DocumentName = "FLOTA_FT1_20260921_1645_x",
        AttachmentId = attachmentId,
        QuipuxProcedureType = 13,
        QuipuxRequirementType = 51,
        DivipoCode = "05001000",
        Status = status,
        RegisteredAt = registeredAt,
        CreatedAt = (registeredAt ?? SelloWizard).AddMinutes(-1),
    };

    private static ProcedureInstance Instancia(Guid id, int consecutivo, string status, bool wizardVigente, bool maestroVigente) => new()
    {
        Id = id,
        TenantId = ClientTenant,
        ProcedureTypeId = TipoMatricula,
        ReferenceNumber = $"FT1-000000{consecutivo}",
        Consecutivo = consecutivo,
        Status = status,
        TransitOfficeId = TransitOffice,
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = new DateTimeOffset(2026, 9, consecutivo, 10, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 9, consecutivo, 10, 0, 0, TimeSpan.Zero),
        ConsolidadoWizardVigente = wizardVigente,
        ConsolidadoWizardGeneradoEn = SelloWizard,
        ConsolidadoMaestroVigente = maestroVigente,
        ConsolidadoMaestroGeneradoEn = SelloMaestro,
    };

    private static ProcedureInstanceAttachment Adjunto(Guid instanceId, string tipo, string source, DateTimeOffset uploadedAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = ClientTenant,
        ProcedureInstanceId = instanceId,
        Tipo = tipo,
        Filename = tipo + ".pdf",
        Mimetype = "application/pdf",
        Sha256 = new string('a', 64),
        StoragePath = "tramites/" + instanceId + "/" + tipo + ".pdf",
        Source = source,
        UploadedAt = uploadedAt,
    };

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options);
}
