using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13304 (numeral 12) — ICT admite nombre de hasta 320 y teléfono de hasta 50, pero
/// <c>tramites.procedure_instance_actors</c> tenía <c>full_name varchar(200)</c> y <c>phone varchar(20)</c>:
/// el actor reventaba con 22001 al materializar el borrador. DDL 130 amplía las dos columnas y también
/// <c>procedure_instances.vendedor_nombre/comprador_nombre</c> (las llena el trigger
/// <c>tr_procedure_instance_actors_denorm</c>; si no se ampliaran, el 22001 se movería al trigger) y
/// recrea <c>analytics.v_procedure_detail_report</c>, que lee <c>full_name</c>.
/// Contra Postgres REAL porque lo que se prueba es la longitud de columna y el trigger.
/// </summary>
public sealed class Bug13304ActorLongitudIctTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid UserId = new("b1330412-0000-4000-8000-0000000000aa");
    private static readonly Guid InstanceId = new("b1330412-0000-4000-8000-000000000001");

    private static readonly string NombreVendedor320 = ("VENDEDOR " + new string('V', 320))[..320];
    private static readonly string NombreComprador320 = ("COMPRADOR " + new string('C', 320))[..320];
    private static readonly string Telefono50 = ("+57 300 000 0000 ext " + new string('9', 50))[..50];

    [PostgresFact]
    public async Task ActoresConNombre320YTelefono50_SeGuardanCompletosYSeDenormalizanAlTramite()
    {
        await SeedInstanceAsync();

        await using (var ctx = NewContext())
        {
            var buyer = await EntityIdAsync(ctx, "BUYER");
            var owner = await EntityIdAsync(ctx, "OWNER");
            ctx.ProcedureInstanceActors.Add(Actor("vendedor", owner, "1013304121", NombreVendedor320));
            ctx.ProcedureInstanceActors.Add(Actor("comprador", buyer, "1013304122", NombreComprador320));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = NewContext();
        var actores = await read.ProcedureInstanceActors.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ProcedureInstanceId == InstanceId)
            .ToListAsync(TestContext.Current.CancellationToken);
        actores.Single(a => a.ActorType == "vendedor").FullName.Should().Be(NombreVendedor320);
        actores.Single(a => a.ActorType == "comprador").FullName.Should().Be(NombreComprador320);
        actores.Should().OnlyContain(a => a.Phone == Telefono50);

        var instancia = await read.ProcedureInstances.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.Id == InstanceId, TestContext.Current.CancellationToken);
        instancia.VendedorNombre.Should().Be(NombreVendedor320, "el trigger denormaliza el nombre completo");
        instancia.CompradorNombre.Should().Be(NombreComprador320);

        // La vista BI se recreó en la misma migración y sigue leyendo full_name completo.
        var personaVista = await read.Database
            .SqlQuery<string>($"SELECT person_full_name AS \"Value\" FROM analytics.v_procedure_detail_report WHERE id = {InstanceId}")
            .SingleAsync(TestContext.Current.CancellationToken);
        personaVista.Should().Be(NombreComprador320);
    }

    [PostgresFact]
    public async Task ValidacionBiometricaConNombre320_SePersisteCompleta()
    {
        await SeedInstanceAsync();

        await using (var ctx = NewContext())
        {
            ctx.ProcedureInstanceBiometricValidations.Add(new ProcedureInstanceBiometricValidation
            {
                Id = Guid.NewGuid(),
                TenantId = TenantSeed.LoneId,
                ProcedureInstanceId = InstanceId,
                PartyRole = "comprador",
                DocumentType = "CC",
                DocumentNumber = "1013304122",
                Name = NombreComprador320,
                Email = "comprador.b13304n12@flit.test",
                Status = BiometricEstados.Enviado,
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = NewContext();
        var nombre = await read.ProcedureInstanceBiometricValidations.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.ProcedureInstanceId == InstanceId)
            .Select(v => v.Name)
            .SingleAsync(TestContext.Current.CancellationToken);
        nombre.Should().Be(NombreComprador320, "la validación se guarda tras crearla en Kyverum; truncarla o fallar la deja huérfana");
    }

    private static ProcedureInstanceActor Actor(string rol, Guid entityId, string documento, string nombre) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantSeed.LoneId,
        ProcedureInstanceId = InstanceId,
        ProcedureEntityId = entityId,
        ActorType = rol,
        DocumentType = "CC",
        DocumentNumber = documento,
        FullName = nombre,
        Phone = Telefono50,
        Ordinal = 1,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Task<Guid> EntityIdAsync(Flit.Infrastructure.Persistence.FlitDbContext ctx, string code) =>
        ctx.ProcedureEntities.AsNoTracking().Where(e => e.Code == code).Select(e => e.Id).SingleAsync();

    private async Task SeedInstanceAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User
        {
            Id = UserId,
            Email = "it-b13304-n12@flit.test",
            DisplayName = "ICT B13304 N12",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var typeId = await ctx.ProcedureTypes.AsNoTracking()
            .Where(t => t.Code == TramiteTipologiaCatalog.CodigoTraspasoStandard)
            .Select(t => t.Id)
            .SingleAsync();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = InstanceId,
            TenantId = TenantSeed.LoneId,
            ProcedureTypeId = typeId,
            CreatedByUserId = UserId,
            Status = TramiteEstado.Borrador,
            Origin = "ict",
            ExternalRef = "ict-b13304-n12",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }
}
