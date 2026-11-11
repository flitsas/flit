using System.Net;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Punto;
using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Regla de producto: una placa de un organismo de tránsito no puede seguir en otro, ni en uno no habilitado, en ninguno de los dos
/// portales. Aquí: la regla única de coincidencia (positivos, negativos, tildes, municipios) y el servidor del portal web
/// (diagnóstico y radicación) sobre el host real y una base migrada. El punto se prueba en <see cref="PuntoCanalTests"/>.
/// </summary>
public sealed class OrganismoDelVehiculoTests : IAsyncLifetime
{
    private const string Base = "/api/v1/persona-natural";
    private const string Propietario = "7005551234";

    private readonly FachadaRequisitosFalsa _requisitos = new();
    private PortalHost _host = null!;
    private bool _iniciado;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_iniciado)
            await _host.DisposeAsync();
    }

    // ── La regla única ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SECRETARIA DE TRANSITO DE SABANETA", "Secretaría de Tránsito de Sabaneta", "Sabaneta")]
    [InlineData("STRIA TTOyTTE MCPAL SABANETA", "Secretaría de Tránsito de Sabaneta", "Sabaneta")] // por municipio
    [InlineData("TRÁNSITO DE ITAGÜÍ", "Transito de Itagui", "Itagüí")] // tildes y diéresis
    [InlineData("  tránsito   de-envigado ", "TRANSITO DE ENVIGADO", "")] // símbolos, espacios y mayúsculas
    [InlineData("MEDELLIN", "Secretaría de Movilidad de Medellín", "Medellín")] // municipio igual al nombre del RUNT
    [InlineData("SECRETARIA DE MOVILIDAD DE ENVIGADO", "Envigado", "Envigado")]
    public void Coincide_Positivos(string runt, string nombre, string municipio) =>
        CoincidenciaOrganismo.Coincide(runt, nombre, municipio).Should().BeTrue();

    [Theory]
    [InlineData("STRIA TTOyTTE MCPAL SABANETA", "Secretaría de Tránsito de Envigado", "Envigado")] // Sabaneta no es Envigado
    [InlineData("SECRETARIA DE TRANSITO DE ENVIGADO", "Secretaría de Tránsito de Sabaneta", "Sabaneta")]
    [InlineData("SECRETARIA DE MOVILIDAD DE MEDELLIN", "Secretaría de Movilidad de Envigado", "Envigado")]
    [InlineData("SECRETARIA DE TRANSITO DE BELLO MEDELLIN", "Secretaría de Tránsito de Itagüí", "Itagüí")] // un nombre que cita dos municipios, ninguno es el del OT
    [InlineData("SECRETARIA DE TRANSITO DE SABANETAS", "Secretaría de Tránsito de Sabaneta", "Sabaneta")] // solo contiene la palabra a medias
    [InlineData("OT DE OTRO MUNICIPIO", "Tránsito de Prueba", "")]
    [InlineData("", "Tránsito de Prueba", "Municipio")] // sin organismo del RUNT nunca coincide
    [InlineData("   ", "Tránsito de Prueba", "Municipio")]
    [InlineData("CALI", "Santiago de Cali", "")] // 4 letras: solo igualdad, no contención (sin aceptar de más)
    public void Coincide_Negativos(string runt, string nombre, string municipio) =>
        CoincidenciaOrganismo.Coincide(runt, nombre, municipio).Should().BeFalse();

    [Fact]
    public void Coincide_ElCandidatoVacioNoCuenta_YElCodigoSoloPorIgualdad()
    {
        CoincidenciaOrganismo.Coincide("AB1", null, "", "AB1").Should().BeTrue();
        CoincidenciaOrganismo.Coincide("TRANSITO AB1 X", null, "", "AB1").Should().BeFalse("AB1 es corto: no se busca contenido");
        CoincidenciaOrganismo.Coincide("TRANSITO", string.Empty, "  ").Should().BeFalse();
    }

    [Fact]
    public void Normalizar_QuitaTildesYSimbolos_PasaAMayusculas()
    {
        CoincidenciaOrganismo.Normalizar("  Secretaría de Tránsito y T/te. de Ñandú ").Should().Be("SECRETARIA DE TRANSITO Y T TE DE NANDU");
        CoincidenciaOrganismo.Normalizar(null).Should().BeEmpty();
    }

    [Fact]
    public void ElMunicipioDeLaSesionDelPunto_EntraALaMismaRegla()
    {
        var sesion = new OrganismoPunto(Guid.NewGuid(), "SAB", "Secretaría de Tránsito de Sabaneta");

        TiposDelPunto.OrganismoDeLaSesion("STRIA TTOyTTE MCPAL SABANETA", sesion).Should().BeFalse("sin municipio no coincide");
        TiposDelPunto.OrganismoDeLaSesion("STRIA TTOyTTE MCPAL SABANETA", sesion, "Sabaneta").Should().BeTrue();
        TiposDelPunto.OrganismoDeLaSesion("STRIA TTOyTTE MCPAL ENVIGADO", sesion, "Sabaneta").Should().BeFalse();
        TiposDelPunto.OrganismoDeLaSesion(null, sesion, "Sabaneta").Should().BeFalse("sin organismo del RUNT no se sigue");
    }

    // ── Portal web: diagnóstico ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Diagnostico_PlacaDeOtroOrganismo_Responde422_YNoGuardaElDiagnostico()
    {
        await IniciarAsync("STRIA TTOyTTE MCPAL SABANETA");
        var (id, token) = await AbrirSesion();

        var r = await Diagnosticar(id, token);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be("organismo_no_corresponde");
        var texto = await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        texto.Should().Contain("Esta placa está registrada en STRIA TTOyTTE MCPAL SABANETA, no en Tránsito de Prueba. Este trámite debe hacerse en STRIA TTOyTTE MCPAL SABANETA.");
        texto.Should().NotContain(Propietario).And.NotContain("PROPIETARIO FICTICIO");
        _requisitos.Ultima.Should().BeNull("ni siquiera se llegó a evaluar el diagnóstico de FLIT 2");
        await using var db = _host.NuevaDb();
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Datos.Should().NotContain("diagnostico").And.NotContain("organismoVerificadoId");
    }

    [Fact]
    public async Task Diagnostico_ElRuntNoTraeOrganismo_Responde422_ConElMensajeClaro()
    {
        await IniciarAsync("");
        var (id, token) = await AbrirSesion();

        var r = await Diagnosticar(id, token);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be("organismo_no_corresponde");
        (await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain(CoincidenciaOrganismo.MensajeOrganismoDesconocido);
    }

    [Fact]
    public async Task Diagnostico_ElOrganismoCoincide_FijaLaVerificacion_YRadicarNoVuelveAlRunt()
    {
        await IniciarAsync("TRANSITO DE PRUEBA");
        var (id, token) = await AbrirSesion();

        (await Diagnosticar(id, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        _host.Runt.LlamadasVehiculo.Should().ContainSingle().Which.Propietario.Should().Be(new DocumentoConsulta("CC", Propietario));
        await using (var db = _host.NuevaDb())
            DatosSolicitud.Leer((await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Datos).OrganismoVerificadoId.Should().BeEquivalentTo(PortalHost.Organismo);

        // Verificado: radicar sigue (aquí se detiene solo por lo que falta del borrador, no por el organismo) y no consulta el RUNT otra vez.
        var radicar = await Radicar(id, token);
        (await PortalHost.CodigoAsync(radicar)).Should().Be("requisitos_incompletos");
        _host.Runt.LlamadasVehiculo.Should().HaveCount(1);
    }

    [Fact]
    public async Task Diagnostico_ConElProveedorCaido_Responde503_SinDecidirEnFalso()
    {
        await IniciarAsync("TRANSITO DE PRUEBA");
        _host.Runt.Vehiculo = ResultadosFalsos.ProveedorCaido;
        var (id, token) = await AbrirSesion();

        var r = await Diagnosticar(id, token);

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await PortalHost.CodigoAsync(r)).Should().Be("proveedor_no_disponible");
        await using var db = _host.NuevaDb();
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Datos.Should().NotContain("organismoVerificadoId");
    }

    [Fact]
    public async Task Diagnostico_ElVehiculoNoExisteEnElRunt_NoDecideElOrganismo_ElDiagnosticoDeFlit2Habla()
    {
        await IniciarAsync("TRANSITO DE PRUEBA");
        _host.Runt.Vehiculo = ResultadosFalsos.VehiculoInexistente;
        var (id, token) = await AbrirSesion();

        (await Diagnosticar(id, token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = _host.NuevaDb();
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Datos.Should().NotContain("organismoVerificadoId");
    }

    [Fact]
    public async Task Diagnostico_ConOrganismoQueEligeElOperador_NoComparaNiConsultaElRunt()
    {
        await IniciarAsync("SECRETARIA DE TRANSITO DE ENVIGADO");
        _host.Definiciones.PorCodigo["MATRICULA_NUEVA"] = DefinicionesDePrueba.UnTitular("MATRICULA_NUEVA") with
        {
            Flags = DefinicionesDePrueba.UnTitular().Flags with { OrganismoLoEligeOperador = true },
        };
        var (id, token) = await AbrirSesion("MATRICULA_NUEVA");

        (await Diagnosticar(id, token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var radicar = await Radicar(id, token);

        (await PortalHost.CodigoAsync(radicar)).Should().NotBe("organismo_no_corresponde").And.NotBe("organismo_no_verificado");
        _host.Runt.LlamadasVehiculo.Should().BeEmpty("el organismo lo elige el operador: no se compara con el RUNT");
    }

    // ── Portal web: radicación ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Radicar_SinDiagnosticar_ConLaPlacaDeOtroOrganismo_Responde422()
    {
        await IniciarAsync("SECRETARIA DE TRANSITO DE ENVIGADO");
        var (id, token) = await AbrirSesion();
        await GuardarVendedor(id, token); // sin diagnóstico el titular del borrador es con quien se consulta el RUNT

        var r = await Radicar(id, token);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be("organismo_no_corresponde");
        await using var db = _host.NuevaDb();
        (await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken)).Estado.Should().Be("borrador");
    }

    [Fact]
    public async Task Radicar_BorradorViejoSinVerificar_SeVerificaAhora_YSiCoincideSigue()
    {
        await IniciarAsync("TRANSITO DE PRUEBA");
        var (id, token) = await AbrirSesion();
        await GuardarVendedor(id, token);

        var r = await Radicar(id, token);

        (await PortalHost.CodigoAsync(r)).Should().Be("requisitos_incompletos", "pasó la verificación del organismo; lo que falta es del borrador");
        _host.Runt.LlamadasVehiculo.Should().ContainSingle();
        // Radicar falló por lo que falta del borrador: la transacción se revierte y la verificación se repite la próxima vez (con la caché del RUNT).
    }

    [Fact]
    public async Task Radicar_SinForma_DeConsultarElVehiculo_NoSeRadicaSinVerificar()
    {
        await IniciarAsync("TRANSITO DE PRUEBA");
        var (id, token) = await AbrirSesion(); // sin diagnóstico ni titular: no hay documento con el cual consultar

        var r = await Radicar(id, token);

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(r)).Should().Be("organismo_no_verificado");
        _host.Runt.LlamadasVehiculo.Should().BeEmpty();
    }

    [Fact]
    public async Task Radicar_ElRuntSinOrganismo_Responde422_YConProveedorCaidoResponde503()
    {
        await IniciarAsync("");
        var (id, token) = await AbrirSesion();
        await GuardarVendedor(id, token);

        var sin = await Radicar(id, token);
        sin.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await PortalHost.CodigoAsync(sin)).Should().Be("organismo_no_corresponde");

        _host.Runt.Vehiculo = ResultadosFalsos.ProveedorCaido;
        var caido = await Radicar(id, token);
        caido.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await PortalHost.CodigoAsync(caido)).Should().Be("proveedor_no_disponible");
    }

    [Fact]
    public async Task Radicar_VerificadoContraOtroOrganismoDelBorrador_SeVuelveAVerificar()
    {
        await IniciarAsync("SECRETARIA DE TRANSITO DE ENVIGADO");
        var (id, token) = await AbrirSesion();
        await GuardarVendedor(id, token);
        await using (var db = _host.NuevaDb())
        {
            var s = await db.Set<SolicitudTramite>().SingleAsync(TestContext.Current.CancellationToken);
            s.Datos = (DatosSolicitud.Leer(s.Datos) with { OrganismoVerificadoId = Guid.NewGuid().ToString() }).Serializar();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var r = await Radicar(id, token);

        (await PortalHost.CodigoAsync(r)).Should().Be("organismo_no_corresponde", "una verificación contra otro organismo no vale para este");
    }

    // ── Soporte ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task IniciarAsync(string organismoRunt)
    {
        _host = await PortalHost.IniciarAsync("pnorg", (servicios, host) =>
        {
            servicios.AddSingleton<IFachadaRequisitos>(_requisitos);
            host.Runt.Vehiculo = ConsultasRuntFalso.DeOrganismo(organismoRunt).Vehiculo;
        });
        _iniciado = true;
        _host.Definiciones.PorCodigo["TRASPASO_STANDARD"] = DefinicionesDePrueba.Traspaso();
    }

    private async Task<(Guid Id, string Token)> AbrirSesion(string tipo = "TRASPASO_STANDARD")
    {
        var r = await _host.EnviarAsync(HttpMethod.Post, $"{Base}/sesiones",
            new { tipoCodigo = tipo, organismoId = PortalHost.Organismo, cedula = PortalHost.Cedula, placa = "ABC123", consentimiento = true, consentimientoVersion = "v1" }, captcha: true);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = (await PortalHost.JsonAsync(r)).RootElement;
        return (json.GetProperty("solicitudId").GetGuid(), json.GetProperty("token").GetString()!);
    }

    private Task<HttpResponseMessage> Diagnosticar(Guid id, string token) =>
        _host.EnviarAsync(HttpMethod.Post, $"{Base}/solicitudes/{id}/diagnostico", new { propietario = new { tipoDocumento = "cc", numeroDocumento = Propietario } }, token);

    private Task<HttpResponseMessage> Radicar(Guid id, string token) =>
        _host.EnviarAsync(HttpMethod.Post, $"{Base}/solicitudes/{id}/radicar", null, token, false, [("Idempotency-Key", "k-org-1")]);

    private async Task GuardarVendedor(Guid id, string token) =>
        (await _host.EnviarAsync(HttpMethod.Patch, $"{Base}/solicitudes/{id}/actores", new
        {
            actores = new object[] { new { rol = "vendedor", tipoDocumento = "CC", numeroDocumento = Propietario, nombre = "Persona de Prueba", correo = "p@example.org", celular = "3001234567" } },
        }, token)).StatusCode.Should().Be(HttpStatusCode.OK);
}
