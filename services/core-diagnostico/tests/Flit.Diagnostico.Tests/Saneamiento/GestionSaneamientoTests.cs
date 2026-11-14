using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Gestión FLIT de un saneamiento desde el backoffice (plan §6.2 grupo B y «tomar varios»; HU #14576 AC9–AC13, AC17–AC24, AC27,
/// AC35, AC43, AC44; seguridad SP-1, SP-2, SP-3, SP-6, SP-13 con E3 y la prueba de <c>RequiereOperacionFlit</c>). PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~GestionSaneamientoTests
/// </code>
/// </example>
public sealed class GestionSaneamientoTests
{
    private const string MotivoValido = "Pago verificado en el portal del organismo.";

    // AC9 — cada transición válida de FLIT con la version vigente: estado, evento con es_flit y la copia en el vehículo.
    [Theory]
    [InlineData("SOLICITADO", "tomar", "EN_GESTION", "TOMADO")]
    [InlineData("SOLICITADO", "anular", "ANULADO", "ANULADO_FLIT")]
    [InlineData("EN_GESTION", "anular", "ANULADO", "ANULADO_FLIT")]
    [InlineData("EN_GESTION", "cerrar_saneado", "SANEADO", "CERRADO_SANEADO")]
    [InlineData("EN_GESTION", "cerrar_no_procede", "NO_PROCEDE", "CERRADO_NO_PROCEDE")]
    [InlineData("SANEADO", "reabrir", "EN_GESTION", "REABIERTO")]
    [InlineData("NO_PROCEDE", "reabrir", "EN_GESTION", "REABIERTO")]
    public async Task HU14576_AC9_transiciones_validas_de_FLIT(string desde, string accion, string hasta, string evento)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO101", Estado(desde), ct);
        if (accion == "cerrar_saneado")
            await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.SimitVehiculo, ResultadoRenglonSaneamiento.Saneado, ct);
        var flit = Guid.NewGuid();
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api, usuario: flit));

        var respuesta = await Accion(cliente, empresa, s.Id, accion, await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await GestionDePrueba.JsonAsync(respuesta, ct);
        json.GetProperty("estado").GetString().Should().Be(hasta);
        json.GetProperty("historial").EnumerateArray().Last().GetProperty("evento").GetString().Should().Be(evento);
        var ultimo = (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Last();
        ultimo.EsFlit.Should().BeTrue();
        ultimo.UsuarioId.Should().Be(flit);
        await using var db = empresa.Db();
        var vehiculo = await db.Set<VehiculoDiagnostico>().AsNoTracking().SingleAsync(v => v.Id == lote.Vehiculos[0].Id, ct);
        vehiculo.SaneamientoEstado.Should().Be(hasta == "ANULADO" ? null : Estado(hasta), "la columna refleja el estado nuevo en la misma transacción");
        vehiculo.VersionTablero.Should().BeGreaterThan(5, "la grilla incremental recoge la fila");
    }

    // AC10 — transición fuera de la tabla: 409 TRANSICION_INVALIDA y nada cambia.
    [Theory]
    [InlineData("EN_GESTION", "tomar")]
    [InlineData("SOLICITADO", "cerrar_no_procede")]
    [InlineData("EN_GESTION", "reabrir")]
    [InlineData("SANEADO", "anular")]
    [InlineData("ANULADO", "tomar")]
    [InlineData("ANULADO", "reabrir")]
    public async Task HU14576_AC10_transiciones_invalidas_responden_409(string desde, string accion)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO102", Estado(desde), ct);
        var antes = await GestionDePrueba.EventosAsync(empresa, s.Id, ct);

        var respuesta = await Accion(api.Cliente(GestionDePrueba.TokenFlit(api)), empresa, s.Id, accion, await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict, await respuesta.Content.ReadAsStringAsync(ct));
        (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be("TRANSICION_INVALIDA");
        (await GestionDePrueba.LeerAsync(empresa, s.Id, ct)).Estado.Should().Be(Estado(desde));
        (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Should().HaveCount(antes.Count);
    }

    // AC11 + prueba de RequiereOperacionFlit (security-previa, nota final) — un usuario de otra compañía con backoffice.ver y
    // saneamiento.gestionar, o un operador FLIT sin gestionar, recibe 403 en los grupos A y B.
    [Fact]
    public async Task HU14576_AC11_actor_incorrecto_recibe_403_en_los_grupos_A_y_B()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO103", EstadoSaneamiento.Solicitado, ct);
        var cliente = api.Token(empresa: empresa.Id, permisos: [Permisos.BackofficeVer, Permisos.SaneamientoGestionar, Permisos.DiagnosticosVer],
            roles: ["admin_diagnostico"], tipoEmpresa: "CLIENTE");
        var flitSinGestionar = GestionDePrueba.TokenFlit(api, gestionar: false);
        (HttpMethod Metodo, string Ruta)[] rutas =
        [
            (HttpMethod.Get, GestionDePrueba.Base), (HttpMethod.Get, GestionDePrueba.Base + "/resumen"), (HttpMethod.Get, GestionDePrueba.Base + "/excel"),
            (HttpMethod.Post, GestionDePrueba.Base + "/tomar"), (HttpMethod.Get, GestionDePrueba.Ruta(s.Id, empresa.Id)),
            (HttpMethod.Post, GestionDePrueba.Ruta(s.Id, empresa.Id, "/tomar")), (HttpMethod.Get, GestionDePrueba.Ruta(s.Id, empresa.Id, "/deuda")),
        ];

        foreach (var token in new[] { cliente, flitSinGestionar })
        {
            foreach (var (metodo, ruta) in rutas)
            {
                using var pedido = new HttpRequestMessage(metodo, ruta);
                var respuesta = await api.Cliente(token).SendAsync(pedido, ct);
                respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{metodo} {ruta}");
            }
        }

        (await GestionDePrueba.LeerAsync(empresa, s.Id, ct)).Estado.Should().Be(EstadoSaneamiento.Solicitado);
        var sesion = await api.Cliente(cliente).GetAsync(Flit.Diagnostico.Api.Program.Prefijo + "/sesion", ct);
        (await GestionDePrueba.JsonAsync(sesion, ct)).GetProperty("capacidades").GetProperty("gestionarSaneamiento").GetBoolean().Should().BeFalse();
    }

    // AC12 — motivo ausente, corto, largo o con salto al inicio: 422 MOTIVO_REQUERIDO con campo motivo y nada cambia.
    [Theory]
    [InlineData("anular", "EN_GESTION", null)]
    [InlineData("cerrar_no_procede", "EN_GESTION", "123456789")]
    [InlineData("reabrir", "SANEADO", "501")]
    [InlineData("reabrir", "NO_PROCEDE", "\nMotivo con salto inicial")]
    public async Task HU14576_AC12_motivo_obligatorio_y_su_formato(string accion, string desde, string? motivo)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO104", Estado(desde), ct);
        var texto = motivo == "501" ? new string('m', 501) : motivo;

        var respuesta = await Accion(api.Cliente(GestionDePrueba.TokenFlit(api)), empresa, s.Id, accion, await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct, texto, conMotivo: motivo is not null);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await GestionDePrueba.JsonAsync(respuesta, ct);
        json.GetProperty("code").GetString().Should().Be("MOTIVO_REQUERIDO");
        json.GetProperty("campo").GetString().Should().Be("motivo");
        (await GestionDePrueba.LeerAsync(empresa, s.Id, ct)).Estado.Should().Be(Estado(desde));
    }

    // AC13 — reabrir conserva tomado_en y los renglones, y limpia el cierre.
    [Fact]
    public async Task HU14576_AC13_reapertura_limpia_el_cierre_y_conserva_tomado_y_renglones()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO105", EstadoSaneamiento.Saneado, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Saneado, ct);

        var respuesta = await Accion(api.Cliente(GestionDePrueba.TokenFlit(api)), empresa, s.Id, "reabrir", await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var guardado = await GestionDePrueba.LeerAsync(empresa, s.Id, ct);
        guardado.Estado.Should().Be(EstadoSaneamiento.EnGestion);
        guardado.CerradoEn.Should().BeNull();
        guardado.CerradoPor.Should().BeNull();
        guardado.MotivoCierre.Should().BeNull();
        guardado.TomadoEn.Should().BeCloseTo(s.TomadoEn!.Value, TimeSpan.FromMilliseconds(1));
        var renglones = (await GestionDePrueba.JsonAsync(respuesta, ct)).GetProperty("renglones").EnumerateArray().ToList();
        renglones.Should().ContainSingle().Which.GetProperty("id").GetGuid().Should().Be(renglon.Id);
        renglones[0].GetProperty("resultado").GetString().Should().Be("SANEADO", "los renglones no se regeneran al reabrir");
    }

    // AC14 en el backoffice + AC43 — tomar uno genera los renglones con hallazgo (SIMIT del vehículo con comparendos).
    [Fact]
    public async Task HU14576_AC43_tomar_genera_los_renglones_con_hallazgo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO106", EstadoSaneamiento.Solicitado, ct);
        await Tablero.Tablero.PayloadAsync(empresa, lote.Celda("SBO106", Fuente.SimitVehiculo),
            """{"semaforo":"ROJO","cantidad_comparendos":1,"comparendos":[{"numero":"SBO-C-1","fecha":"2026-09-30","valor":"468500.50","estado":"PENDIENTE"}]}""", ct);

        var respuesta = await api.Cliente(GestionDePrueba.TokenFlit(api)).PostAsync(GestionDePrueba.Ruta(s.Id, empresa.Id, "/tomar"), null, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var renglones = (await GestionDePrueba.JsonAsync(respuesta, ct)).GetProperty("renglones").EnumerateArray().ToList();
        renglones.Should().ContainSingle(r => r.GetProperty("fuente").GetString() == "SIMIT_VEHICULO"
            && r.GetProperty("hallazgo").GetString() == "DEUDA" && r.GetProperty("automatico").GetBoolean()
            && r.GetProperty("resultado").GetString() == "PENDIENTE");
    }

    // AC17 — editar un renglón con su version; repetir con la misma version es 409 VERSION_DESACTUALIZADA.
    [Fact]
    public async Task HU14576_AC17_editar_un_renglon_y_version_desactualizada()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO107", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.SimitPropietario, ResultadoRenglonSaneamiento.Pendiente, ct);
        var version = await GestionDePrueba.VersionRenglonAsync(empresa, renglon.Id, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));
        var cuerpo = new { resultado = "SANEADO", nota = new string('n', 500), version };

        var primera = await GestionDePrueba.PutAsync(cliente, GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id), cuerpo, ct);
        var segunda = await GestionDePrueba.PutAsync(cliente, GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id), cuerpo, ct);

        primera.StatusCode.Should().Be(HttpStatusCode.OK, await primera.Content.ReadAsStringAsync(ct));
        var json = await GestionDePrueba.JsonAsync(primera, ct);
        json.GetProperty("resultado").GetString().Should().Be("SANEADO");
        json.GetProperty("version").GetUInt32().Should().NotBe(version);
        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SaneamientoDePrueba.CodigoAsync(segunda, ct)).Should().Be("VERSION_DESACTUALIZADA");
        (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Should().ContainSingle(e => e.Evento == TipoEventoSaneamiento.RenglonActualizado && e.Fuente == FuenteSaneamiento.SimitPropietario);
    }

    // AC18 + SP-2 — agregar dos OTRO, descripciones fuera de rango 422, y quitar un automático con borrado lógico.
    [Fact]
    public async Task HU14576_AC18_agregar_y_quitar_renglones_con_borrado_logico()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO108", EstadoSaneamiento.EnGestion, ct);
        var automatico = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Soat, ResultadoRenglonSaneamiento.Pendiente, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));
        var ruta = GestionDePrueba.Ruta(s.Id, empresa.Id, "/renglones");

        var uno = await GestionDePrueba.PostAsync(cliente, ruta, new { descripcion = "Embargo judicial" }, ct);
        var dos = await GestionDePrueba.PostAsync(cliente, ruta, new { descripcion = "Prenda sin levantar" }, ct);
        var corta = await GestionDePrueba.PostAsync(cliente, ruta, new { descripcion = "ab" }, ct);
        var larga = await GestionDePrueba.PostAsync(cliente, ruta, new { descripcion = new string('d', 201) }, ct);
        var quitar = await cliente.DeleteAsync(GestionDePrueba.RutaRenglon(s.Id, automatico.Id, empresa.Id), ct);

        // D-65: agregar un OTRO y quitar un renglón devuelven el saneamiento completo (200), con su version nueva.
        uno.StatusCode.Should().Be(HttpStatusCode.OK, await uno.Content.ReadAsStringAsync(ct));
        dos.StatusCode.Should().Be(HttpStatusCode.OK);
        var trasDos = await GestionDePrueba.JsonAsync(dos, ct);
        trasDos.GetProperty("renglones").EnumerateArray().Count(r => r.GetProperty("fuente").GetString() == "OTRO" && !r.GetProperty("automatico").GetBoolean()).Should().Be(2);
        trasDos.GetProperty("version").GetUInt32().Should().Be(await GestionDePrueba.VersionAsync(empresa, s.Id, ct));
        foreach (var mala in new[] { corta, larga })
        {
            mala.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await GestionDePrueba.JsonAsync(mala, ct)).GetProperty("campo").GetString().Should().Be("descripcion");
        }

        quitar.StatusCode.Should().Be(HttpStatusCode.OK, await quitar.Content.ReadAsStringAsync(ct));
        (await GestionDePrueba.JsonAsync(quitar, ct)).GetProperty("renglones").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(automatico.Id);
        await using var db = empresa.Db();
        var fila = await db.Set<RenglonSaneamiento>().AsNoTracking().SingleAsync(r => r.Id == automatico.Id, ct);
        fila.QuitadoEn.Should().NotBeNull("SP-2: la fila sigue, con borrado lógico");
        (await db.Set<RenglonSaneamiento>().CountAsync(r => r.SaneamientoId == s.Id && r.Fuente == FuenteSaneamiento.Otro && !r.Automatico, ct)).Should().Be(2);
        var eventos = await GestionDePrueba.EventosAsync(empresa, s.Id, ct);
        eventos.Count(e => e.Evento == TipoEventoSaneamiento.RenglonAgregado).Should().Be(2);
        eventos.Should().ContainSingle(e => e.Evento == TipoEventoSaneamiento.RenglonQuitado && e.RenglonId == automatico.Id && e.Fuente == FuenteSaneamiento.Soat);
        var detalle = await GestionDePrueba.JsonAsync(await cliente.GetAsync(GestionDePrueba.Ruta(s.Id, empresa.Id), ct), ct);
        detalle.GetProperty("renglones").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(automatico.Id);
    }

    // AC19 — fuera de EN_GESTION toda edición responde 409 NO_EDITABLE y nada cambia.
    [Theory]
    [InlineData("SOLICITADO")]
    [InlineData("SANEADO")]
    [InlineData("NO_PROCEDE")]
    [InlineData("ANULADO")]
    public async Task HU14576_AC19_edicion_fuera_de_EN_GESTION_es_409_NO_EDITABLE(string estado)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO109", Estado(estado), ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Pendiente, ct);
        var version = await GestionDePrueba.VersionAsync(empresa, s.Id, ct);
        var versionRenglon = await GestionDePrueba.VersionRenglonAsync(empresa, renglon.Id, ct);
        var antes = (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Count;
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));

        HttpResponseMessage[] respuestas =
        [
            await GestionDePrueba.PutAsync(cliente, GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id), new { resultado = "SANEADO", nota = (string?)null, version = versionRenglon }, ct),
            await GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/renglones"), new { descripcion = "Embargo judicial" }, ct),
            await cliente.DeleteAsync(GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id), ct),
            await GestionDePrueba.PutAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/observacion"), new { observacion = "Texto", version }, ct),
            await cliente.PutAsync(GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id, "/evidencia"), GestionDePrueba.Archivo(GestionDePrueba.PdfValido), ct),
            await cliente.DeleteAsync(GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id, "/evidencia"), ct),
        ];

        foreach (var respuesta in respuestas)
        {
            respuesta.StatusCode.Should().Be(HttpStatusCode.Conflict, await respuesta.Content.ReadAsStringAsync(ct));
            (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be("NO_EDITABLE");
        }

        (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Should().HaveCount(antes);
        (await GestionDePrueba.VersionRenglonAsync(empresa, renglon.Id, ct)).Should().Be(versionRenglon);
    }

    // AC20 — la observación general queda guardada, va al historial y la ve la compañía; 1001 caracteres es 422.
    [Fact]
    public async Task HU14576_AC20_observacion_general_de_FLIT()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO110", EstadoSaneamiento.EnGestion, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));
        var texto = new string('o', 1000);

        var larga = await GestionDePrueba.PutAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/observacion"), new { observacion = texto + "o", version = await GestionDePrueba.VersionAsync(empresa, s.Id, ct) }, ct);
        var buena = await GestionDePrueba.PutAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/observacion"), new { observacion = texto, version = await GestionDePrueba.VersionAsync(empresa, s.Id, ct) }, ct);

        larga.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await GestionDePrueba.JsonAsync(larga, ct)).GetProperty("campo").GetString().Should().Be("observacion");
        buena.StatusCode.Should().Be(HttpStatusCode.OK, await buena.Content.ReadAsStringAsync(ct));
        (await GestionDePrueba.JsonAsync(buena, ct)).GetProperty("observacion_flit").GetString().Should().Be(texto);
        (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Should().ContainSingle(e => e.Evento == TipoEventoSaneamiento.ObservacionEditada);
        var delCliente = await api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id)).GetAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), ct);
        (await GestionDePrueba.JsonAsync(delCliente, ct)).GetProperty("observacion_flit").GetString().Should().Be(texto);
    }

    // AC21 + AC22 — SANEADO con renglones resueltos y uno saneado; sin renglones, con un PENDIENTE o todos NO_PROCEDE es 422.
    [Theory]
    [InlineData("SANEADO,NO_PROCEDE", 200)]
    [InlineData("", 422)]
    [InlineData("SANEADO,PENDIENTE", 422)]
    [InlineData("NO_PROCEDE,NO_PROCEDE", 422)]
    public async Task HU14576_AC21_AC22_cierre_como_SANEADO(string resultados, int esperado)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO111", EstadoSaneamiento.EnGestion, ct);
        FuenteSaneamiento[] fuentes = [FuenteSaneamiento.Impuesto, FuenteSaneamiento.Soat];
        var lista = resultados.Split(',', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lista.Length; i++)
            await GestionDePrueba.RenglonAsync(empresa, s.Id, fuentes[i], Resultado(lista[i]), ct);

        var respuesta = await Accion(api.Cliente(GestionDePrueba.TokenFlit(api)), empresa, s.Id, "cerrar_saneado", await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct);

        ((int)respuesta.StatusCode).Should().Be(esperado, await respuesta.Content.ReadAsStringAsync(ct));
        var guardado = await GestionDePrueba.LeerAsync(empresa, s.Id, ct);
        if (esperado == 200)
        {
            guardado.Estado.Should().Be(EstadoSaneamiento.Saneado);
            guardado.CerradoEn.Should().NotBeNull();
            guardado.CerradoPor.Should().NotBeNull();
            return;
        }

        (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be("CIERRE_INCOMPLETO");
        guardado.Estado.Should().Be(EstadoSaneamiento.EnGestion);
    }

    // AC23 — NO_PROCEDE sin renglones o con PENDIENTE los deja como estén; un OTRO saneado permite SANEADO.
    [Fact]
    public async Task HU14576_AC23_cierre_NO_PROCEDE_y_vehiculo_sin_hallazgos()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, sinRenglones) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO112", EstadoSaneamiento.EnGestion, ct);
        var (_, conPendiente) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO113", EstadoSaneamiento.EnGestion, ct);
        var pendiente = await GestionDePrueba.RenglonAsync(empresa, conPendiente.Id, FuenteSaneamiento.Rtm, ResultadoRenglonSaneamiento.Pendiente, ct);
        var (_, conOtro) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO114", EstadoSaneamiento.EnGestion, ct);
        await GestionDePrueba.RenglonAsync(empresa, conOtro.Id, FuenteSaneamiento.Otro, ResultadoRenglonSaneamiento.Saneado, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));

        var r1 = await Accion(cliente, empresa, sinRenglones.Id, "cerrar_no_procede", await GestionDePrueba.VersionAsync(empresa, sinRenglones.Id, ct), ct);
        var r2 = await Accion(cliente, empresa, conPendiente.Id, "cerrar_no_procede", await GestionDePrueba.VersionAsync(empresa, conPendiente.Id, ct), ct);
        var r3 = await Accion(cliente, empresa, conOtro.Id, "cerrar_saneado", await GestionDePrueba.VersionAsync(empresa, conOtro.Id, ct), ct);

        r1.StatusCode.Should().Be(HttpStatusCode.OK, await r1.Content.ReadAsStringAsync(ct));
        (await GestionDePrueba.LeerAsync(empresa, sinRenglones.Id, ct)).MotivoCierre.Should().Be(MotivoValido);
        (await GestionDePrueba.EventosAsync(empresa, sinRenglones.Id, ct)).Last().Evento.Should().Be(TipoEventoSaneamiento.CerradoNoProcede);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var db = empresa.Db())
            (await db.Set<RenglonSaneamiento>().AsNoTracking().SingleAsync(r => r.Id == pendiente.Id, ct)).Resultado.Should().Be(ResultadoRenglonSaneamiento.Pendiente);
        r3.StatusCode.Should().Be(HttpStatusCode.OK, await r3.Content.ReadAsStringAsync(ct));
        (await GestionDePrueba.LeerAsync(empresa, conOtro.Id, ct)).Estado.Should().Be(EstadoSaneamiento.Saneado);
    }

    // AC24 — cerrar como SANEADO no toca el dictamen; un reproceso no pisa la columna saneamiento_estado (C12).
    [Fact]
    public async Task HU14576_AC24_cerrar_no_toca_el_dictamen_y_el_reproceso_no_pisa_la_columna()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO115", EstadoSaneamiento.EnGestion, ct);
        var vehiculo = lote.Vehiculos[0].Id;
        await SaneamientoDePrueba.DictamenAsync(empresa, vehiculo, "BLOQUEADO", ct);
        await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.SimitVehiculo, ResultadoRenglonSaneamiento.Saneado, ct);

        var cierre = await Accion(api.Cliente(GestionDePrueba.TokenFlit(api)), empresa, s.Id, "cerrar_saneado", await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct);
        var reproceso = await api.Cliente(api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConsultasReprocesar], roles: ["admin_diagnostico"]))
            .PostAsync($"{Flit.Diagnostico.Api.Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{lote.Id}/vehiculos/{vehiculo}/reintentar", null, ct);

        cierre.StatusCode.Should().Be(HttpStatusCode.OK, await cierre.Content.ReadAsStringAsync(ct));
        await using var db = empresa.Db();
        var dictamenes = await db.Set<DictamenVehiculo>().AsNoTracking().Where(d => d.VehiculoId == vehiculo).ToListAsync(ct);
        dictamenes.Should().ContainSingle("no se emite dictamen nuevo").Which.Should().Match<DictamenVehiculo>(d => d.Vigente && d.Version == 1 && d.Veredicto == "BLOQUEADO");
        ((int)reproceso.StatusCode).Should().BeLessThan(500, await reproceso.Content.ReadAsStringAsync(ct));
        (await db.Set<VehiculoDiagnostico>().AsNoTracking().SingleAsync(v => v.Id == vehiculo, ct)).SaneamientoEstado.Should().Be(EstadoSaneamiento.Saneado);
        (await GestionDePrueba.LeerAsync(empresa, s.Id, ct)).Estado.Should().Be(EstadoSaneamiento.Saneado);
    }

    // AC27 — FLIT ve el historial completo, también RENGLON_ACTUALIZADO y OBSERVACION_EDITADA, con usuario_id en cada evento.
    [Fact]
    public async Task HU14576_AC27_detalle_para_FLIT_con_historial_completo_y_usuario()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO116", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Pendiente, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));
        await GestionDePrueba.PutAsync(cliente, GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id),
            new { resultado = "SANEADO", nota = (string?)null, version = await GestionDePrueba.VersionRenglonAsync(empresa, renglon.Id, ct) }, ct);
        await GestionDePrueba.PutAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/observacion"), new { observacion = "Revisado", version = await GestionDePrueba.VersionAsync(empresa, s.Id, ct) }, ct);

        var respuesta = await cliente.GetAsync(GestionDePrueba.Ruta(s.Id, empresa.Id), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await GestionDePrueba.JsonAsync(respuesta, ct);
        var historial = json.GetProperty("historial").EnumerateArray().ToList();
        historial.Select(e => e.GetProperty("evento").GetString()).Should().Contain(["SOLICITADO", "RENGLON_ACTUALIZADO", "OBSERVACION_EDITADA"]);
        historial.Should().OnlyContain(e => e.GetProperty("usuario_id").GetGuid() != Guid.Empty);
        // D-60: contexto del encabezado de PS-5; D-63: el servidor manda los estados de cada evento.
        json.GetProperty("vehiculo_id").GetGuid().Should().Be(lote.Vehiculos[0].Id);
        json.GetProperty("diagnostico_id").GetGuid().Should().Be(lote.Id);
        json.GetProperty("diagnostico_nombre").GetString().Should().Be("Lote del tablero");
        json.GetProperty("placa").GetString().Should().Be("SBO116");
        json.GetProperty("veredicto").ValueKind.Should().Be(JsonValueKind.Null, "sin dictamen vigente");
        historial[0].GetProperty("estado_nuevo").GetString().Should().Be("SOLICITADO");
        historial.Single(e => e.GetProperty("evento").GetString() == "RENGLON_ACTUALIZADO").GetProperty("estado_nuevo").ValueKind.Should().Be(JsonValueKind.Null);
        json.TryGetProperty("solicitado_por_mi", out _).Should().BeFalse("D-22: el tipo de FLIT no lleva solicitado_por_mi");
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct)).RootElement.GetRawText().Should().NotContain("evidencia_referencia").And.NotContain("sha256");
    }

    // AC44 — dos operadores con la misma version: el segundo recibe 409 VERSION_DESACTUALIZADA; sin version es 422 VERSION_INVALIDA.
    [Fact]
    public async Task HU14576_AC44_concurrencia_por_version()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO117", EstadoSaneamiento.EnGestion, ct);
        var version = await GestionDePrueba.VersionAsync(empresa, s.Id, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));

        var primero = await GestionDePrueba.PutAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/observacion"), new { observacion = "Primer operador", version }, ct);
        var segundo = await GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/anular"), new { motivo = MotivoValido, version }, ct);
        var sinVersion = await GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/anular"), new { motivo = MotivoValido }, ct);
        var versionTexto = await GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(s.Id, empresa.Id, "/cerrar"), new { resultado = "NO_PROCEDE", motivo = MotivoValido, version = "x" }, ct);

        primero.StatusCode.Should().Be(HttpStatusCode.OK, await primero.Content.ReadAsStringAsync(ct));
        segundo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SaneamientoDePrueba.CodigoAsync(segundo, ct)).Should().Be("VERSION_DESACTUALIZADA");
        foreach (var mala in new[] { sinVersion, versionTexto })
        {
            mala.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await SaneamientoDePrueba.CodigoAsync(mala, ct)).Should().Be("VERSION_INVALIDA");
        }

        (await GestionDePrueba.LeerAsync(empresa, s.Id, ct)).Estado.Should().Be(EstadoSaneamiento.EnGestion);
    }

    // AC43 + SP-1 — tomar varios: lote mixto de A y B, uno EN_GESTION, uno inexistente y un par empresa/id que no coincide.
    [Fact]
    public async Task HU14576_AC43_SP1_tomar_varios_por_empresa_en_una_transaccion()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = (DosEmpresas.En(api).A, DosEmpresas.En(api).B);
        var (_, deA) = await GestionDePrueba.SaneamientoAsync(a, "SBO118", EstadoSaneamiento.Solicitado, ct);
        var (_, deB) = await GestionDePrueba.SaneamientoAsync(b, "SBO119", EstadoSaneamiento.Solicitado, ct);
        var (_, enGestion) = await GestionDePrueba.SaneamientoAsync(a, "SBO120", EstadoSaneamiento.EnGestion, ct);
        var (_, cruzado) = await GestionDePrueba.SaneamientoAsync(a, "SBO121", EstadoSaneamiento.Solicitado, ct);
        var inexistente = Guid.NewGuid();
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));

        var respuesta = await GestionDePrueba.PostAsync(cliente, GestionDePrueba.Base + "/tomar", new
        {
            items = new object[]
            {
                new { empresa = a.Id, id = deA.Id }, new { empresa = b.Id, id = deB.Id }, new { empresa = a.Id, id = enGestion.Id },
                new { empresa = a.Id, id = inexistente }, new { empresa = b.Id, id = cruzado.Id }, new { empresa = a.Id, id = deA.Id },
            },
        }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await GestionDePrueba.JsonAsync(respuesta, ct);
        json.GetProperty("tomados").GetInt32().Should().Be(2);
        var omitidos = json.GetProperty("omitidos").EnumerateArray().ToDictionary(o => o.GetProperty("id").GetGuid(), o => o.GetProperty("motivo").GetString());
        omitidos.Should().BeEquivalentTo(new Dictionary<Guid, string?>
        {
            [enGestion.Id] = "TRANSICION_INVALIDA", [inexistente] = "NO_ENCONTRADO", [cruzado.Id] = "NO_ENCONTRADO",
        });
        (await GestionDePrueba.LeerAsync(a, deA.Id, ct)).Should().Match<SaneamientoEntidad>(s => s.Estado == EstadoSaneamiento.EnGestion && s.TenantId == a.Id);
        (await GestionDePrueba.LeerAsync(b, deB.Id, ct)).Should().Match<SaneamientoEntidad>(s => s.Estado == EstadoSaneamiento.EnGestion && s.TenantId == b.Id);
        (await GestionDePrueba.LeerAsync(a, cruzado.Id, ct)).Estado.Should().Be(EstadoSaneamiento.Solicitado, "el par empresa/id no coincide: no se toca");
    }

    // AC43 + SP-1 — lista vacía, de 201 ítems o con una empresa inválida: 400 LISTA_INVALIDA.
    [Theory]
    [InlineData("vacia")]
    [InlineData("201")]
    [InlineData("empresa_vacia")]
    [InlineData("empresa_texto")]
    public async Task HU14576_AC43_lista_invalida_de_tomar_varios(string caso)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        object items = caso switch
        {
            "vacia" => Array.Empty<object>(),
            "201" => Enumerable.Range(0, 201).Select(_ => new { empresa = Guid.NewGuid(), id = Guid.NewGuid() }).ToArray(),
            "empresa_vacia" => new[] { new { empresa = Guid.Empty.ToString(), id = Guid.NewGuid().ToString() } },
            _ => new[] { new { empresa = "no-es-uuid", id = Guid.NewGuid().ToString() } },
        };

        var respuesta = await GestionDePrueba.PostAsync(api.Cliente(GestionDePrueba.TokenFlit(api)), GestionDePrueba.Base + "/tomar", new { items }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await respuesta.Content.ReadAsStringAsync(ct));
        (await SaneamientoDePrueba.CodigoAsync(respuesta, ct)).Should().Be("LISTA_INVALIDA");
    }

    // SP-6 — cadena de pertenencia: sid de A con ?empresa=B, rid de otro saneamiento y una ruta sin uuid dan 404 sin cambios.
    [Fact]
    public async Task HU14576_SP6_cadena_de_pertenencia_y_rutas_tipadas()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var (a, b) = (DosEmpresas.En(api).A, DosEmpresas.En(api).B);
        var (_, s) = await GestionDePrueba.SaneamientoAsync(a, "SBO122", EstadoSaneamiento.EnGestion, ct);
        var (_, otro) = await GestionDePrueba.SaneamientoAsync(a, "SBO123", EstadoSaneamiento.EnGestion, ct);
        var ajeno = await GestionDePrueba.RenglonAsync(a, otro.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Pendiente, ct);
        var version = await GestionDePrueba.VersionRenglonAsync(a, ajeno.Id, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));

        HttpResponseMessage[] respuestas =
        [
            await cliente.GetAsync(GestionDePrueba.Ruta(s.Id, b.Id), ct),
            await GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(s.Id, b.Id, "/anular"), new { motivo = MotivoValido, version = await GestionDePrueba.VersionAsync(a, s.Id, ct) }, ct),
            await GestionDePrueba.PutAsync(cliente, GestionDePrueba.RutaRenglon(s.Id, ajeno.Id, a.Id), new { resultado = "SANEADO", nota = (string?)null, version }, ct),
            await cliente.DeleteAsync(GestionDePrueba.RutaRenglon(s.Id, ajeno.Id, a.Id), ct),
            await cliente.PutAsync(GestionDePrueba.RutaRenglon(s.Id, ajeno.Id, a.Id, "/evidencia"), GestionDePrueba.Archivo(GestionDePrueba.PdfValido), ct),
            await cliente.GetAsync(GestionDePrueba.RutaRenglon(s.Id, ajeno.Id, a.Id, "/evidencia"), ct),
            await cliente.GetAsync($"{GestionDePrueba.Base}/no-es-guid?empresa={a.Id}", ct),
        ];

        foreach (var respuesta in respuestas)
            respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, respuesta.RequestMessage!.RequestUri!.ToString());
        (await GestionDePrueba.LeerAsync(a, s.Id, ct)).Estado.Should().Be(EstadoSaneamiento.EnGestion);
        (await GestionDePrueba.VersionRenglonAsync(a, ajeno.Id, ct)).Should().Be(version);
    }

    // SP-13 con E3 — texto con U+202E da 422 TEXTO_INVALIDO sin repetirlo; 500 emojis se aceptan, 501 no; 10 dígitos sí (E3).
    [Fact]
    public async Task HU14576_SP13_textos_libres_en_puntos_de_codigo_y_sin_bidi()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO124", EstadoSaneamiento.EnGestion, ct);
        var renglon = await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Pendiente, ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));
        var ruta = GestionDePrueba.RutaRenglon(s.Id, renglon.Id, empresa.Id);
        async Task<HttpResponseMessage> Nota(string nota) =>
            await GestionDePrueba.PutAsync(cliente, ruta, new { resultado = "PENDIENTE", nota, version = await GestionDePrueba.VersionRenglonAsync(empresa, renglon.Id, ct) }, ct);

        var bidi = await Nota("Pago 7788‮ invertido");
        var emojis = await Nota(string.Concat(Enumerable.Repeat("\U0001F600", 500)));
        var demasiados = await Nota(string.Concat(Enumerable.Repeat("\U0001F600", 501)));
        var digitos = await Nota("Recibo 1234567890 del organismo");

        bidi.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cuerpo = await bidi.Content.ReadAsStringAsync(ct);
        JsonDocument.Parse(cuerpo).RootElement.GetProperty("code").GetString().Should().Be("TEXTO_INVALIDO");
        cuerpo.Should().NotContain("7788", "el error no repite el texto");
        emojis.StatusCode.Should().Be(HttpStatusCode.OK, await emojis.Content.ReadAsStringAsync(ct));
        demasiados.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await GestionDePrueba.JsonAsync(demasiados, ct)).GetProperty("campo").GetString().Should().Be("nota");
        digitos.StatusCode.Should().Be(HttpStatusCode.OK, "E3: los dígitos seguidos se aceptan");
    }

    // SP-3 — la lista de tipos es cerrada: una transición desde el backoffice que además modifica una cabecera o un dictamen de la
    // empresa falla con EscrituraEntreEmpresasException y el saneamiento no cambia.
    [Fact]
    public async Task HU14576_SP3_la_lista_de_tipos_del_saneamiento_no_admite_cabecera_ni_dictamen()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO125", EstadoSaneamiento.Solicitado, ct);
        await SaneamientoDePrueba.DictamenAsync(empresa, lote.Vehiculos[0].Id, "BLOQUEADO", ct);

        foreach (var ajena in new[] { "cabecera", "dictamen" })
        {
            await using var db = DosEmpresas.Contexto(api.BaseDeDatos, Guid.NewGuid());
            var tracked = await db.ParaEditarDeLaEmpresa<SaneamientoEntidad>(empresa.Id).SingleAsync(x => x.Id == s.Id, ct);
            tracked.ObservacionFlit = "cambio";
            if (ajena == "cabecera")
                (await db.ParaEditarDeLaEmpresa<CabeceraDiagnostico>(empresa.Id).SingleAsync(c => c.Id == lote.Id, ct)).Nombre = "otro nombre";
            else
                (await db.ParaEditarDeLaEmpresa<DictamenVehiculo>(empresa.Id).SingleAsync(d => d.VehiculoId == lote.Vehiculos[0].Id, ct)).Veredicto = "VIABLE";

            var guardar = () => db.GuardarDeLaEmpresaAsync(empresa.Id, [.. EscritorSaneamiento.TiposDelSaneamiento], ct);

            await guardar.Should().ThrowAsync<Exception>().Where(e => e.GetType().Name == "EscrituraEntreEmpresasException", ajena);
        }

        (await GestionDePrueba.LeerAsync(empresa, s.Id, ct)).ObservacionFlit.Should().BeNull();
    }

    // SP-2 — prueba de arquitectura: sin ExecuteDelete en el saneamiento ni en los archivos de saneamiento del backoffice.
    [Fact]
    public void HU14576_SP2_sin_ExecuteDelete_en_el_saneamiento()
    {
        var src = Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "src", "Flit.Diagnostico.Api", "Modulos");
        var archivos = Directory.GetFiles(Path.Combine(src, "Saneamiento"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(src, "Backoffice"), "*Saneamiento*.cs"))
            .ToList();

        archivos.Should().Contain(a => a.EndsWith("SaneamientosBackofficeEndpoints.cs", StringComparison.Ordinal), "el escáner mira los archivos del backoffice");
        archivos.Where(a => File.ReadAllText(a).Contains("ExecuteDelete", StringComparison.Ordinal)).Should().BeEmpty();
    }

    // AC35 — la grilla del backoffice trae saneamiento {estado, id} por fila, sin elegibilidad.
    [Fact]
    public async Task HU14576_AC35_grilla_del_backoffice_trae_estado_e_id_del_saneamiento()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SBO126", "SBO127", "SBO128"], ct);
        var solicitado = await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[0].Id, EstadoSaneamiento.Solicitado, ct);
        await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[1].Id, EstadoSaneamiento.Anulado, ct);

        var respuesta = await api.Cliente(GestionDePrueba.TokenFlit(api))
            .GetAsync($"{Flit.Diagnostico.Api.Program.Prefijo}/backoffice/diagnosticos/{lote.Id}/grilla?empresa={empresa.Id}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var filas = (await GestionDePrueba.JsonAsync(respuesta, ct)).GetProperty("vehiculos").EnumerateArray()
            .ToDictionary(v => v.GetProperty("id").GetGuid(), v => v.GetProperty("saneamiento"));
        filas[lote.Vehiculos[0].Id].GetProperty("estado").GetString().Should().Be("SOLICITADO");
        filas[lote.Vehiculos[0].Id].GetProperty("id").GetGuid().Should().Be(solicitado.Id);
        filas[lote.Vehiculos[0].Id].TryGetProperty("elegible", out _).Should().BeFalse("el backoffice no marca la bandera");
        foreach (var vid in new[] { lote.Vehiculos[1].Id, lote.Vehiculos[2].Id })
            filas[vid].ValueKind.Should().Be(JsonValueKind.Null, "FE-D: saneamiento {estado, id} | null; el anulado no cuenta");
    }

    // AC24 + CR-4 (S14, S15) — con un vehículo BLOQUEADO y su saneamiento EN_GESTION: cerrar como SANEADO no cambia el dictamen
    // vigente ni su versión; la reemisión del dictamen (ajuste FLIT, versión 2) y un reproceso real de su consulta en ERROR (202, la
    // celda vuelve a PENDIENTE y el vehículo queda no terminado) dejan igual el saneamiento, sus renglones, su historial y
    // vehiculos.saneamiento_estado.
    [Fact]
    public async Task HU14576_AC24_CR4_reemision_y_reproceso_real_no_tocan_el_saneamiento()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SBO129"], ct, abiertas: ["SBO129"]);
        var vehiculo = lote.Vehiculos[0].Id;
        var celdaEnError = lote.Celda("SBO129", Fuente.SimitVehiculo);
        celdaEnError.Estado.Should().Be(EstadoCelda.Error, "la siembra deja la SIMIT en ERROR para reprocesarla");
        var s = await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, vehiculo, EstadoSaneamiento.EnGestion, ct);
        await SaneamientoDePrueba.DictamenAsync(empresa, vehiculo, "BLOQUEADO", ct);
        await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.SimitVehiculo, ResultadoRenglonSaneamiento.Saneado, ct);

        var cierre = await Accion(api.Cliente(GestionDePrueba.TokenFlit(api)), empresa, s.Id, "cerrar_saneado", await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct);
        cierre.StatusCode.Should().Be(HttpStatusCode.OK, await cierre.Content.ReadAsStringAsync(ct));
        var tras = await DictamenesAsync(empresa, vehiculo, ct);
        tras.Should().ContainSingle("cerrar no emite dictamen").Which.Should().Be((1, true, "BLOQUEADO"), "cerrar no cambia el dictamen vigente ni su versión");
        var foto = await FotoSaneamientoAsync(empresa, s.Id, ct);
        foto.Should().StartWith("Saneado|");

        using var soporte = api.Cliente(api.Token(
            empresa: Guid.CreateVersion7(), permisos: [Permisos.BackofficeVer, Permisos.DictamenOverride], roles: ["backoffice_diagnostico"],
            tipoEmpresa: UsuarioActual.TipoEmpresaFlit));
        using var cuerpoAjuste = new StringContent(
            """{"version_esperada":1,"veredicto":"BLOQUEADO","motivo":"Soporte confirmo el embargo con el organismo","alertamientos":[{"codigo":"AJUSTE_FLIT","texto":"Embargo confirmado por soporte","efecto":"BLOQUEA","fuente":"RUNT"}]}""",
            System.Text.Encoding.UTF8, "application/json");
        var reemision = await soporte.PutAsync(
            $"{Flit.Diagnostico.Api.Program.Prefijo}/backoffice/diagnosticos/{lote.Id}/vehiculos/{vehiculo}/dictamen?empresa={empresa.Id}", cuerpoAjuste, ct);
        var trasReemision = await FotoSaneamientoAsync(empresa, s.Id, ct);
        var reproceso = await api.Cliente(api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.ConsultasReprocesar], roles: ["admin_diagnostico"]))
            .PostAsync($"{Flit.Diagnostico.Api.Program.Prefijo}/consultas/{celdaEnError.Id}/reprocesar", null, ct);

        reemision.StatusCode.Should().Be(HttpStatusCode.OK, await reemision.Content.ReadAsStringAsync(ct));
        (await DictamenesAsync(empresa, vehiculo, ct)).Should().Contain((2, true, "BLOQUEADO"), "el ajuste reemitió el dictamen en la versión 2");
        trasReemision.Should().Be(foto, "reemitir el dictamen no toca el saneamiento ni la columna del vehículo");
        reproceso.StatusCode.Should().Be(HttpStatusCode.Accepted, await reproceso.Content.ReadAsStringAsync(ct));
        // El reproceso vuelve la celda a PENDIENTE y, en la misma transacción, el publicador la deja EN_CURSO con su trabajo nuevo.
        (await GestionDePrueba.JsonAsync(reproceso, ct)).GetProperty("estado").GetString().Should().BeOneOf(["PENDIENTE", "EN_CURSO"], "el reproceso ocurrió de verdad");
        await using (var db = empresa.Db())
            (await db.Set<CeldaConsulta>().AsNoTracking().SingleAsync(c => c.Id == celdaEnError.Id, ct)).Estado
                .Should().BeOneOf([EstadoCelda.Pendiente, EstadoCelda.EnCurso], "la celda salió de ERROR: el vehículo quedó no terminado");
        (await FotoSaneamientoAsync(empresa, s.Id, ct)).Should().Be(foto, "reprocesar no toca el estado, los renglones, el historial ni saneamiento_estado (S15)");
    }

    // CR-12 — «tomar varios» no marca ni escribe nada en las filas omitidas: el saneamiento EN_GESTION que sale TRANSICION_INVALIDA
    // conserva su xmin, su historial, la versión de su fila y la del tablero de su lote; el SOLICITADO de otro lote sí se toma.
    [Fact]
    public async Task HU14576_CR12_tomar_varios_no_escribe_en_las_filas_omitidas()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (loteOmitido, omitido) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO130", EstadoSaneamiento.EnGestion, ct, nombreLote: "Lote omitido");
        var (loteTomado, tomado) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO131", EstadoSaneamiento.Solicitado, ct, nombreLote: "Lote tomado");
        var antesOmitido = await FilaAsync(empresa, loteOmitido.Id, loteOmitido.Vehiculos[0].Id, omitido.Id, ct);
        var antesTomado = await FilaAsync(empresa, loteTomado.Id, loteTomado.Vehiculos[0].Id, tomado.Id, ct);

        var respuesta = await GestionDePrueba.PostAsync(api.Cliente(GestionDePrueba.TokenFlit(api)), GestionDePrueba.Base + "/tomar", new
        {
            items = new object[] { new { empresa = empresa.Id, id = omitido.Id }, new { empresa = empresa.Id, id = tomado.Id } },
        }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var json = await GestionDePrueba.JsonAsync(respuesta, ct);
        json.GetProperty("tomados").GetInt32().Should().Be(1);
        json.GetProperty("omitidos").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("motivo").GetString().Should().Be("TRANSICION_INVALIDA");
        (await FilaAsync(empresa, loteOmitido.Id, loteOmitido.Vehiculos[0].Id, omitido.Id, ct))
            .Should().Be(antesOmitido, "la fila omitida no se marca: ni su versión, ni la del tablero, ni el saneamiento ni su historial");
        var despuesTomado = await FilaAsync(empresa, loteTomado.Id, loteTomado.Vehiculos[0].Id, tomado.Id, ct);
        despuesTomado.VersionFila.Should().BeGreaterThan(antesTomado.VersionFila, "la fila tomada sí se marca para el sondeo");
        despuesTomado.Estado.Should().Be(EstadoSaneamiento.EnGestion);
    }

    private static async Task<List<(int Version, bool Vigente, string Veredicto)>> DictamenesAsync(EmpresaDePrueba empresa, Guid vehiculo, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return [.. (await db.Set<DictamenVehiculo>().AsNoTracking().Where(d => d.VehiculoId == vehiculo).OrderBy(d => d.Version).ToListAsync(ct))
            .Select(d => (d.Version, d.Vigente, d.Veredicto))];
    }

    /// <summary>Estado, xmin, renglones (resultado y versión), eventos y la copia del estado en el vehículo.</summary>
    private static async Task<string> FotoSaneamientoAsync(EmpresaDePrueba empresa, Guid sid, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var s = await db.Set<SaneamientoEntidad>().AsNoTracking().SingleAsync(x => x.Id == sid, ct);
        var renglones = await db.Set<RenglonSaneamiento>().AsNoTracking().Where(r => r.SaneamientoId == sid).OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Resultado, r.Xmin, r.QuitadoEn }).ToListAsync(ct);
        var eventos = await db.Set<EventoSaneamiento>().AsNoTracking().CountAsync(e => e.SaneamientoId == sid, ct);
        var columna = await db.Set<VehiculoDiagnostico>().AsNoTracking().Where(v => v.Id == s.VehiculoId).Select(v => v.SaneamientoEstado).SingleAsync(ct);
        return $"{s.Estado}|xmin={s.Xmin}|renglones={string.Join(';', renglones.Select(r => $"{r.Id}:{r.Resultado}:{r.Xmin}:{r.QuitadoEn:O}"))}|eventos={eventos}|columna={columna}|cerrado={s.CerradoEn:O}";
    }

    private sealed record FilaTomar(long VersionTablero, long VersionFila, uint XminVehiculo, uint XminSaneamiento, int Eventos, EstadoSaneamiento Estado);

    private static async Task<FilaTomar> FilaAsync(EmpresaDePrueba empresa, Guid lote, Guid vehiculo, Guid sid, CancellationToken ct)
    {
        await using var db = empresa.Db();
        var cabecera = await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(c => c.Id == lote, ct);
        var fila = await db.Set<VehiculoDiagnostico>().AsNoTracking().SingleAsync(v => v.Id == vehiculo, ct);
        var s = await db.Set<SaneamientoEntidad>().AsNoTracking().SingleAsync(x => x.Id == sid, ct);
        var eventos = await db.Set<EventoSaneamiento>().AsNoTracking().CountAsync(e => e.SaneamientoId == sid, ct);
        return new FilaTomar(cabecera.VersionTablero, fila.VersionTablero, fila.Version, s.Xmin, eventos, s.Estado);
    }

    internal static EstadoSaneamiento Estado(string codigo) => codigo switch
    {
        "SOLICITADO" => EstadoSaneamiento.Solicitado,
        "EN_GESTION" => EstadoSaneamiento.EnGestion,
        "SANEADO" => EstadoSaneamiento.Saneado,
        "NO_PROCEDE" => EstadoSaneamiento.NoProcede,
        _ => EstadoSaneamiento.Anulado,
    };

    private static ResultadoRenglonSaneamiento Resultado(string codigo) => codigo switch
    {
        "SANEADO" => ResultadoRenglonSaneamiento.Saneado,
        "NO_PROCEDE" => ResultadoRenglonSaneamiento.NoProcede,
        _ => ResultadoRenglonSaneamiento.Pendiente,
    };

    /// <summary>La acción de FLIT por su nombre corto, con la version dada y, si aplica, el motivo.</summary>
    internal static Task<HttpResponseMessage> Accion(
        HttpClient cliente, EmpresaDePrueba empresa, Guid sid, string accion, uint version, CancellationToken ct, string? motivo = MotivoValido, bool conMotivo = true)
    {
        Dictionary<string, object?> Cuerpo(string? resultado)
        {
            var cuerpo = new Dictionary<string, object?> { ["version"] = version };
            if (conMotivo)
                cuerpo["motivo"] = motivo;
            if (resultado is not null)
                cuerpo["resultado"] = resultado;
            return cuerpo;
        }

        return accion switch
        {
            "tomar" => cliente.PostAsync(GestionDePrueba.Ruta(sid, empresa.Id, "/tomar"), null, ct),
            "anular" => GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(sid, empresa.Id, "/anular"), Cuerpo(null), ct),
            "reabrir" => GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(sid, empresa.Id, "/reabrir"), Cuerpo(null), ct),
            "cerrar_saneado" => GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(sid, empresa.Id, "/cerrar"), new { resultado = "SANEADO", version }, ct),
            _ => GestionDePrueba.PostAsync(cliente, GestionDePrueba.Ruta(sid, empresa.Id, "/cerrar"), Cuerpo("NO_PROCEDE"), ct),
        };
    }
}
