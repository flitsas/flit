using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Configuracion;

/// <summary>
/// HU #13741 (F00.2-H2), escenarios 7 a 10 y 12 — <c>GET/PUT /backoffice/empresas/{empresa}/configuracion/contrato</c>:
/// el backoffice FLIT edita el nivel contractual de OTRA compañía (la de la ruta) por la vía explícita del módulo Backoffice
/// (ADR-0073), con cada cambio en <c>diagnostico.cambios_contrato</c>. Reducir las habilitadas recorta las por defecto en el
/// mismo comando; un límite no positivo o el impuesto sin C3 dan 422. Postgres real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ContratoBackofficeTests
/// </code>
/// </example>
public sealed class ContratoBackofficeTests
{
    // Escenario 8 — el backoffice FLIT cambia el límite de la empresa B y queda auditado (sin fila previa: anterior 500).
    [Fact]
    public async Task Backoffice_FLIT_edita_contrato_y_queda_auditado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { LimiteVehiculosLote = 700 }, ct);
        var flit = Guid.NewGuid();
        var token = TokenBackoffice(api, flit);
        var usuario = Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject);
        using var cliente = api.Cliente(token);

        var respuesta = await cliente.PutAsync(Url(empresas.B.Id), Cuerpo("""{ "limite_vehiculos_lote": 200 }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("empresa").GetGuid().Should().Be(empresas.B.Id);
        json.GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(200);
        var deB = await LeerAsync(api, empresas.B.Id, ct);
        deB.LimiteVehiculosLote.Should().Be(200);
        deB.ContratoActualizadoPor.Should().Be(usuario);
        deB.ContratoActualizadoEn.Should().NotBeNull();
        (await LeerAsync(api, empresas.A.Id, ct)).LimiteVehiculosLote.Should().Be(700, "solo cambia la empresa de la ruta");

        var cambios = await CambiosAsync(api, empresas.B.Id, ct);
        cambios.Should().ContainSingle();
        cambios[0].Campo.Should().Be("limite_vehiculos_lote");
        cambios[0].ValorAnterior.Should().Be("500");
        cambios[0].ValorNuevo.Should().Be("200");
        cambios[0].UsuarioId.Should().Be(usuario);
        cambios[0].TenantId.Should().Be(empresas.B.Id, "el cambio es de la compañía cuya configuración cambió");
        (await CambiosAsync(api, flit, ct)).Should().BeEmpty();

        // La lectura del contrato por el backoffice devuelve lo guardado y queda en lecturas_backoffice.
        var lectura = await cliente.GetAsync(Url(empresas.B.Id), ct);
        lectura.StatusCode.Should().Be(HttpStatusCode.OK, await lectura.Content.ReadAsStringAsync(ct));
        var contrato = await lectura.Content.ReadFromJsonAsync<JsonElement>(ct);
        contrato.GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(200);
        contrato.GetProperty("redaccion_ia_suspendida").GetBoolean().Should().BeFalse();
        await using var deFlit = DosEmpresas.Contexto(api.BaseDeDatos, flit);
        (await deFlit.Set<LecturaBackoffice>().AsNoTracking().ToListAsync(ct))
            .Should().ContainSingle(l => l.EmpresaConsultada == empresas.B.Id && l.UsuarioId == usuario, "el PUT no se audita como lectura; el GET sí");

        // El interruptor de emergencia también se edita aquí y queda auditado.
        var suspension = await cliente.PutAsync(Url(empresas.B.Id), Cuerpo("""{ "redaccion_ia_suspendida": true }"""), ct);
        suspension.StatusCode.Should().Be(HttpStatusCode.OK, await suspension.Content.ReadAsStringAsync(ct));
        (await LeerAsync(api, empresas.B.Id, ct)).RedaccionIaSuspendida.Should().BeTrue();
        (await CambiosAsync(api, empresas.B.Id, ct)).Should().Contain(c => c.Campo == "redaccion_ia_suspendida" && c.ValorAnterior == "false" && c.ValorNuevo == "true");
    }

    // Escenario 7 — reducir las habilitadas recorta las por defecto en el mismo comando.
    [Fact]
    public async Task Reducir_habilitadas_recorta_por_defecto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa
        {
            FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo],
            FuentesPorDefecto = [Fuente.Runt, Fuente.Avaluo],
        }, ct);

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Url(empresas.B.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT"] }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        Codigos(json.GetProperty("fuentes_por_defecto")).Should().Equal("RUNT");
        var deB = await LeerAsync(api, empresas.B.Id, ct);
        deB.FuentesHabilitadas.Should().Equal(Fuente.Runt);
        deB.FuentesPorDefecto.Should().Equal(Fuente.Runt);
        var cambios = await CambiosAsync(api, empresas.B.Id, ct);
        cambios.Should().Contain(c => c.Campo == "fuentes_habilitadas" && c.ValorNuevo == "[\"RUNT\"]");
        cambios.Should().Contain(c => c.Campo == "fuentes_por_defecto" && c.ValorAnterior == "[\"RUNT\", \"AVALUO\"]" && c.ValorNuevo == "[\"RUNT\"]",
            "el recorte de las por defecto también queda en el historial");
    }

    // Escenario 9 — límite no positivo: 422, sin guardar ni auditar.
    [Fact]
    public async Task Limite_cero_responde_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);
        using var cliente = api.Cliente(TokenBackoffice(api, Guid.NewGuid()));

        foreach (var cuerpo in new[] { """{ "limite_vehiculos_lote": 0 }""", """{ "limite_vehiculos_lote": -5 }""", """{ "limite_vehiculos_lote": "diez" }""" })
        {
            var respuesta = await cliente.PutAsync(Url(empresas.B.Id), Cuerpo(cuerpo), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, cuerpo);
            (await Codigo(respuesta, ct)).Should().Be("LIMITE_INVALIDO", cuerpo);
        }

        (await LeerAsync(api, empresas.B.Id, ct)).LimiteVehiculosLote.Should().Be(500);
        (await CambiosAsync(api, empresas.B.Id, ct)).Should().BeEmpty();
    }

    // Escenario 12 — IMPUESTO_DEPARTAMENTAL con Diagnostico:Fuentes:ImpuestoDisponible=false (por defecto): 422, sin guardar.
    [Fact]
    public async Task Habilitar_impuesto_sin_C3_responde_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Url(empresas.B.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT", "IMPUESTO_DEPARTAMENTAL"], "limite_vehiculos_lote": 100 }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Codigo(respuesta, ct)).Should().Be("FUENTE_NO_DISPONIBLE");
        var deB = await LeerAsync(api, empresas.B.Id, ct);
        deB.FuentesHabilitadas.Should().Equal(Fuente.Runt);
        deB.LimiteVehiculosLote.Should().Be(500, "un 422 no guarda ningún campo");
        (await CambiosAsync(api, empresas.B.Id, ct)).Should().BeEmpty();
        new DisponibilidadFuentes(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .Disponible(Fuente.ImpuestoDepartamental).Should().BeFalse("sin la bandera, el impuesto no está disponible");
    }

    // Escenario 10 — backoffice_diagnostico en una compañía que no es FLIT: 403 en la lectura y en la escritura.
    [Fact]
    public async Task Backoffice_en_compania_no_FLIT_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);

        foreach (var tipo in new[] { "RENTING", "CONCESIONARIO", null })
        {
            var token = api.Token(permisos: [Permisos.BackofficeVer, Permisos.ConfiguracionContratoEditar], roles: ["backoffice_diagnostico"], tipoEmpresa: tipo);
            using var cliente = api.Cliente(token);
            (await cliente.GetAsync(Url(empresas.B.Id), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"GET con tenant_type={tipo}");
            (await cliente.PutAsync(Url(empresas.B.Id), Cuerpo("""{ "limite_vehiculos_lote": 200 }"""), ct))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"PUT con tenant_type={tipo}");
        }

        (await LeerAsync(api, empresas.B.Id, ct)).LimiteVehiculosLote.Should().Be(500);
        (await CambiosAsync(api, empresas.B.Id, ct)).Should().BeEmpty();
    }

    // Escenario 10 — compañía FLIT sin diagnostico.configuracion.contrato.editar (o un admin de la compañía): 403.
    [Fact]
    public async Task Sin_permiso_contrato_responde_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);
        var sinContrato = api.Token(permisos: [Permisos.BackofficeVer, Permisos.ConfiguracionVer], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");
        var adminDeB = api.Token(empresa: empresas.B.Id, permisos: [Permisos.ConfiguracionVer, Permisos.ConfiguracionEditar], roles: ["admin_diagnostico"]);

        foreach (var token in new[] { sinContrato, adminDeB })
        {
            using var cliente = api.Cliente(token);
            (await cliente.GetAsync(Url(empresas.B.Id), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await cliente.PutAsync(Url(empresas.B.Id), Cuerpo("""{ "limite_vehiculos_lote": 200 }"""), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await LeerAsync(api, empresas.B.Id, ct)).LimiteVehiculosLote.Should().Be(500);
        (await CambiosAsync(api, empresas.B.Id, ct)).Should().BeEmpty();
    }

    // La empresa de la escritura sale de la ruta (empresa explícita): si ?empresa= no coincide, 400 y no se escribe.
    [Fact]
    public async Task Empresa_de_la_ruta_y_de_la_consulta_distintas_responde_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync($"{Url(empresas.B.Id)}?empresa={empresas.A.Id}", Cuerpo("""{ "limite_vehiculos_lote": 200 }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Codigo(respuesta, ct)).Should().Be(EmpresaExplicita.CodigoRequerida);
        (await LeerAsync(api, empresas.B.Id, ct)).LimiteVehiculosLote.Should().Be(500);
    }

    internal static string Url(Guid empresa) =>
        Program.Prefijo + BackofficeEndpoints.Grupo + ContratoBackofficeEndpoints.Ruta.Replace("{empresa}", empresa.ToString(), StringComparison.Ordinal);

    internal static string TokenBackoffice(ApiDePrueba api, Guid flit) =>
        api.Token(empresa: flit, permisos: [Permisos.BackofficeVer, Permisos.ConfiguracionContratoEditar], roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT");

    private static async Task<List<CambioContrato>> CambiosAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        return await db.Set<CambioContrato>().AsNoTracking().OrderBy(c => c.Fecha).ThenBy(c => c.Id).ToListAsync(ct);
    }
}
