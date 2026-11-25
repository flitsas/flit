using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Liquidaciones;
using Flit.Impuestos.Tests.Usuarios;
using FluentAssertions;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Reportes;

/// <summary>
/// HU #14694 (M14; F4b-CF1; esquemas <c>PaginaReporte</c> y <c>FilaReporte*</c> de impuestos.v1.yaml 1.1.0, deltas 9 y 11) — los
/// cuatro reportes (<c>detalle</c>, <c>lotes</c>, <c>deuda</c>, <c>errores</c>) con sus columnas en el orden y con las etiquetas de
/// <c>x-flit-etiquetas</c>, los filtros <c>tipoLiquidacion</c>, <c>estado</c>, <c>departamento</c>, <c>usuario</c> y <c>placa</c>, la
/// paginación, el 404 de un tipo desconocido y el 400 con el literal del contrato. Los exportes aplican los mismos filtros que el
/// GET (delta 9). El Super Admin en la vista global recibe <c>empresa{id, nombre}</c> en cada fila y la columna «Compañía»
/// (delta 11); en los demás casos <c>empresa</c> va null. <c>usuario</c> es el nombre que da la plataforma o, si no lo da, el id.
/// </summary>
/// <example>
/// <code>
/// var pagina = await EntornoReportes.GetJsonAsync(cliente, "/api/v1/impuestos/reportes/detalle?estado=EXITOSO", ct);
/// </code>
/// </example>
public sealed class ReportesTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task Detalle_trae_las_columnas_del_contrato_y_una_fila_por_placa()
    {
        var ct = Preparar();
        var (empresa, creador, lote, _) = await SembrarAsync(ct);

        var p = await ReporteAsync(empresa, "detalle", string.Empty, ct);

        p.GetProperty("tipo").GetString().Should().Be("detalle");
        Columnas(p).Should().Equal(
            ("tipo", "Tipo"), ("placa", "Placa"), ("estado", "Estado"), ("departamento", "Departamento"), ("propietarioNombre", "Propietario"),
            ("documentoTipo", "Tipo de documento"), ("documentoNumero", "Número de documento"), ("totalAPagar", "Total a pagar"),
            ("resultadoStatus", "Resultado"), ("parcial", "Parcial"), ("creadaEn", "Fecha"), ("usuario", "Usuario"));
        p.GetProperty("total").GetInt32().Should().Be(4);
        p.GetProperty("page").GetInt32().Should().Be(1);
        p.GetProperty("pageSize").GetInt32().Should().Be(50);
        var f = E.Filas(p, "RPT001").Should().ContainSingle().Subject;
        f.GetProperty("liquidacionId").GetGuid().Should().Be(lote);
        f.GetProperty("itemId").ValueKind.Should().Be(JsonValueKind.String);
        f.GetProperty("tipo").GetString().Should().Be("MASIVA");
        f.GetProperty("estado").GetString().Should().Be("EXITOSO");
        f.GetProperty("departamento").GetString().Should().Be("Cundinamarca");
        f.GetProperty("totalAPagar").GetDecimal().Should().Be(120m);
        f.GetProperty("resultadoStatus").GetString().Should().Be("con_deuda");
        f.GetProperty("parcial").GetBoolean().Should().BeFalse();
        f.GetProperty("usuario").GetString().Should().Be("Ana Gestora", "el nombre que da la plataforma");
        f.GetProperty("empresa").ValueKind.Should().Be(JsonValueKind.Null, "empresa solo en la vista global del Super Admin");
        E.Filas(p, "RPT003").Single().GetProperty("totalAPagar").ValueKind.Should().Be(JsonValueKind.Null, "null = «Sin dato»");
        creador.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Los_filtros_del_reporte_acotan_las_filas_y_la_paginacion_parte_el_total()
    {
        var ct = Preparar();
        var (empresa, creador, _, _) = await SembrarAsync(ct);

        (await ReporteAsync(empresa, "detalle", "?tipoLiquidacion=INDIVIDUAL", ct)).GetProperty("total").GetInt32().Should().Be(1);
        (await ReporteAsync(empresa, "detalle", "?estado=ERROR", ct)).GetProperty("total").GetInt32().Should().Be(1);
        (await ReporteAsync(empresa, "detalle", "?departamento=05", ct)).GetProperty("total").GetInt32().Should().Be(1);
        (await ReporteAsync(empresa, "detalle", $"?usuario={creador}", ct)).GetProperty("total").GetInt32().Should().Be(3);
        (await ReporteAsync(empresa, "detalle", "?placa=rpt00", ct)).GetProperty("total").GetInt32().Should().Be(4, "la placa se normaliza y busca por parte");
        (await ReporteAsync(empresa, "detalle", "?placa=rpt002", ct)).GetProperty("total").GetInt32().Should().Be(1);
        (await ReporteAsync(empresa, "detalle", $"?desde={E.Hoy(60)}&hasta={E.Hoy(31)}", ct)).GetProperty("total").GetInt32().Should().Be(0);
        var pagina2 = await ReporteAsync(empresa, "detalle", "?page=2&pageSize=3", ct);
        pagina2.GetProperty("total").GetInt32().Should().Be(4);
        pagina2.GetProperty("items").GetArrayLength().Should().Be(1);
        pagina2.GetProperty("pageSize").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Lotes_resume_cada_cabecera_con_sus_contadores()
    {
        var ct = Preparar();
        var (empresa, creador, lote, individual) = await SembrarAsync(ct);

        var p = await ReporteAsync(empresa, "lotes", string.Empty, ct);

        Columnas(p).Select(c => c.Clave).Should().Equal("tipo", "estado", "nombreArchivo", "totalItems", "exitosas", "conError", "pendientes", "creadaEn", "terminadaEn", "usuario");
        p.GetProperty("total").GetInt32().Should().Be(2);
        var f = p.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("liquidacionId").GetGuid() == lote);
        f.GetProperty("tipo").GetString().Should().Be("MASIVA");
        f.GetProperty("estado").GetString().Should().Be("EN_PROCESO");
        f.GetProperty("nombreArchivo").GetString().Should().Be("placas.csv");
        f.GetProperty("totalItems").GetInt32().Should().Be(3);
        f.GetProperty("exitosas").GetInt32().Should().Be(1);
        f.GetProperty("conError").GetInt32().Should().Be(1);
        f.GetProperty("pendientes").GetInt32().Should().Be(1);
        f.GetProperty("terminadaEn").ValueKind.Should().Be(JsonValueKind.Null);
        f.GetProperty("usuario").GetString().Should().Be("Ana Gestora");
        var sinNombre = p.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("liquidacionId").GetGuid() == individual.Lote);
        sinNombre.GetProperty("usuario").GetString().Should().Be(individual.Creador.ToString(), "sin nombre en la plataforma, el id");
        (await ReporteAsync(empresa, "lotes", $"?usuario={creador}", ct)).GetProperty("total").GetInt32().Should().Be(1);
        (await ReporteAsync(empresa, "lotes", "?tipoLiquidacion=INDIVIDUAL", ct)).GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Deuda_trae_solo_las_placas_con_deuda_y_sus_vigencias()
    {
        var ct = Preparar();
        var (empresa, _, _, _) = await SembrarAsync(ct);

        var p = await ReporteAsync(empresa, "deuda", string.Empty, ct);

        Columnas(p).Select(c => c.Clave).Should().Equal(
            "placa", "departamento", "propietarioNombre", "documentoTipo", "documentoNumero", "vigencias", "totalAPagar", "parcial", "consultadaEn");
        p.GetProperty("total").GetInt32().Should().Be(2);
        var f = E.Filas(p, "RPT001").Should().ContainSingle().Subject;
        f.GetProperty("vigencias").EnumerateArray().Select(v => v.GetString()).Should().Equal("2024", "2025");
        f.GetProperty("totalAPagar").GetDecimal().Should().Be(120m);
        f.GetProperty("consultadaEn").ValueKind.Should().Be(JsonValueKind.String);
        E.Filas(p, "RPT004").Should().BeEmpty("paz y salvo no es deuda");
    }

    [Fact]
    public async Task Errores_trae_las_placas_con_error_o_sin_poder_liquidar()
    {
        var ct = Preparar();
        var (empresa, _, _, _) = await SembrarAsync(ct);

        var p = await ReporteAsync(empresa, "errores", string.Empty, ct);

        Columnas(p).Select(c => c.Clave).Should().Equal(
            "placa", "estado", "errorCodigo", "errorClase", "errorMensaje", "campoFaltante", "departamento", "creadaEn", "usuario");
        var f = E.Filas(p, "RPT002").Should().ContainSingle().Subject;
        f.GetProperty("estado").GetString().Should().Be("ERROR");
        f.GetProperty("errorCodigo").GetString().Should().Be("portal_unavailable");
        f.GetProperty("errorClase").GetString().Should().Be("REINTENTABLE");
        f.GetProperty("errorMensaje").GetString().Should().Be("El portal no responde");
        f.GetProperty("campoFaltante").ValueKind.Should().Be(JsonValueKind.Null);
        E.Filas(p, "RPT001", "RPT003", "RPT004").Should().BeEmpty();
    }

    [Fact]
    public async Task Un_tipo_desconocido_responde_404_y_un_filtro_invalido_400_con_el_literal()
    {
        var ct = Preparar();
        var (empresa, _, _, _) = await SembrarAsync(ct);
        var cliente = Api.Cliente(Api.Token(empresa: empresa, permisos: E.Exportar));

        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Reportes}/ventas", ct),
            HttpStatusCode.NotFound, "REPORTE_NO_ENCONTRADO", "El reporte no existe.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Reportes}/ventas/excel", ct),
            HttpStatusCode.NotFound, "REPORTE_NO_ENCONTRADO", "El reporte no existe.");
        foreach (var consulta in new[] { "?departamento=5", "?estado=LISTO", "?tipoLiquidacion=OTRA", "?usuario=no-es-uuid", "?placa=AB.1" })
        {
            await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Reportes}/detalle{consulta}", ct),
                HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "Un filtro no es válido.");
        }

        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Reportes}/detalle?pageSize=201", ct),
            HttpStatusCode.BadRequest, "PAGINACION_INVALIDA", "La paginación no es válida.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Reportes}/detalle?desde={E.Hoy()}&hasta={E.Hoy(2)}", ct),
            HttpStatusCode.BadRequest, "RANGO_INVALIDO", "El rango de fechas no es válido.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync($"{E.Reportes}/detalle/pdf?desde={E.Hoy()}&hasta={E.Hoy(2)}", ct),
            HttpStatusCode.BadRequest, "RANGO_INVALIDO", "El rango de fechas no es válido.");
    }

    [Fact]
    public async Task Los_exportes_aplican_los_mismos_filtros_que_el_reporte()
    {
        var ct = Preparar();
        var (empresa, _, _, _) = await SembrarAsync(ct);
        var cliente = Api.Cliente(Api.Token(empresa: empresa, permisos: E.Exportar));

        using var excel = await cliente.GetAsync($"{E.Reportes}/detalle/excel?estado=EXITOSO&departamento=25", ct);
        using var pdf = await cliente.GetAsync($"{E.Reportes}/detalle/pdf?estado=EXITOSO&departamento=25", ct);

        excel.StatusCode.Should().Be(HttpStatusCode.OK, await excel.Content.ReadAsStringAsync(ct));
        excel.Content.Headers.ContentType!.MediaType.Should().Be(E.XlsxContentType);
        excel.Content.Headers.ContentDisposition!.FileNameStar.Should().EndWith(".xlsx");
        var (_, filas) = EntornoMasiva.LeerXlsx(await excel.Content.ReadAsByteArrayAsync(ct));
        filas[0].Should().Equal("Tipo", "Placa", "Estado", "Departamento", "Propietario", "Tipo de documento", "Número de documento", "Total a pagar", "Resultado", "Parcial", "Fecha", "Usuario");
        filas.Skip(1).Select(f => f[1]).Should().Equal(["RPT001", "RPT003"], "solo las exitosas de Cundinamarca");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync(ct);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Ver_sin_exportar_no_descarga_y_sin_ver_no_lee()
    {
        var ct = Preparar();
        var (empresa, _, _, _) = await SembrarAsync(ct);

        using var excel = await Api.Cliente(Api.Token(empresa: empresa, permisos: [Permisos.ReportesVer])).GetAsync($"{E.Reportes}/detalle/excel", ct);
        using var lectura = await Api.Cliente(Api.Token(empresa: empresa, permisos: [Permisos.DashboardVer])).GetAsync($"{E.Reportes}/detalle", ct);

        excel.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        lectura.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task El_Super_Admin_en_la_vista_global_recibe_la_empresa_en_las_cuatro_filas()
    {
        var ct = Preparar();
        var (empresa, _, _, _) = await SembrarAsync(ct);
        fixture.Directorio.Sembrar(empresa, "Flota Andina SAS", "900123456");
        var sa = Api.Cliente(Api.Token(superAdmin: true, tipoEmpresa: "FLIT"));

        foreach (var (tipo, placa) in new[] { ("detalle", "RPT001"), ("lotes", (string?)null), ("deuda", "RPT001"), ("errores", "RPT002") })
        {
            var p = await E.GetJsonAsync(sa, $"{E.Reportes}/{tipo}?pageSize=200", ct);
            Columnas(p)[0].Should().Be(("empresa", "Compañía"), $"la columna de la compañía va primero en {tipo}");
            var filas = p.GetProperty("items").EnumerateArray()
                .Where(f => f.GetProperty("empresa").ValueKind == JsonValueKind.Object && f.GetProperty("empresa").GetProperty("id").GetGuid() == empresa).ToList();
            filas.Should().NotBeEmpty($"las filas de la compañía salen en la vista global de {tipo}");
            filas.Should().OnlyContain(f => f.GetProperty("empresa").GetProperty("nombre").GetString() == "Flota Andina SAS");
            if (placa is not null)
                filas.Should().Contain(f => f.GetProperty("placa").GetString() == placa);
        }

        var conEmpresa = await E.GetJsonAsync(sa, $"{E.Reportes}/detalle?empresa={empresa}", ct);
        Columnas(conEmpresa)[0].Clave.Should().Be("tipo", "con empresa no es la vista global");
        conEmpresa.GetProperty("items").EnumerateArray().Should().OnlyContain(f => f.GetProperty("empresa").ValueKind == JsonValueKind.Null);
        conEmpresa.GetProperty("total").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task El_usuario_es_creada_por_nombre_y_si_falta_el_de_la_plataforma_o_el_id()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        var ana = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        fixture.Plataforma.Filas.Add(UsuariosPlataformaDePrueba.Fila(ana, "Ana Gestora", "ana@empresa.test", "gestor_impuestos"));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, ana, "Ana del Token", ct, null, new ItemSemilla("USR001", EstadoItem.Error));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, ana, null, ct, DateTimeOffset.UtcNow.AddMinutes(-1), new ItemSemilla("USR002", EstadoItem.Error));

        foreach (var tipo in new[] { "detalle", "errores" })
        {
            var p = await ReporteAsync(empresa, tipo, string.Empty, ct);
            E.Filas(p, "USR001").Single().GetProperty("usuario").GetString().Should().Be("Ana del Token", "creada_por_nombre (1.2.0) manda");
            E.Filas(p, "USR002").Single().GetProperty("usuario").GetString().Should().Be("Ana Gestora", "sin columna, el nombre de la plataforma");
        }

        var lotes = await ReporteAsync(empresa, "lotes", string.Empty, ct);
        lotes.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("usuario").GetString()).Should().BeEquivalentTo(["Ana del Token", "Ana Gestora"]);
    }

    private static List<(string Clave, string Etiqueta)> Columnas(JsonElement pagina) =>
        [.. pagina.GetProperty("columnas").EnumerateArray().Select(c => (c.GetProperty("clave").GetString()!, c.GetProperty("etiqueta").GetString()!))];

    private Task<JsonElement> ReporteAsync(Guid empresa, string tipo, string consulta, CancellationToken ct) =>
        E.GetJsonAsync(Api.Cliente(Api.Token(empresa: empresa, permisos: E.Ver)), $"{E.Reportes}/{tipo}{consulta}", ct);

    /// <summary>
    /// Una compañía con una masiva de Ana Gestora (RPT001 exitosa con deuda en Cundinamarca, RPT002 con error en Antioquia, RPT003
    /// exitosa sin monto en Cundinamarca y pendiente) y una individual de un usuario sin nombre en la plataforma (RPT004 paz y salvo).
    /// </summary>
    private async Task<(Guid Empresa, Guid Creador, Guid Lote, (Guid Lote, Guid Creador) Individual)> SembrarAsync(CancellationToken ct)
    {
        var empresa = Guid.NewGuid();
        var creador = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        fixture.Plataforma.Filas.Add(UsuariosPlataformaDePrueba.Fila(creador, "Ana Gestora", "ana@empresa.test", "gestor_impuestos"));
        var (lote, _) = await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, creador, ct, null,
            new ItemSemilla("RPT001", ResultadoStatus: "con_deuda", Total: 120m, Departamento: "25", DepartamentoNombre: "Cundinamarca",
                PropietarioNombre: "Propietario Prueba Dos", DocumentoTipo: "CC", DocumentoNumero: "1000000003", Vigencias: ["2024", "2025"]),
            new ItemSemilla("RPT002", EstadoItem.Error, Departamento: "05", DepartamentoNombre: "Antioquia", ErrorCodigo: "portal_unavailable",
                ErrorClase: ClaseError.Reintentable, ErrorMensaje: "El portal no responde"),
            new ItemSemilla("RPT003", EstadoItem.Enviado, ConCallback: true, Departamento: "25", DepartamentoNombre: "Cundinamarca"));
        await using (var db = Api.Contexto(empresa))
        {
            // RPT003 queda exitosa sin monto; la cabecera conserva los contadores con que se sembró (1 exitosa, 1 error, 1 pendiente).
            var item = db.Set<LiquidacionItem>().Single(i => i.Placa == "RPT003" && i.TenantId == empresa);
            item.Estado = EstadoItem.Exitoso;
            item.ResultadoStatus = "con_deuda";
            await db.SaveChangesAsync(ct);
        }

        var otro = Guid.NewGuid();
        var (individual, _) = await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, otro, ct, null,
            new ItemSemilla("RPT004", ResultadoStatus: "paz_y_salvo", Total: 0m));
        return (empresa, creador, lote, (individual, otro));
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        fixture.Plataforma.Reiniciar();
        return TestContext.Current.CancellationToken;
    }
}
