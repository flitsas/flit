using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Reportes.Catalogo;
using FluentAssertions;
using Xunit;
using static Flit.Diagnostico.Tests.Backoffice.ReportesBackofficeTests;

namespace Flit.Diagnostico.Tests.Backoffice;

/// <summary>
/// HU #14579, BE-4 — B4 <c>/backoffice/reportes/{tipo}?empresa=</c> (contrato-api §3; SP-3, SP-7, SP-8): <c>.SoloEmpresaHabilitada()</c>
/// antes de <c>ConEmpresaExplicita</c>; una compañía apagada o un uuid aleatorio dan el mismo 404 sin auditar; la habilitada deja la fila
/// de <c>lecturas_backoffice</c> del filtro y una de <c>accesos_reporte</c> BACKOFFICE_EMPRESA con <c>empresa_consultada</c>; el reporte
/// de consultas del backoffice trae la columna <c>origen</c> y cuenta los intentos MANUAL. PostgreSQL real. Datos ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~BackofficeReportesEmpresaTests
/// </code>
/// </example>
public sealed class BackofficeReportesEmpresaTests
{
    private static string PorEmpresa(string tipo, Guid? empresa) =>
        $"{Base}/{tipo}?{ReportesDePrueba.Rango()}" + (empresa is { } e ? $"&empresa={e}" : string.Empty);

    // AC «por compañía» — solo filas de A, con la columna origen (API/MANUAL) y los intentos MANUAL contados.
    [Fact]
    public async Task BE4_AC2_por_compania_solo_sus_filas_con_origen_y_doble_registro()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ConIdentidad(new EmpresasDePrueba([new(a, "Alfa", "900000001"), new(b, "Beta", null)])));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (ea, eb) = (new Aislamiento.EmpresaDePrueba(api, a), new Aislamiento.EmpresaDePrueba(api, b));
        var api1 = await ReportesDePrueba.LoteAsync(ea, [new("RPA431")], ct);
        var manual = await ReportesDePrueba.LoteAsync(ea, [new("RPA432")], ct, manual: true);
        await ReportesDePrueba.IntentoAsync(ea, manual.Celdas[0], DisparadorIntento.Manual, 500, ct);
        await ReportesDePrueba.IntentoAsync(ea, api1.Celdas[0], DisparadorIntento.Auto, 700, ct);
        await ReportesDePrueba.LoteAsync(eb, [new("RPB433")], ct);
        var flit = Guid.NewGuid();

        var respuesta = await api.Cliente(TokenFlit(api, flit)).GetAsync(PorEmpresa("consultas", a), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        var pagina = JsonDocument.Parse(texto).RootElement;
        pagina.GetProperty("ambito").GetString().Should().Be("BACKOFFICE_EMPRESA");
        ReportesDePrueba.Claves(pagina).Should().Contain("origen").And.NotContain("empresa_nombre");
        var filas = ReportesDePrueba.Filas(pagina);
        filas.Select(f => f.GetProperty("placa").GetString()).Should().BeEquivalentTo(["RPA431", "RPA432"]);
        filas.Single(f => f.GetProperty("placa").GetString() == "RPA432").GetProperty("origen").GetString().Should().Be("MANUAL");
        filas.Single(f => f.GetProperty("placa").GetString() == "RPA432").GetProperty("intentos").GetInt32().Should().Be(1, "el backoffice ve los intentos MANUAL");
        filas.Single(f => f.GetProperty("placa").GetString() == "RPA431").GetProperty("origen").GetString().Should().Be("API");
        texto.Should().NotContain("RPB433").And.NotContain(b.ToString());

        (await LecturasAsync(api, flit, ct)).Select(l => l.EmpresaConsultada).Should().Equal(a);
        var acceso = (await AccesosAsync(api, flit, ct)).Should().ContainSingle().Which;
        acceso.Alcance.Should().Be(AlcanceReporte.BackofficeEmpresa);
        acceso.EmpresaConsultada.Should().Be(a);
        acceso.Filas.Should().Be(2);
        pagina.GetProperty("acceso_id").GetGuid().Should().Be(acceso.Id);
    }

    // SP-3 — empresa apagada o uuid aleatorio: el mismo 404 EMPRESA_NO_ENCONTRADA sin auditar; sin empresa, 400 EMPRESA_REQUERIDA.
    [Fact]
    public async Task BE4_AC2_empresa_apagada_o_inexistente_404_sin_auditar_y_sin_empresa_400()
    {
        var (a, apagada) = (Guid.NewGuid(), Guid.NewGuid());
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ConIdentidad(new EmpresasDePrueba([new(a, "Alfa", null)])));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ReportesDePrueba.LoteAsync(new Aislamiento.EmpresaDePrueba(api, apagada), [new("RPC441")], ct);
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(TokenFlit(api, flit));

        var deApagada = await cliente.GetAsync(PorEmpresa("vehiculos", apagada), ct);
        var deAleatoria = await cliente.GetAsync(PorEmpresa("vehiculos", Guid.NewGuid()), ct);
        var sin = await cliente.GetAsync(PorEmpresa("vehiculos", null), ct);

        deApagada.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReportesDePrueba.CodigoAsync(deApagada, ct)).Should().Be(EmpresaHabilitada.CodigoNoEncontrada);
        (await deAleatoria.Content.ReadAsStringAsync(ct)).Should().Be(await deApagada.Content.ReadAsStringAsync(ct));
        sin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReportesDePrueba.CodigoAsync(sin, ct)).Should().Be(EmpresaExplicita.CodigoRequerida);
        (await LecturasAsync(api, flit, ct)).Should().BeEmpty();
        (await AccesosAsync(api, flit, ct)).Should().BeEmpty();
    }

    // B4 con consumo — una sola fila, la de la compañía pedida, con su nombre y NIT de Identidad.
    [Fact]
    public async Task BE4_AC4_consumo_por_compania_trae_solo_esa_fila()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ConIdentidad(new EmpresasDePrueba([new(a, "Alfa Renting S.A.S.", "900000001"), new(b, "Beta", null)])));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ReportesDePrueba.LoteAsync(new Aislamiento.EmpresaDePrueba(api, a), [new("RPA451"), new("RPA452")], ct);
        await ReportesDePrueba.LoteAsync(new Aislamiento.EmpresaDePrueba(api, b), [new("RPB453")], ct);

        var respuesta = await api.Cliente(TokenFlit(api, Guid.NewGuid())).GetAsync(PorEmpresa("consumo", a), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var fila = ReportesDePrueba.Filas(await ReportesDePrueba.JsonAsync(respuesta, ct)).Should().ContainSingle().Which;
        fila.GetProperty("empresa_nombre").GetString().Should().Be("Alfa Renting S.A.S.");
        fila.GetProperty("empresa_nit").GetString().Should().Be("900000001");
        fila.GetProperty("vehiculos").GetInt32().Should().Be(2);
    }

    // SP-1 — sin la política del backoffice (operador FLIT sin backoffice.ver): 403 sin preguntar a Identidad.
    [Fact]
    public async Task BE4_AC6_sin_backoffice_ver_403()
    {
        var identidad = new EmpresasDePrueba([]);
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: ConIdentidad(identidad));
        var ct = TestContext.Current.CancellationToken;
        var sinPolitica = api.Token(permisos: [Permisos.VolumetriaVer, Permisos.ReportesVer], roles: ["operador"], tipoEmpresa: "FLIT");

        (await api.Cliente(sinPolitica).GetAsync(PorEmpresa("vehiculos", Guid.NewGuid()), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        identidad.Llamadas.Should().Be(0);
    }
}
