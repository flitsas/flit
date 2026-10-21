using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// HU #13753 (F02-H6), escenarios 1 a 5 — <c>POST /express</c> y <c>POST /express/estimado</c>: el Express crea en una sola
/// transacción la cabecera (<c>es_express=true</c>, reglas de F01-H4) y confirma un vehículo con la misma regla de fila de la
/// carga (F02-H5); nombre autogenerado con el identificador y la fecha si llega vacío; un identificador inválido responde
/// 422 sin cabecera huérfana; el estimado de vista previa solo con <c>ANTES_DE_LANZAR</c> y sin persistir; el documento del
/// locatario cifrado con origen DECLARADO_CLIENTE. 403 al backoffice y nada cruza entre compañías. Postgres real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ExpressEndpointsTests
/// </code>
/// </example>
public sealed class ExpressEndpointsTests
{
    private const string Ruta = Program.Prefijo + "/express";
    private const string RutaEstimado = Program.Prefijo + "/express/estimado";

    // Escenario 1 (F02-C12) — 201 con el id de una cabecera es_express en CREADO con un único vehículo, entregado al encolador.
    [Fact]
    public async Task Express_persiste_un_diagnostico_de_un_vehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa(), ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));

        var respuesta = await cliente.PostAsync(Ruta, Cuerpo(Express("ABC123", nombre: "Revisión flota")), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        var json = JsonDocument.Parse(texto).RootElement;
        var id = json.GetProperty("id").GetGuid();
        json.GetProperty("es_express").GetBoolean().Should().BeTrue();
        json.GetProperty("estado").GetString().Should().Be("CREADO");
        json.GetProperty("vehiculo").GetProperty("identificador_norm").GetString().Should().Be("ABC123");
        var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, id, ct);
        cabecera.EsExpress.Should().BeTrue();
        cabecera.Estado.Should().Be(EstadoDiagnostico.Creado);
        cabecera.Nombre.Should().Be("Revisión flota");
        cabecera.TenantId.Should().Be(empresa.Id);
        cabecera.EstimadoConsultas.Should().NotBeNull("la confirmación del vehículo deja el estimado en la cabecera");
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        vehiculos.Should().ContainSingle();
        vehiculos[0].IdentificadorNorm.Should().Be("ABC123");
        vehiculos[0].TipoIdentificador.Should().Be(TipoIdentificador.PlacaCarro);
        vehiculos[0].Orden.Should().Be(1);
        vehiculos[0].LocatarioDocCifrado.Should().BeNull();
        espia.Entregas.Should().ContainSingle();
        espia.Entregas[0].DiagnosticoId.Should().Be(id);
        espia.Entregas[0].EnTransaccion.Should().BeTrue();
        espia.Entregas[0].Vehiculos.Select(v => v.IdentificadorNorm).Should().Equal("ABC123");
        await using var db = empresa.Db();
        (await db.Set<HistorialEstadoDiagnostico>().CountAsync(h => h.DiagnosticoId == id, ct)).Should().Be(1, "la cabecera nace con su historial inicial");
        (await Cabeceras.ContarAsync(api, empresa.Id, ct)).Should().Be(1);
    }

    // Escenario 2 (F01-C10) — sin nombre (o solo espacios) se compone «Express <identificador> <dd/mm/aaaa>» (hora de Colombia);
    // un nombre enviado se valida como en F01 (máximo 150).
    [Fact]
    public async Task Sin_nombre_se_autogenera_con_el_identificador_y_la_fecha()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa(), ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));

        var sinNombre = await cliente.PostAsync(Ruta, Cuerpo(Express(" abc-123 ")), ct);
        var conEspacios = await cliente.PostAsync(Ruta, Cuerpo(Express("9FBTEST1234567890", nombre: "   ")), ct);
        var largo = await cliente.PostAsync(Ruta, Cuerpo(Express("XYZ987", nombre: new string('x', 151))), ct);

        foreach (var (respuesta, identificador) in new[] { (sinNombre, "ABC123"), (conEspacios, "9FBTEST1234567890") })
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Created, texto);
            var json = JsonDocument.Parse(texto).RootElement;
            var cabecera = await Cabeceras.LeerAsync(api, empresa.Id, json.GetProperty("id").GetGuid(), ct);
            var fecha = cabecera.CreadoEn.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            cabecera.Nombre.Should().Be($"Express {identificador} {fecha}");
            json.GetProperty("nombre").GetString().Should().Be(cabecera.Nombre);
        }

        var textoLargo = await largo.Content.ReadAsStringAsync(ct);
        largo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, textoLargo);
        var error = JsonDocument.Parse(textoLargo).RootElement;
        error.GetProperty("code").GetString().Should().Be(ValidadorCabecera.CodigoNombreInvalido);
        error.GetProperty("campo").GetString().Should().Be("nombre");
        (await Cabeceras.ContarAsync(api, empresa.Id, ct)).Should().Be(2, "el nombre de 151 caracteres no crea nada");
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(2);
    }

    // Escenario 3 (F02-C4) — identificador inválido, demasiado largo, vacío o documento mal formado: 422 con el campo y sin
    // cabecera ni vehículo; si el encolador falla, tampoco queda la cabecera (una sola transacción).
    [Fact]
    public async Task Identificador_invalido_responde_422_y_no_crea_cabecera_ni_vehiculo()
    {
        var ct = TestContext.Current.CancellationToken;
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa(), ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));

        (string Cuerpo, string Codigo, string Campo)[] casos =
        [
            (Express("ABC-1"), ValidadorFilaVehiculo.CodigoIdentificadorInvalido, "identificador"),
            (Express("AB1234567"), ValidadorFilaVehiculo.CodigoIdentificadorDemasiadoLargo, "identificador"),
            (Express("9FBTEST12345678901"), ValidadorFilaVehiculo.CodigoIdentificadorDemasiadoLargo, "identificador"),
            (Express("   "), ValidadorFilaVehiculo.CodigoIdentificadorRequerido, "identificador"),
            (Express(null), ValidadorFilaVehiculo.CodigoIdentificadorRequerido, "identificador"),
            (Express("ABC123", tipo: "CC", documento: "12AB34"), ValidadorDocumentoPersona.CodigoNumeroInvalido, "documento_locatario"),
            (Express("ABC123", tipo: "XX", documento: "123456"), ValidadorDocumentoPersona.CodigoTipoInvalido, "tipo_documento_locatario"),
        ];
        foreach (var (cuerpo, codigo, campo) in casos)
        {
            var respuesta = await cliente.PostAsync(Ruta, Cuerpo(cuerpo), ct);

            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, $"{cuerpo} → {texto}");
            var json = JsonDocument.Parse(texto).RootElement;
            json.GetProperty("code").GetString().Should().Be(codigo, cuerpo);
            json.GetProperty("campo").GetString().Should().Be(campo, cuerpo);
            json.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
        }

        espia.Fallar = true;
        var conFalla = await cliente.PostAsync(Ruta, Cuerpo(Express("ABC123")), ct);
        conFalla.IsSuccessStatusCode.Should().BeFalse();
        espia.Entregas.Should().ContainSingle("solo el último express llegó al encolador");

        (await TodasLasCabecerasAsync(api, ct)).Should().Be(0, "ni los inválidos ni el que falló dejan una cabecera huérfana");
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0);
    }

    // Escenario 4 (F01-C11) — con ANTES_DE_LANZAR el panel recibe el estimado por fuente y no se persiste nada; con NO_MOSTRAR
    // no hay estimado. La configuración es la de la compañía del token.
    [Fact]
    public async Task Estimado_de_vista_previa_solo_con_ANTES_DE_LANZAR_y_sin_persistir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa
        {
            FuentesHabilitadas = [Fuente.Runt, Fuente.RuntLocatario],
            EstimadoConsultas = EstimadoConsultas.AntesDeLanzar,
        }, ct);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.RuntLocatario] }, ct);
        using var deA = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));
        using var deB = api.Cliente(Cabeceras.TokenUsuario(api, empresas.B.Id));

        var conDocumento = await deA.PostAsync(RutaEstimado, Cuerpo(Express("ABC123", tipo: "CC", documento: "1020304050", porLocatario: ["RUNT_LOCATARIO"])), ct);
        var sinDocumento = await deA.PostAsync(RutaEstimado, Cuerpo(Express("ABC123", porLocatario: ["RUNT_LOCATARIO"])), ct);
        var noMostrar = await deB.PostAsync(RutaEstimado, Cuerpo(Express("ABC123", porLocatario: ["RUNT_LOCATARIO"])), ct);
        var fueraDeConjunto = await deA.PostAsync(RutaEstimado, Cuerpo(Express("ABC123", porVehiculo: ["RUNT", "OTRA"])), ct);

        var textoCon = await conDocumento.Content.ReadAsStringAsync(ct);
        conDocumento.StatusCode.Should().Be(HttpStatusCode.OK, textoCon);
        textoCon.Should().NotContain("1020304050");
        var jsonCon = JsonDocument.Parse(textoCon).RootElement;
        jsonCon.GetProperty("mostrar_estimado").GetBoolean().Should().BeTrue();
        Estimado(jsonCon).Should().Equal(new Dictionary<string, int> { ["RUNT"] = 1, ["RUNT_LOCATARIO"] = 1 });
        var textoSin = await sinDocumento.Content.ReadAsStringAsync(ct);
        sinDocumento.StatusCode.Should().Be(HttpStatusCode.OK, textoSin);
        Estimado(JsonDocument.Parse(textoSin).RootElement).Should().Equal(new Dictionary<string, int> { ["RUNT"] = 1, ["RUNT_LOCATARIO"] = 0 });
        var textoNo = await noMostrar.Content.ReadAsStringAsync(ct);
        noMostrar.StatusCode.Should().Be(HttpStatusCode.OK, textoNo);
        var jsonNo = JsonDocument.Parse(textoNo).RootElement;
        jsonNo.GetProperty("mostrar_estimado").GetBoolean().Should().BeFalse("B tiene NO_MOSTRAR: no se usa la configuración de A");
        jsonNo.TryGetProperty("estimado_consultas", out _).Should().BeFalse();
        fueraDeConjunto.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await fueraDeConjunto.Content.ReadAsStringAsync(ct));
        (await Codigo(fueraDeConjunto, ct)).Should().Be(ServicioConfiguracion.CodigoFueraDeConjunto);

        (await TodasLasCabecerasAsync(api, ct)).Should().Be(0, "el estimado no persiste nada");
        (await Lotes.TodosLosVehiculosAsync(api, ct)).Should().Be(0);
    }

    // Escenario 5 (F02-C13) — el documento del locatario queda cifrado (bytea, no el texto) con origen DECLARADO_CLIENTE; la
    // respuesta lo trae enmascarado y ningún log lo contiene.
    [Fact]
    public async Task Documento_del_locatario_queda_cifrado_con_origen_DECLARADO_CLIENTE()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        var espia = new EncoladorEspia();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            espia.Registrar(b.Services);
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa(), ct);
        using var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        const string Documento = "1020304050";

        var respuesta = await cliente.PostAsync(Ruta, Cuerpo(Express("ABC123", tipo: "cc", documento: Documento)), ct);

        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, texto);
        texto.Should().NotContain(Documento).And.Contain("****4050");
        var id = JsonDocument.Parse(texto).RootElement.GetProperty("id").GetGuid();
        var vehiculo = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single();
        vehiculo.LocatarioDocTipo.Should().Be("CC");
        vehiculo.LocatarioDocOrigen.Should().Be(OrigenDocumento.DeclaradoCliente);
        vehiculo.LocatarioDocCifrado.Should().NotBeNullOrEmpty();
        Encoding.UTF8.GetString(vehiculo.LocatarioDocCifrado!).Should().NotContain(Documento);
        api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Desproteger(vehiculo.LocatarioDocCifrado!).Should().Be(Documento);
        JsonSerializer.Serialize(espia.Entregas).Should().NotContain(Documento, "el encolador no recibe documentos");
        logs.Mensajes.Should().NotBeEmpty();
        logs.Mensajes.Should().NotContain(m => m.Contains(Documento, StringComparison.Ordinal), "ningún log lleva el documento");
    }

    // R-6 (F01-C12) — el backoffice FLIT no tiene diagnostico.diagnosticos.crear: 403 en el express y en su estimado.
    [Fact]
    public async Task Backoffice_recibe_403_en_express_y_estimado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        var backoffice = Permisos.RolesPorDefecto.Single(r => r.Codigo == "backoffice_diagnostico").Permisos;
        using var cliente = api.Cliente(api.Token(permisos: backoffice, roles: ["backoffice_diagnostico"], tipoEmpresa: "FLIT"));

        var express = await cliente.PostAsync(Ruta, Cuerpo(Express("ABC123")), ct);
        var estimado = await cliente.PostAsync(RutaEstimado, Cuerpo(Express("ABC123")), ct);

        express.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        estimado.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Filtro por compañía (#13715) — el express de A usa la configuración de A (una fuente habilitada solo en B da 422) y
    // deja la cabecera y el vehículo en A; B no ve nada.
    [Fact]
    public async Task Express_de_una_compania_no_cruza_a_otra()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.A, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt], LimiteVehiculosLote = 111 }, ct);
        await SembrarAsync(empresas.B, new ConfiguracionEmpresa { FuentesHabilitadas = [Fuente.Runt, Fuente.Avaluo], LimiteVehiculosLote = 222 }, ct);
        using var deA = api.Cliente(Cabeceras.TokenUsuario(api, empresas.A.Id));

        var conFuenteDeB = await deA.PostAsync(Ruta, Cuerpo(Express("ABC123", porVehiculo: ["RUNT", "AVALUO"])), ct);
        var creada = await deA.PostAsync(Ruta, Cuerpo(Express("ABC123")), ct);

        conFuenteDeB.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "AVALUO está habilitada en B, no en A");
        (await Codigo(conFuenteDeB, ct)).Should().Be(ValidadorCabecera.CodigoFuenteNoHabilitada);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        var id = JsonDocument.Parse(await creada.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("id").GetGuid();
        var cabecera = await Cabeceras.LeerAsync(api, empresas.A.Id, id, ct);
        JsonDocument.Parse(cabecera.Configuracion).RootElement.GetProperty("limite_vehiculos_lote").GetInt32().Should().Be(111);
        (await Lotes.VehiculosAsync(api, empresas.A.Id, id, ct)).Should().ContainSingle().Which.TenantId.Should().Be(empresas.A.Id);
        (await Cabeceras.ContarAsync(api, empresas.B.Id, ct)).Should().Be(0);
        (await Lotes.VehiculosAsync(api, empresas.B.Id, id, ct)).Should().BeEmpty();
        (await TodasLasCabecerasAsync(api, ct)).Should().Be(1);
    }

    /// <summary>Cuerpo del express en snake_case: identificador, nombre opcional, modo SIMPLE, alcance y el locatario opcional.</summary>
    private static string Express(
        string? identificador, string? nombre = null, string? tipo = null, string? documento = null, string[]? porVehiculo = null, string[]? porLocatario = null)
    {
        var json = new Dictionary<string, object?>
        {
            ["identificador"] = identificador,
            ["modo"] = "SIMPLE",
            ["tipo_tramite_codigo"] = null,
            ["alcance"] = new Dictionary<string, string[]>
            {
                ["por_vehiculo"] = porVehiculo ?? ["RUNT"],
                ["por_propietario"] = [],
                ["por_locatario"] = porLocatario ?? [],
            },
            ["tipo_documento_locatario"] = tipo,
            ["documento_locatario"] = documento,
        };
        if (nombre is not null)
            json["nombre"] = nombre;
        return JsonSerializer.Serialize(json);
    }

    private static Dictionary<string, int> Estimado(JsonElement json) =>
        json.GetProperty("estimado_consultas").EnumerateArray().ToDictionary(e => e.GetProperty("fuente").GetString()!, e => e.GetProperty("consultas").GetInt32());

    /// <summary>Filas de <c>diagnostico.diagnosticos</c> de todas las empresas, por SQL directo (sin filtro).</summary>
    private static async Task<long> TodasLasCabecerasAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var conexion = new Npgsql.NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new Npgsql.NpgsqlCommand("SELECT count(*) FROM diagnostico.diagnosticos", conexion);
        return (long)(await consulta.ExecuteScalarAsync(ct))!;
    }
}
