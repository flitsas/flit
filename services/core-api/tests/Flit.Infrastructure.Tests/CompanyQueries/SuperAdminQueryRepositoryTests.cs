using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.CompanyQueries;

/// <summary>
/// El motor de SuperAdmin: la misma consulta de la empresa gestora, pero sobre TODAS las compañías
/// activas a la vez — ver <see cref="SuperAdminTenantScope"/> y
/// <see cref="ICompanyQueryRepository.ExecuteForSuperAdminAsync"/>.
///
/// <para>Lo que más importa probar aquí NO es el motor de filtros —eso ya lo cubre
/// <see cref="CompanyQueryRepositoryTests"/>, que este código reutiliza entero— sino QUÉ conjunto de
/// compañías entra en «todas»: activas sí, inactivas no, organismos de tránsito jamás (comparten la
/// tabla de tenants pero no son compañías clientes).</para>
/// </summary>
public sealed class SuperAdminQueryRepositoryTests
{
    private static readonly Guid Tesla = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Renting = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Inactiva = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid OrganismoTenant = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid TransitOfficeId = Guid.Parse("f0000000-0000-4000-8000-000000000001");
    private static readonly Guid TipoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Gustavo = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact] // El caso de uso completo: operaciones necesita el mismo reporte para varias compañías sin
           // repetirlo una por una.
    public async Task SinFiltroDeCompania_TraeLasFilasDeTodasLasCompaniasActivas()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-TESLA", Tesla, "ABC123");
            Tramite(seed, "REF-RENTING", Renting, "DEF456");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir());

        result.Total.Should().Be(2);
        result.Filas.Select(f => f.CompaniaId).Should().BeEquivalentTo([Tesla, Renting]);
    }

    [Fact] // «Compañía» es un filtro más, del mismo mecanismo que estado u organismo — no un modo
           // aparte: así una, varias o ninguna salen del mismo control.
    public async Task FiltroDeCompania_AcotaAUnaOVariasSinTocarElRestoDelMotor()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-TESLA", Tesla, "ABC123");
            Tramite(seed, "REF-RENTING", Renting, "DEF456");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(
            db, Definir(new QueryCondition(
                CompanyQueryFieldCatalog.Compania, QueryOperator.EsAlguno, [Tesla.ToString()])));

        result.Total.Should().Be(1);
        result.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-TESLA");
    }

    [Fact] // Una compañía inactiva (dada de baja) no debe seguir apareciendo en «todas»: el usuario
           // ya no debería poder ni consultarla ni ofrecerla como opción del filtro.
    public async Task CompaniaInactiva_NoEntraEnTodas()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-ACTIVA", Tesla, "ABC123");
            Tramite(seed, "REF-INACTIVA", Inactiva, "ZZZ999");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir());

        result.Total.Should().Be(1);
        result.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-ACTIVA");
    }

    [Fact] // Un organismo de tránsito es también una fila de identity.tenants, pero no es una
           // compañía cliente: si se colara en «todas», el aislamiento perdería sentido — el filtro
           // de compañía tiene que distinguir quién tramita de quién recibe lo tramitado.
    public async Task TenantDeOrganismo_NoEntraEnTodas()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            seed.Tenants.Add(new Tenant
            {
                Id = OrganismoTenant, Code = "OT-BOG", LegalName = "Secretaría de Movilidad",
                TaxId = "900000004", TenantType = "FLIT", IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            seed.TransitOfficeProfiles.Add(new TransitOfficeProfile
            {
                Id = Guid.NewGuid(),
                TenantId = OrganismoTenant,
                TransitOfficeId = TransitOfficeId,
            });
            Tramite(seed, "REF-TESLA", Tesla, "ABC123");
            Tramite(seed, "REF-ORGANISMO", OrganismoTenant, "ZZZ999");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await RunAsync(db, Definir());

        result.Total.Should().Be(1);
        result.Filas.Should().ContainSingle().Which.ReferenceNumber.Should().Be("REF-TESLA");
    }

    [Fact] // Sin acotar por compañía ni por fecha, pero con pocos trámites en toda la plataforma, la
           // consulta corre igual: el aviso no es una regla fija, es un conteo real.
    public async Task SinAcotarPorCompaniaNiPorFecha_YConPocosTramites_NoSeRechaza()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-TESLA", Tesla, "ABC123");
            Tramite(seed, "REF-RENTING", Renting, "DEF456");
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var definicionAmplia = new QueryDefinition(
            new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.AnioActual), [], []);

        var result = await RunAsync(db, definicionAmplia);

        result.Total.Should().Be(2);
    }

    [Fact] // Sin acotar por compañía ni por fecha, y con más trámites que el tope de cordura en toda
           // la plataforma, la consulta se rechaza con el conteo real — no con una regla de días.
    public async Task SinAcotarPorCompaniaNiPorFecha_YSuperaElTope_SeRechazaConElConteoReal()
    {
        var db = NewDbName();
        var total = QueryLimits.MaxUniverso + 1;

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            for (var i = 0; i < total; i++)
            {
                Tramite(seed, $"REF-{i:D6}", i % 2 == 0 ? Tesla : Renting, $"PLA{i:D3}");
            }

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var definicionAmplia = new QueryDefinition(
            new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.AnioActual), [], []);

        var act = () => RunAsync(db, definicionAmplia);

        var excepcion = await act.Should().ThrowAsync<SuperAdminQueryTooBroadException>();
        excepcion.Which.Total.Should().Be(total);
        excepcion.Which.Max.Should().Be(QueryLimits.MaxUniverso);
    }

    [Fact] // Acotar por compañía reduce el universo que se cuenta a esa compañía nomás — por eso
           // levanta la exigencia aunque el rango de fecha siga siendo amplio y la plataforma entera
           // supere el tope: lo que importa es lo que de verdad se va a cargar.
    public async Task AcotarPorCompania_CuentaSoloEsaCompania()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            Tramite(seed, "REF-TESLA", Tesla, "ABC123");
            for (var i = 0; i < QueryLimits.MaxUniverso + 1; i++)
            {
                Tramite(seed, $"REF-RENTING-{i:D6}", Renting, $"PLB{i:D3}");
            }

            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var definicionAmplia = new QueryDefinition(
            new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.AnioActual),
            [new QueryCondition(CompanyQueryFieldCatalog.Compania, QueryOperator.EsAlguno, [Tesla.ToString()])],
            []);

        var result = await RunAsync(db, definicionAmplia);

        result.Total.Should().Be(1);
    }

    [Fact] // «Compañía» no le dice nada a una empresa normal: todas sus filas son de sí misma. Se
           // quita del catálogo que ve, no solo se le deja sin opciones.
    public async Task CatalogoNormal_NoIncluyeElCampoCompania()
    {
        var db = NewDbName();
        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);

        var campos = await repo.GetFieldsAsync(Tesla, TestContext.Current.CancellationToken);

        campos.Should().NotContain(f => f.Id == CompanyQueryFieldCatalog.Compania);
    }

    [Fact] // El catálogo de SuperAdmin sí lo trae, con las compañías activas como opciones — así el
           // «+ Filtro» de siempre alcanza para elegir una, varias o ninguna.
    public async Task CatalogoDeSuperAdmin_IncluyeCompaniaConLasActivas()
    {
        var db = NewDbName();

        await using (var seed = NewContext(db))
        {
            SeedCatalogos(seed);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);

        var campos = await repo.GetFieldsForSuperAdminAsync(TestContext.Current.CancellationToken);

        var compania = campos.Should().ContainSingle(f => f.Id == CompanyQueryFieldCatalog.Compania).Subject;
        compania.Options.Select(o => o.Value).Should().BeEquivalentTo([Tesla.ToString(), Renting.ToString()]);
    }

    // ── Infraestructura de prueba ─────────────────────────────────────────────────────────────

    private static QueryDefinition Definir(params QueryCondition[] condiciones) =>
        new(new QueryDateFilter(CompanyQueryDateField.Creacion, QueryRangePreset.Ultimos30), condiciones, []);

    private static async Task<CompanyQueryResultDto> RunAsync(
        string db, QueryDefinition definition, int page = 1, int pageSize = 50)
    {
        await using var ctx = NewContext(db);
        var repo = new CompanyQueryRepository(ctx);

        return await repo.ExecuteForSuperAdminAsync(
            new QueryRequest(definition, page, pageSize), TestContext.Current.CancellationToken);
    }

    private static void Tramite(FlitDbContext ctx, string reference, Guid tenantId, string placa) =>
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = TipoId,
            ReferenceNumber = reference,
            Status = "entregado",
            ModalidadEntrada = "matricula_inicial",
            Plate = placa,
            TransitOfficeId = null,
            CreatedByUserId = Gustavo,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        });

    private static void SeedCatalogos(FlitDbContext ctx)
    {
        ctx.Tenants.Add(new Tenant
        {
            Id = Tesla, Code = "TESLA", LegalName = "Tesla Colombia", TaxId = "900000001",
            TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Tenants.Add(new Tenant
        {
            Id = Renting, Code = "RENTING", LegalName = "Renting SAS", TaxId = "900000002",
            TenantType = "RENTING", IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.Tenants.Add(new Tenant
        {
            Id = Inactiva, Code = "INACTIVA", LegalName = "Compañía dada de baja", TaxId = "900000003",
            TenantType = "RENTING", IsActive = false, CreatedAt = DateTimeOffset.UtcNow,
        });

        ctx.ProcedureTypes.Add(new ProcedureType
        {
            Id = TipoId, Code = "TRASPASO", Name = "Traspaso de vehículo", Family = "TRASPASO",
        });

        ctx.Users.Add(new User
        {
            Id = Gustavo, Email = "gustavo@gestora.local", DisplayName = "Gustavo Gestor",
        });
    }

    private static string NewDbName() => Guid.NewGuid().ToString();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);
}
