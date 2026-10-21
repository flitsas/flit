using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13750 (F01-H4), escenario 8 (F01-C9) — <c>PUT /diagnosticos/{id}</c> edita nombre, alcance y modo solo mientras la
/// cabecera está en CREADO, con las mismas reglas de la creación y contra la copia de configuración guardada. Fuera de CREADO
/// responde 409 y no cambia nada. Postgres real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EditarDiagnosticoTests
/// </code>
/// </example>
public sealed class EditarDiagnosticoTests
{
    // Escenario 8 — en CREADO la edición responde 200 y cambia nombre, alcance y modo (la copia de configuración no).
    [Fact]
    public async Task Edicion_en_CREADO_responde_200_y_cambia_nombre_alcance_y_modo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.SimitVehiculo, Fuente.SimitLocatario] }, ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT"])), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var antes = await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct);

        var respuesta = await cliente.PutAsync($"{Cabeceras.Ruta}/{id}",
            Cuerpo(Cabeceras.Json("Lote renombrado", "CON_ANALISIS", "TRASPASO_UNILATERAL", ["RUNT", "SIMIT_VEHICULO"], porLocatario: ["SIMIT_LOCATARIO"])), ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var json = JsonDocument.Parse(cuerpo).RootElement;
        json.GetProperty("id").GetGuid().Should().Be(id);
        json.GetProperty("nombre").GetString().Should().Be("Lote renombrado");
        json.GetProperty("modo").GetString().Should().Be("CON_ANALISIS");
        Codigos(json.GetProperty("alcance").GetProperty("por_locatario")).Should().Equal("SIMIT_LOCATARIO");
        var despues = await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct);
        despues.Nombre.Should().Be("Lote renombrado");
        despues.Modo.Should().Be(ModoDiagnostico.ConAnalisis);
        despues.TipoTramiteCodigo.Should().Be("TRASPASO_UNILATERAL");
        despues.Alcance.Should().Equal(Fuente.Runt, Fuente.SimitVehiculo, Fuente.SimitLocatario);
        despues.Estado.Should().Be(EstadoDiagnostico.Creado);
        despues.Configuracion.Should().Be(antes.Configuracion);
        despues.CreadoEn.Should().Be(antes.CreadoEn);
        despues.UsuarioCreadorId.Should().Be(antes.UsuarioCreadorId);

        // Volver a Simple descarta el tipo de trámite (ck_tipo_con_analisis solo lo exige en Con Análisis IA).
        var aSimple = await cliente.PutAsync($"{Cabeceras.Ruta}/{id}", Cuerpo(Cabeceras.Json("Lote renombrado", "SIMPLE", "TRASPASO_UNILATERAL", ["RUNT"])), ct);
        aSimple.StatusCode.Should().Be(HttpStatusCode.OK, await aSimple.Content.ReadAsStringAsync(ct));
        (await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct)).TipoTramiteCodigo.Should().BeNull();
    }

    // Escenario 8 — la edición valida igual que la creación: 422 y no cambia nada.
    [Fact]
    public async Task Edicion_invalida_responde_422_y_no_cambia_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT"])), ct);
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();

        var sinRunt = await cliente.PutAsync($"{Cabeceras.Ruta}/{id}", Cuerpo(Cabeceras.Json("Otro", "SIMPLE", null, ["SIMIT_VEHICULO"])), ct);
        var noHabilitada = await cliente.PutAsync($"{Cabeceras.Ruta}/{id}", Cuerpo(Cabeceras.Json("Otro", "SIMPLE", null, ["RUNT", "AVALUO"])), ct);

        sinRunt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Codigo(sinRunt, ct)).Should().Be(ValidadorCabecera.CodigoAlcanceSinRunt);
        noHabilitada.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "se valida contra la copia de la configuración guardada (solo RUNT)");
        (await Codigo(noHabilitada, ct)).Should().Be(ValidadorCabecera.CodigoFuenteNoHabilitada);
        (await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct)).Nombre.Should().Be("Lote");
    }

    // Escenario 8 (F01-C9) — con la cabecera en EN_PROCESO (sembrada) responde 409 y no modifica nada.
    [Fact]
    public async Task Edicion_fuera_de_CREADO_responde_409_y_no_modifica_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var sembrada = new CabeceraDiagnostico
        {
            Nombre = "En curso",
            Configuracion = new ConfiguracionEmpresa().ComoCopiaJson(),
            Estado = EstadoDiagnostico.EnProceso,
            UsuarioCreadorId = Guid.NewGuid(),
            CreadoEn = DateTimeOffset.UtcNow,
        };
        await using (var db = empresas.A.Db())
        {
            db.Add(sembrada);
            await db.SaveChangesAsync(ct);
        }

        var respuesta = await api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id))
            .PutAsync($"{Cabeceras.Ruta}/{sembrada.Id}", Cuerpo(Cabeceras.Json("Renombrado", "CON_ANALISIS", "TRASPASO_STANDARD", ["RUNT"])), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict, await respuesta.Content.ReadAsStringAsync(ct));
        (await Codigo(respuesta, ct)).Should().Be(ServicioCabecera.CodigoNoEditable);
        var despues = await Cabeceras.LeerAsync(api, empresas.A.Id, sembrada.Id, ct);
        despues.Nombre.Should().Be("En curso");
        despues.Modo.Should().Be(ModoDiagnostico.Simple);
        despues.TipoTramiteCodigo.Should().BeNull();
        despues.Estado.Should().Be(EstadoDiagnostico.EnProceso);
        despues.Alcance.Should().Equal(Fuente.Runt);
    }

    // Una cabecera que no existe responde 404.
    [Fact]
    public async Task Edicion_de_una_cabecera_inexistente_responde_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        var respuesta = await api.Cliente(Cabeceras.TokenUsuario(api, Guid.NewGuid()))
            .PutAsync($"{Cabeceras.Ruta}/{Guid.NewGuid()}", Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT"])), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

/// <summary>Copia JSON de una configuración en memoria, para sembrar cabeceras directamente en la base.</summary>
internal static class ConfiguracionEmpresaDePrueba
{
    public static string ComoCopiaJson(this ConfiguracionEmpresa configuracion) => CopiaConfiguracion.De(configuracion).ComoJson();
}
