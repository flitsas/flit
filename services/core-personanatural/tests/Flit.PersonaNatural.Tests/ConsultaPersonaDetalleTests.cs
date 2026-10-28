using System.Net;
using System.Text.Json;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14060 (Feature F3 / B2, Épica #14021) — consulta de persona CON nombres para rellenar el actor, el prellenado de FLIT 2.
/// Sobre el host real, una base migrada y un core-consultas falso (base efímera propia; sin motor se omite en local y falla en
/// CI). Todos los datos son ficticios. Lo que más pesa aquí es lo que NO debe pasar: ni sin sesión, ni con la solicitud ya
/// radicada, ni más allá de los topes, ni con nombres o documentos en logs y auditoría.
/// </summary>
public sealed class ConsultaPersonaDetalleTests : IAsyncLifetime
{
    private const string Base = "/api/v1/persona-natural";
    private const string DocumentoTercero = "1098765432";
    private const string Cedula = "1020456789";

    private readonly ConsultasRuntFalso _runt = new();
    private PortalHost _host = null!;
    private bool _iniciado;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    private async Task IniciarAsync(params (string Clave, string Valor)[] ajustes)
    {
        _host = await PortalHost.IniciarAsync("pndet", (servicios, _) => servicios.AddSingleton<IConsultasRunt>(_runt), ajustes);
        _iniciado = true;
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
        _runt.Conductor = ResultadosFalsos.ConductorConNombres;
    }

    [Fact]
    public async Task ConSesionYConsentimiento_DevuelveLosNombres_YSoloEsos()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await Detalle(id, token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.CacheControl!.NoStore.Should().BeTrue("son datos de un tercero: no se cachean");
        var texto = await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(texto).RootElement;
        json.GetProperty("existe").GetBoolean().Should().BeTrue();
        json.GetProperty("nombres").GetString().Should().Be("ANA MARIA");
        json.GetProperty("apellidos").GetString().Should().Be("GOMEZ RUIZ");
        json.GetProperty("nombreCompleto").GetString().Should().Be("ANA MARIA GOMEZ RUIZ");
        json.GetProperty("ciudad").ValueKind.Should().Be(JsonValueKind.Null, "la ciudad de FLIT 2 sale del histórico de la empresa, que el portal no expone");
        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("existe", "nombres", "apellidos", "nombreCompleto", "ciudad");
        texto.Should().NotContainAny("1990", "PYS-FICTICIO", "ACTIVO", DocumentoTercero);
        var llamada = _runt.LlamadasConductor.Should().ContainSingle().Subject;
        llamada.Documento.Should().Be(new DocumentoConsulta("CC", DocumentoTercero));
        llamada.Referencia.Should().Be(id.ToString(), "la consulta queda trazada a la solicitud");
    }

    [Theory]
    [InlineData("PAS", "AB123456", "PA")]
    [InlineData("TI", "1098765432", "PPT")]
    [InlineData("CE", "1098765432", "CE")]
    public async Task ElTipoSeMapeaAlProveedorComoEnFlit2(string tipo, string numero, string esperado)
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await Detalle(id, token, tipo: tipo, documento: numero);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        _runt.LlamadasConductor.Should().ContainSingle().Which.Documento.Should().Be(new DocumentoConsulta(esperado, numero));
    }

    [Fact]
    public async Task SinCoincidencia_Responde200ConExisteFalso_SinError()
    {
        await IniciarAsync();
        _runt.Conductor = ResultadosFalsos.ConductorInexistente;
        var (id, token) = await AbrirSesion();

        var r = await Detalle(id, token);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        json.GetProperty("existe").GetBoolean().Should().BeFalse();
        json.GetProperty("nombres").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("nombreCompleto").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UnNombreEnmascarado_NoSeEntrega()
    {
        await IniciarAsync();
        _runt.Conductor = () =>
        {
            var r = ResultadosFalsos.ConductorConNombres();
            foreach (var c in r.Campos.Where(c => c.Clave.StartsWith("person_", StringComparison.Ordinal)))
                c.ValorTexto = "A**A M***A";
            return r;
        };
        var (id, token) = await AbrirSesion();

        var r = await Detalle(id, token);

        (await PortalHost.JsonAsync(r)).RootElement.GetProperty("existe").GetBoolean().Should().BeFalse("un nombre enmascarado no es un nombre");
    }

    [Theory]
    [InlineData("NIT", "900123456")]
    [InlineData("PPT", "1098765432")]
    [InlineData("CC", "12")]
    [InlineData("CC", "abc")]
    [InlineData("", "1098765432")]
    public async Task UnTipoOUnDocumentoInvalido_Responde400_SinLlamarNiGastarCupo(string tipo, string documento)
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await Detalle(id, token, tipo: tipo, documento: documento);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PortalHost.CodigoAsync(r)).Should().Be("datos_invalidos");
        _runt.LlamadasConductor.Should().BeEmpty();
        (await FilasAuditoria()).Should().BeEmpty();
    }

    [Fact]
    public async Task SinConsentimientoOConVersionDesconocida_NoSeConsulta()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var sin = await _host.EnviarAsync(HttpMethod.Post, Ruta(id), new { tipoDocumento = "CC", documento = DocumentoTercero, rol = "comprador", consentimiento = false }, token);
        var version = await _host.EnviarAsync(HttpMethod.Post, Ruta(id), new { tipoDocumento = "CC", documento = DocumentoTercero, rol = "comprador", consentimiento = true, consentimientoVersion = "v99" }, token);

        (await PortalHost.CodigoAsync(sin)).Should().Be("consentimiento_requerido");
        (await PortalHost.CodigoAsync(version)).Should().Be("consentimiento_version_desconocida");
        _runt.LlamadasConductor.Should().BeEmpty();
    }

    [Fact]
    public async Task UnRolFueraDelSnapshotDeEsaSolicitud_Responde422_SinLlamar()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();

        var r = await Detalle(id, token, rol: "tramitador");
        var vacio = await Detalle(id, token, rol: "");

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be("rol_invalido");
        vacio.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _runt.LlamadasConductor.Should().BeEmpty();
        (await FilasAuditoria()).Should().BeEmpty("un rol inválido no gasta cupo");
    }

    [Fact]
    public async Task SinSesion_OConLaSesionDeOtraSolicitud_NoHayConsulta()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        var (otra, _) = await AbrirSesion();

        var sinToken = await Detalle(id, null);
        var ajeno = await Detalle(otra, token);

        sinToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PortalHost.CodigoAsync(sinToken)).Should().Be("session_invalid");
        ajeno.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PortalHost.CodigoAsync(ajeno)).Should().Be("session_scope_mismatch");
        _runt.LlamadasConductor.Should().BeEmpty();
    }

    [Theory]
    [InlineData("en_cola")]
    [InlineData("cancelada")]
    public async Task ConLaSolicitudYaRadicada_Responde409_SinConsultar(string estado)
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        await using (var db = _host.NuevaDb())
            await db.Set<SolicitudTramite>().Where(s => s.Id == id).ExecuteUpdateAsync(u => u.SetProperty(s => s.Estado, estado), TestContext.Current.CancellationToken);

        var r = await Detalle(id, token);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PortalHost.CodigoAsync(r)).Should().Be("estado_no_permite_cambio");
        _runt.LlamadasConductor.Should().BeEmpty();
    }

    [Fact]
    public async Task ElTopePorSolicitud_Responde429DetalleAgotado_YNoLlamaOtraVez()
    {
        await IniciarAsync(("Consultas:MaxDetallePorSolicitud", "2"));
        var (id, token) = await AbrirSesion();
        var (otra, tokenOtra) = await AbrirSesion();

        (await Detalle(id, token, documento: "1098765432")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Detalle(id, token, documento: "1098765433")).StatusCode.Should().Be(HttpStatusCode.OK);
        var agotada = await Detalle(id, token, documento: "1098765434");
        var deOtra = await Detalle(otra, tokenOtra, documento: "1098765435");

        agotada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await PortalHost.CodigoAsync(agotada)).Should().Be("detalle_agotado");
        deOtra.StatusCode.Should().Be(HttpStatusCode.OK, "el tope es por solicitud");
        _runt.LlamadasConductor.Should().HaveCount(3);
        var filas = await FilasAuditoria();
        filas.Count(a => a.Accion == "consulta_persona_detalle" && a.SolicitudId == id).Should().Be(2);
        filas.Should().Contain(a => a.Accion == "consulta_persona_detalle_agotada" && a.SolicitudId == id);
    }

    [Fact]
    public async Task ElTopePorDocumentoSeComparteConLaConsultaPublica_YLoRechazadoNoGastaCupoDeLaSolicitud()
    {
        await IniciarAsync(("Abuso:LimiteCedulaPorHora", "1"), ("Consultas:MaxDetallePorSolicitud", "2"));
        var (id, token) = await AbrirSesion();

        var primera = await Detalle(id, token);
        var excedida = await Detalle(id, token);

        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        excedida.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await PortalHost.CodigoAsync(excedida)).Should().Be("cedula_rate_limited");
        excedida.Headers.RetryAfter.Should().NotBeNull();
        var otroDocumento = await Detalle(id, token, documento: "79555111");
        otroDocumento.StatusCode.Should().Be(HttpStatusCode.OK, "la llamada rechazada por documento no gastó cupo: el segundo cupo sigue libre");
        _runt.LlamadasConductor.Should().HaveCount(2);
        (await FilasAuditoria()).Should().Contain(a => a.Accion == "consulta_persona_detalle_rechazada");
    }

    [Fact]
    public async Task ConElTopeGlobalAlcanzado_Responde503_SinConsultar()
    {
        await IniciarAsync(("Abuso:TopeGlobalDiario", "1"));
        var (id, token) = await AbrirSesion();

        (await Detalle(id, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var pausada = await Detalle(id, token, documento: "79555111");

        pausada.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await PortalHost.CodigoAsync(pausada)).Should().Be("consultas_no_disponibles");
        _runt.LlamadasConductor.Should().ContainSingle();
    }

    [Fact]
    public async Task ConElProveedorCaido_Responde503ConEspera()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        _runt.Conductor = ResultadosFalsos.ProveedorCaido;
        var caido = await Detalle(id, token);
        _runt.Falla = new ProveedorNoDisponibleException("core-consultas respondió Unavailable.");
        var sinRespuesta = await Detalle(id, token);

        foreach (var r in new[] { caido, sinRespuesta })
        {
            r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await PortalHost.CodigoAsync(r)).Should().Be("proveedor_no_disponible");
            r.Headers.RetryAfter.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task LaAuditoriaDejaLaAccionSinPii_YNingunLogLlevaNombresNiDocumento()
    {
        await IniciarAsync();
        var (id, token) = await AbrirSesion();
        _runt.Falla = null;

        (await Detalle(id, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        _runt.Falla = new ProveedorNoDisponibleException("core-consultas respondió Unavailable.");
        (await Detalle(id, token)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var filas = (await FilasAuditoria()).Where(a => a.Accion == "consulta_persona_detalle").ToList();
        filas.Should().HaveCount(2);
        filas.Should().OnlyContain(a => a.Actor == "sesion" && a.SolicitudId == id && a.IpHash != null && a.IpHash.Length == 32);
        var serializado = JsonSerializer.Serialize((await FilasAuditoria()).Select(a => new { a.Actor, a.Accion, a.SolicitudId }));
        serializado.Should().NotContainAny(DocumentoTercero, "ANA", "GOMEZ", "MARIA");
        await using (var db = _host.NuevaDb())
        {
            var consentimientos = await db.Set<Consentimiento>().Where(c => c.SolicitudId == null).ToListAsync(TestContext.Current.CancellationToken);
            consentimientos.Should().HaveCount(2, "la constancia mínima de cada consulta, solo con huellas");
            consentimientos.Should().OnlyContain(c => c.DocumentoHash.Length == 32 && c.TextoVersion == "v1");
        }

        string.Join('\n', _host.Logs.Mensajes).Should().NotContainAny(DocumentoTercero, "ANA MARIA", "GOMEZ", "RUIZ");
    }

    [Fact]
    public async Task ElEndpointPublicoDeDatoMinimo_SigueSinDevolverNombres()
    {
        await IniciarAsync();

        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/consultas/persona", new { documento = new { tipo = "CC", numero = DocumentoTercero }, consentimiento = true }, captcha: true);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var texto = await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument.Parse(texto).RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("existe", "estadoLicencia", "vigencia");
        texto.Should().NotContainAny("ANA", "GOMEZ", "RUIZ");
    }

    private static string Ruta(Guid id) => $"{Base}/solicitudes/{id}/consultas/persona-detalle";

    private Task<HttpResponseMessage> Detalle(Guid id, string? token, string tipo = "CC", string documento = DocumentoTercero, string rol = "comprador") =>
        _host.EnviarAsync(HttpMethod.Post, Ruta(id), new { tipoDocumento = tipo, documento, rol, consentimiento = true, consentimientoVersion = "v1" }, token);

    private async Task<(Guid Id, string Token)> AbrirSesion()
    {
        var cuerpo = new { tipoCodigo = "TRASPASO_STANDARD", organismoId = PortalHost.Organismo, cedula = Cedula, placa = "abc-123", vin = (string?)null, consentimiento = true, consentimientoVersion = "v1" };
        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones", cuerpo, captcha: true);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        return (json.GetProperty("solicitudId").GetGuid(), json.GetProperty("token").GetString()!);
    }

    private async Task<List<AccesoPiiLog>> FilasAuditoria()
    {
        await using var db = _host.NuevaDb();
        return await db.Set<AccesoPiiLog>().AsNoTracking().Where(a => a.Accion.StartsWith("consulta_persona_detalle")).OrderBy(a => a.Id).ToListAsync(TestContext.Current.CancellationToken);
    }
}
