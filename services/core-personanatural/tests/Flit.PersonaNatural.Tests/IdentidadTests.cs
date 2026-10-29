using System.Net;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14044 (Feature C, Épica #14021) — pedir la validación de identidad de cada parte por enlace. Sobre el host real con
/// la fachada de core-api falsa (que emula la idempotencia de FLIT 2). Decisión del PO (2026-10-27): cada solicitud pide una
/// validación nueva; lo que la misma cédula validó en otro trámite no cuenta.
/// </summary>
public sealed class IdentidadTests : IdentidadBase
{
    [Fact]
    public async Task Solicitar_LlamaAFlit2UnaVez_YDejaLaParteEnPendiente_SinVigencia()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();

        var r = await SolicitarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await Json(r);
        cuerpo.GetProperty("estado").GetString().Should().Be("pendiente");
        cuerpo.GetProperty("parte").GetString().Should().Be("vendedor");
        Fachada.Pedidos.Should().ContainSingle().Which.Should().Be((solicitud, "vendedor", Cedula, Correo));
        var persona = await PersonaAsync(solicitud, "vendedor");
        persona.ValidacionEstado.Should().Be("pendiente");
        persona.ValidacionId.Should().NotBeNull();
        persona.ValidacionVigenteHasta.Should().BeNull("el portal no tiene vigencia en días");
        (await PersonaAsync(solicitud, "comprador")).ValidacionEstado.Should().Be("sin_solicitar", "cada parte se pide por separado");
    }

    [Fact]
    public async Task Solicitar_ConUnaEnCurso_Responde409ConElEstado_YNoPideOtra()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");

        var segunda = await SolicitarAsync(solicitud, token, "vendedor");

        segunda.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var cuerpo409 = await Json(segunda);
        cuerpo409.GetProperty("code").GetString().Should().Be("validacion_en_curso");
        cuerpo409.GetProperty("state").GetString().Should().Be("pendiente");
        Fachada.ValidacionesCreadas.Should().Be(1);
        Fachada.Llamadas.Should().Be(1, "ni siquiera llegó a FLIT 2");
    }

    [Fact]
    public async Task Solicitar_AunqueLaMismaCedulaYaValidoAyerEnOtroTramite_PideUnEnlaceNuevo_YNoSeConsultaLaAnterior()
    {
        if (!await IniciarAsync()) return;
        var (ayer, tokenAyer) = await SembrarAsync();
        await SolicitarAsync(ayer, tokenAyer, "vendedor");
        var primera = await PersonaAsync(ayer, "vendedor");
        Fachada.Estados[primera.ValidacionId!.Value] = "aprobada";
        await PonerEstadoAsync(ayer, "vendedor", "aprobada", primera.ValidacionId);

        var (hoy, tokenHoy) = await SembrarAsync();
        var r = await SolicitarAsync(hoy, tokenHoy, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(r)).GetProperty("estado").GetString().Should().Be("pendiente", "debe validar de nuevo");
        var nueva = await PersonaAsync(hoy, "vendedor");
        nueva.ValidacionId!.Value.Should().NotBe(primera.ValidacionId!.Value);
        Fachada.Pedidos.Last().Solicitud.Should().Be(hoy, "la validación se pide para ESTA solicitud");
        Fachada.ValidacionesCreadas.Should().Be(2);
    }

    [Fact]
    public async Task Solicitar_ConLaAprobadaDeEstaSolicitud_ResponderSuEstado_SinLlamarAFlit2()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");

        var r = await SolicitarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(r)).GetProperty("estado").GetString().Should().Be("aprobada");
        Fachada.Llamadas.Should().Be(0);
    }

    [Theory]
    [InlineData("rechazada")]
    [InlineData("vencida")]
    public async Task Solicitar_ConLaAnteriorRechazadaOVencida_PideUnaNueva_YReiniciaLosReenvios(string estado)
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        var antes = await PersonaAsync(solicitud, "vendedor");
        Fachada.Estados[antes.ValidacionId!.Value] = estado;
        await PonerEstadoAsync(solicitud, "vendedor", estado, antes.ValidacionId, reenvios: 3);

        var r = await SolicitarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var despues = await PersonaAsync(solicitud, "vendedor");
        despues.ValidacionId!.Value.Should().NotBe(antes.ValidacionId!.Value);
        despues.ValidacionEstado.Should().Be("pendiente");
        despues.Reenvios.Should().Be(0);
    }

    [Fact]
    public async Task Solicitar_UnRolQueElTipoNoMarcaConValidacion_NoEnviaEnlace_YNoCuentaPendiente()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();

        var r = await SolicitarAsync(solicitud, token, "testigo");

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodigoAsync(r)).Should().Be("rol_sin_validacion");
        Fachada.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Solicitar_UnRolQueElTipoNoTiene_Responde404()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();

        var r = await SolicitarAsync(solicitud, token, "acreedor");

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodigoAsync(r)).Should().Be("rol_desconocido");
    }

    [Fact]
    public async Task Solicitar_ConUnNit_RespondePersonaJuridicaNoSoportada_SinLlamarAFlit2()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync(tipoDocumento: "NIT", cedulaVendedor: "900123456");

        var r = await SolicitarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodigoAsync(r)).Should().Be("persona_juridica_no_soportada");
        Fachada.Llamadas.Should().Be(0);
    }

    [Theory]
    [InlineData("no-es-un-correo", Celular, "correo")]
    [InlineData(Correo, "123", "celular")]
    public async Task Solicitar_ConContactoInvalido_Responde422ConElCampo_SinLlamarAFlit2(string correo, string celular, string campo)
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<PersonaService>().GuardarAsync(solicitud, new DatosPersona("vendedor", 1, "CC", Cedula, Nombre, correo, celular, null), TestContext.Current.CancellationToken);
            return 0;
        });

        var r = await SolicitarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cuerpo422 = await Json(r);
        cuerpo422.GetProperty("code").GetString().Should().Be("contacto_invalido");
        cuerpo422.GetProperty("field").GetString().Should().Be(campo);
        Fachada.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Solicitar_SinLaPersonaRegistrada_OConLaSolicitudCerrada_Responde409()
    {
        if (!await IniciarAsync()) return;
        var (sinPersonas, tokenSin) = await SembrarAsync(conPersonas: false);
        var (enCola, tokenCola) = await SembrarAsync(estado: EstadoSolicitud.EnCola);

        var sinActor = await SolicitarAsync(sinPersonas, tokenSin, "vendedor");
        var cerrada = await SolicitarAsync(enCola, tokenCola, "vendedor");

        sinActor.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(sinActor)).Should().Be("actor_no_registrado");
        cerrada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(cerrada)).Should().Be("solicitud_no_editable");
        Fachada.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Solicitar_ExigeLaSesionDeEsaSolicitud()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        var (_, tokenAjeno) = await SembrarAsync();

        var sinSesion = await SolicitarAsync(solicitud, token: "", "vendedor");
        var ajena = await SolicitarAsync(solicitud, tokenAjeno, "vendedor");

        sinSesion.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ajena.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Fachada.Llamadas.Should().Be(0);
    }

    // ── AC7: FLIT 2 no disponible ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConFlit2Caido_QuedaPendienteEnvio_ElJobReintenta_YNoSeDuplicaElEnlace()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        Fachada.Caida = true;

        var r = await SolicitarAsync(solicitud, token, "vendedor");

        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await Json(r)).GetProperty("estado").GetString().Should().Be("pendiente_envio");
        (await PersonaAsync(solicitud, "vendedor")).ValidacionId.Should().BeNull();

        // Sigue caído: el job reintenta, no pasa nada y el estado se conserva.
        Reloj.Avanzar(TimeSpan.FromSeconds(5));
        (await Reintentar()).Should().Be(0);
        (await PersonaAsync(solicitud, "vendedor")).ValidacionEstado.Should().Be("pendiente_envio");

        // FLIT 2 crea la validación pero la respuesta se pierde: el reintento no duplica el enlace.
        Fachada.Caida = false;
        Fachada.PerderLaProximaRespuesta = true;
        Reloj.Avanzar(TimeSpan.FromSeconds(5));
        (await Reintentar()).Should().Be(0);
        (await PersonaAsync(solicitud, "vendedor")).ValidacionEstado.Should().Be("pendiente_envio");

        Reloj.Avanzar(TimeSpan.FromSeconds(5));
        (await Reintentar()).Should().Be(1);
        var persona = await PersonaAsync(solicitud, "vendedor");
        persona.ValidacionEstado.Should().Be("pendiente");
        persona.ValidacionId.Should().NotBeNull();
        Fachada.ValidacionesCreadas.Should().Be(1, "un solo enlace pese a los reintentos y a la respuesta perdida");
    }

    [Fact]
    public async Task ElJobNoReintentaSolicitudesYaCerradas()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        Fachada.Caida = true;
        await SolicitarAsync(solicitud, token, "vendedor");
        Fachada.Caida = false;
        await EnScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PersonaNaturalDb>();
            await db.Set<SolicitudTramite>().Where(s => s.Id == solicitud).ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, EstadoSolicitud.Cancelada), TestContext.Current.CancellationToken);
            return 0;
        });
        Reloj.Avanzar(TimeSpan.FromSeconds(5));

        (await Reintentar()).Should().Be(0);

        Fachada.Llamadas.Should().Be(1, "solo la llamada original que falló");
    }

    // ── Habeas Data ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NingunLogContieneCedulaNombreCorreoNiCelular()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        Fachada.Caida = true;
        await SolicitarAsync(solicitud, token, "vendedor");
        Fachada.Caida = false;
        await SolicitarAsync(solicitud, token, "comprador");
        await ReenviarAsync(solicitud, token, "comprador", new { correo = "otro.secreto@example.org" });
        await GetAsync($"{Base}/solicitudes/{solicitud}/identidad", token);

        lock (Logs.Mensajes)
            Logs.Mensajes.Should().NotContain(m => m.Contains(Cedula, StringComparison.Ordinal) || m.Contains(CedulaOtra, StringComparison.Ordinal)
                || m.Contains("secreto", StringComparison.OrdinalIgnoreCase) || m.Contains(Celular, StringComparison.Ordinal), "ni cédula, ni nombre, ni correo, ni celular");
        (await AuditoriaAsync(solicitud)).Should().NotBeEmpty("la auditoría guarda solo estados");
    }

    [Fact]
    public async Task CambiarElDocumentoDeUnaPartePersonaYaValidada_ReiniciaSuValidacion()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada", reenvios: 2);

        await EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<PersonaService>().GuardarAsync(solicitud, new DatosPersona("vendedor", 1, "CC", "79000111", Nombre, Correo, Celular, null), TestContext.Current.CancellationToken);
            return 0;
        });

        var persona = await PersonaAsync(solicitud, "vendedor");
        persona.ValidacionEstado.Should().Be("sin_solicitar");
        persona.ValidacionId.Should().BeNull();
        persona.Reenvios.Should().Be(0);
    }

    [Fact]
    public async Task GuardarLaMismaPersonaSinCambiarElDocumento_ConservaSuValidacion()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");

        await EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<PersonaService>().GuardarAsync(solicitud, new DatosPersona("vendedor", 1, "CC", Cedula, Nombre, "nuevo@example.org", Celular, null), TestContext.Current.CancellationToken);
            return 0;
        });

        (await PersonaAsync(solicitud, "vendedor")).ValidacionEstado.Should().Be("aprobada");
    }

    // ── Forma de la definición ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LaDefinicion_SeInterpretaEnCamelCaseYEnSnakeCase_ConMinPorDefecto()
    {
        var camel = ReglasDeIdentidadDesdeSnapshot.Interpretar("""{"actores":[{"rol":"a","min":2,"biometria":true},{"rol":"b","biometria":false}]}""");
        var snake = ReglasDeIdentidadDesdeSnapshot.Interpretar("""{"Actores":[{"Rol":"a","Biometria":true,"persona_natural_permitida":true}]}""");

        camel.RolesDefinidos.Should().BeEquivalentTo("a", "b");
        camel.RolesConIdentidad.Should().ContainSingle().Which.Should().Be(new RolConIdentidad("a", 2));
        snake.RolesConIdentidad.Should().ContainSingle().Which.Should().Be(new RolConIdentidad("a", 1), "sin min, el rol pide a alguien");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("no es json")]
    [InlineData("""{"actores":"x"}""")]
    public void UnaDefinicionSinActores_OIlegible_NoInventaReglas(string snapshot)
    {
        var reglas = ReglasDeIdentidadDesdeSnapshot.Interpretar(snapshot);

        reglas.RolesDefinidos.Should().BeEmpty();
        reglas.RolesConIdentidad.Should().BeEmpty();
    }

    private Task<int> Reintentar() => EnScopeAsync(sp => sp.GetRequiredService<IdentidadService>().ReintentarPendientesAsync(TestContext.Current.CancellationToken));
}
