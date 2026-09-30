using Flit.Api.Grpc;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Ict.Grpc.Contracts;
using Flit.Integration.Tests.MarcaBlanca;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13109 (punto 8) — los adjuntos de la materialización ICT se perdían. <c>CreateDraftFromIct</c>
/// comparte un solo <c>FlitDbContext</c> entre pasos; <c>row_version</c> de la instancia lo sube el
/// trigger <c>tr_procedure_instances_row_version</c> y EF no lo relee, así que el AutoMark del
/// checklist en <see cref="Flit.Tramites.Application.UseCases.ProcedureInstances.RegisterIntegrationAttachmentHandler.HandleBatchAsync"/>
/// viajaba con el token viejo → <c>DbUpdateConcurrencyException</c> → gRPC Unknown. En el reintento
/// la rama idempotente devolvía el borrador sin adjuntos. Por eso va contra Postgres REAL: el fallo
/// depende del trigger.
/// <para>Uso de ejemplo: el servicio se resuelve del contenedor real (<see cref="MarcaBlancaApiFactory"/>)
/// con <c>ActivatorUtilities</c>, igual que lo activaría el host gRPC, y se invoca con un
/// <see cref="ServerCallContext"/> mínimo.</para>
/// </summary>
public sealed class IctOrchestrationAdjuntosIntegrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static readonly Guid UserId = new("b13109a8-0000-4000-8000-0000000000aa");
    private static readonly Guid SeededInstanceId = new("b13109a8-0000-4000-8000-000000000001");

    [PostgresFact]
    public async Task CreateDraftFromIct_ConComercialYAdjuntos_RegistraLosAdjuntosSinConflictoDeConcurrencia()
    {
        await SeedTenantAsync();
        var request = Request(externalRef: "ict-b13109-normal");
        request.Commercial = new CommercialData { ValorVenta = "45000000" };
        request.Actors.Add(Parte("seller", "1013109001", "vendedor.b13109@flit.test"));
        request.Actors.Add(Parte("buyer", "1013109002", "comprador.b13109@flit.test"));

        var reply = await InvokeAsync(request);

        reply.ProcedureInstanceId.Should().NotBeNullOrEmpty("el borrador se crea antes de los adjuntos");
        (reply.ErrorCode ?? string.Empty).Should().NotContain("attachments_warning", "warnings: {0}", reply.ErrorCode);
        var instanceId = Guid.Parse(reply.ProcedureInstanceId);
        (await CountAttachmentsAsync(instanceId)).Should().Be(2,
            "los dos adjuntos ICT deben persistir aunque pasos previos hayan subido row_version");
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_ReintentoIdempotenteSobreBorradorSinAdjuntos_VuelveARegistrarlosSinDuplicar()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, externalRef: "ict-b13109-retry");

        var first = await InvokeAsync(Request(externalRef: "ict-b13109-retry"));
        var second = await InvokeAsync(Request(externalRef: "ict-b13109-retry"));

        first.ProcedureInstanceId.Should().Be(SeededInstanceId.ToString(), "el reintento devuelve el MISMO borrador");
        second.ProcedureInstanceId.Should().Be(SeededInstanceId.ToString());
        (first.ErrorCode ?? string.Empty).Should().BeEmpty();
        (await CountAttachmentsAsync(SeededInstanceId)).Should().Be(2,
            "el reintento completa los adjuntos perdidos y la dedup por sha256 evita duplicarlos");
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_ReintentoIdempotenteSobreTramiteNoEditable_NoRegistraAdjuntos()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, externalRef: "ict-b13109-anulado", status: TramiteEstado.Anulado);

        var reply = await InvokeAsync(Request(externalRef: "ict-b13109-anulado"));

        reply.ProcedureInstanceId.Should().Be(SeededInstanceId.ToString());
        reply.Status.Should().Be(TramiteEstado.Anulado);
        (reply.ErrorCode ?? string.Empty).Should().BeEmpty("fuera de edición no se intenta el registro");
        (await CountAttachmentsAsync(SeededInstanceId)).Should().Be(0);
    }

    [PostgresFact]
    public async Task CreateDraftFromIct_FalloInesperadoAlRegistrarAdjuntos_DevuelveElBorradorConWarningEnVezDeLanzar()
    {
        var typeId = await SeedTenantAsync();
        await SeedExistingDraftAsync(typeId, externalRef: "ict-b13109-throw");
        var failingRepo = Substitute.For<IProcedureInstanceRepository>();
        failingRepo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("fallo simulado"));

        var reply = await InvokeAsync(
            Request(externalRef: "ict-b13109-throw"),
            new RegisterIntegrationAttachmentHandler(failingRepo));

        reply.ProcedureInstanceId.Should().Be(SeededInstanceId.ToString(), "el borrador ya existe: no se pierde");
        reply.ErrorCode.Should().Be("attachments_warning:exception");
    }

    // ── invocación ───────────────────────────────────────────────────────────

    private async Task<DraftReply> InvokeAsync(
        CreateDraftFromIctRequest request,
        RegisterIntegrationAttachmentHandler? attachmentsOverride = null)
    {
        await using var factory = new MarcaBlancaApiFactory(Fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = attachmentsOverride is null
            ? ActivatorUtilities.CreateInstance<IctOrchestrationService>(scope.ServiceProvider)
            : ActivatorUtilities.CreateInstance<IctOrchestrationService>(scope.ServiceProvider, attachmentsOverride);
        return await service.CreateDraftFromIct(request, new TestCallContext(TestContext.Current.CancellationToken));
    }

    private static CreateDraftFromIctRequest Request(string externalRef)
    {
        // Sin placa/VIN (no hay preflight que salga a proveedores) y sin actores: el foco es la
        // secuencia create → patch → commercial → adjuntos sobre el MISMO DbContext.
        var request = new CreateDraftFromIctRequest
        {
            TenantId = TenantSeed.LoneId.ToString(),
            ProcedureTypeCode = TramiteTipologiaCatalog.CodigoTraspasoStandard,
            Origin = "ict",
            ExternalRef = externalRef,
        };
        request.FieldValues.Add(new FieldValue { FieldKey = "it_b13109_marker", ValueText = "x" });
        // Tipos CON ítem de checklist en TRASPASO_STANDARD: fuerzan el AutoMark (UPDATE de la instancia).
        request.Attachments.Add(Attachment("soat", "a1"));
        request.Attachments.Add(Attachment("cedulas", "b2"));
        return request;
    }

    private static Actor Parte(string tipo, string documento, string email) => new()
    {
        ActorType = tipo,
        DocumentType = "CC",
        DocumentNumber = documento,
        FullName = $"Parte {tipo} Integración",
        Email = email,
    };

    private static AttachmentRef Attachment(string tipo, string shaSeed) => new()
    {
        DocumentType = tipo,
        Filename = $"{tipo}.pdf",
        MimeType = "application/pdf",
        SizeBytes = 1024,
        Sha256 = new string(shaSeed[0], 32) + new string(shaSeed[1], 32),
        StoragePath = $"it/b13109/{tipo}.pdf",
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
            Email = "it-b13109-ict@flit.test",
            DisplayName = "ICT B13109",
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

    /// <summary>Borrador ya materializado por un intento previo que perdió los adjuntos.</summary>
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
