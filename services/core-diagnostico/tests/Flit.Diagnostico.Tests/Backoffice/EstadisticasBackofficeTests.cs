using System.Net;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Saneamiento;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Estadisticas.EstadisticasDePrueba;

namespace Flit.Diagnostico.Tests.Backoffice;

/// <summary>
/// HU #14579, BE-1 — estadísticas del backoffice (contrato-api §1; SP-1, SP-3, SP-4, SP-7): E2 <c>/backoffice/estadisticas/global</c>
/// solo con las compañías habilitadas en Identidad, top de empresas, auditoría en <c>lecturas_backoffice</c> de cada empresa que aporta
/// en un solo guardado y fail-closed; E3 <c>/backoffice/estadisticas?empresa=</c> con <c>.SoloEmpresaHabilitada()</c> antes de la
/// empresa explícita; 400/403/503. PostgreSQL real. Empresas ficticias (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EstadisticasBackofficeTests
/// </code>
/// </example>
public sealed class EstadisticasBackofficeTests
{
    // AC «global solo de compañías habilitadas y auditado» — A y B habilitadas, C apagada.
    [Fact]
    public async Task BE1_AC5_global_solo_de_companias_habilitadas_y_auditado_en_un_guardado()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var identidad = new EmpresasFijas([new(a, "Alfa Renting S.A.S.", "900000001"), new(b, "Beta Leasing S.A.", "900000002")]);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (ea, eb, ec) = (new EmpresaDePrueba(api, a), new EmpresaDePrueba(api, b), new EmpresaDePrueba(api, c));
        await LoteAsync(ea, Dia(-1), ct, placas: ["EST201"]);
        await LoteAsync(eb, Dia(-1), ct, placas: ["EST202", "EST203"]);
        await LoteAsync(eb, Dia(-2), ct, express: true, placas: ["EST204"]);
        var deC = await LoteAsync(ec, Dia(-1), ct, placas: ["EST205", "EST206", "EST207"]);
        await SaneamientoDePrueba.SembrarAsync(ec, deC.Id, deC.Vehiculos[0].Id, EstadoSaneamiento.Solicitado, ct);
        var flit = Guid.NewGuid();

        var respuesta = await api.Cliente(TokenFlit(api, flit)).GetAsync(Ruta(Hoy.AddDays(-3), Hoy, RutaGlobal), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        respuesta.Headers.CacheControl?.NoStore.Should().BeTrue();
        var dto = await JsonAsync(respuesta, ct);
        dto.GetProperty("ambito").GetProperty("tipo").GetString().Should().Be("GLOBAL");
        dto.GetProperty("ambito").GetProperty("empresa_id").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        dto.GetProperty("resumen").GetProperty("diagnosticos").GetInt32().Should().Be(3, "C está apagada: no aporta conteos");
        dto.GetProperty("resumen").GetProperty("vehiculos").GetInt32().Should().Be(4);
        dto.GetProperty("resumen").GetProperty("saneamientos_solicitados").GetInt32().Should().Be(0);
        var top = dto.GetProperty("top_empresas").EnumerateArray().ToList();
        top.Select(t => t.GetProperty("empresa_id").GetGuid()).Should().Equal(b, a);
        top[0].GetProperty("empresa_nombre").GetString().Should().Be("Beta Leasing S.A.");
        top[0].GetProperty("diagnosticos").GetInt32().Should().Be(2);
        top[0].GetProperty("vehiculos").GetInt32().Should().Be(3);
        dto.GetRawText().Should().NotContain(c.ToString()).And.NotContain("EST20").And.NotContain("900000001");
        var lecturas = await LecturasAsync(api, flit, ct);
        lecturas.Select(l => l.EmpresaConsultada).Should().BeEquivalentTo([a, b]);
        lecturas.Select(l => l.Fecha).Distinct().Should().ContainSingle("un solo SaveChanges (misma transacción, misma now())");
        identidad.Llamadas.Should().Be(1, "una llamada a Identidad por petición");
    }

    // SP-7 — sin datos en el rango se auditan las habilitadas consultadas.
    [Fact]
    public async Task BE1_AC5_global_sin_datos_audita_las_habilitadas()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var identidad = new EmpresasFijas([new(a, "Alfa", null), new(b, "Beta", null)]);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var flit = Guid.NewGuid();

        var respuesta = await api.Cliente(TokenFlit(api, flit)).GetAsync(Ruta(Hoy, Hoy, RutaGlobal), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        (await LecturasAsync(api, flit, ct)).Select(l => l.EmpresaConsultada).Should().BeEquivalentTo([a, b]);
    }

    // SP-7 (b) — si la auditoría falla, la respuesta no trae datos.
    [Fact]
    public async Task BE1_AC5_global_sin_auditoria_no_hay_datos()
    {
        var a = Guid.NewGuid();
        var identidad = new EmpresasFijas([new(a, "Alfa", null)]);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await LoteAsync(new EmpresaDePrueba(api, a), Dia(0), ct, placas: ["EST211"]);
        // SuperAdmin con un sub que no es un uuid: pasa la política, pero la auditoría no puede identificarlo y falla.
        var token = api.Token(permisos: [], roles: ["SuperAdmin"], tipoEmpresa: "FLIT", sujeto: "operador-sin-uuid");

        var respuesta = await api.Cliente(token).GetAsync(Ruta(Hoy, Hoy, RutaGlobal), ct);

        ((int)respuesta.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().NotContain("resumen").And.NotContain("diagnosticos");
    }

    // E3 — empresa habilitada: ámbito EMPRESA con el nombre de Identidad, solo sus datos y una fila de auditoría.
    [Fact]
    public async Task BE1_AC5_E3_por_compania_habilitada_con_nombre_y_una_lectura()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var identidad = new EmpresasFijas([new(a, "Alfa Renting S.A.S.", null), new(b, "Beta", null)]);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await LoteAsync(new EmpresaDePrueba(api, a), Dia(-1), ct, placas: ["EST221"]);
        await LoteAsync(new EmpresaDePrueba(api, b), Dia(-1), ct, placas: ["EST222", "EST223"]);
        var flit = Guid.NewGuid();

        var respuesta = await api.Cliente(TokenFlit(api, flit)).GetAsync(Ruta(Hoy.AddDays(-2), Hoy, $"{RutaEmpresa}?empresa={a}"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var dto = await JsonAsync(respuesta, ct);
        dto.GetProperty("ambito").GetProperty("tipo").GetString().Should().Be("EMPRESA");
        dto.GetProperty("ambito").GetProperty("empresa_id").GetGuid().Should().Be(a);
        dto.GetProperty("ambito").GetProperty("empresa_nombre").GetString().Should().Be("Alfa Renting S.A.S.");
        dto.GetProperty("resumen").GetProperty("diagnosticos").GetInt32().Should().Be(1);
        dto.GetProperty("top_empresas").GetArrayLength().Should().Be(0);
        (await LecturasAsync(api, flit, ct)).Select(l => l.EmpresaConsultada).Should().Equal(a);
    }

    // SP-3 — E3 con una compañía apagada o un uuid aleatorio: el mismo 404 EMPRESA_NO_ENCONTRADA sin auditar; sin empresa, 400.
    [Fact]
    public async Task BE1_AC5_E3_empresa_apagada_o_inexistente_404_sin_auditoria()
    {
        var identidad = new EmpresasFijas([new(Guid.NewGuid(), "Alfa", null)]);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(TokenFlit(api, flit));

        var apagada = await cliente.GetAsync(Ruta(Hoy, Hoy, $"{RutaEmpresa}?empresa={Guid.NewGuid()}"), ct);
        var otra = await cliente.GetAsync(Ruta(Hoy, Hoy, $"{RutaEmpresa}?empresa={Guid.NewGuid()}"), ct);
        var sin = await cliente.GetAsync(Ruta(Hoy, Hoy, RutaEmpresa), ct);

        apagada.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(apagada, ct)).GetProperty("code").GetString().Should().Be(EmpresaHabilitada.CodigoNoEncontrada);
        (await otra.Content.ReadAsStringAsync(ct)).Should().Be(await apagada.Content.ReadAsStringAsync(ct));
        sin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await JsonAsync(sin, ct)).GetProperty("code").GetString().Should().Be(EmpresaExplicita.CodigoRequerida);
        (await LecturasAsync(api, flit, ct)).Should().BeEmpty();
    }

    // AC «rango y parámetros inválidos» — E2 con ?empresa= (y E2/E3 con documento o rango largo): 400 antes de preguntar a Identidad.
    [Theory]
    [InlineData(RutaGlobal, "&empresa=0192f1a2-0000-7000-8000-000000000001", "FILTRO_INVALIDO")]
    [InlineData(RutaGlobal, "&nombre=x", "FILTRO_INVALIDO")]
    [InlineData(RutaGlobal, "&hasta=2020-01-01", "FILTRO_INVALIDO")]
    public async Task BE1_AC6_E2_parametros_invalidos_400_sin_Identidad(string ruta, string extra, string codigo)
    {
        var identidad = new EmpresasFijas([]);
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        var ct = TestContext.Current.CancellationToken;

        var respuesta = await api.Cliente(TokenFlit(api, Guid.NewGuid())).GetAsync(Ruta(Hoy, Hoy, ruta, extra), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await respuesta.Content.ReadAsStringAsync(ct));
        (await JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be(codigo);
        identidad.Llamadas.Should().Be(0);
    }

    // SP-1 — un cliente con los claims del backoffice armados a mano recibe 403 en E2 y E3; sin volumetria.ver, también.
    [Fact]
    public async Task BE1_AC5_autorizacion_de_E2_y_E3()
    {
        var identidad = new EmpresasFijas([]);
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Token(roles: ["admin_diagnostico"], permisos: [Permisos.BackofficeVer, Permisos.VolumetriaVer], tipoEmpresa: "CLIENTE");
        var sinVolumetria = api.Token(roles: ["operador"], permisos: [Permisos.BackofficeVer], tipoEmpresa: "FLIT");

        foreach (var token in new[] { cliente, sinVolumetria })
        {
            (await api.Cliente(token).GetAsync(Ruta(Hoy, Hoy, RutaGlobal), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await api.Cliente(token).GetAsync(Ruta(Hoy, Hoy, $"{RutaEmpresa}?empresa={Guid.NewGuid()}"), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        identidad.Llamadas.Should().Be(0);
    }

    // SP-4 — Identidad caída: 503 IDENTIDAD_NO_DISPONIBLE con Retry-After, sin leer.
    [Fact]
    public async Task BE1_AC5_global_con_Identidad_caida_503()
    {
        var identidad = new EmpresasFijas([]) { Falla = new RpcException(new Status(StatusCode.Unavailable, "falla de prueba")) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: x => x.Services.AddScoped<IEmpresasPlataforma>(_ => identidad));
        var ct = TestContext.Current.CancellationToken;

        var respuesta = await api.Cliente(TokenFlit(api, Guid.NewGuid())).GetAsync(Ruta(Hoy, Hoy, RutaGlobal), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await respuesta.Content.ReadAsStringAsync(ct));
        (await JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be("IDENTIDAD_NO_DISPONIBLE");
        respuesta.Headers.RetryAfter.Should().NotBeNull();
    }

    private static string TokenFlit(ApiDePrueba api, Guid usuario) =>
        api.Token(permisos: [Permisos.BackofficeVer, Permisos.VolumetriaVer], roles: ["operador"], tipoEmpresa: "FLIT", sujeto: usuario.ToString());

    private static async Task<List<LecturaBackoffice>> LecturasAsync(ApiDePrueba api, Guid usuario, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        return await db.Set<LecturaBackoffice>().IgnoreQueryFilters().AsNoTracking().Where(l => l.UsuarioId == usuario).ToListAsync(ct);
    }

    /// <summary>Doble de la lista de empresas de Identidad: responde <c>empresas</c> o lanza <see cref="Falla"/>.</summary>
    private sealed class EmpresasFijas(IReadOnlyList<EmpresaPlataforma> empresas) : IEmpresasPlataforma
    {
        private int _llamadas;

        public Exception? Falla { get; init; }

        public int Llamadas => _llamadas;

        public Task<IReadOnlyList<EmpresaPlataforma>?> ListarAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _llamadas);
            return Falla is not null ? Task.FromException<IReadOnlyList<EmpresaPlataforma>?>(Falla) : Task.FromResult<IReadOnlyList<EmpresaPlataforma>?>(empresas);
        }
    }
}
