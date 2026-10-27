using System.Net;
using System.Text.Json;
using Flit.PersonaNatural.Api.Adjuntos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Radicacion;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>Compuerta de prueba para comprobar que el punto de contacto manda: lo que diga ella es lo que cuenta.</summary>
internal sealed class CompuertaFalsa(ResultadoCompuerta resultado) : ICompuertaDeRadicacion
{
    public int Llamadas;

    public Task<ResultadoCompuerta> EvaluarAsync(Guid solicitudId, CancellationToken ct)
    {
        Interlocked.Increment(ref Llamadas);
        return Task.FromResult(resultado);
    }
}

/// <summary>
/// HU #14049 (Feature D, Épica #14021) — radicar: valida completitud y compuerta contra el snapshot, pasa la solicitud a
/// <c>en_cola</c> y deja el trabajo en la outbox del SDK en la misma transacción, con <c>Idempotency-Key</c> y <c>202</c>.
/// Sobre el host real y una base migrada (base efímera propia; sin motor se omite en local y falla en CI).
/// </summary>
public sealed class RadicacionTests : IAsyncLifetime
{
    private const string Base = "/api/v1/persona-natural";
    private const string Vendedor = "7001234567";
    private const string Comprador = "7009876543";
    private const string CorreoVendedor = "vendedor.secreto@example.org";

    private readonly AlmacenEnMemoria _almacen = new();
    private PortalHost _host = null!;
    private bool _iniciado;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync(ICompuertaDeRadicacion? compuerta = null, params (string Clave, string Valor)[] ajustes)
    {
        _host = await PortalHost.IniciarAsync("pnrad", (servicios, _) =>
        {
            servicios.AddSingleton<IAlmacenAdjuntos>(_almacen);
            if (compuerta is not null)
                servicios.AddSingleton(compuerta);
        }, ajustes);
        _iniciado = true;
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
    }

    // ── AC1 y AC2: aceptar, encolar y no duplicar ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnBorradorCompletoYValidado_Responde202EnCola_YDejaElTrabajoEnLaOutboxEnLaMismaTransaccion()
    {
        await IniciarAsync();
        var (id, token) = await Completa();

        var r = await Radicar(id, token, "k-1");

        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("solicitudId").GetGuid().Should().Be(id);
        json.GetProperty("estado").GetString().Should().Be("en_cola");
        json.GetProperty("codigoPublico").GetString().Should().StartWith("PN-");
        await using var db = _host.NuevaDb();
        var s = await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken);
        s.Estado.Should().Be("en_cola");
        s.IdempotencyKey.Should().Be("k-1");
        (await db.Set<HistorialEstado>().Where(h => h.Hasta == "en_cola").CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        var evento = await db.Set<OutboxMessage>().SingleAsync(TestContext.Current.CancellationToken);
        evento.RoutingKey.Should().Be("tramites.persona_natural.radicacion_solicitada");
        evento.Exchange.Should().Be("flit.tramites");
        evento.PublishedAt.Should().BeNull("el publicador lo entrega cuando el broker esté disponible; la respuesta no lo espera");
    }

    [Fact]
    public async Task ElEvento_LlevaIdentificadoresYReferencias_YLosDatosPersonalesSoloCifrados()
    {
        await IniciarAsync();
        var (id, token) = await Completa();

        await Radicar(id, token, "k-1");

        await using var db = _host.NuevaDb();
        var payload = (await db.Set<OutboxMessage>().SingleAsync(TestContext.Current.CancellationToken)).Payload;
        payload.Should().NotContain(Vendedor).And.NotContain(Comprador).And.NotContain("secreto").And.NotContain("Persona de Prueba").And.NotContain(PortalHost.Cedula, "ningún dato personal viaja en claro");
        var sobre = JsonDocument.Parse(payload).RootElement;
        sobre.GetProperty("type").GetString().Should().Be("tramites.persona_natural.radicacion_solicitada");
        sobre.GetProperty("version").GetInt32().Should().Be(1);
        sobre.GetProperty("producer").GetString().Should().Be("personanatural");
        var data = sobre.GetProperty("data");
        data.GetProperty("solicitudId").GetGuid().Should().Be(id);
        data.GetProperty("tipoTramiteCodigo").GetString().Should().Be("TRASPASO_STANDARD");
        data.GetProperty("definicionVersion").GetInt32().Should().Be(1);
        data.GetProperty("definicionHuella").GetString().Should().Be("h1");
        data.GetProperty("placa").GetString().Should().Be("ABC123");
        data.GetProperty("fieldValues").GetProperty("km").GetString().Should().Be("85000");
        data.GetProperty("datos").GetProperty("comercial").GetProperty("valor").GetDecimal().Should().Be(45_000_000);
        data.GetProperty("actores").EnumerateArray().Select(a => a.GetProperty("rol").GetString()).Should().BeEquivalentTo("vendedor", "comprador");
        data.GetProperty("adjuntos").EnumerateArray().Select(a => a.GetProperty("tipo").GetString()).Should().BeEquivalentTo("compraventa", "soat", "comprobante_pago");
        data.GetProperty("adjuntos")[0].GetProperty("referencia").GetString().Should().StartWith("ref-");
        data.GetProperty("comprobantePagoId").GetGuid().Should().NotBeEmpty();

        // Quien comparte la llave abre los datos de las partes; el contexto autenticado impide abrirlos como otra cosa.
        var cifrado = _host.App.Services.GetRequiredService<CifradoPii>();
        var cifrados = Convert.FromBase64String(data.GetProperty("actoresCifrados").GetString()!);
        var abierto = cifrado.Descifrar(cifrados, "radicacion.actores");
        abierto.Should().Contain(Vendedor).And.Contain(CorreoVendedor);
        var otroContexto = () => cifrado.Descifrar(cifrados, "persona.correo");
        otroContexto.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public async Task LaMismaLlave_Responde202ConElMismoSolicitudId_YNoEncolaUnSegundoEvento()
    {
        await IniciarAsync();
        var (id, token) = await Completa();

        var primera = await Radicar(id, token, "misma");
        var repetida = await Radicar(id, token, "misma");

        repetida.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await PortalHost.JsonAsync(repetida)).RootElement.GetProperty("solicitudId").GetGuid().Should().Be((await PortalHost.JsonAsync(primera)).RootElement.GetProperty("solicitudId").GetGuid());
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.Set<HistorialEstado>().CountAsync(h => h.Hasta == "en_cola", TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task DosPeticionesALaVezConLaMismaLlave_EncolanUnSoloEvento()
    {
        await IniciarAsync();
        var (id, token) = await Completa();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Radicar(id, token, "carrera")));

        respuestas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1, "el doble clic no crea dos trámites");
    }

    [Fact]
    public async Task SinLlave_Responde400_YUnaLlaveYaUsadaPorOtraSolicitudResponde409()
    {
        await IniciarAsync();
        var (id, token) = await Completa();
        var (otro, tokenOtro) = await Completa();

        var sinLlave = await Radicar(id, token, null);
        var larga = await Radicar(id, token, new string('x', 101));
        await Radicar(id, token, "usada");
        var conflicto = await Radicar(otro, tokenOtro, "usada");

        sinLlave.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PortalHost.CodigoAsync(sinLlave)).Should().Be("idempotency_key_requerida");
        (await PortalHost.CodigoAsync(larga)).Should().Be("idempotency_key_requerida");
        conflicto.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(conflicto)).Should().Be("idempotency_conflicto");
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.Set<SolicitudTramite>().SingleAsync(s => s.Id == otro, TestContext.Current.CancellationToken)).Estado.Should().Be("borrador");
    }

    // ── AC3: borrador incompleto ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnBorradorIncompleto_Responde422ConLaListaDePendientes_YNoEncola()
    {
        await IniciarAsync();
        var (id, token) = await Completa(conComprobante: false, conKm: false);

        var r = await Radicar(id, token, "k-1");

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be("requisitos_incompletos");
        var pendientes = (await PortalHost.JsonAsync(r)).RootElement.GetProperty("pendientes").EnumerateArray()
            .Select(p => $"{p.GetProperty("codigo").GetString()}:{(p.TryGetProperty("referencia", out var x) ? x.GetString() : null)}").ToList();
        pendientes.Should().BeEquivalentTo("campo_requerido:km", "comprobante_pago_requerido:");
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Estado.Should().Be("borrador");
    }

    // ── AC4: compuerta de identidad ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConPartesSinValidar_Responde409IdentityPending_ConLosRolesQueFaltan_YNoEncola()
    {
        await IniciarAsync();
        var (id, token) = await Completa(validar: false);

        var r = await Radicar(id, token, "k-1");

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("identity_pending");
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("motivo").GetString().Should().Be("identidad_pendiente");
        json.GetProperty("partes").EnumerateArray().Select(p => p.GetProperty("parte").GetString()).Should().BeEquivalentTo("vendedor", "comprador");
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task UnaParteRechazadaOUnaSola_NoDejaPasar()
    {
        await IniciarAsync();
        var (id, token) = await Completa();
        await using (var db = _host.NuevaDb())
            await db.Set<PersonaSolicitante>().Where(p => p.Parte == "comprador").ExecuteUpdateAsync(set => set.SetProperty(p => p.ValidacionEstado, "rechazada"), TestContext.Current.CancellationToken);

        var rechazada = await Radicar(id, token, "k-1");
        await using (var db = _host.NuevaDb())
            await db.Set<PersonaSolicitante>().Where(p => p.Parte == "comprador").ExecuteUpdateAsync(set => set.SetProperty(p => p.ValidacionEstado, "aprobada").SetProperty(p => p.ValidacionId, Guid.NewGuid()), TestContext.Current.CancellationToken);
        var validada = await Radicar(id, token, "k-2");

        (await PortalHost.JsonAsync(rechazada)).RootElement.GetProperty("motivo").GetString().Should().Be("identidad_rechazada");
        validada.StatusCode.Should().Be(HttpStatusCode.Accepted, "con la validación aprobada de esta solicitud ya puede radicar");
    }

    [Fact]
    public async Task LaCompuertaRegistradaEsLaQueManda_PorEncimaDeLaPorDefecto()
    {
        var bloqueo = new CompuertaFalsa(ResultadoCompuerta.Bloqueada("identidad_pendiente", [new ParteFaltante("comprador", 1, "pendiente")]));
        await IniciarAsync(bloqueo);
        var (id, token) = await Completa();

        var r = await Radicar(id, token, "k-1");

        r.StatusCode.Should().Be(HttpStatusCode.Conflict, "la por defecto la habría dejado pasar: las partes están aprobadas");
        bloqueo.Llamadas.Should().Be(1);
    }

    // ── AC6 y AC7: tipo y estado ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SiElTipoDejoDeSerElegible_Responde409TipoNoDisponible_YNoEncola()
    {
        await IniciarAsync(null, ("Cotizaciones:CacheCatalogoSegundos", "0"));
        var (id, token) = await Completa();
        _host.Definiciones.PorCodigo.Remove("TRASPASO_STANDARD");

        var r = await Radicar(id, token, "k-1");

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("tipo_no_disponible");
        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task SiFlit2NoResponde_AunAsiSeRadica_PorqueLaColaExisteParaEso()
    {
        await IniciarAsync(null, ("Cotizaciones:CacheCatalogoSegundos", "0"));
        var (id, token) = await Completa();
        _host.Definiciones.ErrorAlPedir = "servicio_no_disponible";

        var r = await Radicar(id, token, "k-1");

        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task UnaSolicitudEnCola_NoAdmiteCambios_NiCancelacion_NiOtraRadicacionConOtraLlave()
    {
        await IniciarAsync();
        var (id, token) = await Completa();
        await Radicar(id, token, "k-1");

        var editar = await _host.EnviarAsync(HttpMethod.Put, $"{Base}/solicitudes/{id}/campos", new { valores = new Dictionary<string, object?> { ["km"] = 1 } }, token);
        var cancelar = await _host.EnviarAsync(HttpMethod.Delete, $"{Base}/solicitudes/{id}", token: token);
        var otraLlave = await Radicar(id, token, "k-2");

        foreach (var r in new[] { editar, cancelar, otraLlave })
        {
            r.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await PortalHost.CodigoAsync(r)).Should().Be("estado_no_permite_cambio");
        }

        await using var db = _host.NuevaDb();
        (await db.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task SinSesionOConLaDeOtraSolicitud_NoSeRadica()
    {
        await IniciarAsync();
        var (id, _) = await Completa();
        var (_, tokenAjeno) = await Completa();

        (await Radicar(id, null, "k-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Radicar(id, tokenAjeno, "k-1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RadicarNoDejaDatosPersonalesEnLosLogs()
    {
        await IniciarAsync();
        var (id, token) = await Completa();

        await Radicar(id, token, "k-1");

        lock (_host.Logs.Mensajes)
            _host.Logs.Mensajes.Should().NotContain(m => m.Contains(Vendedor) || m.Contains("secreto") || m.Contains("Persona de Prueba"));
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> Radicar(Guid id, string? token, string? llave) =>
        _host.EnviarAsync(HttpMethod.Post, $"{Base}/solicitudes/{id}/radicar", null, token, false, llave is null ? Array.Empty<(string, string)>() : [("Idempotency-Key", llave)]);

    /// <summary>Un borrador de traspaso listo para radicar: partes, campos, valor comercial, documentos y comprobante.</summary>
    private async Task<(Guid Id, string Token)> Completa(bool conComprobante = true, bool conKm = true, bool validar = true)
    {
        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones",
            new { tipoCodigo = "TRASPASO_STANDARD", organismoId = PortalHost.Organismo, cedula = PortalHost.Cedula, placa = "ABC123", consentimiento = true }, captcha: true);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        var id = json.GetProperty("solicitudId").GetGuid();
        var token = json.GetProperty("token").GetString()!;

        (await _host.EnviarAsync(HttpMethod.Patch, $"{Base}/solicitudes/{id}/actores", new
        {
            actores = new object[]
            {
                new { rol = "vendedor", tipoDocumento = "CC", numeroDocumento = Vendedor, nombre = "Persona de Prueba", correo = CorreoVendedor, celular = "3001234567" },
                new { rol = "comprador", tipoDocumento = "CC", numeroDocumento = Comprador, nombre = "Persona de Prueba", correo = "comprador@example.org", celular = "3009876543" },
            },
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        if (conKm)
            (await _host.EnviarAsync(HttpMethod.Put, $"{Base}/solicitudes/{id}/campos", new { valores = new Dictionary<string, object?> { ["km"] = 85000 } }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.EnviarAsync(HttpMethod.Put, $"{Base}/solicitudes/{id}/comercial", new { valorComercial = 45_000_000 }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var doc in new[] { "compraventa", "soat" })
            (await _host.SubirAsync($"{Base}/solicitudes/{id}/adjuntos", token, Archivos.Pdf(), $"{doc}.pdf", doc)).StatusCode.Should().Be(HttpStatusCode.Created);
        if (conComprobante)
            (await _host.SubirAsync($"{Base}/solicitudes/{id}/pago/comprobante", token, Archivos.Png(), "pago.png")).StatusCode.Should().Be(HttpStatusCode.Created);

        if (validar)
        {
            await using var db = _host.NuevaDb();
            await db.Set<PersonaSolicitante>().Where(p => p.SolicitudId == id).ExecuteUpdateAsync(set => set.SetProperty(p => p.ValidacionEstado, "aprobada").SetProperty(p => p.ValidacionId, Guid.NewGuid()), TestContext.Current.CancellationToken);
        }

        return (id, token);
    }
}
