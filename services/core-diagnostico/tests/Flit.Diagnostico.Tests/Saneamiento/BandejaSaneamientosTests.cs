using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Bandeja de saneamientos entre compañías (plan §6.2 grupo A, ADR-0087 D-5; HU #14576 AC40–AC42; seguridad SP-4, SP-10, SP-18 y
/// SP-21): filtros de lista cerrada, orden por <c>solicitado_en</c>, auditoría de las empresas de la página en un solo guardado antes
/// de responder, resumen sin auditoría y Excel con tope de filas. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~BandejaSaneamientosTests
/// </code>
/// </example>
public sealed class BandejaSaneamientosTests
{
    private static readonly string[] ClavesFila =
    [
        "id", "empresa", "diagnostico_id", "diagnostico_nombre", "vehiculo_id", "placa", "modo", "veredicto", "estado", "solicitado_en",
        "tomado_en", "cerrado_en", "renglones_total", "renglones_pendientes",
        // AP-1 (UA-10, D-131): vin e identificador para nombrar una fila sin placa.
        "vin", "identificador",
    ];

    // AP-1 — la fila de la bandeja trae vin e identificador (el valor cargado) para nombrar el vehículo sin placa; con placa, el
    // vin es null si el vehículo no lo tiene y el identificador es el cargado.
    [Fact]
    public async Task HU14576_AP1_fila_de_la_bandeja_trae_vin_e_identificador()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (conPlacaLote, conPlaca) = await GestionDePrueba.SaneamientoAsync(empresa, "SBA110", EstadoSaneamiento.Solicitado, ct);
        // Se siembra con placa (la columna placa admite hasta 10) y se convierte en un vehículo identificado por VIN.
        var (sinPlacaLote, sinPlaca) = await GestionDePrueba.SaneamientoAsync(empresa, "SBA111", EstadoSaneamiento.Solicitado, ct);
        await using (var db = empresa.Db())
        {
            var vehiculo = await db.Set<Flit.Diagnostico.Api.Modulos.Vehiculos.VehiculoDiagnostico>().SingleAsync(v => v.Id == sinPlacaLote.Vehiculos[0].Id, ct);
            vehiculo.Placa = null;
            vehiculo.IdentificadorNorm = "9ZZPRUEBA00000110";
            vehiculo.Vin = "9ZZPRUEBA00000110";
            vehiculo.IdentificadorOriginal = "9zzprueba00000110";
            vehiculo.TipoIdentificador = Flit.Diagnostico.Domain.Vehiculos.TipoIdentificador.Vin;
            await db.SaveChangesAsync(ct);
        }

        var respuesta = await api.Cliente(GestionDePrueba.TokenFlit(api)).GetAsync($"{GestionDePrueba.Base}?empresa={empresa.Id}&tamano=100", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var filas = (await GestionDePrueba.JsonAsync(respuesta, ct)).GetProperty("saneamientos").EnumerateArray()
            .ToDictionary(f => f.GetProperty("id").GetGuid());
        var sin = filas[sinPlaca.Id];
        sin.GetProperty("placa").ValueKind.Should().Be(JsonValueKind.Null);
        sin.GetProperty("vin").GetString().Should().Be("9ZZPRUEBA00000110");
        sin.GetProperty("identificador").GetString().Should().Be("9zzprueba00000110", "el valor tal como se cargó");
        var con = filas[conPlaca.Id];
        con.GetProperty("placa").GetString().Should().Be("SBA110");
        con.GetProperty("vin").ValueKind.Should().Be(JsonValueKind.Null);
        con.GetProperty("identificador").GetString().Should().Be("SBA110");
        conPlacaLote.Vehiculos.Should().ContainSingle();
    }

    // AC40 — tres compañías, filtro estado repetible, orden ascendente por solicitado_en, total y la forma de la fila.
    [Fact]
    public async Task HU14576_AC40_bandeja_entre_companias_con_filtros_y_orden()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = (DosEmpresas.En(api).A, DosEmpresas.En(api).B);
        var c = new EmpresaDePrueba(api, Guid.NewGuid());
        var (_, sa) = await GestionDePrueba.SaneamientoAsync(a, "SBA101", EstadoSaneamiento.Solicitado, ct);
        var (_, sb) = await GestionDePrueba.SaneamientoAsync(b, "SBA102", EstadoSaneamiento.EnGestion, ct);
        var (_, sc) = await GestionDePrueba.SaneamientoAsync(c, "SBA103", EstadoSaneamiento.Saneado, ct);
        await GestionDePrueba.RenglonAsync(b, sb.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Pendiente, ct);
        await GestionDePrueba.RenglonAsync(b, sb.Id, FuenteSaneamiento.Soat, ResultadoRenglonSaneamiento.Saneado, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));

        var todos = await cliente.GetAsync($"{GestionDePrueba.Base}?tamano=100", ct);
        var dosEstados = await cliente.GetAsync($"{GestionDePrueba.Base}?estado=SOLICITADO&estado=EN_GESTION&tamano=100", ct);
        var deB = await cliente.GetAsync($"{GestionDePrueba.Base}?empresa={b.Id}", ct);
        var porPlaca = await cliente.GetAsync($"{GestionDePrueba.Base}?placa=sba10", ct);
        var fuera = await cliente.GetAsync($"{GestionDePrueba.Base}?desde=2020-01-01&hasta=2020-01-02", ct);
        var porDefecto = await cliente.GetAsync(GestionDePrueba.Base, ct);

        todos.StatusCode.Should().Be(HttpStatusCode.OK, await todos.Content.ReadAsStringAsync(ct));
        var lista = await GestionDePrueba.JsonAsync(todos, ct);
        var filas = lista.GetProperty("saneamientos").EnumerateArray().ToList();
        filas.Select(f => f.GetProperty("id").GetGuid()).Should().Contain([sa.Id, sb.Id, sc.Id]);
        lista.GetProperty("total").GetInt32().Should().Be(filas.Count);
        filas.Select(f => f.GetProperty("solicitado_en").GetDateTimeOffset()).Should().BeInAscendingOrder();
        filas[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(ClavesFila);
        var deLaB = filas.Single(f => f.GetProperty("id").GetGuid() == sb.Id);
        deLaB.GetProperty("empresa").GetGuid().Should().Be(b.Id);
        deLaB.GetProperty("placa").GetString().Should().Be("SBA102");
        deLaB.GetProperty("modo").GetString().Should().Be("SIMPLE");
        deLaB.GetProperty("renglones_total").GetInt32().Should().Be(2);
        deLaB.GetProperty("renglones_pendientes").GetInt32().Should().Be(1);
        (await GestionDePrueba.JsonAsync(dosEstados, ct)).GetProperty("saneamientos").EnumerateArray()
            .Select(f => f.GetProperty("estado").GetString()).Should().OnlyContain(e => e == "SOLICITADO" || e == "EN_GESTION");
        (await GestionDePrueba.JsonAsync(deB, ct)).GetProperty("saneamientos").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("id").GetGuid().Should().Be(sb.Id);
        (await GestionDePrueba.JsonAsync(porPlaca, ct)).GetProperty("total").GetInt32().Should().Be(3, "la placa se normaliza y busca por prefijo");
        (await GestionDePrueba.JsonAsync(fuera, ct)).GetProperty("total").GetInt32().Should().Be(0);
        (await GestionDePrueba.JsonAsync(porDefecto, ct)).GetProperty("tamano").GetInt32().Should().Be(25);
        foreach (var tamano in new[] { 10, 20, 30, 50 })
            (await cliente.GetAsync($"{GestionDePrueba.Base}?tamano={tamano}", ct)).StatusCode.Should().Be(HttpStatusCode.OK, $"UA-2: tamano={tamano} del selector del kit");
    }

    // SP-10 — placa sin letras ni dígitos, estado fuera de lista, tamaño fuera de rango, desplazamiento grande, rango invertido o
    // un parámetro único repetido: 400.
    [Theory]
    [InlineData("placa=%25", "FILTRO_INVALIDO")]
    [InlineData("estado=XYZ", "FILTRO_INVALIDO")]
    [InlineData("tamano=101", "PAGINACION_INVALIDA")]
    [InlineData("tamano=0", "PAGINACION_INVALIDA")]
    [InlineData("pagina=100000", "PAGINACION_INVALIDA")]
    [InlineData("desde=2026-11-10&hasta=2026-11-01", "RANGO_INVALIDO")]
    [InlineData("empresa=no-es-uuid", "FILTRO_INVALIDO")]
    [InlineData("placa=ABC&placa=DEF", "FILTRO_INVALIDO")]
    public async Task HU14576_SP10_filtros_invalidos_responden_400(string consulta, string codigo)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        var respuesta = await api.Cliente(GestionDePrueba.TokenFlit(api)).GetAsync($"{GestionDePrueba.Base}?{consulta}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await respuesta.Content.ReadAsStringAsync(ct));
        (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be(codigo);
    }

    // AC41 + SP-4 — una página con tres empresas deja tres lecturas auditadas; el filtro de una empresa sin resultados también
    // audita; el resumen no audita.
    [Fact]
    public async Task HU14576_AC41_SP4_auditoria_por_empresa_de_la_pagina()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = (DosEmpresas.En(api).A, DosEmpresas.En(api).B);
        var c = new EmpresaDePrueba(api, Guid.NewGuid());
        var vacia = Guid.NewGuid();
        foreach (var (empresa, placa) in new[] { (a, "SBA111"), (b, "SBA112"), (c, "SBA113") })
            await GestionDePrueba.SaneamientoAsync(empresa, placa, EstadoSaneamiento.Solicitado, ct);
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api, usuario: flit));

        (await cliente.GetAsync($"{GestionDePrueba.Base}?tamano=100", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var lecturasPagina = await LecturasAsync(api, flit, ct);
        (await cliente.GetAsync($"{GestionDePrueba.Base}/resumen", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var trasResumen = await LecturasAsync(api, flit, ct);
        (await cliente.GetAsync($"{GestionDePrueba.Base}?empresa={vacia}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var trasVacia = await LecturasAsync(api, flit, ct);

        lecturasPagina.Select(l => l.EmpresaConsultada).Should().Contain([a.Id, b.Id, c.Id]);
        lecturasPagina.GroupBy(l => l.EmpresaConsultada).Should().OnlyContain(g => g.Count() == 1, "una lectura por empresa distinta de la página");
        lecturasPagina.Select(l => l.Fecha).Distinct().Should().ContainSingle("las lecturas se guardan en un solo SaveChanges (misma transacción, misma now())");
        trasResumen.Should().HaveCount(lecturasPagina.Count, "el resumen no audita");
        trasVacia.Should().ContainSingle(l => l.EmpresaConsultada == vacia, "?empresa=X sin resultados también es lectura entre compañías");
    }

    // SP-4 (b) — si la auditoría no se puede escribir, la respuesta no trae filas.
    [Fact]
    public async Task HU14576_SP4_sin_auditoria_no_hay_filas()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await GestionDePrueba.SaneamientoAsync(empresa, "SBA114", EstadoSaneamiento.Solicitado, ct);
        // SuperAdmin con un sub que no es un uuid: pasa la política, pero la auditoría no puede identificarlo y falla.
        var token = api.Token(permisos: [], roles: ["SuperAdmin"], tipoEmpresa: "FLIT", sujeto: "operador-sin-uuid");

        foreach (var ruta in new[] { GestionDePrueba.Base, GestionDePrueba.Base + "/excel" })
        {
            var respuesta = await api.Cliente(token).GetAsync(ruta, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

            ((int)respuesta.StatusCode).Should().BeGreaterThanOrEqualTo(400, ruta);
            cuerpo.Should().NotContain("SBA114", ruta).And.NotContain("saneamientos", ruta);
        }
    }

    // AC42 — el resumen cuenta los SOLICITADO de todas las compañías; el Excel trae las filas filtradas con las cabeceras de
    // descarga segura y, sobre el tope, 422 DEMASIADAS_FILAS. SP-21: un nombre de lote con fórmula queda como texto literal.
    [Fact]
    public async Task HU14576_AC42_resumen_y_Excel_de_la_bandeja()
    {
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Tope(3));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = (DosEmpresas.En(api).A, DosEmpresas.En(api).B);
        await GestionDePrueba.SaneamientoAsync(a, "SBA121", EstadoSaneamiento.Solicitado, ct, nombreLote: "=HYPERLINK(\"http://x\",\"y\")");
        await GestionDePrueba.SaneamientoAsync(b, "SBA122", EstadoSaneamiento.Solicitado, ct, nombreLote: "@SUM(1)");
        await GestionDePrueba.SaneamientoAsync(b, "SBA123", EstadoSaneamiento.EnGestion, ct);
        await GestionDePrueba.SaneamientoAsync(b, "SBA124", EstadoSaneamiento.Saneado, ct);
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api, usuario: flit));

        var resumen = await cliente.GetAsync(GestionDePrueba.Base + "/resumen", ct);
        var excel = await cliente.GetAsync(GestionDePrueba.Base + "/excel?estado=SOLICITADO", ct);
        var demasiadas = await cliente.GetAsync(GestionDePrueba.Base + "/excel", ct);

        (await GestionDePrueba.JsonAsync(resumen, ct)).GetProperty("solicitados").GetInt32().Should().Be(2);
        excel.StatusCode.Should().Be(HttpStatusCode.OK, await excel.Content.ReadAsStringAsync(ct));
        DeudaDePrueba.CabecerasSeguras(excel);
        excel.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        var hojas = await XmlDeHojasAsync(excel, ct);
        hojas.Should().Contain("SBA121").And.Contain("SBA122").And.NotContain("SBA123");
        hojas.Should().NotContain("<f>").And.NotContain("<f ");
        hojas.Should().Contain("HYPERLINK", "el nombre del lote sale como texto en línea");
        (await LecturasAsync(api, flit, ct)).Select(l => l.EmpresaConsultada).Should().Contain([a.Id, b.Id], "el Excel audita las empresas que exporta");
        demasiadas.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SaneamientoDePrueba.CodigoAsync(demasiadas, ct)).Should().Be("DEMASIADAS_FILAS");
    }

    private static Action<WebApplicationBuilder> Tope(int filas) => b =>
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostico:Saneamiento:ExcelBandejaMaximoFilas"] = filas.ToString(System.Globalization.CultureInfo.InvariantCulture) });

    private static async Task<List<LecturaBackoffice>> LecturasAsync(ApiDePrueba api, Guid usuario, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        return await db.Set<LecturaBackoffice>().IgnoreQueryFilters().AsNoTracking().Where(l => l.UsuarioId == usuario).ToListAsync(ct);
    }

    private static async Task<string> XmlDeHojasAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
        using var zip = new ZipArchive(flujo, ZipArchiveMode.Read);
        var partes = new List<string>();
        foreach (var entrada in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
        {
            using var lector = new StreamReader(entrada.Open());
            partes.Add(await lector.ReadToEndAsync(ct));
        }

        return string.Join('\n', partes);
    }
}
