using Flit.PersonaNatural.Api.Solicitudes;
using FluentAssertions;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Identidad;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Punto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Canal del punto: el radicante es el primer rol de la definición del tipo y las partes invitables son los demás. Sin roles
/// fijos en el BFF (un tipo de un solo rol «titular» no necesita comprador; traspaso sigue igual).
/// </summary>
public sealed class PuntoIdentidadRolesTests : IdentidadBase
{
    private static string SnapshotTitular() =>
        DefinicionesService.Serializar(DefinicionesDePrueba.UnTitular() with { Actores = [new ActorRolDto("titular", "Titular", 1, 1, true, true, true)] });

    /// <summary>RUNT falso: un documento es propietario si está en <see cref="Propietarios"/> (copropiedad: varios).</summary>
    private sealed class RuntPropietariosFalso(HashSet<string> propietarios) : Flit.PersonaNatural.Api.Consultas.IConsultasRunt
    {
        public Task<Flit.Consultas.Grpc.V1.ResultadoConsulta> ConsultarVehiculoAsync(string? placa, string? vin, Flit.PersonaNatural.Api.Consultas.DocumentoConsulta? propietario, string? referencia, CancellationToken ct)
        {
            if (propietario is null || !propietarios.Contains(propietario.Numero))
                return Task.FromResult(ResultadosFalsos.VehiculoInexistente());
            var r = ResultadosFalsos.VehiculoCompleto();
            r.Campos.First(c => c.Clave == "owner_document_number").ValorTexto = propietario.Numero;
            return Task.FromResult(r);
        }

        public Task<Flit.Consultas.Grpc.V1.ResultadoConsulta> ConsultarMultasAsync(string? placa, Flit.PersonaNatural.Api.Consultas.DocumentoConsulta? documento, string? referencia, CancellationToken ct) => throw new NotSupportedException();

        public Task<Flit.PersonaNatural.Api.Consultas.AvaluoSugeridoConsulta> ConsultarAvaluosAsync(string? placa, string? vin, CancellationToken ct) => throw new NotSupportedException();

        public Task<Flit.Consultas.Grpc.V1.ResultadoConsulta> ConsultarConductorAsync(Flit.PersonaNatural.Api.Consultas.DocumentoConsulta documento, string? referencia, CancellationToken ct) => throw new NotSupportedException();
    }

    private HashSet<string> Propietarios { get; } = [Cedula, "1098765432"];

    private Task<bool> IniciarPuntoAsync()
    {
        Servicios = s => s.AddSingleton<Flit.PersonaNatural.Api.Consultas.IConsultasRunt>(new RuntPropietariosFalso(Propietarios));
        return IniciarAsync();
    }

    private async Task<Guid> SembrarPuntoAsync(string snapshot, string cedulaPrimerPaso)
    {
        var (solicitud, _) = await SembrarAsync(cedulaComprador: cedulaPrimerPaso, conPersonas: false, snapshot: snapshot);
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>();
        var fila = await db.Set<SolicitudTramite>().SingleAsync(s => s.Id == solicitud, TestContext.Current.CancellationToken);
        fila.Datos = "{\"canal\":\"pn_punto\"}";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return solicitud;
    }

    [Fact]
    public async Task Radicante_TipoDeUnSoloRol_UsaElTitular_SinComprador()
    {
        if (!await IniciarPuntoAsync()) return;
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;

        var r = await EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, null, 1, "CC", Cedula, "Titular Prueba", null, null, null, null, ct));

        r.Exito.Should().BeTrue(r.Mensaje);
        Fachada.Pedidos.Should().ContainSingle().Which.Parte.Should().Be("titular");
        (await PersonaAsync(solicitud, "titular")).DocumentoHash.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Radicante_Traspaso_SigueSiendoElVendedor()
    {
        if (!await IniciarPuntoAsync()) return;
        var solicitud = await SembrarPuntoAsync(DefinicionesService.Serializar(DefinicionesDePrueba.Traspaso()), Cedula);
        var ct = TestContext.Current.CancellationToken;

        var r = await EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, null, 1, "CC", Cedula, "Vendedor Prueba", null, null, null, null, ct));

        r.Exito.Should().BeTrue(r.Mensaje);
        Fachada.Pedidos.Should().ContainSingle().Which.Parte.Should().Be("vendedor");
    }

    [Fact]
    public async Task Radicante_ConRolQueNoExisteEnLaDefinicion_Responde422()
    {
        if (!await IniciarPuntoAsync()) return;
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;

        var r = await EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, "comprador", 1, "CC", Cedula, "X", null, null, null, null, ct));

        r.Exito.Should().BeFalse();
        r.Http.Should().Be(422);
        r.Codigo.Should().Be(IdentidadPuntoService.CodigoRolDesconocido);
        Fachada.Pedidos.Should().BeEmpty();
    }


    [Fact]
    public async Task Invitar_RolQueNoExisteORolRadicante_Responde422()
    {
        if (!await IniciarPuntoAsync()) return;
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;
        var svc = (IServiceProvider sp) => sp.GetRequiredService<IdentidadPuntoService>();
        (await EnScopeAsync(sp => svc(sp).RadicanteInSituAsync(solicitud, null, 1, "CC", Cedula, "Titular", null, null, null, null, ct))).Exito.Should().BeTrue();
        using (var scope = App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PersonaNaturalDb>();
            var p = await db.Set<PersonaSolicitante>().SingleAsync(x => x.SolicitudId == solicitud && x.Parte == "titular", ct);
            p.ValidacionEstado = EstadosIdentidad.Aprobada;
            await db.SaveChangesAsync(ct);
        }

        var desconocido = await EnScopeAsync(sp => svc(sp).InvitarAsync(solicitud,
            new InvitarPartePuntoRequest("comprador", 1, "CC", CedulaOtra, "Otro", "otro@example.org", null), ct));
        var radicante = await EnScopeAsync(sp => svc(sp).InvitarAsync(solicitud,
            new InvitarPartePuntoRequest("titular", 1, "CC", CedulaOtra, "Otro", "otro@example.org", null), ct));

        desconocido.Http.Should().Be(422);
        desconocido.Codigo.Should().Be(IdentidadPuntoService.CodigoRolDesconocido);
        radicante.Http.Should().Be(422);
        radicante.Codigo.Should().Be(IdentidadPuntoService.CodigoRolRadicante);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wiññyn.londono@flitsas.com")]
    public async Task Radicante_ConCorreoVacioOInvalido_UsaCorreoTecnico(string correo)
    {
        if (!await IniciarPuntoAsync()) return;
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;

        var r = await EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, null, 1, "CC", Cedula, "Titular", correo, null, null, null, ct));

        r.Exito.Should().BeTrue(r.Mensaje);
        Fachada.Pedidos.Should().ContainSingle().Which.Correo.Should().EndWith("@noreply.flit.local");
    }

    [Fact]
    public async Task GuardarActores_CorreoVacio_SoloLoAceptaElRadicanteDelPunto()
    {
        if (!await IniciarPuntoAsync()) return;
        var ct = TestContext.Current.CancellationToken;
        var snapshot = DefinicionesService.Serializar(DefinicionesDePrueba.Traspaso());
        var punto = await SembrarPuntoAsync(snapshot, Cedula);
        var (web, _) = await SembrarAsync(conPersonas: false, snapshot: snapshot);
        ActoresRequest Cuerpo(string rol, string doc) => new(null, [new ActorRequest(rol, 1, "CC", doc, "Persona Prueba", "", null, null, null)]);

        // Punto: el radicante (primer rol) puede ir sin correo.
        await EnScopeAsync(async sp => await sp.GetRequiredService<Flit.PersonaNatural.Api.Solicitudes.SolicitudesService>().GuardarActoresAsync(punto, Cuerpo("vendedor", Cedula), ct));
        // Punto: una parte invitada sí necesita correo.
        var invitada = async () => await EnScopeAsync(async sp => await sp.GetRequiredService<Flit.PersonaNatural.Api.Solicitudes.SolicitudesService>().GuardarActoresAsync(punto, Cuerpo("comprador", CedulaOtra), ct));
        await invitada.Should().ThrowAsync<Flit.PersonaNatural.Api.Solicitudes.ReglaDeNegocioException>();
        // Portal web: el correo sigue obligatorio, también para el primer rol.
        var enWeb = async () => await EnScopeAsync(async sp => await sp.GetRequiredService<Flit.PersonaNatural.Api.Solicitudes.SolicitudesService>().GuardarActoresAsync(web, Cuerpo("vendedor", Cedula), ct));
        await enWeb.Should().ThrowAsync<Flit.PersonaNatural.Api.Solicitudes.ReglaDeNegocioException>();
    }

    [Fact]
    public async Task Radicante_CopropietarioNoPrincipal_PuedeRadicar()
    {
        if (!await IniciarPuntoAsync()) return;
        Propietarios.Add(CedulaOtra); // copropiedad: el dueño del primer paso es Cedula; CedulaOtra también es propietario
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;

        var r = await EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, null, 1, "CC", CedulaOtra, "Copropietario", null, null, null, null, ct));

        r.Exito.Should().BeTrue(r.Mensaje);
        Fachada.Pedidos.Should().ContainSingle();
    }

    [Fact]
    public async Task Radicante_NoPropietario_RespondeMensajeDeNoPropietario_YNoPideValidacion()
    {
        if (!await IniciarPuntoAsync()) return;
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;

        var r = await EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, null, 1, "CC", CedulaOtra, "Otro", null, null, null, null, ct));

        r.Exito.Should().BeFalse();
        r.Http.Should().Be(422);
        r.Codigo.Should().Be(IdentidadPuntoService.CodigoNoDueño);
        r.Mensaje.Should().Be("No puedes realizar este trámite porque no eres el propietario del vehículo.");
        Fachada.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Radicante_RechazoDelProveedor_InformaElMotivo_YPermiteReintentar()
    {
        if (!await IniciarPuntoAsync()) return;
        Fachada.Rechazo = true;
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;
        Task<ResultadoIdentidad> Pedir() => EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, null, 1, "CC", Cedula, "Titular", null, null, null, null, ct));

        var r = await Pedir();

        r.Http.Should().Be(422);
        r.Codigo.Should().Be("proveedor_rechazo");
        r.Mensaje.Should().Be("No pudimos iniciar la validación con estos datos. Revisa el correo y el celular e inténtalo de nuevo.");
        (await PersonaAsync(solicitud, "titular")).ValidacionEstado.Should().Be("sin_solicitar");

        Fachada.Rechazo = false;
        (await Pedir()).Exito.Should().BeTrue();
    }

    [Fact]
    public async Task Radicante_FlitSinRespuesta_NoDejaPendienteEnvio_Responde503()
    {
        if (!await IniciarPuntoAsync()) return;
        Fachada.Caida = true;
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;

        var r = await EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, null, 1, "CC", Cedula, "Titular", null, null, null, null, ct));

        r.Exito.Should().BeFalse();
        r.Http.Should().Be(503);
        (await PersonaAsync(solicitud, "titular")).ValidacionEstado.Should().NotBe("pendiente_envio");
    }
}
