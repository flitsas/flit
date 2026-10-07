using Flit.Api.Grpc;
using Flit.Ict.Grpc.Contracts;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13304 (numerales 1 y 2) — ICT materializaba trámites SIN actores ni precio de venta y sin aviso.
/// <c>CreateDraftFromIct</c> no es atómico: si <c>PutActorsHandler</c> LANZABA (p. ej. Postgres 22001 por
/// un teléfono más largo que <c>procedure_instance_actors.phone</c>) la llamada salía como gRPC Unknown con
/// el borrador ya creado, y el reintento por <c>external_ref</c> lo devolvía tal cual. Además ICT no podía
/// corregir el precio después: se agrega <c>UpdateDraftCommercial</c>, válido solo en borrador.
/// Va contra Postgres REAL porque el fallo es una restricción de columna y el estado del change tracker.
/// <para>Uso de ejemplo: el servicio se activa del contenedor real (<see cref="MarcaBlancaApiFactory"/>)
/// con <c>ActivatorUtilities</c>, como lo haría el host gRPC, y se invoca con un
/// <see cref="ServerCallContext"/> mínimo.</para>
/// </summary>
public sealed class IctOrchestrationActoresComercialIntegrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static readonly Guid UserId = new("b13304a8-0000-4000-8000-0000000000aa");
    private static readonly Guid SeededInstanceId = new("b13304a8-0000-4000-8000-000000000001");
    // Más largo que procedure_instance_actors.phone varchar(50) (DDL 132): provoca el 22001 al guardar.
    private const string TelefonoDemasiadoLargo = "+57 300 000 0000 ext 123456789 / +57 310 000 0000 ext 987654321";

    // ── Capa 2: ningún fallo silencioso ──────────────────────────────────────

    [PostgresFact]
    public async Task CreateDraftFromIct_TraspasoConComercial_PersisteElValorDeVentaConCausalPorDefecto()
    {
        await SeedTenantAsync();
        var request = Request("ict-b13304-comercial");
        request.Commercial = new CommercialData { ValorVenta = "45000000", MetodoPago = "TRANSFERENCIA" };

        var reply = await InvokeCreateAsync(request);

        reply.ProcedureInstanceId.Should().NotBeNullOrEmpty();
        (reply.ErrorCode ?? string.Empty).Should().NotContain("commercial_warning", "warnings: {0}", reply.ErrorCode);
        var commercial = await LoadCommercialAsync(Guid.Parse(reply.ProcedureInstanceId));
        commercial.Should().NotBeNull();
        commercial!.ValorVenta.Should().Be(45_000_000m);
        commercial.Causal.Should().Be("COMPRAVENTA", "el contrato v1 no trae causal");
        commercial.MetodoPago.Should().Be("TRANSFERENCIA");
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_ActorQueRevientaLaColumna_DevuelveWarningSinPiiYGuardaComercialYAdjuntos()
    {
        await SeedTenantAsync();
        var request = Request("ict-b13304-actor-largo");
        request.Commercial = new CommercialData { ValorVenta = "52000000" };
        request.Actors.Add(Parte("seller", "1013304001", "vendedor.b13304@flit.test", TelefonoDemasiadoLargo));
        request.Actors.Add(Parte("buyer", "1013304002", "comprador.b13304@flit.test"));
        request.Attachments.Add(Attachment("soat", "c3"));

        var reply = await InvokeCreateAsync(request);

        reply.ProcedureInstanceId.Should().NotBeNullOrEmpty("la excepción ya no sale como gRPC Unknown");
        reply.ErrorCode.Should().Contain("actors_warning:persist_failed");
        reply.ErrorCode.Should().NotContain(TelefonoDemasiadoLargo, "el warning nunca lleva el valor (PII)")
            .And.NotContain("1013304001");
        var instanceId = Guid.Parse(reply.ProcedureInstanceId);
        (await LoadCommercialAsync(instanceId))!.ValorVenta.Should().Be(52_000_000m,
            "el change tracker se limpió y el comercial pudo guardar");
        (await CountAttachmentsAsync(instanceId)).Should().Be(1);
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_ReintentoSobreBorradorSinActoresNiComercial_CompletaAmbos()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-retry");
        var request = Request("ict-b13304-retry");
        request.Commercial = new CommercialData { ValorVenta = "38000000" };
        request.Actors.Add(Parte("seller", "1013304011", "vendedor2.b13304@flit.test"));
        request.Actors.Add(Parte("buyer", "1013304012", "comprador2.b13304@flit.test"));

        var reply = await InvokeCreateAsync(request);

        reply.ProcedureInstanceId.Should().Be(SeededInstanceId.ToString(), "el reintento devuelve el MISMO borrador");
        (reply.ErrorCode ?? string.Empty).Should().NotContain("actors_warning").And.NotContain("commercial_warning");
        (await CountActorsAsync(SeededInstanceId)).Should().Be(2, "el reintento completa los actores faltantes");
        (await LoadCommercialAsync(SeededInstanceId))!.ValorVenta.Should().Be(38_000_000m);
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_ReintentoConComercialYaCapturado_NoLoPisa()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-retry-comercial");
        await SeedCommercialAsync(10_000_000m, "DONACION");
        var request = Request("ict-b13304-retry-comercial");
        request.Commercial = new CommercialData { ValorVenta = "99000000" };

        await InvokeCreateAsync(request);

        var commercial = await LoadCommercialAsync(SeededInstanceId);
        commercial!.ValorVenta.Should().Be(10_000_000m, "lo que ya existe no se reescribe en el reintento");
        commercial.Causal.Should().Be("DONACION");
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_ReintentoSobreTramiteNoBorrador_NoCompletaActoresNiComercial()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-retry-anulado", TramiteEstado.Anulado);
        var request = Request("ict-b13304-retry-anulado");
        request.Commercial = new CommercialData { ValorVenta = "38000000" };
        request.Actors.Add(Parte("buyer", "1013304021", "comprador3.b13304@flit.test"));

        var reply = await InvokeCreateAsync(request);

        reply.Status.Should().Be(TramiteEstado.Anulado);
        (await CountActorsAsync(SeededInstanceId)).Should().Be(0);
        (await LoadCommercialAsync(SeededInstanceId)).Should().BeNull();
    }

    // ── UpdateDraftCommercial ────────────────────────────────────────────────

    [PostgresFact]
    public async Task UpdateDraftCommercial_EnBorrador_ActualizaElValorYConservaLaCausal()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-upd");
        await SeedCommercialAsync(10_000_000m, "DONACION");

        var reply = await InvokeUpdateAsync(UpdateRequest(TenantSeed.LoneId, "ict-b13304-upd", "61000000"));

        (reply.ErrorCode ?? string.Empty).Should().BeEmpty();
        reply.ProcedureInstanceId.Should().Be(SeededInstanceId.ToString());
        reply.Status.Should().Be(TramiteEstado.Borrador);
        var commercial = await LoadCommercialAsync(SeededInstanceId);
        commercial!.ValorVenta.Should().Be(61_000_000m);
        commercial.Causal.Should().Be("DONACION", "solo cambia el valor; la causal del gestor se conserva");
    }

    [PostgresFact]
    public async Task UpdateDraftCommercial_SinComercialPrevio_LoCreaConCompraventa()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-upd-nuevo");

        var reply = await InvokeUpdateAsync(UpdateRequest(TenantSeed.LoneId, "ict-b13304-upd-nuevo", "20000000"));

        (reply.ErrorCode ?? string.Empty).Should().BeEmpty();
        var commercial = await LoadCommercialAsync(SeededInstanceId);
        commercial!.ValorVenta.Should().Be(20_000_000m);
        commercial.Causal.Should().Be("COMPRAVENTA");
    }

    [PostgresFact]
    public async Task UpdateDraftCommercial_FueraDeBorrador_DevuelveNotDraftSinTocarElValor()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-upd-anulado", TramiteEstado.Anulado);
        await SeedCommercialAsync(10_000_000m, "COMPRAVENTA");

        var reply = await InvokeUpdateAsync(UpdateRequest(TenantSeed.LoneId, "ict-b13304-upd-anulado", "61000000"));

        reply.ErrorCode.Should().Be("not_draft");
        (await LoadCommercialAsync(SeededInstanceId))!.ValorVenta.Should().Be(10_000_000m);
    }

    [PostgresFact]
    public async Task UpdateDraftCommercial_OtroTenant_DevuelveNotFound()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-upd-tenant");

        var reply = await InvokeUpdateAsync(UpdateRequest(Guid.NewGuid(), "ict-b13304-upd-tenant", "61000000"));

        reply.ErrorCode.Should().Be("not_found");
        (await LoadCommercialAsync(SeededInstanceId)).Should().BeNull();
    }

    [PostgresFact]
    public async Task UpdateDraftCommercial_ExternalRefQueNoCoincide_DevuelveNotFound()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-upd-ref");

        var reply = await InvokeUpdateAsync(UpdateRequest(TenantSeed.LoneId, "otro-pre-tramite", "61000000"));

        reply.ErrorCode.Should().Be("not_found");
    }

    [PostgresFact]
    public async Task UpdateDraftCommercial_ValorCero_DevuelveInvalidValorVenta()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, "ict-b13304-upd-cero");

        var reply = await InvokeUpdateAsync(UpdateRequest(TenantSeed.LoneId, "ict-b13304-upd-cero", "0"));

        reply.ErrorCode.Should().Be("invalid_valor_venta");
    }

    // ── invocación ───────────────────────────────────────────────────────────

    private async Task<DraftReply> InvokeCreateAsync(CreateDraftFromIctRequest request)
    {
        await using var factory = new MarcaBlancaApiFactory(Fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = ActivatorUtilities.CreateInstance<IctOrchestrationService>(scope.ServiceProvider);
        return await service.CreateDraftFromIct(request, new TestCallContext(TestContext.Current.CancellationToken));
    }

    private async Task<DraftReply> InvokeUpdateAsync(UpdateDraftCommercialRequest request)
    {
        await using var factory = new MarcaBlancaApiFactory(Fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = ActivatorUtilities.CreateInstance<IctOrchestrationService>(scope.ServiceProvider);
        return await service.UpdateDraftCommercial(request, new TestCallContext(TestContext.Current.CancellationToken));
    }

    private static UpdateDraftCommercialRequest UpdateRequest(Guid tenantId, string externalRef, string valor) => new()
    {
        TenantId = tenantId.ToString(),
        ProcedureInstanceId = SeededInstanceId.ToString(),
        ExternalRef = externalRef,
        Commercial = new CommercialData { ValorVenta = valor },
    };

    private static CreateDraftFromIctRequest Request(string externalRef)
    {
        // Sin placa/VIN: no corre el preflight (no sale a proveedores).
        var request = new CreateDraftFromIctRequest
        {
            TenantId = TenantSeed.LoneId.ToString(),
            ProcedureTypeCode = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            Origin = "ict",
            ExternalRef = externalRef,
        };
        request.FieldValues.Add(new FieldValue { FieldKey = "marcador", ValueText = "x" });
        return request;
    }

    private static Actor Parte(string tipo, string documento, string email, string? telefono = null) => new()
    {
        ActorType = tipo,
        DocumentType = "CC",
        DocumentNumber = documento,
        FullName = $"Parte {tipo} Integración",
        Email = email,
        Phone = telefono ?? string.Empty,
    };

    private static AttachmentRef Attachment(string tipo, string shaSeed) => new()
    {
        DocumentType = tipo,
        Filename = $"{tipo}.pdf",
        MimeType = "application/pdf",
        SizeBytes = 1024,
        Sha256 = new string(shaSeed[0], 32) + new string(shaSeed[1], 32),
        StoragePath = $"it/b13304/{tipo}.pdf",
    };

    // ── datos ────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedTenantAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = UserId,
            Email = "it-b13304-ict@flit.test",
            DisplayName = "ICT B13304",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        return await ctx.ProcedureTypes.AsNoTracking()
            .Where(t => t.Code == TramiteTipologiaCatalog.CodigoTraspasoStandard)
            .Select(t => t.Id)
            .SingleAsync();
    }

    /// <summary>Borrador materializado por un intento previo que se cayó antes de actores/comercial.</summary>
    private async Task SeedExistingDraftAsync(Guid typeId, string externalRef, string status = TramiteEstado.Borrador)
    {
        await using var ctx = NewContext();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = SeededInstanceId,
            TenantId = TenantSeed.LoneId,
            ProcedureTypeId = typeId,
            CreatedByUserId = UserId,
            Status = status,
            Origin = "ict",
            ExternalRef = externalRef,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedCommercialAsync(decimal valor, string causal)
    {
        await using var ctx = NewContext();
        ctx.Set<ProcedureInstanceCommercial>().Add(new ProcedureInstanceCommercial
        {
            Id = Guid.NewGuid(),
            TenantId = TenantSeed.LoneId,
            ProcedureInstanceId = SeededInstanceId,
            ValorVenta = valor,
            Causal = causal,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<ProcedureInstanceCommercial?> LoadCommercialAsync(Guid instanceId)
    {
        await using var ctx = NewContext();
        return await ctx.Set<ProcedureInstanceCommercial>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.ProcedureInstanceId == instanceId);
    }

    private async Task<int> CountActorsAsync(Guid instanceId)
    {
        await using var ctx = NewContext();
        return await ctx.Set<ProcedureInstanceActor>()
            .IgnoreQueryFilters()
            .CountAsync(a => a.ProcedureInstanceId == instanceId);
    }

    private async Task<int> CountAttachmentsAsync(Guid instanceId)
    {
        await using var ctx = NewContext();
        return await ctx.ProcedureInstanceAttachments
            .IgnoreQueryFilters()
            .CountAsync(a => a.ProcedureInstanceId == instanceId);
    }

    /// <summary><see cref="ServerCallContext"/> mínimo: el servicio solo lee el token de cancelación.</summary>
    private sealed class TestCallContext(CancellationToken ct) : ServerCallContext
    {
        protected override string MethodCore => "/flit.ict.IctOrchestration/CreateDraftFromIct";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:0";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => ct;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, []);

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
