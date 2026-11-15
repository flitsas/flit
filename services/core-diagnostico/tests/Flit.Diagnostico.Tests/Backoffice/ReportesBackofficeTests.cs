using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Reportes.Catalogo;
using Flit.Diagnostico.Tests.Saneamiento;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Backoffice;

/// <summary>
/// HU #14579, BE-4 — reportes del backoffice sin empresa explícita (contrato-api §2–§4; SP-1, SP-4, SP-7, SP-8): B0 catálogo con los 5
/// tipos, B1 <c>/backoffice/reportes/{tipo}/global</c> solo con las compañías habilitadas en Identidad, <c>empresa_nombre</c> primero,
/// doble auditoría (<c>accesos_reporte</c> BACKOFFICE_GLOBAL + <c>lecturas_backoffice</c> por empresa) en un guardado y fail-closed,
/// reporte <c>consumo</c> y autorización. PostgreSQL real. Datos ficticios (Ley 1581).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Backoffice.ReportesBackofficeTests
/// </code>
/// </example>
public sealed class ReportesBackofficeTests
{
    public const string Base = Program.Prefijo + "/backoffice/reportes";

    private static readonly string[] ClavesConsumo =
    [
        "empresa_nombre", "empresa_nit", "diagnosticos", "vehiculos", "consultas_exitosas", "consultas_con_error", "tasa_exito",
        "consultas_runt", "consultas_simit", "consultas_avaluo", "consultas_impuesto", "saneamientos", "tramites_creados",
    ];

    public static string Global(string tipo, string extra = "") => $"{Base}/{tipo}/global?{ReportesDePrueba.Rango()}{extra}";

    // B0 — el catálogo del backoffice trae los 5 tipos, consumo incluido; sin empresa y sin auditoría.
    [Fact]
    public async Task BE4_B0_catalogo_del_backoffice_con_los_5_tipos()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: ConIdentidad(new EmpresasDePrueba([])));
        var ct = TestContext.Current.CancellationToken;

        var respuesta = await api.Cliente(TokenFlit(api, Guid.NewGuid())).GetAsync(Base, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        respuesta.Headers.CacheControl?.NoStore.Should().BeTrue();
        (await ReportesDePrueba.JsonAsync(respuesta, ct)).GetProperty("reportes").EnumerateArray().Select(r => r.GetProperty("tipo").GetString())
            .Should().Equal("vehiculos", "consultas", "saneamientos", "derivaciones", "consumo");
    }

    // AC «global solo con compañías habilitadas» — A y B habilitadas, C apagada; empresa_nombre primero; doble auditoría en un guardado.
    [Fact]
    public async Task BE4_AC1_global_solo_con_companias_habilitadas_y_doble_auditoria_en_un_guardado()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var identidad = new EmpresasDePrueba([new(a, "Alfa Renting S.A.S.", "900000001"), new(b, "Beta Leasing S.A.", "900000002")]);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ConIdentidad(identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (ea, eb, ec) = (new EmpresaDePrueba(api, a), new EmpresaDePrueba(api, b), new EmpresaDePrueba(api, c));
        await ReportesDePrueba.LoteAsync(ea, [new("RPA401", Propietarios: [new("CC", "1020304050")])], ct);
        await ReportesDePrueba.LoteAsync(eb, [new("RPB402"), new("RPB403")], ct);
        await ReportesDePrueba.LoteAsync(ec, [new("RPC404", Propietarios: [new("CC", "5566778899")])], ct);
        var flit = Guid.NewGuid();
        var token = TokenFlit(api, flit);

        var respuesta = await api.Cliente(token).GetAsync(Global("vehiculos", "&tamano=100"), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        respuesta.Headers.CacheControl?.NoStore.Should().BeTrue();
        var pagina = JsonDocument.Parse(texto).RootElement;
        pagina.GetProperty("ambito").GetString().Should().Be("BACKOFFICE_GLOBAL");
        ReportesDePrueba.Claves(pagina)[0].Should().Be("empresa_nombre");
        var filas = ReportesDePrueba.Filas(pagina);
        filas.Select(f => f.GetProperty("placa").GetString()).Should().BeEquivalentTo(["RPA401", "RPB402", "RPB403"]);
        filas.Single(f => f.GetProperty("placa").GetString() == "RPA401").GetProperty("empresa_nombre").GetString().Should().Be("Alfa Renting S.A.S.");
        filas.Single(f => f.GetProperty("placa").GetString() == "RPA401").GetProperty("propietarios_documentos").GetString().Should().Be("CC 1020304050");
        texto.Should().NotContain("RPC404").And.NotContain("5566778899").And.NotContain(c.ToString());

        var accesos = await AccesosAsync(api, flit, ct);
        var acceso = accesos.Should().ContainSingle().Which;
        acceso.Alcance.Should().Be(AlcanceReporte.BackofficeGlobal);
        acceso.Formato.Should().Be(FormatoReporte.Pantalla);
        acceso.Filas.Should().Be(3);
        acceso.Empresas.Should().Be(2);
        acceso.EmpresaConsultada.Should().BeNull();
        pagina.GetProperty("acceso_id").GetGuid().Should().Be(acceso.Id);
        var lecturas = await LecturasAsync(api, flit, ct);
        lecturas.Select(l => l.EmpresaConsultada).Should().BeEquivalentTo([a, b]);
        lecturas.Select(l => l.Fecha).Append(acceso.Fecha).Distinct().Should().ContainSingle("accesos_reporte y lecturas_backoffice en un solo SaveChanges");
        identidad.Llamadas.Should().Be(1);
    }

    // SP-7 — una página vacía audita las habilitadas consultadas; el registro de acceso sale igual.
    [Fact]
    public async Task BE4_AC1_global_con_pagina_vacia_audita_las_habilitadas()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ConIdentidad(new EmpresasDePrueba([new(a, "Alfa", null), new(b, "Beta", null)])));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var flit = Guid.NewGuid();

        var respuesta = await api.Cliente(TokenFlit(api, flit)).GetAsync(Global("saneamientos"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        ReportesDePrueba.Filas(await ReportesDePrueba.JsonAsync(respuesta, ct)).Should().BeEmpty();
        (await LecturasAsync(api, flit, ct)).Select(l => l.EmpresaConsultada).Should().BeEquivalentTo([a, b]);
        (await AccesosAsync(api, flit, ct)).Should().ContainSingle().Which.Filas.Should().Be(0);
    }

    // SP-7 (b) — sin auditoría no hay filas.
    [Fact]
    public async Task BE4_AC1_global_sin_auditoria_no_hay_datos()
    {
        var a = Guid.NewGuid();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ConIdentidad(new EmpresasDePrueba([new(a, "Alfa", null)])));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, a), [new("RPA411", Propietarios: [new("CC", "1020304051")])], ct);
        var token = api.Token(permisos: [], roles: ["SuperAdmin"], tipoEmpresa: "FLIT", sujeto: "operador-sin-uuid");

        var respuesta = await api.Cliente(token).GetAsync(Global("vehiculos"), ct);

        ((int)respuesta.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().NotContain("RPA411").And.NotContain("1020304051");
    }

    // AC «consumo» — una fila por compañía habilitada con datos, con las columnas de contrato-api §4; la compañía recibe 404.
    [Fact]
    public async Task BE4_AC4_consumo_global_una_fila_por_compania_habilitada_con_datos()
    {
        var (a, b, sinDatos, apagada) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var identidad = new EmpresasDePrueba([new(a, "Alfa Renting S.A.S.", "900000001"), new(b, "Beta Leasing S.A.", null), new(sinDatos, "Gamma", "900000003")]);
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: ConIdentidad(identidad));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (ea, eb) = (new EmpresaDePrueba(api, a), new EmpresaDePrueba(api, b));
        var lote = await ReportesDePrueba.LoteAsync(ea, [new("RPA421"), new("RPA422")], ct);
        await ReportesDePrueba.LoteAsync(ea, [new("RPA423")], ct, express: true);
        await SaneamientoDePrueba.SembrarAsync(ea, lote.Id, lote.Vehiculos[0].Id, EstadoSaneamiento.Solicitado, ct);
        await ReportesDePrueba.DerivacionAsync(ea, lote.Id, lote.Vehiculos[1].Id, Domain.Derivacion.EstadoItemDerivacion.Creado, ct);
        await ReportesDePrueba.LoteAsync(eb, [new("RPB424")], ct);
        await ReportesDePrueba.LoteAsync(new EmpresaDePrueba(api, apagada), [new("RPC425")], ct);
        var cliente = api.Cliente(TokenFlit(api, Guid.NewGuid()));

        var respuesta = await cliente.GetAsync(Global("consumo"), ct);
        var deLaCompania = await api.Cliente(ReportesDePrueba.Token(api, a)).GetAsync(ReportesDePrueba.Ruta("consumo"), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        var pagina = JsonDocument.Parse(texto).RootElement;
        ReportesDePrueba.Claves(pagina).Should().Equal(ClavesConsumo);
        var filas = ReportesDePrueba.Filas(pagina);
        filas.Select(f => f.GetProperty("empresa_nombre").GetString()).Should().Equal("Alfa Renting S.A.S.", "Beta Leasing S.A.");
        filas.Should().OnlyContain(f => f.EnumerateObject().Select(p => p.Name).SequenceEqual(ClavesConsumo));
        var alfa = filas[0];
        alfa.GetProperty("empresa_nit").GetString().Should().Be("900000001");
        alfa.GetProperty("diagnosticos").GetInt32().Should().Be(2);
        alfa.GetProperty("vehiculos").GetInt32().Should().Be(3);
        alfa.GetProperty("consultas_exitosas").GetInt32().Should().Be(3);
        alfa.GetProperty("consultas_con_error").GetInt32().Should().Be(0);
        alfa.GetProperty("tasa_exito").GetDouble().Should().Be(1.0);
        alfa.GetProperty("consultas_runt").GetInt32().Should().Be(3);
        alfa.GetProperty("consultas_simit").GetInt32().Should().Be(0);
        alfa.GetProperty("saneamientos").GetInt32().Should().Be(1);
        alfa.GetProperty("tramites_creados").GetInt32().Should().Be(1);
        filas[1].GetProperty("empresa_nit").ValueKind.Should().Be(JsonValueKind.Null);
        texto.Should().NotContain("Gamma").And.NotContain(apagada.ToString()).And.NotContain("RPC425");
        deLaCompania.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReportesDePrueba.CodigoAsync(deLaCompania, ct)).Should().Be("REPORTE_NO_ENCONTRADO");
    }

    // AC «autorización» — un cliente con los claims del backoffice armados a mano recibe 403 en B0, B1 y B4; ?empresa= en B1: 400.
    [Fact]
    public async Task BE4_AC6_autorizacion_y_empresa_en_la_global()
    {
        var identidad = new EmpresasDePrueba([]);
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: ConIdentidad(identidad));
        var ct = TestContext.Current.CancellationToken;
        var cliente = api.Cliente(api.Token(roles: ["admin_diagnostico"],
            permisos: [Permisos.BackofficeVer, Permisos.VolumetriaVer, Permisos.ReportesDescargar, Permisos.ReportesVer], tipoEmpresa: "CLIENTE"));

        foreach (var ruta in new[] { Base, Global("vehiculos"), Global("consumo"), $"{Base}/vehiculos?{ReportesDePrueba.Rango()}&empresa={Guid.NewGuid()}" })
            (await cliente.GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, ruta);

        var conEmpresa = await api.Cliente(TokenFlit(api, Guid.NewGuid())).GetAsync(Global("vehiculos", $"&empresa={Guid.NewGuid()}"), ct);
        conEmpresa.StatusCode.Should().Be(HttpStatusCode.BadRequest, await conEmpresa.Content.ReadAsStringAsync(ct));
        (await ReportesDePrueba.CodigoAsync(conEmpresa, ct)).Should().Be("FILTRO_INVALIDO");
        var desconocido = await api.Cliente(TokenFlit(api, Guid.NewGuid())).GetAsync(Global("otro"), ct);
        (await ReportesDePrueba.CodigoAsync(desconocido, ct)).Should().Be("REPORTE_NO_ENCONTRADO");
        identidad.Llamadas.Should().Be(0, "los 400/403/404 se resuelven antes de preguntar a Identidad");
    }

    // SP-4 — Identidad caída: 503 con Retry-After, sin leer.
    [Fact]
    public async Task BE4_AC1_global_con_Identidad_caida_503()
    {
        var identidad = new EmpresasDePrueba([]) { Falla = new RpcException(new Status(StatusCode.Unavailable, "falla de prueba")) };
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: ConIdentidad(identidad));
        var ct = TestContext.Current.CancellationToken;

        var respuesta = await api.Cliente(TokenFlit(api, Guid.NewGuid())).GetAsync(Global("vehiculos"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await respuesta.Content.ReadAsStringAsync(ct));
        (await ReportesDePrueba.CodigoAsync(respuesta, ct)).Should().Be("IDENTIDAD_NO_DISPONIBLE");
        respuesta.Headers.RetryAfter.Should().NotBeNull();
    }

    /// <summary>Operador FLIT con <c>backoffice.ver</c> (la política de B*).</summary>
    internal static string TokenFlit(ApiDePrueba api, Guid usuario) =>
        api.Token(permisos: [Permisos.BackofficeVer], roles: ["operador"], tipoEmpresa: "FLIT", sujeto: usuario.ToString());

    internal static Action<Microsoft.AspNetCore.Builder.WebApplicationBuilder> ConIdentidad(EmpresasDePrueba identidad) =>
        b => b.Services.AddScoped<IEmpresasPlataforma>(_ => identidad);

    internal static async Task<List<LecturaBackoffice>> LecturasAsync(ApiDePrueba api, Guid usuario, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        return await db.Set<LecturaBackoffice>().IgnoreQueryFilters().AsNoTracking().Where(l => l.UsuarioId == usuario).ToListAsync(ct);
    }

    internal static async Task<List<AccesoReporte>> AccesosAsync(ApiDePrueba api, Guid usuario, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        return await db.Set<AccesoReporte>().IgnoreQueryFilters().AsNoTracking().Where(a => a.UsuarioId == usuario).ToListAsync(ct);
    }
}

/// <summary>Doble de la lista de empresas de Identidad: responde <c>empresas</c> o lanza <see cref="Falla"/>; cuenta las llamadas.</summary>
internal sealed class EmpresasDePrueba(IReadOnlyList<EmpresaPlataforma> empresas) : IEmpresasPlataforma
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
