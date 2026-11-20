using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// HU #13722 (F00.1-H2), escenarios 1 y 5 — el contexto de datos filtra siempre por la empresa del token y no escribe
/// filas de otra empresa. Postgres real (base efímera de <see cref="ApiDePrueba"/>), con filas de A y de B en la misma base.
/// </summary>
public sealed class TenantFilterTests
{
    // Escenario 1 — filtro global por compañía.
    [Fact]
    public async Task Consulta_con_token_de_A_no_ve_filas_de_B()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var deA = await SembrarAsync(empresas.A, ct);
        var deB = await SembrarAsync(empresas.B, ct);

        await using var db = empresas.A.Db();
        var visibles = await db.Set<LecturaBackoffice>().AsNoTracking().ToListAsync(ct);

        visibles.Select(l => l.Id).Should().Equal(deA.Id);
        visibles.Should().OnlyContain(l => l.TenantId == empresas.A.Id);
        (await db.Set<LecturaBackoffice>().FindAsync([deB.Id], ct)).Should().BeNull("ni por clave se ve la fila de B");
    }

    [Fact]
    public async Task Sin_empresa_en_el_token_no_ve_ninguna_fila()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, ct);

        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa: null);

        (await db.Set<LecturaBackoffice>().CountAsync(ct)).Should().Be(0, "sin empresa resuelta no hay acceso (cerrado por defecto)");
    }

    // Escenario 5 — escritura con tenant ajeno.
    [Fact]
    public async Task Guardar_con_tenant_distinto_al_token_falla()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);

        await using var db = empresas.A.Db();
        db.Add(new LecturaBackoffice { TenantId = empresas.A.Id, EmpresaConsultada = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), Ruta = "/propia" });
        db.Add(new LecturaBackoffice { TenantId = empresas.B.Id, EmpresaConsultada = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), Ruta = "/ajena" });
        var guardar = () => db.SaveChangesAsync(ct);

        (await guardar.Should().ThrowAsync<EscrituraEntreEmpresasException>()).WithMessage($"*{empresas.B.Id}*");
        (await ContarFilasAsync(api.BaseDeDatos, ct)).Should().Be(0, "no se escribe nada, tampoco la fila propia");
    }

    [Fact]
    public async Task Guardar_sin_tenant_toma_el_del_token_y_sin_empresa_falla()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);

        var lectura = await SembrarAsync(empresas.A, ct);

        lectura.TenantId.Should().Be(empresas.A.Id);
        await using var sinEmpresa = DosEmpresas.Contexto(api.BaseDeDatos, empresa: null);
        sinEmpresa.Add(new LecturaBackoffice { EmpresaConsultada = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), Ruta = "/sin-empresa" });
        var guardar = () => sinEmpresa.SaveChangesAsync(ct);
        await guardar.Should().ThrowAsync<EscrituraEntreEmpresasException>("sin empresa no se escribe");
        (await ContarFilasAsync(api.BaseDeDatos, ct)).Should().Be(1);
    }

    private static async Task<LecturaBackoffice> SembrarAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        var lectura = new LecturaBackoffice { EmpresaConsultada = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), Ruta = "/api/v1/diagnostico/backoffice/prueba" };
        await using var db = empresa.Db();
        db.Add(lectura);
        await db.SaveChangesAsync(ct);
        return lectura;
    }

    private static async Task<long> ContarFilasAsync(string baseDeDatos, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(baseDeDatos);
        await connection.OpenAsync(ct);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM diagnostico.lecturas_backoffice", connection);
        return (long)(await count.ExecuteScalarAsync(ct))!;
    }
}
