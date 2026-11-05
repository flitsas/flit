using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13728 (F00.1-H5), escenarios 3 y 4 — las rutas <c>/backoffice/*</c> leen entre compañías solo con la empresa
/// indicada de forma explícita (<c>?empresa=</c>): sin ella, 400 y nada auditado; con ella, solo los datos de esa empresa
/// y una fila en <c>diagnostico.lecturas_backoffice</c> con el usuario, la empresa consultada y la fecha. Postgres real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~BackofficeEmpresaExplicitaTests
/// </code>
/// </example>
public sealed class BackofficeEmpresaExplicitaTests
{
    private const string Resumen = Program.Prefijo + BackofficeEndpoints.Grupo + BackofficeEndpoints.RutaResumen;

    // Escenario 4 — sin ?empresa= (o con un valor que no es una empresa): 400 y no se audita lectura.
    [Fact]
    public async Task Sin_parametro_empresa_responde_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(TokenBackoffice(api, flit));

        foreach (var url in new[] { Resumen, $"{Resumen}?empresa=", $"{Resumen}?empresa=no-es-una-empresa", $"{Resumen}?empresa={Guid.Empty}" })
        {
            var respuesta = await cliente.GetAsync(url, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, url);
            (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString().Should().Be(EmpresaExplicita.CodigoRequerida);
        }

        (await Auditoria(api, flit, ct)).Should().BeEmpty("una petición sin empresa no lee nada y no se audita");
    }

    // Escenario 3 — con ?empresa=B recibe solo datos de B y queda un registro con usuario, empresa B y fecha.
    [Fact]
    public async Task Cada_lectura_queda_auditada_con_usuario_empresa_y_fecha()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, diagnosticos: 2, conConfiguracion: true, ct);
        await SembrarAsync(empresas.B, diagnosticos: 1, conConfiguracion: false, ct);
        var flit = Guid.NewGuid();
        var token = TokenBackoffice(api, flit);
        var usuario = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject);
        var antes = DateTimeOffset.UtcNow.AddSeconds(-5);

        var deB = await api.Cliente(token).GetAsync($"{Resumen}?empresa={empresas.B.Id}", ct);
        var deA = await api.Cliente(token).GetAsync($"{Resumen}?empresa={empresas.A.Id}", ct);

        deB.StatusCode.Should().Be(HttpStatusCode.OK, await deB.Content.ReadAsStringAsync(ct));
        var cuerpoB = await deB.Content.ReadAsStringAsync(ct);
        var resumenB = JsonDocument.Parse(cuerpoB).RootElement;
        resumenB.GetProperty("empresa").GetGuid().Should().Be(empresas.B.Id);
        resumenB.GetProperty("diagnosticos").GetInt32().Should().Be(1, "solo los diagnósticos de B");
        resumenB.GetProperty("configuracion").GetBoolean().Should().BeFalse("B no tiene configuración aunque A sí");
        cuerpoB.Should().NotContain(empresas.A.Id.ToString(), "nada de A en la lectura de B");
        var resumenA = await deA.Content.ReadFromJsonAsync<JsonElement>(ct);
        resumenA.GetProperty("diagnosticos").GetInt32().Should().Be(2);
        resumenA.GetProperty("configuracion").GetBoolean().Should().BeTrue();

        var auditoria = await Auditoria(api, flit, ct);
        auditoria.Select(l => l.EmpresaConsultada).Should().Equal(empresas.B.Id, empresas.A.Id);
        auditoria.Should().OnlyContain(l => l.UsuarioId == usuario && l.TenantId == flit, "el lector y su compañía FLIT");
        auditoria.Should().OnlyContain(l => l.Fecha >= antes && l.Fecha <= DateTimeOffset.UtcNow.AddSeconds(5));
        auditoria[0].Ruta.Should().Be($"{Resumen}?empresa={empresas.B.Id}");
    }

    // La empresa explícita sale de la ruta ({empresa}) o de la consulta (?empresa=); si llegan las dos y difieren, no hay empresa.
    [Fact]
    public void Empresa_de_la_ruta_o_de_la_consulta_y_si_difieren_no_hay_empresa()
    {
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        EmpresaExplicita.Resolver(null, b.ToString()).Should().Be(b);
        EmpresaExplicita.Resolver(b.ToString(), null).Should().Be(b);
        EmpresaExplicita.Resolver(b.ToString(), b.ToString().ToUpperInvariant()).Should().Be(b);
        EmpresaExplicita.Resolver(b.ToString(), c.ToString()).Should().BeNull("dos empresas distintas no son una empresa explícita");
        EmpresaExplicita.Resolver(null, null).Should().BeNull();
        EmpresaExplicita.Resolver("  ", "x").Should().BeNull();
        EmpresaExplicita.Resolver(null, Guid.Empty.ToString()).Should().BeNull();
    }

    private static string TokenBackoffice(ApiDePrueba api, Guid flit) =>
        api.Token(empresa: flit, permisos: [Permisos.BackofficeVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

    private static async Task SembrarAsync(EmpresaDePrueba empresa, int diagnosticos, bool conConfiguracion, CancellationToken ct)
    {
        await using var db = empresa.Db();
        for (var i = 0; i < diagnosticos; i++)
            db.Add(new CabeceraDiagnostico { Nombre = $"Lote {i}", Configuracion = "{}", UsuarioCreadorId = Guid.NewGuid(), CreadoEn = DateTimeOffset.UtcNow });
        if (conConfiguracion)
            db.Add(new ConfiguracionEmpresa());
        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<LecturaBackoffice>> Auditoria(ApiDePrueba api, Guid flit, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, flit);
        return await db.Set<LecturaBackoffice>().AsNoTracking().OrderBy(l => l.Fecha).ThenBy(l => l.Id).ToListAsync(ct);
    }
}
