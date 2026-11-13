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
        // HU #14563 AC26 (D6): la compañía solo gana la marca del ajuste FLIT y su fecha.
        Nombres(typeof(DictamenCompaniaDto)).Should().BeEquivalentTo(
            ["Version", "Veredicto", "EstadoRedaccion", "Alertamientos", "CatalogoVersion", "CreadoEn", "AjustadoPorFlit", "AjustadoEn"]);
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
            ["version", "veredicto", "estado_redaccion", "alertamientos", "catalogo_version", "creado_en", "ajustado_por_flit", "ajustado_en"]);
        // P1 = a: un override histórico (sin motivo) no lleva la marca.
        compania["ajustado_por_flit"]!.GetValue<bool>().Should().BeFalse();
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
        // B-PR6: el cuerpo legado sin motivo ya no se acepta, cambie o no el veredicto.
        foreach (var legado in new[] { """{"veredicto":"BLOQUEADO","confirmado":true}""", """{"veredicto":"VIABLE","confirmado":true}""" })
        {
            var acto = () => OverrideDictamenEndpoints.LeerCuerpo(JsonElement.Parse(legado));
            var error = acto.Should().Throw<ReglaException>().Which;
            error.Codigo.Should().Be(CodigosDictamen.MotivoRequerido);
            error.Status.Should().Be(422);
            error.Campo.Should().Be("motivo");
        }

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

        // El cuerpo nuevo del frontend no trae «confirmado»: se acepta; un confirmado distinto de true sigue siendo 422.
        var sinConfirmado = OverrideDictamenEndpoints.LeerCuerpo(JsonElement.Parse(
            """{"veredicto":"BLOQUEADO","motivo":"Soporte verifico el caso","version_esperada":1}"""));
        sinConfirmado.Veredicto.Should().Be("BLOQUEADO");
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
            ["version", "veredicto", "estado_redaccion", "alertamientos", "catalogo_version", "creado_en", "ajustado_por_flit", "ajustado_en"]);
        json.GetProperty("ajustado_por_flit").GetBoolean().Should().BeFalse();
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

    // AC20 + AC30 + AC33 — ajuste con alertamientos (cuerpo del frontend, sin «confirmado»): versión nueva MANUAL/COMPLETADA con
    // motivo y versión de origen, marca AGREGADO/EDITADO por alertamiento, la fila del tablero sube su versión, el evento lleva
    // ajuste_alertamientos sin el motivo y no se escribe ninguna notificación (D8).
    [Fact]
    public async Task B7_AC20_ajuste_con_alertamientos_crea_version_MANUAL_con_marcas_y_sube_la_fila()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);
        var vid = lote.Vehiculos[0].Id;
        var token = TokenBackoffice(api, Guid.CreateVersion7(), corregir: true);
        var versionFilaAntes = await VersionFilaAsync(empresa, vid, ct);

        var respuesta = await api.Cliente(token).PutAsync(RutaBackoffice(lote.Id, vid, empresa.Id), Ajuste(1, "BLOQUEADO", AlertasAjuste), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await TableroAyudas.JsonAsync(respuesta, ct);
        json.GetProperty("version").GetInt32().Should().Be(2);
        json.GetProperty("veredicto").GetString().Should().Be("BLOQUEADO");
        json.GetProperty("estado_redaccion").GetString().Should().Be("COMPLETADA");

        var filas = await FilasAsync(empresa, vid, ct);
        filas.Select(f => f.Version).Should().Equal(1, 2);
        filas.Single(f => f.Version == 1).Vigente.Should().BeFalse();
        var nueva = filas.Single(f => f.Vigente);
        nueva.RedactadoPor.Should().Be("MANUAL");
        nueva.EstadoRedaccion.Should().Be("COMPLETADA");
        nueva.RedaccionDisponibleDesde.Should().BeNull("el LLM no vuelve a redactar un ajuste FLIT");
        nueva.AnalisisOverride.Should().BeTrue();
        nueva.AjusteMotivo.Should().Be(MotivoAjuste);
        nueva.AjustadoDesdeVersion.Should().Be(1);
        using (var alertas = JsonDocument.Parse(nueva.Alertamientos))
        {
            var lista = alertas.RootElement.EnumerateArray().ToList();
            lista.Select(a => a.GetProperty("codigo").GetString()).Should().Equal("SOAT_NO_VIGENTE", CodigosDictamen.AjusteFlit);
            lista[0].GetProperty("texto").GetString().Should().Be(TextoEditado);
            lista[0].GetProperty("ajuste").GetString().Should().Be("EDITADO");
            lista[1].GetProperty("ajuste").GetString().Should().Be("AGREGADO");
        }

        (await VersionFilaAsync(empresa, vid, ct)).Should().BeGreaterThan(versionFilaAntes, "la fila sube su versión (AC5)");

        var evento = (await OutboxDePrueba.EventosAsync(api, DictamenEmitido.Tipo, ct)).Should().ContainSingle().Subject;
        evento.Data.GetProperty("override").GetBoolean().Should().BeTrue();
        evento.Data.GetProperty("ajuste_alertamientos").GetBoolean().Should().BeTrue();
        evento.Payload.Should().NotContain(MotivoAjuste).And.NotContain(TextoEditado);

        await using var db = empresa.Db();
        (await db.Set<Flit.Diagnostico.Api.Modulos.Notificaciones.Notificacion>().AsNoTracking().CountAsync(ct)).Should().Be(0, "D8: el ajuste no notifica");
    }

    // AC21 — dos PUT simultáneos con la misma version_esperada: uno gana y el otro responde 409, nunca 500. La cadena queda 1..2
    // con una sola vigente.
    [Fact]
    public async Task B7_AC21_dos_PUT_concurrentes_uno_gana_y_el_otro_409_nunca_500()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);
        var vid = lote.Vehiculos[0].Id;
        var ruta = RutaBackoffice(lote.Id, vid, empresa.Id);
        using var uno = api.Cliente(TokenBackoffice(api, Guid.CreateVersion7(), corregir: true));
        using var dos = api.Cliente(TokenBackoffice(api, Guid.CreateVersion7(), corregir: true));

        var respuestas = await Task.WhenAll(
            uno.PutAsync(ruta, Ajuste(1, "BLOQUEADO", AlertasAjuste), ct),
            dos.PutAsync(ruta, Ajuste(1, "BLOQUEADO", AlertasAjuste.Replace(TextoEditado, "Otro texto de soporte", StringComparison.Ordinal)), ct));

        var estados = respuestas.Select(r => r.StatusCode).ToList();
        estados.Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var perdedor = respuestas.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(perdedor, ct)).GetProperty("code").GetString()
            .Should().BeOneOf(CodigosDictamen.DictamenDesactualizado, CodigosDictamen.DictamenEnConflicto);
        var filas = await FilasAsync(empresa, vid, ct);
        filas.Select(f => f.Version).Should().Equal(1, 2);
        filas.Should().ContainSingle(f => f.Vigente);
    }

    // AC22 + AC23 — dictamen incoherente o alertamiento fuera del catálogo: 422 sin cambios. Vehículo con trámite: 409
    // VEHICULO_CON_TRAMITE sin confirmar_con_tramite; con él aplica el ajuste y no toca el trámite.
    [Fact]
    public async Task B7_AC22_AC23_incoherente_422_y_vehiculo_con_tramite_409_salvo_confirmacion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);
        var vid = lote.Vehiculos[0].Id;
        var ruta = RutaBackoffice(lote.Id, vid, empresa.Id);
        using var cliente = api.Cliente(TokenBackoffice(api, Guid.CreateVersion7(), corregir: true));

        var casos = new (string Veredicto, string Alertas, string Codigo)[]
        {
            ("VIABLE", AlertasAjuste, CodigosDictamen.DictamenIncoherente),
            ("BLOQUEADO", """[{"codigo":"AJUSTE_FLIT","texto":"Solo una advertencia de soporte","efecto":"ADVIERTE","fuente":"RUNT"}]""", CodigosDictamen.DictamenIncoherente),
            ("BLOQUEADO", """[{"codigo":"INVENTADO","texto":"Regla que no existe","efecto":"BLOQUEA","fuente":"RUNT"}]""", CodigosDictamen.AlertamientoInvalido),
            ("BLOQUEADO", """[{"codigo":"AJUSTE_FLIT","texto":"Fuente rara","efecto":"BLOQUEA","fuente":"NO_ES_FUENTE"}]""", CodigosDictamen.AlertamientoInvalido),
            ("BLOQUEADO", $$"""[{"codigo":"AJUSTE_FLIT","texto":"{{new string('a', 501)}}","efecto":"BLOQUEA","fuente":"RUNT"}]""", CodigosDictamen.AlertamientoInvalido),
        };
        foreach (var (veredicto, alertas, codigo) in casos)
        {
            var respuesta = await cliente.PutAsync(ruta, Ajuste(1, veredicto, alertas), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, alertas);
            (await TableroAyudas.JsonAsync(respuesta, ct)).GetProperty("code").GetString().Should().Be(codigo, alertas);
        }

        (await FilasAsync(empresa, vid, ct)).Should().ContainSingle();

        var tramite = Guid.CreateVersion7();
        await using (var db = empresa.Db())
        {
            var vehiculo = await db.Set<Flit.Diagnostico.Api.Modulos.Vehiculos.VehiculoDiagnostico>().SingleAsync(v => v.Id == vid, ct);
            vehiculo.TramiteId = tramite;
            vehiculo.TramiteNumero = "TRM-B7-0001";
            await db.SaveChangesAsync(ct);
        }

        var sinConfirmar = await cliente.PutAsync(ruta, Ajuste(1, "BLOQUEADO", AlertasAjuste), ct);
        sinConfirmar.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TableroAyudas.JsonAsync(sinConfirmar, ct)).GetProperty("code").GetString().Should().Be(CodigosDictamen.VehiculoConTramite);
        (await FilasAsync(empresa, vid, ct)).Should().ContainSingle();

        var confirmado = await cliente.PutAsync(ruta, Ajuste(1, "BLOQUEADO", AlertasAjuste, confirmarConTramite: true), ct);
        confirmado.StatusCode.Should().Be(HttpStatusCode.OK, await confirmado.Content.ReadAsStringAsync(ct));
        (await FilasAsync(empresa, vid, ct)).Should().HaveCount(2);
        await using (var db = empresa.Db())
            (await db.Set<Flit.Diagnostico.Api.Modulos.Vehiculos.VehiculoDiagnostico>().AsNoTracking().SingleAsync(v => v.Id == vid, ct)).TramiteId.Should().Be(tramite);
    }

    // B-PR6 + P3 por HTTP — sin motivo es 422 MOTIVO_REQUERIDO aunque el veredicto no cambie (el cuerpo legado se retiró); un
    // alertamiento AJUSTE_FLIT con 6 o más dígitos es 422 con el campo señalado. Nada cambia.
    [Fact]
    public async Task B7_BPR6_sin_motivo_siempre_422_y_P3_senala_el_campo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);
        var vid = lote.Vehiculos[0].Id;
        var ruta = RutaBackoffice(lote.Id, vid, empresa.Id);
        using var cliente = api.Cliente(TokenBackoffice(api, Guid.CreateVersion7(), corregir: true));

        using var legado = new StringContent("""{"veredicto":"VIABLE","confirmado":true}""", Encoding.UTF8, "application/json");
        var sinMotivo = await cliente.PutAsync(ruta, legado, ct);
        sinMotivo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await TableroAyudas.JsonAsync(sinMotivo, ct);
        error.GetProperty("code").GetString().Should().Be(CodigosDictamen.MotivoRequerido);
        error.GetProperty("campo").GetString().Should().Be("motivo");

        var conCedula = await cliente.PutAsync(ruta, Ajuste(1, "BLOQUEADO",
            """[{"codigo":"AJUSTE_FLIT","texto":"Propietario 1023456 con embargo","efecto":"BLOQUEA","fuente":"RUNT"}]"""), ct);
        conCedula.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var p3 = await TableroAyudas.JsonAsync(conCedula, ct);
        p3.GetProperty("code").GetString().Should().Be(CodigosDictamen.AlertamientoConDigitos);
        p3.GetProperty("campo").GetString().Should().Be("alertamientos");
        p3.GetRawText().Should().NotContain("1023456", "el error no repite lo digitado");
        (await FilasAsync(empresa, vid, ct)).Should().ContainSingle();
    }

    // AC26 + AC27 + AC28 + AC29 — la compañía ve ajustado_por_flit y ajustado_en (sin autor, motivo ni marca por alertamiento) y
    // la grilla dictamen_ajustado; el backoffice ve las versiones auditadas; un recálculo automático quita la marca y deja la
    // versión ajustada como superada por recálculo. Sin diagnostico.auditoria.ver, las versiones responden 403.
    [Fact]
    public async Task B7_AC26_AC29_compania_ve_la_marca_backoffice_ve_versiones_y_el_recalculo_gana()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await SembrarAsync(empresa, ct);
        var vid = lote.Vehiculos[0].Id;
        var flit = Guid.CreateVersion7();
        var token = TokenBackoffice(api, flit, corregir: true);
        (await api.Cliente(token).PutAsync(RutaBackoffice(lote.Id, vid, empresa.Id), Ajuste(1, "BLOQUEADO", AlertasAjuste), ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var compania = await empresa.Cliente().GetAsync(RutaCompania(lote.Id, vid), ct);
        var textoCompania = await compania.Content.ReadAsStringAsync(ct);
        var dictamen = JsonElement.Parse(textoCompania);
        dictamen.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["version", "veredicto", "estado_redaccion", "alertamientos", "catalogo_version", "creado_en", "ajustado_por_flit", "ajustado_en"]);
        dictamen.GetProperty("ajustado_por_flit").GetBoolean().Should().BeTrue();
        dictamen.GetProperty("ajustado_en").ValueKind.Should().Be(JsonValueKind.String);
        textoCompania.Should().NotContain(MotivoAjuste).And.NotContain("\"ajuste\"").And.NotContain(flit.ToString()).And.NotContain("motivo");

        var grilla = await TableroAyudas.JsonAsync(await empresa.Cliente().GetAsync(TableroAyudas.RutaGrilla(lote.Id), ct), ct);
        var filasGrilla = grilla.GetProperty("vehiculos").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetGuid());
        filasGrilla[vid].GetProperty("dictamen_ajustado").GetBoolean().Should().BeTrue();
        filasGrilla[lote.Vehiculos[1].Id].GetProperty("dictamen_ajustado").GetBoolean().Should().BeFalse();

        var auditor = TokenAuditor(api, flit);
        var versiones = await api.Cliente(auditor).GetAsync(RutaVersiones(lote.Id, vid, empresa.Id), ct);
        versiones.StatusCode.Should().Be(HttpStatusCode.OK, await versiones.Content.ReadAsStringAsync(ct));
        var lista = (await TableroAyudas.JsonAsync(versiones, ct)).GetProperty("versiones").EnumerateArray().ToList();
        lista.Select(v => v.GetProperty("version").GetInt32()).Should().Equal(2, 1);
        lista[0].GetProperty("origen").GetString().Should().Be("AJUSTE_FLIT");
        lista[0].GetProperty("motivo").GetString().Should().Be(MotivoAjuste);
        lista[0].GetProperty("usuario").GetProperty("id").GetGuid().Should().Be(TableroAyudas.Usuario(token));
        lista[0].GetProperty("alertamientos").EnumerateArray().Select(a => a.GetProperty("ajuste").GetString()).Should().Equal("EDITADO", "AGREGADO");
        lista[0].GetProperty("eliminados").EnumerateArray().Select(a => a.GetProperty("codigo").GetString()).Should().Equal("NO_ESTA");
        lista[0].GetProperty("superado_por_recalculo").GetBoolean().Should().BeFalse();
        lista[1].GetProperty("origen").GetString().Should().Be("AUTOMATICO");
        lista[1].GetProperty("motivo").ValueKind.Should().Be(JsonValueKind.Null);
        lista[1].GetProperty("usuario").ValueKind.Should().Be(JsonValueKind.Null);
        (await LecturasAsync(api, flit, ct)).Should().Contain(l => l.Ruta.Contains("/dictamen/versiones", StringComparison.Ordinal));

        var sinAuditoria = await api.Cliente(TokenBackoffice(api, flit, corregir: true)).GetAsync(RutaVersiones(lote.Id, vid, empresa.Id), ct);
        sinAuditoria.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // D7: el recálculo automático inserta la versión 3 sin marca.
        await using (var db = empresa.Db())
        {
            var vigente = await db.Set<DictamenVehiculo>().SingleAsync(d => d.VehiculoId == vid && d.Vigente, ct);
            vigente.Vigente = false;
            db.Add(EmisorDictamen.FilaAlCerrar(empresa.Id, vid, 3, "TRASPASO_STANDARD", new DictamenEvaluado("VIABLE", []), false, true, Ahora));
            await db.SaveChangesAsync(ct);
        }

        var recalculado = await TableroAyudas.JsonAsync(await empresa.Cliente().GetAsync(RutaCompania(lote.Id, vid), ct), ct);
        recalculado.GetProperty("ajustado_por_flit").GetBoolean().Should().BeFalse();
        recalculado.GetProperty("ajustado_en").ValueKind.Should().Be(JsonValueKind.Null);
        var tras = (await TableroAyudas.JsonAsync(await api.Cliente(auditor).GetAsync(RutaVersiones(lote.Id, vid, empresa.Id), ct), ct))
            .GetProperty("versiones").EnumerateArray().ToList();
        tras.Select(v => v.GetProperty("version").GetInt32()).Should().Equal(3, 2, 1);
        tras[0].GetProperty("origen").GetString().Should().Be("AUTOMATICO");
        tras[1].GetProperty("superado_por_recalculo").GetBoolean().Should().BeTrue();
    }

    private const string MotivoAjuste = "Soporte verifico el SOAT con la aseguradora, radicado 20261113";

    private const string TextoEditado = "Soporte confirmo que el SOAT esta vencido";

    private const string AlertasAjuste =
        """[{"codigo":"SOAT_NO_VIGENTE","texto":"Soporte confirmo que el SOAT esta vencido","efecto":"BLOQUEA","fuente":"RUNT"},{"codigo":"AJUSTE_FLIT","texto":"Revisar el RUNT antes del traspaso","efecto":"ADVIERTE","fuente":"RUNT"}]""";

    private static StringContent Ajuste(int versionEsperada, string veredicto, string alertamientos, bool confirmarConTramite = false) =>
        new($$"""{"version_esperada":{{versionEsperada}},"veredicto":"{{veredicto}}","alertamientos":{{alertamientos}},"motivo":"{{MotivoAjuste}}"{{(confirmarConTramite ? ",\"confirmar_con_tramite\":true" : string.Empty)}}}""",
            Encoding.UTF8, "application/json");

    private static string RutaVersiones(Guid lote, Guid vehiculo, Guid empresa) =>
        $"{Program.Prefijo}/backoffice/diagnosticos/{lote}/vehiculos/{vehiculo}/dictamen/versiones?empresa={empresa}";

    private static string TokenAuditor(ApiDePrueba api, Guid flit) =>
        api.Token(
            empresa: flit,
            permisos: [Permisos.BackofficeVer, Permisos.AuditoriaVer],
            roles: ["backoffice_diagnostico"],
            tipoEmpresa: UsuarioActual.TipoEmpresaFlit);

    private static async Task<long> VersionFilaAsync(EmpresaDePrueba empresa, Guid vehiculo, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Flit.Diagnostico.Api.Modulos.Vehiculos.VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.Id == vehiculo).Select(v => v.VersionTablero).SingleAsync(ct);
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
