using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13751 (F02-H5), escenario 1 — <c>POST /diagnosticos/{id}/carga/validar-filas</c>: revalida las filas corregidas
/// con el mismo análisis de la carga (#13749: regla de Trámites, longitud, documento, duplicados) y devuelve el mismo
/// resumen, sin escribir nada en la base. Solo en CREADO; la cabecera de otra compañía responde 404. Postgres real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ValidarFilasTests
/// </code>
/// </example>
public sealed class ValidarFilasTests
{
    // Escenario 1 (F02-C9) — una fila errónea corregida vuelve válida con su tipo y nada se persiste.
    [Fact]
    public async Task Fila_corregida_pasa_de_erronea_a_valida_y_nada_se_persiste()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (cliente, id) = await Lotes.CrearAsync(api, empresa, new ConfiguracionEmpresa(), ct);

        var antes = await Lotes.ValidarAsync(cliente, id, [Lotes.Fila("AB12X", fila: 4)], ct);
        var despues = await Lotes.ValidarAsync(cliente, id, [Lotes.Fila("ABC-12X", fila: 4), Lotes.Fila("ABC12X", "CC", "1020304050", fila: 7)], ct);

        var textoAntes = await antes.Content.ReadAsStringAsync(ct);
        antes.StatusCode.Should().Be(HttpStatusCode.OK, textoAntes);
        var erronea = Fila(JsonDocument.Parse(textoAntes).RootElement, 4);
        erronea.GetProperty("valida").GetBoolean().Should().BeFalse();
        Errores(erronea).Should().Equal("IDENTIFICADOR_INVALIDO");

        var textoDespues = await despues.Content.ReadAsStringAsync(ct);
        despues.StatusCode.Should().Be(HttpStatusCode.OK, textoDespues);
        var resumen = JsonDocument.Parse(textoDespues).RootElement;
        var corregida = Fila(resumen, 4);
        corregida.GetProperty("valida").GetBoolean().Should().BeTrue(textoDespues);
        corregida.GetProperty("identificador_norm").GetString().Should().Be("ABC12X");
        corregida.GetProperty("tipo_identificador").GetString().Should().Be("PLACA_MOTO");
        corregida.GetProperty("filas_duplicadas").EnumerateArray().Select(f => f.GetInt32()).Should().Equal(7);
        resumen.GetProperty("validos").GetInt32().Should().Be(1);
        resumen.GetProperty("duplicados").GetInt32().Should().Be(1);
        resumen.GetProperty("puede_continuar").GetBoolean().Should().BeTrue();
        textoDespues.Should().NotContain("1020304050", "el documento del locatario vuelve enmascarado");

        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0, "validar-filas no persiste");
        (await Cabeceras.LeerAsync(api, empresa.Id, id, ct)).EstimadoConsultas.Should().BeNull("validar-filas no toca la cabecera");
    }

    // La misma regla de la carga: longitud después de limpiar (nunca se recorta), documento y fila por defecto.
    [Fact]
    public async Task Validar_filas_aplica_la_regla_de_longitud_y_la_del_documento()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (cliente, id) = await Lotes.CrearAsync(api, DosEmpresas.En(api).A, new ConfiguracionEmpresa(), ct);

        var respuesta = await Lotes.ValidarAsync(cliente, id,
            [Lotes.Fila("AB1234567"), Lotes.Fila("9FBTEST12345678901"), Lotes.Fila("XYZ987", "CC", "12.345"), Lotes.Fila(null)], ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        var resumen = JsonDocument.Parse(texto).RootElement;
        Errores(Fila(resumen, 2)).Should().Equal("IDENTIFICADOR_DEMASIADO_LARGO");
        Fila(resumen, 2).GetProperty("identificador_norm").GetString().Should().Be("AB1234567", "no se recorta");
        Errores(Fila(resumen, 3)).Should().Equal("IDENTIFICADOR_DEMASIADO_LARGO");
        Errores(Fila(resumen, 4)).Should().Equal("DOCUMENTO_INVALIDO");
        Errores(Fila(resumen, 5)).Should().Equal("IDENTIFICADOR_REQUERIDO");
        resumen.GetProperty("validos").GetInt32().Should().Be(0);
        resumen.GetProperty("puede_continuar").GetBoolean().Should().BeFalse();
    }

    // Solo en CREADO (409); la cabecera de otra compañía responde 404 y no devuelve nada suyo (fuga entre compañías).
    [Fact]
    public async Task Validar_filas_fuera_de_CREADO_o_de_otra_compania_no_procede()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var (deA, idA) = await Lotes.CrearAsync(api, empresas.A, new ConfiguracionEmpresa(), ct);
        var (deB, _) = await Lotes.CrearAsync(api, empresas.B, new ConfiguracionEmpresa(), ct);
        var (_, enProceso) = await Lotes.CrearAsync(api, empresas.A, new ConfiguracionEmpresa(), ct, sembrar: false);
        await using (var db = empresas.A.Db())
        {
            await db.Set<CabeceraDiagnostico>().Where(c => c.Id == enProceso)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Estado, EstadoDiagnostico.EnProceso), ct);
        }

        var ajena = await Lotes.ValidarAsync(deB, idA, [Lotes.Fila("ABC123")], ct);
        var noCreado = await Lotes.ValidarAsync(deA, enProceso, [Lotes.Fila("ABC123")], ct);

        var textoAjena = await ajena.Content.ReadAsStringAsync(ct);
        ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, textoAjena);
        textoAjena.Should().NotContain("ABC123").And.NotContain(empresas.A.Id.ToString()).And.NotContain("Lote a confirmar");
        noCreado.StatusCode.Should().Be(HttpStatusCode.Conflict, await noCreado.Content.ReadAsStringAsync(ct));
        (await Codigo(noCreado, ct)).Should().Be(ServicioCabecera.CodigoNoEditable);
    }

    private static JsonElement Fila(JsonElement resumen, int numero) =>
        resumen.GetProperty("filas").EnumerateArray().Single(f => f.GetProperty("fila").GetInt32() == numero);

    private static List<string?> Errores(JsonElement fila) =>
        fila.GetProperty("errores").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();
}
