using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Catalogos;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Diagnostico.Tests.Dominio;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13750 (F01-H4), escenarios 1 a 7 y 9 — <c>POST /diagnosticos</c>: crea la cabecera en CREADO con la copia inmutable
/// del alcance y de la configuración vigente de la compañía (y cierra el criterio 9 de F00.2-H2 sobre diagnósticos ya
/// creados), con la fila inicial del historial. 422 con el campo por cada regla, 403 al backoffice (R-6), 503 con
/// Con Análisis IA y el catálogo caído, y nada cruza entre compañías. Postgres real (base efímera de <see cref="ApiDePrueba"/>).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~CrearDiagnosticoTests
/// </code>
/// </example>
public sealed class CrearDiagnosticoTests
{
    // Escenario 7 (F01-C8) — 201 en CREADO con copia de alcance y configuración, y el historial inicial (anterior nulo, AUTO).
    [Fact]
    public async Task Crea_la_cabecera_en_CREADO_con_copia_de_configuracion_e_historial_inicial()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa
        {
            FuentesHabilitadas = [Fuente.Runt, Fuente.SimitVehiculo, Fuente.RuntPropietario],
            FuentesPorDefecto = [Fuente.Runt],
            LimiteVehiculosLote = 300,
        }, ct);

        var respuesta = await api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id)).PostAsync(Cabeceras.Ruta,
            Cuerpo(Cabeceras.Json("  Flota norte  ", "CON_ANALISIS", "TRASPASO_STANDARD", ["RUNT", "SIMIT_VEHICULO"], ["RUNT_PROPIETARIO"])), ct);

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, cuerpo);
        var json = JsonDocument.Parse(cuerpo).RootElement;
        var id = json.GetProperty("id").GetGuid();
        json.GetProperty("nombre").GetString().Should().Be("Flota norte");
        json.GetProperty("estado").GetString().Should().Be("CREADO");
        json.GetProperty("modo").GetString().Should().Be("CON_ANALISIS");
        json.GetProperty("tipo_tramite_codigo").GetString().Should().Be("TRASPASO_STANDARD");
        Codigos(json.GetProperty("alcance").GetProperty("por_vehiculo")).Should().Equal("RUNT", "SIMIT_VEHICULO");
        Codigos(json.GetProperty("alcance").GetProperty("por_propietario")).Should().Equal("RUNT_PROPIETARIO");
        Codigos(json.GetProperty("alcance").GetProperty("por_locatario")).Should().BeEmpty();
        json.GetProperty("configuracion").GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(300);
        cuerpo.Should().NotContainEquivalentOf("redaccion");

        var cabecera = await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct);
        cabecera.Estado.Should().Be(EstadoDiagnostico.Creado);
        cabecera.TenantId.Should().Be(empresas.A.Id);
        cabecera.Alcance.Should().Equal(Fuente.Runt, Fuente.SimitVehiculo, Fuente.RuntPropietario);
        cabecera.EstimadoConsultas.Should().BeNull("el estimado se guarda al confirmar los vehículos (F02-H5)");
        cabecera.UsuarioCreadorId.Should().NotBeEmpty();
        JsonDocument.Parse(cabecera.Configuracion).RootElement.GetProperty("fuentes_habilitadas").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("RUNT", "SIMIT_VEHICULO", "RUNT_PROPIETARIO");

        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresas.A.Id);
        var historial = await db.Set<HistorialEstadoDiagnostico>().AsNoTracking().Where(h => h.DiagnosticoId == id).ToListAsync(ct);
        historial.Should().ContainSingle();
        historial[0].EstadoAnterior.Should().BeNull();
        historial[0].EstadoNuevo.Should().Be(EstadoDiagnostico.Creado);
        historial[0].Disparador.Should().Be(DisparadorEstado.Auto);
        historial[0].UsuarioId.Should().Be(cabecera.UsuarioCreadorId);
    }

    // Escenario 7 (F01-C8) y criterio 9 de F00.2-H2 (F00.2-C9) — cambiar después la configuración no altera la copia guardada.
    [Fact]
    public async Task Cambiar_la_configuracion_despues_no_altera_la_copia_de_la_cabecera()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo], FuentesPorDefecto = [Fuente.Runt, Fuente.Avaluo] }, ct);
        var creada = await api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id)).PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT", "AVALUO"])), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var antes = (await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct)).Configuracion;

        var edicion = await api.Cliente(TokenAdmin(api, empresas.A.Id)).PutAsync(Program.Prefijo + ConfiguracionEndpoints.Ruta,
            Cuerpo("""{ "visibilidad_usuario": "PROPIOS", "fuentes_por_defecto": ["RUNT"], "estimado_consultas": "ANTES_DE_LANZAR" }"""), ct);
        edicion.StatusCode.Should().Be(HttpStatusCode.OK, await edicion.Content.ReadAsStringAsync(ct));

        var despues = await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct);
        despues.Configuracion.Should().Be(antes, "la cabecera guarda una copia, no una referencia a la configuración");
        var copia = JsonDocument.Parse(despues.Configuracion).RootElement;
        copia.GetProperty("visibilidad_usuario").GetString().Should().Be("EMPRESA");
        copia.GetProperty("estimado_consultas").GetString().Should().Be("NO_MOSTRAR");
        Codigos(copia.GetProperty("fuentes_por_defecto")).Should().Equal("RUNT", "AVALUO");
        despues.Alcance.Should().Equal(Fuente.Runt, Fuente.Avaluo);
    }

    // HU #13863 (F09-H4), AC5 (F09-C7) — la compañía tiene el impuesto habilitado y la réplica se queda sin departamentos
    // soportados: crear un diagnóstico con el impuesto en el alcance responde 422 FUENTE_NO_DISPONIBLE con campo=alcance, el
    // impuesto sale de fuentes_disponibles (sigue en fuentes_habilitadas) y quitarlo del contrato sigue permitido.
    [Fact]
    public async Task Impuesto_habilitado_que_deja_de_estar_disponible_responde_422_en_alcance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await Configuracion.ContratoBackofficeTests.ConBanderaImpuestoAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.ImpuestoDepartamental] }, ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));
        var conImpuesto = Cabeceras.Json("Lote con impuesto", "SIMPLE", null, ["RUNT", "IMPUESTO_DEPARTAMENTAL"]);
        await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa("05"), new RelojManual(), ct);
        (await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(conImpuesto), ct)).StatusCode.Should().Be(HttpStatusCode.Created, "con 05 soportado sí se puede pedir");

        (await ReplicaImpuesto.SincronizarAsync(api, new CapacidadFalsa(), new RelojManual(), ct))!.Soportados.Should().Be(0);
        var respuesta = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(conImpuesto), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, texto);
        var json = JsonDocument.Parse(texto).RootElement;
        json.GetProperty("code").GetString().Should().Be(ValidadorCabecera.CodigoFuenteNoDisponible);
        json.GetProperty("campo").GetString().Should().Be("alcance");
        (await Cabeceras.ContarAsync(api, empresas.A.Id, ct)).Should().Be(1, "el 422 no guarda la segunda cabecera");
        var configuracion = JsonDocument.Parse(await api.Cliente(TokenVer(api, empresas.A.Id)).GetStringAsync(Program.Prefijo + ConfiguracionEndpoints.Ruta, ct)).RootElement;
        Codigos(configuracion.GetProperty("fuentes_disponibles")).Should().NotContain("IMPUESTO_DEPARTAMENTAL");
        Codigos(configuracion.GetProperty("contrato").GetProperty("fuentes_habilitadas")).Should().Equal("RUNT", "IMPUESTO_DEPARTAMENTAL");
        var quitar = await api.Cliente(Configuracion.ContratoBackofficeTests.TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Configuracion.ContratoBackofficeTests.Url(empresas.A.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT"] }"""), ct);
        quitar.StatusCode.Should().Be(HttpStatusCode.OK, "quitar una fuente que dejó de estar disponible sigue permitido");
    }

    // Escenarios 1 a 5 (F01-C1..C5) y la decisión de la ronda (fuente sin adaptador) — 422 con el campo y nada guardado.
    [Fact]
    public async Task Responde_422_por_cada_regla_y_no_guarda_la_cabecera()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        // Habilitada en la base aunque el backoffice ya no podría habilitarla: la creación vuelve a mirar la disponibilidad.
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.RuntLocatario, Fuente.ImpuestoDepartamental] }, ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));

        (string Cuerpo, string Codigo, string Campo)[] casos =
        [
            (Cabeceras.Json(string.Empty, "SIMPLE", null, ["RUNT"]), ValidadorCabecera.CodigoNombreInvalido, "nombre"),
            (Cabeceras.Json(new string('x', 151), "SIMPLE", null, ["RUNT"]), ValidadorCabecera.CodigoNombreInvalido, "nombre"),
            ("""{ "modo": "SIMPLE", "alcance": { "por_vehiculo": ["RUNT"] } }""", ValidadorCabecera.CodigoNombreInvalido, "nombre"),
            (Cabeceras.Json("Lote", "SIMPLE", null, []), ValidadorCabecera.CodigoAlcanceSinRunt, "alcance"),
            (Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT", "SIMIT_VEHICULO"]), ValidadorCabecera.CodigoFuenteNoHabilitada, "alcance"),
            (Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT", "RUNT_LOCATARIO"]), ValidadorCabecera.CodigoFuenteEnGrupoAjeno, "alcance.por_vehiculo"),
            (Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT", "IMPUESTO_DEPARTAMENTAL"]), ValidadorCabecera.CodigoFuenteNoDisponible, "alcance"),
            (Cabeceras.Json("Lote", "CON_ANALISIS", null, ["RUNT"]), ValidadorCabecera.CodigoTipoTramiteRequerido, "tipo_tramite_codigo"),
            (Cabeceras.Json("Lote", "CON_ANALISIS", "MATRICULA_INICIAL", ["RUNT"]), ValidadorCabecera.CodigoTipoTramiteNoDisponible, "tipo_tramite_codigo"),
            (Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT", "OTRA"]), ServicioConfiguracion.CodigoFueraDeConjunto, "alcance.por_vehiculo"),
            (Cabeceras.Json("Lote", "PREMIUM", null, ["RUNT"]), ServicioConfiguracion.CodigoFueraDeConjunto, "modo"),
        ];
        foreach (var (cuerpo, codigo, campo) in casos)
        {
            var respuesta = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(cuerpo), ct);

            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, $"{cuerpo} → {texto}");
            var json = JsonDocument.Parse(texto).RootElement;
            json.GetProperty("code").GetString().Should().Be(codigo, cuerpo);
            json.GetProperty("campo").GetString().Should().Be(campo, cuerpo);
            json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        }

        (await Cabeceras.ContarAsync(api, empresas.A.Id, ct)).Should().Be(0, "ninguna petición inválida guarda la cabecera");
    }

    // Escenario 6 (F01-C7) — con un doble del catálogo cuyo tipo exige SIMIT_VEHICULO, el alcance sin ella da 422.
    [Fact]
    public async Task Fuentes_obligatorias_faltantes_del_tipo_responden_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ValidadorCabeceraTests.CatalogoDoble(new TipoTramite("TRASPASO_STANDARD", "TRASPASO", "TRASPASO", [Fuente.SimitVehiculo]));
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<ICatalogoTiposTramite>(doble));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.SimitVehiculo] }, ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));

        var sin = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "CON_ANALISIS", "TRASPASO_STANDARD", ["RUNT"])), ct);
        var con = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "CON_ANALISIS", "TRASPASO_STANDARD", ["RUNT", "SIMIT_VEHICULO"])), ct);

        sin.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await sin.Content.ReadAsStringAsync(ct));
        (await Codigo(sin, ct)).Should().Be(ValidadorCabecera.CodigoFuentesObligatoriasFaltantes);
        con.StatusCode.Should().Be(HttpStatusCode.Created, await con.Content.ReadAsStringAsync(ct));
        (await Cabeceras.ContarAsync(api, empresas.A.Id, ct)).Should().Be(1);
    }

    // Escenario 9 (F01-C12, R-6) — el backoffice FLIT no tiene diagnostico.diagnosticos.crear: 403.
    [Fact]
    public async Task Backoffice_no_crea_diagnosticos()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var backoffice = Permisos.RolesPorDefecto.Single(r => r.Codigo == "backoffice_diagnostico").Permisos;

        var respuesta = await api.Cliente(api.Token(permisos: backoffice, roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT"))
            .PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT"])), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        backoffice.Should().NotContain(Permisos.DiagnosticosCrear);
    }

    // Conducta degradada de F01-H2 — Con Análisis IA y el catálogo caído: 503 con Retry-After, sin guardar.
    [Fact]
    public async Task Con_analisis_con_el_catalogo_caido_responde_503_y_no_guarda()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        api.Catalogos.FallaSiempre = StatusCode.Unavailable;
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));

        var conAnalisis = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "CON_ANALISIS", "TRASPASO_STANDARD", ["RUNT"])), ct);
        var simple = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote", "SIMPLE", null, ["RUNT"])), ct);

        conAnalisis.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await conAnalisis.Content.ReadAsStringAsync(ct));
        conAnalisis.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(30));
        (await Codigo(conAnalisis, ct)).Should().Be(ReglaException.CodigoCatalogoNoDisponible);
        simple.StatusCode.Should().Be(HttpStatusCode.Created, "el modo Simple no depende del catálogo");
        (await Cabeceras.ContarAsync(api, empresas.A.Id, ct)).Should().Be(1, "solo se guardó la Simple");
    }

    // Filtro por compañía (#13715) — la copia sale de la configuración de A, no de B; B no edita ni ve la cabecera de A.
    [Fact]
    public async Task Nada_cruza_entre_dos_companias()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt], LimiteVehiculosLote = 111 }, ct);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo], LimiteVehiculosLote = 222 }, ct);
        using var deA = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));
        using var deB = api.Cliente(Cabeceras.TokenUsuario(api, empresas.B.Id));

        var conFuenteDeB = await deA.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote A", "SIMPLE", null, ["RUNT", "AVALUO"])), ct);
        var creada = await deA.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote A", "SIMPLE", null, ["RUNT"])), ct);
        var id = (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        var edicionDeB = await deB.PutAsync($"{Cabeceras.Ruta}/{id}", Cuerpo(Cabeceras.Json("Lote de B", "SIMPLE", null, ["RUNT"])), ct);

        conFuenteDeB.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "AVALUO está habilitada en B, no en A");
        creada.StatusCode.Should().Be(HttpStatusCode.Created);
        var cabecera = await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct);
        JsonDocument.Parse(cabecera.Configuracion).RootElement.GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(111);
        var textoDeB = await edicionDeB.Content.ReadAsStringAsync(ct);
        edicionDeB.StatusCode.Should().Be(HttpStatusCode.NotFound, textoDeB);
        textoDeB.Should().NotContain("Lote A").And.NotContain(empresas.A.Id.ToString());
        (await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct)).Nombre.Should().Be("Lote A");
        (await Cabeceras.ContarAsync(api, empresas.B.Id, ct)).Should().Be(0);
    }
}

/// <summary>Ayudas de las pruebas de la cabecera (HU #13750): ruta, token del usuario, cuerpo y lectura directa de la base.</summary>
internal static class Cabeceras
{
    public const string Ruta = Program.Prefijo + DiagnosticosEndpoints.Ruta;

    public static string TokenUsuario(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosCrear, Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);

    public static string Json(string nombre, string modo, string? tipo, string[] porVehiculo, string[]? porPropietario = null, string[]? porLocatario = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["nombre"] = nombre,
            ["modo"] = modo,
            ["tipo_tramite_codigo"] = tipo,
            ["alcance"] = new Dictionary<string, string[]>
            {
                ["por_vehiculo"] = porVehiculo,
                ["por_propietario"] = porPropietario ?? [],
                ["por_locatario"] = porLocatario ?? [],
            },
        });

    public static async Task<CabeceraDiagnostico> LeerAsync(ApiDePrueba api, Guid empresa, Guid id, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        return await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(c => c.Id == id, ct);
    }

    public static async Task<int> ContarAsync(ApiDePrueba api, Guid empresa, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        return await db.Set<CabeceraDiagnostico>().CountAsync(ct);
    }
}
