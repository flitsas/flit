using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Búsqueda por número de radicado en la grilla de Validaciones de identidad agrupada por persona, contra
/// PostgreSQL real. La grilla es SQL crudo (<c>GroupedByPersonCteSql</c>, parámetros posicionales) que
/// InMemory no ejecuta: esta prueba es la que demuestra que el filtro <c>ILIKE '%x%' ESCAPE</c> sobre
/// <c>procedure_instances.reference_number</c> va en el CTE <c>base</c> y que la página y los KPIs hablan
/// del mismo conjunto. Uso: <c>new BiometricPersonGroupFilter { ReferenceNumber = "0001234" }</c>.
/// </summary>
public sealed class BiometricPersonReferenceNumberFilterTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = HierarchyScenario.C1;
    private static readonly Guid TramiteA = new("88888888-8888-4888-8888-000000000001");
    private static readonly Guid TramiteB = new("88888888-8888-4888-8888-000000000002");

    [PostgresFact]
    public async Task Radicado_parcial_sin_distinguir_mayusculas_filtra_filas_y_KPIs_del_mismo_conjunto()
    {
        await SeedAsync();

        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var scope = TenantScope.Single(Tenant);

        var uno = new BiometricPersonGroupFilter { ReferenceNumber = "0001234" };
        var (rows, total) = await repo.ListBiometricValidationsGroupedByPersonAsync(scope, 0, 50, uno, now, ct);
        var counts = await repo.CountBiometricPersonsByEstadoAsync(scope, uno, now, ct);

        total.Should().Be(1);
        var fila = rows.Should().ContainSingle().Which;
        fila.DocumentNumberNorm.Should().Be("9001");
        fila.ReferenceNumber.Should().Be("FT1-0001234");
        counts.Values.Sum().Should().Be(1);

        // Minúsculas: encuentra los dos trámites; la prevalidación standalone (sin trámite) queda fuera.
        var ambos = new BiometricPersonGroupFilter { ReferenceNumber = "ft1-" };
        var (rowsAmbos, totalAmbos) = await repo.ListBiometricValidationsGroupedByPersonAsync(scope, 0, 50, ambos, now, ct);
        totalAmbos.Should().Be(2);
        rowsAmbos.Select(r => r.DocumentNumberNorm).Should().BeEquivalentTo(["9001", "9002"]);
        (await repo.CountBiometricPersonsByEstadoAsync(scope, ambos, now, ct)).Values.Sum().Should().Be(2);

        // Los comodines son texto literal.
        var (comodin, _) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            scope, 0, 50, new BiometricPersonGroupFilter { ReferenceNumber = "%" }, now, ct);
        comodin.Should().BeEmpty();

        // Combinado con otro filtro del CTE (documento) y con la paginación: los parámetros posicionales no se cruzan.
        var (combinado, totalCombinado) = await repo.ListBiometricValidationsGroupedByPersonAsync(
            scope, 0, 1, new BiometricPersonGroupFilter { ReferenceNumber = "FT1-", DocumentNumber = "9002" }, now, ct);
        totalCombinado.Should().Be(1);
        combinado.Should().ContainSingle().Which.DocumentNumberNorm.Should().Be("9002");
    }

    private async Task SeedAsync()
    {
        var tipo = await HierarchyScenario.SeedAsync(Fixture);

        await using (var ctx = NewContext())
        {
            ctx.ProcedureInstances.AddRange(
                Tramite(TramiteA, "FT1-0001234", tipo.Id),
                Tramite(TramiteB, "FT1-0005678", tipo.Id));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.Persons.Add(new Person
            {
                Id = new Guid("88888888-8888-4888-8888-000000000103"),
                TenantId = Tenant,
                DocumentType = "CC",
                DocumentNumber = "9003",
                FullName = "Persona 9003",
                Email = "persona@correo.co",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            ctx.ProcedureInstanceBiometricValidations.AddRange(
                Validacion("9001", TramiteA, personId: null),
                Validacion("9002", TramiteB, personId: null),
                Validacion("9003", null, personId: new Guid("88888888-8888-4888-8888-000000000103")));
            await ctx.SaveChangesAsync();
        }
    }

    private static ProcedureInstance Tramite(Guid id, string radicado, Guid tipoId) => new()
    {
        Id = id,
        TenantId = Tenant,
        ProcedureTypeId = tipoId,
        ReferenceNumber = radicado,
        Status = TramiteEstado.Borrador,
        Plate = radicado[^3..],
        CreatedByUserId = HierarchyScenario.UserOf(Tenant),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ProcedureInstanceBiometricValidation Validacion(string documento, Guid? tramite, Guid? personId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Tenant,
        ProcedureInstanceId = tramite,
        PersonId = personId,
        PartyRole = tramite is null ? null : "comprador",
        DocumentType = "CC",
        DocumentNumber = documento,
        Name = $"Persona {documento}",
        Email = "persona@correo.co",
        Status = BiometricEstados.Enviado,
        TokenHash = Guid.NewGuid().ToString("N"),
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        // Enlace vigente: el estado efectivo es el guardado.
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        Provider = BiometricProviders.Mock,
    };
}
