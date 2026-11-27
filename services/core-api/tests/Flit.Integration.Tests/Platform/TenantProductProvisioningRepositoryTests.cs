using Flit.Infrastructure.Persistence.Repositories.Platform;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Platform;

/// <summary>
/// HU #14694 (DDL 205, columnas en inglés) contra PostgreSQL real — <see cref="TenantProductProvisioningRepository"/>: guarda y
/// relee el estado con las columnas <c>status</c>, <c>reason_code</c>, <c>message</c>, <c>action</c>, <c>signature_confirmed</c>,
/// <c>reported_version</c> y <c>reported_at</c>, y un <c>product_code</c> que no está en el catálogo (<c>23503</c>) sale como error
/// de dominio <see cref="TenantProductException.ProductNotFound"/>, no como <see cref="PostgresException"/>.
/// </summary>
/// <example>
/// <code>
/// var guardado = await new TenantProductProvisioningRepository(ctx).GuardarSiEsMasNuevoAsync(reporte, ct);
/// </code>
/// </example>
public sealed class TenantProductProvisioningRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Tenant = new("14694000-0000-4000-8000-0000000000b1");
    private static readonly DateTimeOffset Momento = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private async Task SembrarEmpresaAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at) VALUES
              ('{Tenant}','IT-14694-B','Compañía 14694 B','9146940000002','RENTING',true,false,now());
            """, cn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [PostgresFact]
    public async Task Guarda_y_relee_el_estado_con_las_columnas_en_ingles()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarEmpresaAsync();
        await using var ctx = NewContext();
        var repo = new TenantProductProvisioningRepository(ctx);

        var guardado = await repo.GuardarSiEsMasNuevoAsync(
            new TenantProductProvisioning(Tenant, "impuestos", "ERROR", "EMPRESA_YA_REGISTRADA", "La empresa ya existe", "REINTENTAR", null, 3, Momento), ct);
        var viejo = await repo.GuardarSiEsMasNuevoAsync(
            new TenantProductProvisioning(Tenant, "impuestos", "LISTO", null, null, null, true, 2, Momento), ct);

        guardado.Should().Be(new ProvisioningGuardado(EmpresaExiste: true, Aplicado: true, VersionGuardada: 3));
        viejo.Should().Be(new ProvisioningGuardado(EmpresaExiste: true, Aplicado: false, VersionGuardada: 3));
        var fila = (await repo.ListByTenantAsync(Tenant, ct)).Should().ContainSingle().Subject;
        fila.Should().Be(new TenantProductProvisioning(Tenant, "impuestos", "ERROR", "EMPRESA_YA_REGISTRADA", "La empresa ya existe", "REINTENTAR", null, 3, Momento));
    }

    [PostgresFact]
    public async Task Un_producto_fuera_del_catalogo_es_error_de_dominio_y_no_23503()
    {
        var ct = TestContext.Current.CancellationToken;
        await SembrarEmpresaAsync();
        await using var ctx = NewContext();
        var repo = new TenantProductProvisioningRepository(ctx);

        var act = () => repo.GuardarSiEsMasNuevoAsync(
            new TenantProductProvisioning(Tenant, "flotas", "PENDIENTE", null, null, null, null, 1, Momento), ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductNotFound);
    }
}
