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
using Flit.Diagnostico.Tests.Catalogos;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    // HU #13863 (F09-H4), AC1 (F09-C7) — bandera en true y adaptador conectado, pero la réplica de departamentos con impuesto
    // vacía (sin C3): habilitar IMPUESTO_DEPARTAMENTAL responde 422 FUENTE_NO_DISPONIBLE y no guarda ningún campo.
    [Fact]
    public async Task Habilitar_impuesto_con_bandera_y_replica_vacia_responde_422_FUENTE_NO_DISPONIBLE_sin_guardar()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ConBanderaImpuestoAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Url(empresas.B.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT", "IMPUESTO_DEPARTAMENTAL"], "limite_vehiculos_lote": 100 }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await respuesta.Content.ReadAsStringAsync(ct));
        (await Codigo(respuesta, ct)).Should().Be(ServicioConfiguracion.CodigoFuenteNoDisponible);
        var deB = await LeerAsync(api, empresas.B.Id, ct);
        deB.FuentesHabilitadas.Should().Equal(Fuente.Runt);
        deB.LimiteVehiculosLote.Should().Be(500, "un 422 no guarda ningún campo");
        (await CambiosAsync(api, empresas.B.Id, ct)).Should().BeEmpty();
    }

    // HU #13863, AC3 (F09-C7) — bandera en true, adaptador conectado y 05 soportado en la réplica: se guarda, queda el cambio en
    // cambios_contrato, la respuesta ya lo trae en fuentes_disponibles y la compañía puede pedirlo en un diagnóstico.
    [Fact]
    public async Task Habilitar_impuesto_con_las_condiciones_cumplidas_guarda_y_deja_el_cambio_auditado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ConBanderaImpuestoAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        (await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("05"), new RelojManual(), ct))!.Soportados.Should().Be(1);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa(), ct);

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Url(empresas.B.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT", "IMPUESTO_DEPARTAMENTAL"] }"""), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        Codigos(JsonDocument.Parse(texto).RootElement.GetProperty("fuentes_disponibles")).Should().Equal([.. SinImpuesto, "IMPUESTO_DEPARTAMENTAL"]);
        (await LeerAsync(api, empresas.B.Id, ct)).FuentesHabilitadas.Should().Equal(Fuente.Runt, Fuente.ImpuestoDepartamental);
        var cambio = (await CambiosAsync(api, empresas.B.Id, ct)).Should().ContainSingle().Subject;
        cambio.Campo.Should().Be(ServicioConfiguracion.CampoFuentesHabilitadas);
        // valor_anterior y valor_nuevo son jsonb: se comparan como JSON (Postgres normaliza los espacios).
        JsonSerializer.Deserialize<string[]>(cambio.ValorAnterior!).Should().Equal("RUNT");
        JsonSerializer.Deserialize<string[]>(cambio.ValorNuevo).Should().Equal("RUNT", "IMPUESTO_DEPARTAMENTAL");

        var creacion = await api.Cliente(Cabeceras.TokenUsuario(api, empresas.B.Id))
            .PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote con impuesto", "SIMPLE", null, ["RUNT", "IMPUESTO_DEPARTAMENTAL"])), ct);
        creacion.StatusCode.Should().Be(HttpStatusCode.Created, await creacion.Content.ReadAsStringAsync(ct));
    }

    // HU #13863, AC4 (F09-C8) — el GET y el PUT del contrato traen fuentes_disponibles en el nivel superior, en el orden del
    // enum y con la regla del 422: con la bandera en false (por defecto) el impuesto no está, aunque todas tengan adaptador.
    [Fact]
    public async Task GET_y_PUT_del_contrato_traen_fuentes_disponibles()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo] }, ct);
        using var cliente = api.Cliente(TokenBackoffice(api, Guid.NewGuid()));

        var lectura = await cliente.GetAsync(Url(empresas.B.Id), ct);
        var edicion = await cliente.PutAsync(Url(empresas.B.Id), Cuerpo("""{ "limite_vehiculos_lote": 200 }"""), ct);

        foreach (var respuesta in new[] { lectura, edicion })
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, texto);
            var json = JsonDocument.Parse(texto).RootElement;
            Codigos(json.GetProperty("fuentes_disponibles")).Should().Equal(SinImpuesto, texto);
            Codigos(json.GetProperty("fuentes_habilitadas")).Should().Equal("RUNT", "AVALUO");
        }
    }

    /// <summary>Todas las fuentes salvo el impuesto, en el orden del enum: las disponibles en <see cref="ApiDePrueba"/> sin C3.</summary>
    internal static readonly string[] SinImpuesto =
        ["RUNT", "RUNT_PROPIETARIO", "RUNT_LOCATARIO", "SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO", "AVALUO"];

    /// <summary>API de prueba (todas las fuentes con adaptador conectado) con la bandera del impuesto encendida y la réplica vacía.</summary>
    internal static Task<ApiDePrueba> ConBanderaImpuestoAsync() =>
        ApiDePrueba.ConBaseAsync(configurar: b => b.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [DisponibilidadFuentes.ClaveImpuestoDisponible] = "true" }));

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
