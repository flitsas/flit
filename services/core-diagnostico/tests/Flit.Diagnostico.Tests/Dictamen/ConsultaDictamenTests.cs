using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Corte 7 — consulta, override y evento. Lo que no abre Postgres afirma el DTO, el evento y la fila del reemplazo.
/// Con <c>ConnectionStrings__Core</c> la compañía lee, el backoffice reemplaza y la outbox guarda el evento de esa
/// transacción. Sin base, en local se omite y en CI falla.
/// </summary>
public sealed class ConsultaDictamenTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Creado = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    private const string TextoViejo = "TEXTO VIEJO QUE NO DEBE QUEDAR";

    private const string TextoRaro = "texto guardado";

    private const string Alertas =
        """[{"codigo":"SOAT_NO_VIGENTE","texto":"TEXTO VIEJO QUE NO DEBE QUEDAR","efecto":"BLOQUEA","fuente":"RUNT"},{"codigo":"NO_ESTA","texto":"texto guardado","efecto":"ADVIERTE","fuente":"OTRA"}]""";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void El_DTO_de_la_compania_no_tiene_override_y_el_de_backoffice_solo_suma_esos_tres()
    {
        Nombres(typeof(DictamenCompaniaDto)).Should().BeEquivalentTo(
            ["Version", "Veredicto", "EstadoRedaccion", "Alertamientos", "CatalogoVersion", "CreadoEn"]);
        Nombres(typeof(DictamenBackofficeDto)).Should().BeEquivalentTo(
            ["Version", "Veredicto", "EstadoRedaccion", "Alertamientos", "CatalogoVersion", "CreadoEn", "AnalisisOverride", "UsuarioOverrideId", "OverrideEn"]);
        Nombres(typeof(AlertaDictamenDto)).Should().BeEquivalentTo(["Codigo", "Texto", "Efecto", "Fuente", "Etiqueta", "FuenteEtiqueta"]);

        var fila = Fila();
        fila.AnalisisOverride = true;
        fila.UsuarioOverrideId = Guid.CreateVersion7();
        fila.OverrideEn = Ahora;
        var compania = JsonSerializer.SerializeToNode(ConsultaDictamen.Compania(fila), Web)!.AsObject();
        var backoffice = JsonSerializer.SerializeToNode(ConsultaDictamen.Backoffice(fila), Web)!.AsObject();

        compania.Select(p => p.Key).Should().BeEquivalentTo(
            ["version", "veredicto", "estado_redaccion", "alertamientos", "catalogo_version", "creado_en"]);
        backoffice.Select(p => p.Key).Should().BeEquivalentTo(
        [
            "version", "veredicto", "estado_redaccion", "alertamientos", "catalogo_version", "creado_en",
            "analisis_override", "usuario_override_id", "override_en",
        ]);
        compania["alertamientos"]![0]!.AsObject().Select(p => p.Key).Should().Equal("codigo", "texto", "efecto", "fuente", "etiqueta", "fuente_etiqueta");
        backoffice["analisis_override"]!.GetValue<bool>().Should().BeTrue();
        backoffice["usuario_override_id"]!.GetValue<Guid>().Should().Be(fila.UsuarioOverrideId!.Value);
    }

    /// <summary>Punto 5 (2026-11-11): cada alerta lleva su rótulo y el nombre de su fuente; un código sin rótulo nunca sale crudo.</summary>
    [Fact]
    public void Cada_alerta_lleva_etiqueta_y_fuente_etiqueta_y_el_codigo_sin_rotulo_sale_generico()
    {
        var compania = JsonSerializer.SerializeToNode(ConsultaDictamen.Compania(Fila()), Web)!.AsObject();
        var alertas = compania["alertamientos"]!.AsArray();

        alertas[0]!["codigo"]!.GetValue<string>().Should().Be("SOAT_NO_VIGENTE");
        alertas[0]!["etiqueta"]!.GetValue<string>().Should().Be("SOAT no vigente");
        alertas[0]!["fuente"]!.GetValue<string>().Should().Be("RUNT");
        alertas[0]!["fuente_etiqueta"]!.GetValue<string>().Should().Be("RUNT");
        alertas[1]!["etiqueta"]!.GetValue<string>().Should().Be("Novedad sin clasificar");
        alertas[1]!["fuente_etiqueta"]!.GetValue<string>().Should().Be("Novedad sin clasificar");

        var backoffice = JsonSerializer.SerializeToNode(ConsultaDictamen.Backoffice(Fila()), Web)!.AsObject();
        backoffice["alertamientos"]![0]!["etiqueta"]!.GetValue<string>().Should().Be("SOAT no vigente");
    }

    [Fact]
    public void El_cierre_lleva_override_false_y_el_reemplazo_true_sin_datos_personales()
    {
        const string placa = "ABC123";
        const string vin = "9ZZPRUEBA00000001";
        const string documento = "1234567890";
        const string texto = "El SOAT no está vigente, así que el traspaso queda bloqueado.";
        var cabecera = new CabeceraDiagnostico
        {
            Nombre = placa,
            Configuracion = "{}",
            Modo = ModoDiagnostico.ConAnalisis,
            TipoTramiteCodigo = "TRASPASO_STANDARD",
        };
        var fila = Fila(texto);
        var cierre = JsonSerializer.Serialize(DictamenEmitido.De(cabecera, fila, esOverride: false), EventEnvelope.JsonOptions);
        var reemplazo = JsonSerializer.Serialize(DictamenEmitido.De(cabecera, fila, esOverride: true), EventEnvelope.JsonOptions);

        using var docCierre = JsonDocument.Parse(cierre);
        using var docReemplazo = JsonDocument.Parse(reemplazo);
        Claves(docCierre).Should().Equal(
            "diagnosticoId", "vehiculoId", "dictamenId", "version", "veredicto", "catalogoVersion", "modo", "tipoTramiteCodigo", "estadoRedaccion", "override");
        docCierre.RootElement.GetProperty("override").GetBoolean().Should().BeFalse();
        docReemplazo.RootElement.GetProperty("override").GetBoolean().Should().BeTrue();
        docCierre.RootElement.GetProperty("modo").GetString().Should().Be("CON_ANALISIS");
        docCierre.RootElement.GetProperty("tipoTramiteCodigo").GetString().Should().Be("TRASPASO_STANDARD");
        cierre.Should().NotContain(placa).And.NotContain(vin).And.NotContain(documento).And.NotContain(texto);
        reemplazo.Should().NotContain(placa).And.NotContain(texto);
    }

    [Fact]
    public void Sin_confirmacion_o_con_el_mismo_veredicto_no_nace_version_y_el_otro_vuelve_a_la_plantilla()
    {
        var tipo = "TRASPASO_STANDARD";
        var usuario = Guid.CreateVersion7();
        var sinConfirmar = Fila();
        var confirmar = () => EmisorDictamen.FilaDeOverride(sinConfirmar, "BLOQUEADO", false, usuario, tipo, false, true, Ahora);
        confirmar.Should().Throw<ReglaException>().Which.Codigo.Should().Be(CodigosDictamen.ConfirmadoRequerido);
        sinConfirmar.Vigente.Should().BeTrue();
        sinConfirmar.Version.Should().Be(1);

        var invalido = Fila();
        var malo = () => EmisorDictamen.FilaDeOverride(invalido, "OTRO", true, usuario, tipo, false, true, Ahora);
        malo.Should().Throw<ReglaException>().Which.Codigo.Should().Be(CodigosDictamen.VeredictoInvalido);
        invalido.Vigente.Should().BeTrue();

        var igual = Fila();
        var mismo = () => EmisorDictamen.FilaDeOverride(igual, "VIABLE", true, usuario, tipo, false, true, Ahora);
        mismo.Should().Throw<ReglaException>().Which.Codigo.Should().Be(CodigosDictamen.VeredictoSinCambio);
        igual.Vigente.Should().BeTrue();

        var vigente = Fila(catalogo: 7);
        var nueva = EmisorDictamen.FilaDeOverride(vigente, "BLOQUEADO", true, usuario, tipo, redaccionSuspendida: false, proveedorConfigurado: true, Ahora);
        vigente.Vigente.Should().BeFalse();
        nueva.Version.Should().Be(2);
        nueva.Vigente.Should().BeTrue();
        nueva.Veredicto.Should().Be("BLOQUEADO");
        nueva.EstadoRedaccion.Should().Be("PENDIENTE");
        nueva.RedactadoPor.Should().Be("PLANTILLA");
        nueva.RedaccionIntentos.Should().Be(0);
        nueva.RedaccionLeaseHasta.Should().BeNull();
        nueva.AnalisisOverride.Should().BeTrue();
        nueva.UsuarioOverrideId.Should().Be(usuario);
        nueva.OverrideEn.Should().Be(Ahora);
        nueva.CatalogoVersion.Should().Be(7);
        nueva.RedaccionDisponibleDesde.Should().Be(EmisorDictamen.DisponibleAlCerrar(false, true, Ahora));
        Texto(nueva.Alertamientos, 0).Should().Be(Plantilla(tipo, "SOAT_NO_VIGENTE"));
        Texto(nueva.Alertamientos, 1).Should().Be(TextoRaro, "la fila que no está en el catálogo conserva el texto");
        Codigo(nueva.Alertamientos, 0).Should().Be("SOAT_NO_VIGENTE");
        Codigo(nueva.Alertamientos, 1).Should().Be("NO_ESTA");
        Efecto(nueva.Alertamientos, 1).Should().Be("ADVIERTE");
        FuenteDe(nueva.Alertamientos, 1).Should().Be("OTRA");

        var suspendida = EmisorDictamen.FilaDeOverride(Fila(), "BLOQUEADO", true, usuario, tipo, true, true, Ahora);
        suspendida.RedaccionDisponibleDesde.Should().BeNull();
    }

    [Fact]
    public void EmitirAsync_y_el_PUT_no_llaman_al_redactor_y_la_cola_no_publica()
    {
        var raiz = IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio();
        var emisor = File.ReadAllText(Path.Combine(raiz, "src", "Flit.Diagnostico.Api", "Modulos", "Dictamen", "EmisorDictamen.cs"));
        var inicio = emisor.IndexOf("Task EmitirAsync", StringComparison.Ordinal);
        var fin = emisor.IndexOf("private async Task<CabeceraDiagnostico>", inicio, StringComparison.Ordinal);
        var cuerpo = emisor[inicio..fin];
        cuerpo.Should().NotContain("RedactarAsync").And.Contain("DictamenEmitido.Encolar");

        var put = File.ReadAllText(Path.Combine(raiz, "src", "Flit.Diagnostico.Api", "Modulos", "Backoffice", "OverrideDictamenEndpoints.cs"));
        put.Should().NotContain("RedactarAsync").And.Contain("esOverride: true");

        var cola = File.ReadAllText(Path.Combine(raiz, "src", "Flit.Diagnostico.Api", "Modulos", "Trabajos", "RedactorEnCola.cs"));
        cola.Should().NotContain("DictamenEmitido").And.NotContain("diagnostico.dictamen.emitido");
    }

    [Fact]
    public void Un_choque_de_version_o_de_vigente_es_conflicto_de_dictamen()
    {
        OverrideDictamenEndpoints.EsConflictoDeDictamen(Duplicado("ux_dictamen_version")).Should().BeTrue();
        OverrideDictamenEndpoints.EsConflictoDeDictamen(Duplicado("ux_dictamen_vigente")).Should().BeTrue();
        OverrideDictamenEndpoints.EsConflictoDeDictamen(Duplicado("ux_evidencias_vigente")).Should().BeFalse();
        var cuerpo = JsonElement.Parse("""{"veredicto":"BLOQUEADO","confirmado":false}""");
        var acto = () => OverrideDictamenEndpoints.ExigirCuerpo(cuerpo);
        acto.Should().Throw<ReglaException>().Which.Codigo.Should().Be(CodigosDictamen.ConfirmadoRequerido);
    }

    [Fact]
    public void B7_D9_cambiar_el_veredicto_sin_motivo_es_422_MOTIVO_REQUERIDO()
    {
        var vigente = Fila();
        vigente.Veredicto = "VIABLE";
        var sinMotivo = OverrideDictamenEndpoints.LeerCuerpo(JsonElement.Parse("""{"veredicto":"BLOQUEADO","confirmado":true}"""));
        var acto = () => OverrideDictamenEndpoints.ExigirFrenteAVigente(sinMotivo, vigente);
        var error = acto.Should().Throw<ReglaException>().Which;
        error.Codigo.Should().Be(CodigosDictamen.MotivoRequerido);
        error.Status.Should().Be(422);

        var corto = () => OverrideDictamenEndpoints.LeerCuerpo(
            JsonElement.Parse("""{"veredicto":"BLOQUEADO","confirmado":true,"motivo":"   corto   "}"""));
        corto.Should().Throw<ReglaException>().Which.Codigo.Should().Be(CodigosDictamen.MotivoRequerido);

        var nuevoSinMotivo = () => OverrideDictamenEndpoints.LeerCuerpo(
            JsonElement.Parse("""{"veredicto":"VIABLE","confirmado":true,"version_esperada":1}"""));
        nuevoSinMotivo.Should().Throw<ReglaException>().Which.Codigo.Should().Be(CodigosDictamen.MotivoRequerido);

        var conMotivo = OverrideDictamenEndpoints.LeerCuerpo(JsonElement.Parse(
            """{"veredicto":"BLOQUEADO","confirmado":true,"motivo":"  Soporte verifico 1234567 con la aseguradora  ","version_esperada":1,"confirmar_con_tramite":true}"""));
        conMotivo.Motivo.Should().Be("Soporte verifico 1234567 con la aseguradora");
        conMotivo.VersionEsperada.Should().Be(1);
        conMotivo.ConfirmarConTramite.Should().BeTrue();
    }

    [Fact]
    public void B7_P3_un_alertamiento_AJUSTE_FLIT_con_seis_o_mas_digitos_es_422()
    {
        const string plantilla = """{"veredicto":"BLOQUEADO","confirmado":true,"motivo":"Soporte verifico el caso","version_esperada":1,"alertamientos":[{"codigo":"AJUSTE_FLIT","texto":"TEXTO","efecto":"BLOQUEA","fuente":"RUNT"}]}""";
        var conCedula = () => OverrideDictamenEndpoints.LeerCuerpo(JsonElement.Parse(plantilla.Replace("TEXTO", "Propietario 1023456 con embargo", StringComparison.Ordinal)));
        conCedula.Should().Throw<ReglaException>().Which.Codigo.Should().Be(CodigosDictamen.AlertamientoConDigitos);

        var cinco = OverrideDictamenEndpoints.LeerCuerpo(JsonElement.Parse(plantilla.Replace("TEXTO", "Embargo del juzgado 12345", StringComparison.Ordinal)));
        cinco.Alertamientos.Should().ContainSingle().Which.Codigo.Should().Be(CodigosDictamen.AjusteFlit);
    }

    [Fact]
    public void B7_version_esperada_vieja_es_409_DICTAMEN_DESACTUALIZADO_y_la_concurrencia_es_conflicto()
    {
        var vigente = Fila();
        vigente.Veredicto = "VIABLE";
        var cuerpo = OverrideDictamenEndpoints.LeerCuerpo(JsonElement.Parse(
            """{"veredicto":"BLOQUEADO","confirmado":true,"motivo":"Soporte verifico el caso","version_esperada":2}"""));
        var acto = () => OverrideDictamenEndpoints.ExigirFrenteAVigente(cuerpo, vigente);
        var error = acto.Should().Throw<ReglaException>().Which;
        error.Codigo.Should().Be(CodigosDictamen.DictamenDesactualizado);
        error.Status.Should().Be(409);

        OverrideDictamenEndpoints.EsConflictoDeDictamen(new DbUpdateConcurrencyException("xmin")).Should().BeTrue();
    }

    [Fact]
    public async Task La_compania_lee_su_vigente_y_otra_compania_no_la_ve()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var lote = await SembrarAsync(empresas.A, ct);
        var otro = await TableroAyudas.SembrarAsync(empresas.A, ct, vehiculos: [new("TST903", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var con = lote.Vehiculos[0].Id;
        var sin = lote.Vehiculos[1].Id;

        var respuesta = await empresas.A.Cliente().GetAsync(RutaCompania(lote.Id, con), ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await TableroAyudas.JsonAsync(respuesta, ct);
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["version", "veredicto", "estado_redaccion", "alertamientos", "catalogo_version", "creado_en"]);
        json.GetProperty("veredicto").GetString().Should().Be("VIABLE");
        json.GetProperty("alertamientos").EnumerateArray().Select(a => a.GetProperty("codigo").GetString()).Should().Equal("SOAT_NO_VIGENTE", "NO_ESTA");

        var sinFila = await empresas.A.Cliente().GetAsync(RutaCompania(lote.Id, sin), ct);
        sinFila.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TableroAyudas.JsonAsync(sinFila, ct)).GetProperty("code").GetString().Should().Be(CodigosDictamen.SinDictamen);

        var ajeno = await empresas.A.Cliente().GetAsync(RutaCompania(lote.Id, otro.Vehiculos[0].Id), ct);
        ajeno.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TableroAyudas.JsonAsync(ajeno, ct)).GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");

        var otra = await empresas.B.Cliente().GetAsync(RutaCompania(lote.Id, con), ct);
        otra.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TableroAyudas.JsonAsync(otra, ct)).GetProperty("code").GetString().Should().Be("NO_ENCONTRADO");
    }

    [Fact]
    public async Task El_backoffice_exige_empresa_audita_la_lectura_y_la_compania_recibe_403()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);
        var flit = Guid.CreateVersion7();
        var backoffice = TokenBackoffice(api, flit, corregir: false);

        var sinEmpresa = await api.Cliente(backoffice).GetAsync(RutaBackoffice(lote.Id, lote.Vehiculos[0].Id), ct);
        sinEmpresa.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TableroAyudas.JsonAsync(sinEmpresa, ct)).GetProperty("code").GetString().Should().Be(EmpresaExplicita.CodigoRequerida);
        (await LecturasAsync(api, flit, ct)).Should().BeEmpty();

        var compania = await empresa.Cliente().GetAsync(RutaBackoffice(lote.Id, lote.Vehiculos[0].Id, empresa.Id), ct);
        compania.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LecturasAsync(api, flit, ct)).Should().BeEmpty("el 403 de la política ocurre antes de auditar");

        var lectura = await api.Cliente(backoffice).GetAsync(RutaBackoffice(lote.Id, lote.Vehiculos[0].Id, empresa.Id), ct);
        lectura.StatusCode.Should().Be(HttpStatusCode.OK, await lectura.Content.ReadAsStringAsync(ct));
        var json = await TableroAyudas.JsonAsync(lectura, ct);
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
        [
            "version", "veredicto", "estado_redaccion", "alertamientos", "catalogo_version", "creado_en",
            "analisis_override", "usuario_override_id", "override_en",
        ]);
        json.GetProperty("analisis_override").GetBoolean().Should().BeFalse();
        json.GetProperty("usuario_override_id").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("override_en").ValueKind.Should().Be(JsonValueKind.Null);
        (await LecturasAsync(api, flit, ct)).Should().ContainSingle(l => l.EmpresaConsultada == empresa.Id && l.Ruta.Contains("/dictamen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_PUT_solo_reemplaza_con_confirmacion_y_publica_el_evento_de_esa_transaccion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);
        var vid = lote.Vehiculos[0].Id;
        var flit = Guid.CreateVersion7();
        var token = TokenBackoffice(api, flit, corregir: true);
        using var cliente = api.Cliente(token);
        var ruta = RutaBackoffice(lote.Id, vid, empresa.Id);

        using (var sinConfirmar = Cuerpo("BLOQUEADO", false))
        {
            var respuesta = await cliente.PutAsync(ruta, sinConfirmar, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be(CodigosDictamen.ConfirmadoRequerido);
        }

        using (var igual = Cuerpo("VIABLE", true))
        {
            var respuesta = await cliente.PutAsync(ruta, igual, ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be(CodigosDictamen.VeredictoSinCambio);
        }

        (await cliente.PutAsync(RutaCompania(lote.Id, vid), Cuerpo("BLOQUEADO", true), ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var admin = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionEditar], roles: ["admin_diagnostico"]);
        (await api.Cliente(admin).PutAsync(ruta, Cuerpo("BLOQUEADO", true), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await FilasAsync(empresa, vid, ct)).Should().ContainSingle();

        using var cuerpo = Cuerpo("BLOQUEADO", true);
        var reemplazo = await cliente.PutAsync(ruta, cuerpo, ct);
        reemplazo.StatusCode.Should().Be(HttpStatusCode.OK, await reemplazo.Content.ReadAsStringAsync(ct));
        var json = await TableroAyudas.JsonAsync(reemplazo, ct);
        json.GetProperty("version").GetInt32().Should().Be(2);
        json.GetProperty("veredicto").GetString().Should().Be("BLOQUEADO");
        json.GetProperty("estado_redaccion").GetString().Should().Be("PENDIENTE");
        json.GetProperty("analisis_override").GetBoolean().Should().BeTrue();
        json.GetProperty("usuario_override_id").GetGuid().Should().Be(TableroAyudas.Usuario(token));

        var filas = await FilasAsync(empresa, vid, ct);
        filas.Should().HaveCount(2);
        var anterior = filas.Single(f => f.Version == 1);
        var nueva = filas.Single(f => f.Vigente);
        anterior.Vigente.Should().BeFalse();
        anterior.Veredicto.Should().Be("VIABLE");
        Texto(anterior.Alertamientos, 0).Should().Be(TextoViejo);
        nueva.Version.Should().Be(2);
        nueva.Veredicto.Should().Be("BLOQUEADO");
        nueva.EstadoRedaccion.Should().Be("PENDIENTE");
        nueva.RedactadoPor.Should().Be("PLANTILLA");
        nueva.RedaccionIntentos.Should().Be(0);
        nueva.RedaccionLeaseHasta.Should().BeNull();
        nueva.AnalisisOverride.Should().BeTrue();
        nueva.RedaccionDisponibleDesde.Should().NotBeNull();
        Texto(nueva.Alertamientos, 0).Should().Be(Plantilla("TRASPASO_STANDARD", "SOAT_NO_VIGENTE"));
        Texto(nueva.Alertamientos, 1).Should().Be(TextoRaro);

        var evento = (await OutboxDePrueba.EventosAsync(api, DictamenEmitido.Tipo, ct)).Should().ContainSingle().Subject;
        evento.Sobre.TenantId.Should().Be(empresa.Id);
        evento.Data.GetProperty("override").GetBoolean().Should().BeTrue();
        evento.Data.GetProperty("version").GetInt32().Should().Be(2);
        evento.Data.GetProperty("dictamenId").GetGuid().Should().Be(nueva.Id);
        evento.Data.GetProperty("veredicto").GetString().Should().Be("BLOQUEADO");
        evento.Data.GetProperty("modo").GetString().Should().Be("CON_ANALISIS");
        evento.Payload.Should().NotContain("TST901").And.NotContain(TextoViejo).And.NotContain("El SOAT no está vigente");

        var deLaCompania = await TableroAyudas.JsonAsync(await empresa.Cliente().GetAsync(RutaCompania(lote.Id, vid), ct), ct);
        deLaCompania.GetProperty("veredicto").GetString().Should().Be("BLOQUEADO");
        deLaCompania.EnumerateObject().Select(p => p.Name).Should().NotContain(["analisis_override", "usuario_override_id", "override_en"]);
    }

    private static void Simulado(WebApplicationBuilder builder)
    {
        builder.Configuration["Diagnostico:Ambiente"] = "LOCAL";
        builder.Configuration["Diagnostico:Anthropic:Simulado"] = "true";
    }

    private static async Task<Flit.Diagnostico.Tests.Tablero.LoteSembrado> SembrarAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos:
        [
            new("TST901", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
            new("TST902", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
        ], modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO_STANDARD");
        await using var db = empresa.Db();
        db.Add(new DictamenVehiculo
        {
            VehiculoId = lote.Vehiculos[0].Id,
            Version = 1,
            Vigente = true,
            Veredicto = "VIABLE",
            Alertamientos = Alertas,
            CatalogoVersion = CatalogoReglasDictamen.Version,
            RedactadoPor = "PLANTILLA",
            EstadoRedaccion = "PENDIENTE",
            CreadoEn = Creado,
        });
        await db.SaveChangesAsync(ct);
        return lote;
    }

    private static DictamenVehiculo Fila(string? primerTexto = null, int catalogo = CatalogoReglasDictamen.Version) => new()
    {
        VehiculoId = Guid.CreateVersion7(),
        Version = 1,
        Vigente = true,
        Veredicto = "VIABLE",
        Alertamientos = primerTexto is null ? Alertas : Alertas.Replace(TextoViejo, primerTexto, StringComparison.Ordinal),
        CatalogoVersion = catalogo,
        RedactadoPor = "PLANTILLA",
        EstadoRedaccion = "PENDIENTE",
        CreadoEn = Creado,
    };

    private static string RutaCompania(Guid lote, Guid vehiculo) =>
        $"{Program.Prefijo}/diagnosticos/{lote}/vehiculos/{vehiculo}/dictamen";

    private static string RutaBackoffice(Guid lote, Guid vehiculo, Guid? empresa = null) =>
        $"{Program.Prefijo}/backoffice/diagnosticos/{lote}/vehiculos/{vehiculo}/dictamen" + (empresa is { } id ? $"?empresa={id}" : string.Empty);

    private static string TokenBackoffice(ApiDePrueba api, Guid flit, bool corregir) =>
        api.Token(
            empresa: flit,
            permisos: corregir ? [Permisos.BackofficeVer, Permisos.DictamenOverride] : [Permisos.BackofficeVer],
            roles: ["backoffice_diagnostico"],
            tipoEmpresa: UsuarioActual.TipoEmpresaFlit);

    private static StringContent Cuerpo(string veredicto, bool confirmado) =>
        new($"{{\"veredicto\":\"{veredicto}\",\"confirmado\":{(confirmado ? "true" : "false")},\"motivo\":\"Soporte verifico el caso\"}}", Encoding.UTF8, "application/json");

    private static async Task<List<DictamenVehiculo>> FilasAsync(EmpresaDePrueba empresa, Guid vehiculo, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<DictamenVehiculo>().AsNoTracking().Where(d => d.VehiculoId == vehiculo).OrderBy(d => d.Version).ToListAsync(ct);
    }

    private static async Task<List<LecturaBackoffice>> LecturasAsync(ApiDePrueba api, Guid flit, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, flit);
        return await db.Set<LecturaBackoffice>().AsNoTracking().ToListAsync(ct);
    }

    private static DbUpdateException Duplicado(string indice) =>
        new("duplicado", new PostgresException($"duplicate key value violates unique constraint \"{indice}\"", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));

    private static List<string> Nombres(Type tipo) =>
        tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();

    private static string[] Claves(JsonDocument documento) =>
        documento.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

    private static string Plantilla(string tipo, string codigo) =>
        CatalogoReglasDictamen.Filas.Single(f => f.TipoTramiteCodigo == tipo && f.Codigo == codigo).PlantillaTexto;

    private static JsonElement Item(string json, int indice)
    {
        using var documento = JsonDocument.Parse(json);
        return documento.RootElement[indice].Clone();
    }

    private static string? Texto(string json, int indice) => Item(json, indice).GetProperty("texto").GetString();

    private static string? Codigo(string json, int indice) => Item(json, indice).GetProperty("codigo").GetString();

    private static string? Efecto(string json, int indice) => Item(json, indice).GetProperty("efecto").GetString();

    private static string? FuenteDe(string json, int indice) => Item(json, indice).GetProperty("fuente").GetString();
}
