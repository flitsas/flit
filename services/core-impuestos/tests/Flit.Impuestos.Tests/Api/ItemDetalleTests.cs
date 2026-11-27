using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Api;

/// <summary>
/// HU #14694 (M12; F2b-CF6, F2b-CF7, esquema <c>ItemDetalle</c> de impuestos.v1.yaml 1.1.0) —
/// <c>GET /liquidaciones/{id}/items/{itemId}</c>: el ítem (propietario completo, departamento, error clasificado, <c>totalAPagar</c>
/// null = «Sin dato») más <c>empresa</c>, <c>creadaPor</c>, <c>creadaEn</c>, <c>consultaStatus</c>, <c>resultado</c> (el
/// <c>result</c> del proveedor tal cual, con <c>file_errors</c>; null sin resultado), <c>periodos[]</c> y <c>recibos[]</c> tal como
/// están declarados (la descarga es de M11). Nunca dirección, correo ni teléfono del propietario, ni siquiera dentro de
/// <c>validations</c>. Otra compañía o un ítem de otro lote: 404.
/// </summary>
/// <example>
/// <code>
/// var detalle = await cliente.GetStringAsync($"/api/v1/impuestos/liquidaciones/{lote}/items/{item}", ct);
/// </code>
/// </example>
public sealed class ItemDetalleTests
{
    private static readonly string[] Ver = [Permisos.LiquidacionesVer];

    [Fact]
    public async Task Exitoso_trae_propietario_resultado_completo_vigencias_y_recibos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, ct);
        var consulta = R.Consulta("c-1", status: "partial", fileErrors: "[{\"fileId\":\"f9\",\"code\":\"file_missing\"}]",
            warnings: "[{\"code\":\"period_not_liquidable\",\"period\":2019}]");
        (await R.AplicarAsync(api, empresa, item, consulta, ct)).Should().Be(Flit.Impuestos.Api.Modulos.Resultados.Aplicacion.Aplicado);
        Guid recibo;
        await using (var db = api.Contexto(empresa))
        {
            var fila = new Recibo { ItemId = item, TenantId = empresa, FileIdApi = "f1a2", Tipo = TipoRecibo.Vigencia, Periodos = ["2025"], Estado = EstadoRecibo.Guardado };
            db.Add(fila);
            await db.SaveChangesAsync(ct);
            recibo = fila.Id;
        }

        var d = await DetalleAsync(api, empresa, lote, item, ct);

        d.GetProperty("id").GetGuid().Should().Be(item);
        d.GetProperty("liquidacionId").GetGuid().Should().Be(lote);
        d.GetProperty("placa").GetString().Should().Be("LHQ479");
        d.GetProperty("estado").GetString().Should().Be("EXITOSO");
        d.GetProperty("numeroIntento").GetInt32().Should().Be(1, "R3: un ítem que nunca se relanzó va en su primer intento");
        d.GetProperty("intentos").GetArrayLength().Should().Be(0);
        d.GetProperty("parcial").GetBoolean().Should().BeTrue();
        d.GetProperty("consultaStatus").GetString().Should().Be("partial");
        d.GetProperty("resultadoStatus").GetString().Should().Be("con_deuda");
        d.GetProperty("totalAPagar").GetDecimal().Should().Be(771700m);
        d.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresa);
        d.GetProperty("creadaPor").GetProperty("id").ValueKind.Should().Be(JsonValueKind.String);
        d.GetProperty("creadaPor").GetProperty("nombre").GetString().Should().Be("Gestora Demo", "creada_por_nombre (1.2.0)");
        d.GetProperty("secretaria").GetProperty("nombre").GetString().Should().Be("STRIA TTO CHIA", "secretaria_nombre (1.2.0)");
        d.GetProperty("secretaria").GetProperty("ciudad").GetString().Should().Be("CHIA");
        d.GetProperty("creadaEn").ValueKind.Should().Be(JsonValueKind.String);
        d.GetProperty("departamento").GetProperty("codigo").GetString().Should().Be("25");
        var propietario = d.GetProperty("propietario");
        propietario.GetProperty("nombre").GetString().Should().Be("Propietario Demo");
        propietario.GetProperty("documentoTipo").GetString().Should().Be("CC");
        propietario.GetProperty("documentoNumero").GetString().Should().Be("0000000");

        var resultado = d.GetProperty("resultado");
        resultado.GetProperty("totals").GetProperty("totalToPay").GetDecimal().Should().Be(771700m);
        resultado.GetProperty("validations").GetArrayLength().Should().Be(1);
        resultado.GetProperty("warnings")[0].GetProperty("code").GetString().Should().Be("period_not_liquidable");
        resultado.GetProperty("file_errors")[0].GetProperty("code").GetString().Should().Be("file_missing");
        resultado.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);

        var periodo = d.GetProperty("periodos").EnumerateArray().Should().ContainSingle().Subject;
        periodo.GetProperty("periodo").GetString().Should().Be("2025");
        periodo.GetProperty("impuesto").GetDecimal().Should().Be(710000m);
        periodo.GetProperty("pagosPrevios").ValueKind.Should().Be(JsonValueKind.Null, "null del proveedor ≠ 0");
        periodo.GetProperty("totalAPagar").GetDecimal().Should().Be(771700m);
        periodo.GetProperty("otrosCargos")[0].GetProperty("descripcion").GetString().Should().Be("Costos administrativos");
        periodo.GetProperty("vencimiento").GetString().Should().Be("2026-11-30");
        periodo.GetProperty("referenciaPago").ValueKind.Should().Be(JsonValueKind.Null);
        periodo.GetProperty("noLiquidable").GetBoolean().Should().BeFalse();
        periodo.GetProperty("reciboId").GetGuid().Should().Be(recibo);

        var r = d.GetProperty("recibos").EnumerateArray().Should().ContainSingle().Subject;
        r.GetProperty("id").GetGuid().Should().Be(recibo);
        r.GetProperty("tipo").GetString().Should().Be("VIGENCIA");
        r.GetProperty("periodos").EnumerateArray().Select(p => p.GetString()).Should().Equal("2025");
        r.GetProperty("estado").GetString().Should().Be("GUARDADO");
        r.GetProperty("motivo").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Un_total_null_del_proveedor_sale_null_y_nunca_0()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, ct);
        await R.AplicarAsync(api, empresa, item, R.Consulta("c-1", totalToPay: "null", periodTotal: "null"), ct);

        var d = await DetalleAsync(api, empresa, lote, item, ct);

        d.GetProperty("totalAPagar").ValueKind.Should().Be(JsonValueKind.Null);
        d.GetProperty("resultado").GetProperty("totals").GetProperty("totalToPay").ValueKind.Should().Be(JsonValueKind.Null);
        d.GetProperty("periodos")[0].GetProperty("totalAPagar").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Con_error_trae_la_clase_el_codigo_el_mensaje_y_el_error_del_resultado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, ct);
        await R.AplicarAsync(api, empresa, item, R.Consulta("c-1", status: "failed", errorCode: "portal_unavailable", errorMessage: "El portal no responde"), ct);

        var d = await DetalleAsync(api, empresa, lote, item, ct);

        d.GetProperty("estado").GetString().Should().Be("ERROR");
        d.GetProperty("errorCodigo").GetString().Should().Be("portal_unavailable");
        d.GetProperty("errorClase").ValueKind.Should().Be(JsonValueKind.String);
        d.GetProperty("errorMensaje").GetString().Should().Be("El portal no responde");
        d.GetProperty("resultado").GetProperty("error").GetProperty("code").GetString().Should().Be("portal_unavailable");
        d.GetProperty("periodos").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Sin_resultado_trae_resultado_null_y_listas_vacias()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, ct);

        var d = await DetalleAsync(api, empresa, lote, item, ct);

        d.GetProperty("estado").GetString().Should().Be("ENVIADO");
        d.GetProperty("resultado").ValueKind.Should().Be(JsonValueKind.Null);
        d.GetProperty("consultaStatus").ValueKind.Should().Be(JsonValueKind.Null);
        d.GetProperty("totalAPagar").ValueKind.Should().Be(JsonValueKind.Null);
        d.GetProperty("periodos").GetArrayLength().Should().Be(0);
        d.GetProperty("recibos").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Nunca_expone_correo_telefono_ni_direccion_del_propietario_aunque_vengan_en_validations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, ct);
        var consulta = R.Consulta("c-1").Replace(
            "\"validations\":[{\"field\":\"OwnerName\",\"sent\":\"PROPIETARIO PRUEBA\",\"portal\":null,\"result\":\"match\"}]",
            "\"validations\":[{\"field\":\"OwnerName\",\"sent\":\"PROPIETARIO PRUEBA\",\"portal\":null,\"result\":\"match\"}," +
            "{\"field\":\"OwnerEmail\",\"sent\":\"pedro@correo.example\",\"portal\":null,\"result\":\"match\"}," +
            "{\"field\":\"OwnerPhone\",\"sent\":\"3001234567\",\"portal\":\"3001234567\",\"result\":\"match\"}," +
            "{\"field\":\"OwnerAddress\",\"sent\":\"CL 10 20 30\",\"portal\":null,\"result\":\"mismatch\"}]",
            StringComparison.Ordinal);
        consulta.Should().Contain("OwnerEmail", "la consulta de prueba trae los campos de contacto");
        await R.AplicarAsync(api, empresa, item, consulta, ct);

        var texto = await api.Cliente(api.Token(empresa: empresa, permisos: Ver)).GetStringAsync(Ruta(lote, item), ct);

        texto.Should().NotContain("pedro@correo.example").And.NotContain("3001234567").And.NotContain("CL 10 20 30");
        using var doc = JsonDocument.Parse(texto);
        doc.RootElement.GetProperty("resultado").GetProperty("validations").EnumerateArray()
            .Select(v => v.GetProperty("field").GetString()).Should().Equal("OwnerName");
    }

    [Fact]
    public async Task Item_de_otro_lote_inexistente_o_de_otra_compania_responde_404_y_el_Super_Admin_lee_con_empresa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, item) = await SembrarAsync(api, ct);
        var (otroLote, _) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["ZZZ999"], ct);
        var otra = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, otra, ct);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: Ver));

        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync(Ruta(lote, Guid.NewGuid()), ct),
            HttpStatusCode.NotFound, "ITEM_NO_ENCONTRADO", "La placa no existe en esta liquidación.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await cliente.GetAsync(Ruta(otroLote, item), ct),
            HttpStatusCode.NotFound, "ITEM_NO_ENCONTRADO", "La placa no existe en esta liquidación.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await api.Cliente(api.Token(empresa: otra, permisos: Ver)).GetAsync(Ruta(lote, item), ct),
            HttpStatusCode.NotFound, "LIQUIDACION_NO_ENCONTRADA", "La liquidación no existe.");
        var sa = await api.Cliente(api.Token(superAdmin: true, tipoEmpresa: "FLIT")).GetAsync($"{Ruta(lote, item)}?empresa={empresa}", ct);
        sa.StatusCode.Should().Be(HttpStatusCode.OK, await sa.Content.ReadAsStringAsync(ct));
        await using var db = api.Contexto(empresa);
        (await db.Set<LecturaBackoffice>().CountAsync(ct)).Should().BeGreaterThan(0, "la lectura del Super Admin queda auditada");
    }

    private static string Ruta(Guid lote, Guid item) => $"{Program.Prefijo}/liquidaciones/{lote}/items/{item}";

    private static async Task<JsonElement> DetalleAsync(ApiDePrueba api, Guid empresa, Guid lote, Guid item, CancellationToken ct)
    {
        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Ver)).GetAsync(Ruta(lote, item), ct);
        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.Clone();
    }

    /// <summary>Compañía lista y una individual en proceso con su ítem ENVIADO (consulta <c>c-1</c>) y el propietario cifrado.</summary>
    private static async Task<(Guid Empresa, Guid Lote, Guid Item)> SembrarAsync(ApiDePrueba api, CancellationToken ct)
    {
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["LHQ479"], ct, EstadoItem.Enviado);
        var proteccion = api.App.Services.GetRequiredService<IProteccionImpuestos>();
        var nombre = proteccion.ProtegerPii("Propietario Demo");
        var numero = proteccion.ProtegerPii("0000000");
        await using var db = api.Contexto(empresa);
        await db.Set<Liquidacion>().Where(l => l.Id == lote)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.EnProceso).SetProperty(l => l.CreadaPorNombre, "Gestora Demo"), ct);
        await db.Set<LiquidacionItem>().Where(i => i.Id == items[0]).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.ConsultaApiId, "c-1")
            .SetProperty(i => i.DocumentoTipo, "CC")
            .SetProperty(i => i.PropietarioNombreCifrado, nombre)
            .SetProperty(i => i.DocumentoNumeroCifrado, numero)
            .SetProperty(i => i.SecretariaNombre, "STRIA TTO CHIA")
            .SetProperty(i => i.SecretariaCiudad, "CHIA"), ct);
        return (empresa, lote, items[0]);
    }
}
