using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Application.Companies.TransitOffices.PortalDominio;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.TransitOffices.PortalDominio;
using Flit.Infrastructure.Domains;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Domains;

/// <summary>
/// Feature #14380 (Épica #14297) — DDL 183 y repositorio del dominio del portal contra PostgreSQL real: unicidad del host entre las
/// dos tablas de dominios, un dominio vigente por organismo, ciclo pending → verified → active, falla de certificado y resolución
/// del host. Marca blanca no cambia (<see cref="TenantDomainLifecycleTests"/> y compañía siguen pasando).
/// </summary>
public sealed class PortalDominioPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid OrganismoA = Guid.Parse("07000000-0000-4000-8000-00000014381a");
    private static readonly Guid OrganismoB = Guid.Parse("07000000-0000-4000-8000-00000014381b");
    private static readonly Guid CabezaMarcaBlanca = TenantSeed.ParentId;
    private static Guid? Actor => null; // el autor real es un usuario; sin usuarios sembrados la auditoria exige NULL
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task SembrarOrganismosAsync()
    {
        await using var db = NewContext();
        foreach (var (id, codigo) in new[] { (OrganismoA, "PDA14381"), (OrganismoB, "PDB14381") })
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, city_name, department_name)
                VALUES ({id}, {codigo}, {"Organismo " + codigo}, '99', '999', 'Municipio de prueba', 'Departamento de prueba')
                """, Ct);
    }

    private async Task<DominioDePortal> RegistrarAsync(Guid organismo, string host, string token)
    {
        await using var db = NewContext();
        var r = await new PortalDominioRepository(db).RegistrarAsync(organismo, host, token, Actor, Ct);
        r.Fallo.Should().Be(PortalDominioRegistroFallo.Ninguno);
        return r.Dominio!;
    }

    private async Task<DominioDePortal> ActivarAsync(Guid organismo, string host)
    {
        await using var db = NewContext();
        var repo = new PortalDominioRepository(db);
        await repo.AplicarComprobacionAsync(organismo, "verified", null, Ahora, null, 0, null, Ahora, Actor, null, Ct);
        var handler = new PortalDominioCertificadoHandler(repo, new DomainVerificationOptions(), NullLogger<PortalDominioCertificadoHandler>.Instance);
        (await handler.EmitidoAsync(host, Ahora, Ahora.AddDays(90), Ct)).Should().Be(PortalDominioCertificadoResultado.Actualizado);
        return (await repo.ObtenerAsync(organismo, Ct))!;
    }

    [PostgresFact]
    public async Task AC1_RegistrarDejaElDominioPendienteConTokenYSeProgramaSuComprobacion()
    {
        await SembrarOrganismosAsync();

        var d = await RegistrarAsync(OrganismoA, "tramites.a.example.com", "tok-pd-0000000000000001");

        d.Status.Should().Be("pending");
        d.VerificationToken.Should().Be("tok-pd-0000000000000001");
        d.NextCheckAt.Should().NotBeNull("el trabajo programado solo toma filas con next_check_at");
        (await RegistrarAsync(OrganismoA, "tramites.a.example.com", "tok-pd-0000000000000009")).VerificationToken.Should().Be("tok-pd-0000000000000001", "mismo host: idempotente");
    }

    [PostgresFact]
    public async Task AC2_UnHostVigenteDeOtroOrganismoSeRechazaYUnoRetiradoSePuedeReusar()
    {
        await SembrarOrganismosAsync();
        await RegistrarAsync(OrganismoA, "tramites.a.example.com", "tok-pd-0000000000000002");

        await using (var db = NewContext())
            (await new PortalDominioRepository(db).RegistrarAsync(OrganismoB, "tramites.a.example.com", "tok-pd-0000000000000003", Actor, Ct))
                .Fallo.Should().Be(PortalDominioRegistroFallo.HostEnUso);

        await using (var db = NewContext())
            (await new PortalDominioRepository(db).RetirarAsync(OrganismoA, Actor, Ct))!.Host.Should().Be("tramites.a.example.com");
        await RegistrarAsync(OrganismoB, "tramites.a.example.com", "tok-pd-0000000000000004");
    }

    [PostgresFact]
    public async Task AC3_UnOrganismoConDominioVigenteNoRegistraOtroPeroSiTrasRetirar()
    {
        await SembrarOrganismosAsync();
        await RegistrarAsync(OrganismoA, "uno.a.example.com", "tok-pd-0000000000000005");

        await using (var db = NewContext())
            (await new PortalDominioRepository(db).RegistrarAsync(OrganismoA, "dos.a.example.com", "tok-pd-0000000000000006", Actor, Ct))
                .Fallo.Should().Be(PortalDominioRegistroFallo.OrganismoYaTiene);

        await using (var db = NewContext())
            await new PortalDominioRepository(db).RetirarAsync(OrganismoA, Actor, Ct);
        (await RegistrarAsync(OrganismoA, "dos.a.example.com", "tok-pd-0000000000000007")).Host.Should().Be("dos.a.example.com");
    }

    [PostgresFact]
    public async Task AC2_ElHostEsUnicoEntreMarcaBlancaYPortalEnLosDosSentidos()
    {
        await SembrarOrganismosAsync();
        await using (var db = NewContext())
        {
            db.Tenants.Add(TenantSeed.New(CabezaMarcaBlanca, "IT-MB-PD", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca));
            await db.SaveChangesAsync(Ct);
            db.TenantDomains.Add(new TenantDomainEntity { TenantId = CabezaMarcaBlanca, Host = "marca.example.com", VerificationToken = "tok-mb-0000000000000001" });
            await db.SaveChangesAsync(Ct);
        }

        // Portal intenta un host de marca blanca.
        await using (var db = NewContext())
            (await new PortalDominioRepository(db).RegistrarAsync(OrganismoA, "marca.example.com", "tok-pd-0000000000000008", Actor, Ct))
                .Fallo.Should().Be(PortalDominioRegistroFallo.HostEnUso);

        // Marca blanca intenta un host de portal: el disparador lo rechaza y el repositorio de marca blanca lo traduce igual que un host repetido.
        await RegistrarAsync(OrganismoA, "portal.example.com", "tok-pd-0000000000000009");
        await using var mb = NewContext();
        var repoMb = new TenantDomainRepository(mb);
        var act = () => repoMb.RegisterOrReplaceAsync(CabezaMarcaBlanca, "portal.example.com", "tok-mb-0000000000000002", Actor, TenantDomainPurposes.Hub, Ct);
        await act.Should().ThrowAsync<DomainHostAlreadyRegisteredException>();
    }

    [PostgresFact]
    public async Task AC2_ElCiclo_PendienteVerificadoYActivoConCertificado_ResuelveElHostAlPortal()
    {
        await SembrarOrganismosAsync();
        await RegistrarAsync(OrganismoA, "tramites.a.example.com", "tok-pd-0000000000000010");
        await using (var db = NewContext())
            (await new PortalDominioRepository(db).ObtenerOrganismoActivoPorHostAsync("tramites.a.example.com", Ct)).Should().BeNull("pendiente no abre el portal");

        var activo = await ActivarAsync(OrganismoA, "tramites.a.example.com");

        activo.Status.Should().Be("active");
        activo.ActivatedAt.Should().NotBeNull();
        activo.CertificateIssuedAt.Should().NotBeNull();
        await using var lectura = NewContext();
        var repo = new PortalDominioRepository(lectura);
        (await repo.ObtenerOrganismoActivoPorHostAsync("tramites.a.example.com", Ct)).Should().Be(new PortalDominioOrganismo(OrganismoA, "PDA14381"));
        (await repo.ObtenerHostActivoPorCodigoAsync("pda14381", Ct)).Should().Be("tramites.a.example.com");
        (await repo.ListarHostsActivosAsync(Ct)).Should().Contain("tramites.a.example.com");
        (await repo.ListarAprovisionamientoAsync(Ct)).Should().ContainSingle(p => p.Host == "tramites.a.example.com" && p.Status == "active");
        (await repo.ListarHostsPendientesDeCertificadoAsync(Ahora.AddDays(30), Ct)).Should().NotContain("tramites.a.example.com", "vence en 90 días");
        (await repo.ListarHostsPendientesDeCertificadoAsync(Ahora.AddDays(100), Ct)).Should().Contain("tramites.a.example.com", "renovación");
    }

    [PostgresFact]
    public async Task AC4_UnDominioFallidoORetiradoOdeUnOrganismoInactivoNoAbreElPortal()
    {
        await SembrarOrganismosAsync();
        await RegistrarAsync(OrganismoA, "tramites.a.example.com", "tok-pd-0000000000000011");
        await ActivarAsync(OrganismoA, "tramites.a.example.com");

        await using (var db = NewContext())
            await new PortalDominioRepository(db).AplicarComprobacionAsync(OrganismoA, "failed", "TXT_NOT_FOUND", null, null, 1, Ahora.AddMinutes(5), Ahora, null, "job", Ct);
        await using (var db = NewContext())
            (await new PortalDominioRepository(db).ObtenerOrganismoActivoPorHostAsync("tramites.a.example.com", Ct)).Should().BeNull();

        await ActivarAsync(OrganismoA, "tramites.a.example.com");
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE catalogs.transit_offices SET is_active = false WHERE id = {OrganismoA}", Ct);
            (await new PortalDominioRepository(db).ObtenerOrganismoActivoPorHostAsync("tramites.a.example.com", Ct)).Should().BeNull("organismo inactivo");
        }
    }

    [PostgresFact]
    public async Task AC2_B4_UnCertificadoQueNoSePudoEmitirDejaElMotivoYNoActiva()
    {
        await SembrarOrganismosAsync();
        await RegistrarAsync(OrganismoA, "tramites.a.example.com", "tok-pd-0000000000000012");
        await using var db = NewContext();
        var repo = new PortalDominioRepository(db);
        await repo.AplicarComprobacionAsync(OrganismoA, "verified", null, Ahora, null, 0, null, Ahora, Actor, null, Ct);
        var handler = new PortalDominioCertificadoHandler(repo, new DomainVerificationOptions(), NullLogger<PortalDominioCertificadoHandler>.Instance);

        await handler.FalloAsync("tramites.a.example.com", "puerto 80 cerrado", Ct);

        var d = (await repo.ObtenerAsync(OrganismoA, Ct))!;
        d.Status.Should().Be("failed");
        d.StatusReason.Should().Be("CERT_NOT_ISSUED: puerto 80 cerrado");
        d.NextCheckAt.Should().NotBeNull("se reintenta con retroceso");
        (await repo.ObtenerOrganismoActivoPorHostAsync("tramites.a.example.com", Ct)).Should().BeNull();
    }

    [PostgresFact]
    public async Task ElTrabajoReclamaSoloLosVencidosYElActivoSeRevalidaMasTarde()
    {
        await SembrarOrganismosAsync();
        await RegistrarAsync(OrganismoA, "tramites.a.example.com", "tok-pd-0000000000000013");
        await using var db = NewContext();
        var repo = new PortalDominioRepository(db);

        var primero = await repo.ReclamarVencidosAsync(10, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow.AddSeconds(5), Ct);
        var segundo = await repo.ReclamarVencidosAsync(10, TimeSpan.FromMinutes(2), DateTimeOffset.UtcNow.AddSeconds(5), Ct);

        primero.Should().ContainSingle(c => c.Host == "tramites.a.example.com");
        segundo.Should().BeEmpty("el arriendo lo saca del lote");
    }

    [PostgresFact]
    public async Task AC1_B3_ElResolutorDeHostEntregaPortalSinRedYSeInvalidaAlActivarYAlRetirar()
    {
        await SembrarOrganismosAsync();
        await RegistrarAsync(OrganismoA, "tramites.a.example.com", "tok-pd-0000000000000014");
        var cache = new MemoryCache(new MemoryCacheOptions());

        async Task<NetworkResolution> Resolver()
        {
            await using var db = NewContext();
            var resolver = new CachedTenantDomainResolver(
                new TenantDomainRepository(db), cache, NullLogger<CachedTenantDomainResolver>.Instance, new PortalDominioRepository(db, cache));
            return await resolver.ResolveAsync("tramites.a.example.com", Ct);
        }

        (await Resolver()).Should().Be(NetworkResolution.None, "pendiente (queda cacheado)");
        await using (var db = NewContext())
        {
            var repo = new PortalDominioRepository(db, cache);
            await repo.AplicarComprobacionAsync(OrganismoA, "verified", null, Ahora, null, 0, null, Ahora, Actor, null, Ct);
            await new PortalDominioCertificadoHandler(repo, new DomainVerificationOptions(), NullLogger<PortalDominioCertificadoHandler>.Instance)
                .EmitidoAsync("tramites.a.example.com", Ahora, null, Ct);
        }

        var activo = await Resolver();
        activo.IsNetwork.Should().BeFalse("un portal no es una red");
        activo.HeadTenantId.Should().BeNull();
        activo.TransitOfficeId.Should().Be(OrganismoA);

        await using (var db = NewContext())
            await new PortalDominioRepository(db, cache).RetirarAsync(OrganismoA, Actor, Ct);
        (await Resolver()).Should().Be(NetworkResolution.None, "retirado se ve de inmediato, sin esperar los 60 s");
    }

    [PostgresFact]
    public async Task DDL_UnActivoSinCertificadoOUnHostMalFormadoSeRechazanPorLosChecks()
    {
        await SembrarOrganismosAsync();
        await using var db = NewContext();

        var activoSinCertificado = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.transit_office_domains (transit_office_id, host, verification_token, status, verified_at, activated_at)
            VALUES ({OrganismoA}, 'a.example.com', 'tok-pd-0000000000000015', 'active', now(), now())
            """, Ct);
        (await activoSinCertificado.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
            .Should().Be("ck_transit_office_domains_active_requires_verified");

        var hostMalo = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin.transit_office_domains (transit_office_id, host, verification_token)
            VALUES ({OrganismoB}, 'Con Mayusculas', 'tok-pd-0000000000000016')
            """, Ct);
        (await hostMalo.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().StartWith("ck_transit_office_domains_host");
    }
}
