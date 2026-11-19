using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Repositories.Platform;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// HU #13726 (F00.1-H4), escenario 7 — <see cref="TenantProductFlagsReader"/> lee también la bandera de Diagnóstico de
/// <c>platform.tenant_products</c>, para que su suscripción se refleje como la de Trámites y Comparendos. La lectura contra
/// Postgres real de las otras dos la cubre <c>PlatformProductsTests</c>; aquí, un <c>FlitDbContext</c> InMemory.
/// </summary>
/// <example>
/// <code>
/// var flags = await new TenantProductFlagsReader(db).GetAsync(empresa);   // flags.Diagnostico
/// </code>
/// </example>
public sealed class TenantProductFlagsReaderTests
{
    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private static TenantProductEntity Fila(Guid tenant, string producto, bool encendido) =>
        new() { Id = Guid.NewGuid(), TenantId = tenant, ProductCode = producto, Enabled = encendido };

    // Escenario 7 — compañía con Diagnóstico encendido: las banderas lo incluyen.
    [Fact]
    public async Task Lee_la_bandera_de_diagnostico()
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        await using var db = NewContext(nameof(Lee_la_bandera_de_diagnostico));
        db.Set<TenantProductEntity>().AddRange(Fila(empresa, "tramites", true), Fila(empresa, "diagnostico", true));
        await db.SaveChangesAsync(ct);

        var flags = await new TenantProductFlagsReader(db).GetAsync(empresa, ct);

        flags.Should().Be(new TenantProductFlags(Tramites: true, Comparendos: false, Diagnostico: true));
    }

    // Escenario 7 — apagada, sin fila o encendida en otra compañía: Diagnóstico es false (fail-closed).
    [Fact]
    public async Task Diagnostico_apagado_sin_fila_o_de_otra_compania_es_false()
    {
        var ct = TestContext.Current.CancellationToken;
        var apagada = Guid.NewGuid();
        var sinFila = Guid.NewGuid();
        var otra = Guid.NewGuid();
        await using var db = NewContext(nameof(Diagnostico_apagado_sin_fila_o_de_otra_compania_es_false));
        db.Set<TenantProductEntity>().AddRange(Fila(apagada, "diagnostico", false), Fila(otra, "diagnostico", true), Fila(sinFila, "comparendos", true));
        await db.SaveChangesAsync(ct);
        var reader = new TenantProductFlagsReader(db);

        (await reader.GetAsync(apagada, ct)).Diagnostico.Should().BeFalse();
        (await reader.GetAsync(sinFila, ct)).Should().Be(new TenantProductFlags(Tramites: false, Comparendos: true, Diagnostico: false));
        (await reader.GetAsync(otra, ct)).Diagnostico.Should().BeTrue();
    }
}
