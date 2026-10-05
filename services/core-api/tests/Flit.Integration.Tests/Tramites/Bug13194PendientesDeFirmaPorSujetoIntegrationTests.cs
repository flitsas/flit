using System.Text.Json;
using Flit.Api.Grpc;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13194 (punto 4) — el lote de firma al aprobar una identidad encuentra TODOS los trámites
/// pendientes de la persona en el MISMO tenant, también cuando es el representante legal de una PJ
/// (documento en <c>actor.metadata</c>, jsonb). Va contra PostgreSQL real porque depende del operador
/// <c>@&gt;</c> sobre jsonb y del filtro por tenant en SQL.
/// <para>Uso de ejemplo: <c>await repo.ListPendientesDeFirmaPorSujetoAsync(tenantId, "CC", documento)</c>.</para>
/// </summary>
public sealed class Bug13194PendientesDeFirmaPorSujetoIntegrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = TenantSeed.LoneId;
    private static readonly Guid TenantB = new("13194000-0000-4000-8000-0000000000b0");
    private const string Persona = "7013194";
    private const string OtraPersona = "7013195";

    private static readonly Guid PjAsignado = new("13194000-0000-4000-8000-000000000001");
    private static readonly Guid PnBorradorFinalizadoVendedor = new("13194000-0000-4000-8000-000000000002");
    private static readonly Guid PnBorradorSinFinalizar = new("13194000-0000-4000-8000-000000000003");
    private static readonly Guid PnEntregado = new("13194000-0000-4000-8000-000000000004");
    private static readonly Guid OtraPersonaPreparado = new("13194000-0000-4000-8000-000000000005");
    private static readonly Guid OtroTenantAsignado = new("13194000-0000-4000-8000-000000000006");
    private static readonly Guid PnSubsanacion = new("13194000-0000-4000-8000-000000000007");

    [PostgresFact]
    public async Task ListPendientesDeFirmaPorSujeto_SoloMismoTenantMismaPersonaYEstadosPendientes()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        var result = await repo.ListPendientesDeFirmaPorSujetoAsync(
            TenantA, "CC", Persona, TestContext.Current.CancellationToken);

        result.Select(i => i.Id).Should().BeEquivalentTo(
            [PjAsignado, PnBorradorFinalizadoVendedor, PnSubsanacion],
            "PJ por su RL en metadata, PN como cualquier parte; fuera: borrador sin finalizar, entregado, "
            + "otra persona y otro tenant");
        result.Should().OnlyContain(i => i.ProcedureType != null && i.Actors.Count > 0,
            "el consumidor necesita el tipo y los actores para decidir qué firmar");
    }

    /// <summary>
    /// Review PR #510 (MENOR-5) — el tipo de documento de la validación puede venir en minúsculas («cc»):
    /// columna y metadata del representante se comparan sin distinguir mayúsculas.
    /// </summary>
    [PostgresFact]
    public async Task ListPendientesDeFirmaPorSujeto_NoDistingueMayusculasEnElTipo()
    {
        await SeedAsync();
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        var result = await repo.ListPendientesDeFirmaPorSujetoAsync(
            TenantA, "cc", Persona, TestContext.Current.CancellationToken);

        result.Select(i => i.Id).Should().BeEquivalentTo(
            [PjAsignado, PnBorradorFinalizadoVendedor, PnSubsanacion],
            "«cc» y «CC» son el mismo tipo de documento, en columna y en el RL de la metadata");
    }

    [PostgresFact]
    public async Task ListInstanceIdsConEventoDeValidacion_SoloLaMismaValidacionYTenant()
    {
        await SeedAsync();
        var validationId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.ProcedureInstanceEvents.Add(new ProcedureInstanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                ProcedureInstanceId = PjAsignado,
                Tipo = "firma_auto_solicitada",
                Payload = JsonSerializer.Serialize(new { validation_id = validationId, parte = "comprador" }),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var ct = TestContext.Current.CancellationToken;

        (await repo.ListInstanceIdsConEventoDeValidacionAsync(TenantA, "firma_auto_solicitada", validationId, ct))
            .Should().BeEquivalentTo([PjAsignado]);
        (await repo.ListInstanceIdsConEventoDeValidacionAsync(TenantA, "firma_auto_solicitada", Guid.NewGuid(), ct))
            .Should().BeEmpty("otra validación no cuenta como ya aplicada");
        (await repo.ListInstanceIdsConEventoDeValidacionAsync(TenantB, "firma_auto_solicitada", validationId, ct))
            .Should().BeEmpty("otro tenant");
    }

    [Fact]
    public void IctIdentityWarningCode_ReportaFallosYSilenciaLoCubierto()
    {
        IctOrchestrationService.IdentityWarningCode(
                new EnsureIdentityAndNotifyResult(EnsureIdentityOutcomes.RequiereValidacion, null,
                    IdentityNotificationOutcomes.Fallida, "proveedor_error"), null)
            .Should().Be("proveedor_error", "el fallo de Kyverum ya no se descarta");
        IctOrchestrationService.IdentityWarningCode(
                new EnsureIdentityAndNotifyResult(EnsureIdentityOutcomes.SinActor, null,
                    IdentityNotificationOutcomes.NoRequerida), null)
            .Should().Be(EnsureIdentityOutcomes.SinActor);
        IctOrchestrationService.IdentityWarningCode(null, "not_found").Should().Be("not_found");
        IctOrchestrationService.IdentityWarningCode(
                new EnsureIdentityAndNotifyResult(EnsureIdentityOutcomes.FirmaBaul, null,
                    IdentityNotificationOutcomes.NoRequerida), null)
            .Should().BeNull();
        IctOrchestrationService.IdentityWarningCode(
                new EnsureIdentityAndNotifyResult(EnsureIdentityOutcomes.RequiereValidacion, Guid.NewGuid(),
                    IdentityNotificationOutcomes.Enviada), null)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("NIT", true)]
    [InlineData("n.i.t.", true)]
    [InlineData("CC", false)]
    public void IctEsTipoNit_ToleraVariantes(string tipo, bool esperado) =>
        IctOrchestrationService.EsTipoNit(tipo).Should().Be(esperado);

    // ── datos ────────────────────────────────────────────────────────────────

    private async Task SeedAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Tenants.Add(TenantSeed.Lone(TenantB, "IT-B13194"));
        await ctx.SaveChangesAsync();

        var userA = await SeedUserAsync(ctx, TenantA, "it-b13194-a@flit.test");
        var userB = await SeedUserAsync(ctx, TenantB, "it-b13194-b@flit.test");
        var typeId = await ctx.ProcedureTypes.AsNoTracking()
            .Where(t => t.Code == TramiteTipologiaCatalog.CodigoTraspasoStandard)
            .Select(t => t.Id)
            .SingleAsync();
        var buyer = await ctx.ProcedureEntities.AsNoTracking()
            .Where(e => e.Code == "BUYER").Select(e => e.Id).SingleAsync();
        var owner = await ctx.ProcedureEntities.AsNoTracking()
            .Where(e => e.Code == "OWNER").Select(e => e.Id).SingleAsync();

        void Instancia(Guid id, Guid tenant, Guid user, string status, bool finalizado = false, bool subsanacion = false) =>
            ctx.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = id,
                TenantId = tenant,
                ProcedureTypeId = typeId,
                CreatedByUserId = user,
                Status = status,
                SubsanacionActiva = subsanacion,
                DraftFinalizedAt = finalizado ? DateTimeOffset.UtcNow : null,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        void Actor(Guid instance, Guid tenant, string rol, string tipo, string doc, string metadata = "{}") =>
            ctx.ProcedureInstanceActors.Add(new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureInstanceId = instance,
                ProcedureEntityId = rol == "comprador" ? buyer : owner,
                ActorType = rol,
                DocumentType = tipo,
                DocumentNumber = doc,
                PersonType = tipo == "NIT" ? "juridical" : "natural",
                FullName = $"{rol} B13194",
                Metadata = metadata,
                Ordinal = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var rlMetadata = JsonSerializer.Serialize(new
        {
            representanteLegal = new { tipoDocumento = "CC", numeroDocumento = Persona, nombreCompleto = "RL B13194" },
        });

        Instancia(PjAsignado, TenantA, userA, TramiteEstado.Asignado);
        Instancia(PnBorradorFinalizadoVendedor, TenantA, userA, TramiteEstado.Borrador, finalizado: true);
        Instancia(PnBorradorSinFinalizar, TenantA, userA, TramiteEstado.Borrador);
        Instancia(PnEntregado, TenantA, userA, TramiteEstado.Entregado);
        Instancia(OtraPersonaPreparado, TenantA, userA, TramiteEstado.Preparado);
        Instancia(OtroTenantAsignado, TenantB, userB, TramiteEstado.Asignado);
        Instancia(PnSubsanacion, TenantA, userA, TramiteEstado.Rechazado, subsanacion: true);
        await ctx.SaveChangesAsync();

        Actor(PjAsignado, TenantA, "comprador", "NIT", "900131940", rlMetadata);
        Actor(PnBorradorFinalizadoVendedor, TenantA, "vendedor", "CC", Persona);
        Actor(PnBorradorSinFinalizar, TenantA, "comprador", "CC", Persona);
        Actor(PnEntregado, TenantA, "comprador", "CC", Persona);
        Actor(OtraPersonaPreparado, TenantA, "comprador", "CC", OtraPersona);
        Actor(OtroTenantAsignado, TenantB, "comprador", "CC", Persona);
        Actor(PnSubsanacion, TenantA, "comprador", "CC", Persona);
        await ctx.SaveChangesAsync();
    }

    private static async Task<Guid> SeedUserAsync(Flit.Infrastructure.Persistence.FlitDbContext ctx, Guid tenant, string email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "B13194",
            Status = "active",
            HomeTenantId = tenant,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }
}
