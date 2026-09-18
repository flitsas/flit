using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #12706 — lecturas transversales de Validación de Identidad acotadas por <see cref="TenantScope"/>,
/// contra PostgreSQL real. La grilla por persona es SQL crudo (<c>DISTINCT ON</c> + <c>= ANY(uuid[])</c>)
/// que InMemory no ejecuta, así que esta suite es la que demuestra:
/// <list type="bullet">
///   <item>AC1 — <c>TenantScope.All</c> (SuperAdmin sin acotar) devuelve las personas de todas las compañías
///   con la compañía de cada fila, y los KPIs suman todas.</item>
///   <item>AC2 — <c>TenantScope.Single</c> devuelve exactamente lo de esa compañía.</item>
///   <item>AC3 — la misma cédula en dos compañías son dos personas, cada una con su propio historial.</item>
///   <item>Cerrado por defecto — un conjunto de lectura sin la compañía no devuelve sus filas.</item>
/// </list>
/// </summary>
public sealed class IdentityValidationScopeTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid CompaniaA = TenantSeed.LoneId;
    private static readonly Guid CompaniaB = new("55555555-5555-4555-8555-555555555555");
    private static readonly Guid CompaniaC = new("66666666-6666-4666-8666-666666666666");

    private const string CedulaCompartida = "1020445118";

    private static readonly DateTimeOffset Base = DateTimeOffset.UtcNow.AddDays(-2);

    [PostgresFact]
    public async Task Todas_las_companias_devuelve_una_persona_por_compania_y_documento()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var (rows, total) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            TenantScope.All(), 0, 50, null, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        total.Should().Be(4, "A tiene 2 personas, B 1 y C 1");
        rows.Select(r => r.TenantId).Distinct().Should().BeEquivalentTo([CompaniaA, CompaniaB, CompaniaC]);

        // AC3 — la cédula compartida aparece dos veces, una por compañía, cada una con SU historial.
        var compartida = rows.Where(r => r.DocumentNumberNorm == CedulaCompartida).ToList();
        compartida.Should().HaveCount(2);
        compartida.Single(r => r.TenantId == CompaniaA).ValidationCount.Should().Be(2);
        compartida.Single(r => r.TenantId == CompaniaA).Status.Should().Be(BiometricEstados.Aprobado);
        compartida.Single(r => r.TenantId == CompaniaB).ValidationCount.Should().Be(1);
        compartida.Single(r => r.TenantId == CompaniaB).Status.Should().Be(BiometricEstados.Rechazado);
    }

    [PostgresFact]
    public async Task Los_KPIs_de_todas_las_companias_suman_las_personas_de_cada_una()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantScope.All(), null, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        counts.Values.Sum().Should().Be(4);
        counts.GetValueOrDefault(BiometricEstados.Aprobado).Should().Be(2, "la compartida en A y la de C");
        counts.GetValueOrDefault(BiometricEstados.Rechazado).Should().Be(1, "la compartida en B");
    }

    [PostgresFact]
    public async Task Una_compania_devuelve_solo_sus_personas()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var ct = TestContext.Current.CancellationToken;
        var (rows, total) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            TenantScope.Single(CompaniaB), 0, 50, null, DateTimeOffset.UtcNow, ct);
        var counts = await repo.CountBiometricPersonsByEstadoAsync(
            TenantScope.Single(CompaniaB), null, DateTimeOffset.UtcNow, ct);

        total.Should().Be(1);
        rows.Should().ContainSingle().Which.TenantId.Should().Be(CompaniaB);
        counts.Values.Sum().Should().Be(1);
    }

    [PostgresFact]
    public async Task La_firma_por_Guid_equivale_a_una_sola_compania()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var ct = TestContext.Current.CancellationToken;
        var (porGuid, totalGuid) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            CompaniaA, 0, 50, null, DateTimeOffset.UtcNow, ct);
        var (porScope, totalScope) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            TenantScope.Single(CompaniaA), 0, 50, null, DateTimeOffset.UtcNow, ct);

        totalGuid.Should().Be(2).And.Be(totalScope);
        porGuid.Select(r => r.LatestValidationId).Should().Equal(porScope.Select(r => r.LatestValidationId));
    }

    [PostgresFact]
    public async Task Los_filtros_siguen_aplicando_sobre_todas_las_companias()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var (rows, total) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            TenantScope.All(), 0, 50,
            new BiometricPersonGroupFilter { DocumentNumber = "0445" },
            DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        total.Should().Be(2, "la coincidencia parcial del documento encuentra la cédula compartida en A y en B");
        rows.Should().OnlyContain(r => r.DocumentNumberNorm == CedulaCompartida);
    }

    [PostgresFact]
    public async Task El_listado_plano_de_todas_las_companias_trae_filas_y_KPIs_de_todas()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var ct = TestContext.Current.CancellationToken;
        var rows = await repo.ListBiometricValidationsByTenantAsync(TenantScope.All(), 0, 50, null, DateTimeOffset.UtcNow, ct);
        var counts = await repo.CountBiometricValidationsByEstadoAsync(TenantScope.All(), null, DateTimeOffset.UtcNow, ct);
        var soloC = await repo.ListBiometricValidationsByTenantAsync(TenantScope.Single(CompaniaC), 0, 50, null, DateTimeOffset.UtcNow, ct);

        rows.Should().HaveCount(5, "son 5 validaciones en 3 compañías");
        rows.Select(r => r.TenantId).Distinct().Should().HaveCount(3);
        counts.Values.Sum().Should().Be(5);
        soloC.Should().ContainSingle().Which.TenantId.Should().Be(CompaniaC);
    }

    [PostgresFact]
    public async Task La_red_de_una_cabeza_lee_la_cabeza_y_su_hija_y_nunca_la_compania_ajena()
    {
        // HU #12708 — A es cabeza con la hija B; C es ajena. Mismo SQL crudo que la vista propia, con el
        // conjunto de lectura del grupo en el = ANY(uuid[]).
        await SeedAsync();

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var ct = TestContext.Current.CancellationToken;
        var red = TenantScope.Group(CompaniaA, [CompaniaB], GroupKind.MarcaBlanca);
        var (rows, total) = await repo.ListBiometricValidationsGroupedByPersonAsync(red, 0, 50, null, DateTimeOffset.UtcNow, ct);
        var counts = await repo.CountBiometricPersonsByEstadoAsync(red, null, DateTimeOffset.UtcNow, ct);

        total.Should().Be(3, "2 personas de la cabeza A y 1 de la hija B");
        rows.Select(r => r.TenantId).Should().NotContain(CompaniaC);
        counts.Values.Sum().Should().Be(3);
    }

    [PostgresFact]
    public async Task Las_atascadas_de_todas_las_companias_traen_su_compania_y_una_compania_solo_las_suyas()
    {
        // AC4 — la cola de envío atascada (error_envio) de A y de C; B no tiene.
        await SeedAsync();
        await using (var seed = NewContext())
        {
            seed.ProcedureInstanceBiometricValidations.AddRange(
                Validacion(CompaniaA, "79000111", BiometricEstados.ErrorEnvio, Base.AddHours(5)),
                Validacion(CompaniaC, "52000222", BiometricEstados.ErrorEnvio, Base.AddHours(6)));
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext();
        var outbox = new IdentityValidationOutboxRepository(ctx);
        var ct = TestContext.Current.CancellationToken;
        var todas = await outbox.ListStuckAsync(TenantScope.All(), 200, ct);
        var soloA = await outbox.ListStuckAsync(TenantScope.Single(CompaniaA), 200, ct);

        todas.Select(r => r.TenantId).Should().BeEquivalentTo([CompaniaA, CompaniaC]);
        soloA.Should().ContainSingle().Which.TenantId.Should().Be(CompaniaA);
    }

    /// <summary>
    /// A: cédula compartida (rechazada → aprobada) + otra persona en proceso. B: cédula compartida
    /// rechazada. C: una persona aprobada. Todas standalone (prevalidaciones, sin trámite).
    /// </summary>
    private async Task SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.AddRange(
                TenantSeed.Lone(CompaniaA, "IT-A"),
                TenantSeed.Lone(CompaniaB, "IT-B"),
                TenantSeed.Lone(CompaniaC, "IT-C"));
            await ctx.SaveChangesAsync();
        }

        // Una prevalidación standalone cuelga de su persona (ck_biometric_validation_anchor), y la
        // persona es de UNA compañía: la cédula compartida son dos personas, una en A y otra en B.
        await using (var ctx = NewContext())
        {
            foreach (var (tenant, documento) in Personas)
            {
                ctx.Persons.Add(new Person
                {
                    Id = PersonId(tenant, documento),
                    TenantId = tenant,
                    DocumentType = "CC",
                    DocumentNumber = documento,
                    FullName = $"Persona {documento}",
                    Email = "persona@correo.co",
                    CreatedAt = Base,
                });
            }

            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.ProcedureInstanceBiometricValidations.AddRange(
                Validacion(CompaniaA, CedulaCompartida, BiometricEstados.Rechazado, Base),
                Validacion(CompaniaA, CedulaCompartida, BiometricEstados.Aprobado, Base.AddHours(1)),
                Validacion(CompaniaA, "79000111", BiometricEstados.EnProceso, Base.AddHours(2)),
                Validacion(CompaniaB, CedulaCompartida, BiometricEstados.Rechazado, Base.AddHours(3)),
                Validacion(CompaniaC, "52000222", BiometricEstados.Aprobado, Base.AddHours(4)));
            await ctx.SaveChangesAsync();
        }
    }

    private static readonly (Guid Tenant, string Documento)[] Personas =
    [
        (CompaniaA, CedulaCompartida),
        (CompaniaA, "79000111"),
        (CompaniaB, CedulaCompartida),
        (CompaniaC, "52000222"),
    ];

    /// <summary>Id determinista de la persona (compañía + documento) para enlazar sus validaciones.</summary>
    private static Guid PersonId(Guid tenant, string documento) =>
        new($"77777777-7777-4777-8777-{Array.IndexOf(Personas, (tenant, documento)) + 1:D12}");

    private static ProcedureInstanceBiometricValidation Validacion(
        Guid tenantId, string documento, string status, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        PersonId = PersonId(tenantId, documento),
        DocumentType = "CC",
        DocumentNumber = documento,
        Name = $"Persona {documento}",
        Email = "persona@correo.co",
        Status = status,
        TokenHash = Guid.NewGuid().ToString("N"),
        CreatedAt = createdAt,
        // Enlace vigente: así el estado efectivo es el guardado y no «expirado».
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        ValidatedAt = status == BiometricEstados.Aprobado ? createdAt : null,
        ValidUntil = status == BiometricEstados.Aprobado ? createdAt.AddDays(BiometricRules.VigenciaDias) : null,
        Provider = BiometricProviders.Mock,
    };
}
