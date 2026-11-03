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
        if (!await IniciarAsync()) return;
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
        if (!await IniciarAsync()) return;
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
        if (!await IniciarAsync()) return;
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
    public async Task Radicante_ConDocumentoDistintoDelPrimerPaso_Responde422()
    {
        if (!await IniciarAsync()) return;
        var solicitud = await SembrarPuntoAsync(SnapshotTitular(), Cedula);
        var ct = TestContext.Current.CancellationToken;

        var r = await EnScopeAsync(sp => sp.GetRequiredService<IdentidadPuntoService>()
            .RadicanteInSituAsync(solicitud, null, 1, "CC", CedulaOtra, "X", null, null, null, null, ct));

        r.Http.Should().Be(422);
        r.Codigo.Should().Be(IdentidadPuntoService.CodigoDocumentoDistinto);
    }

    [Fact]
    public async Task Invitar_RolQueNoExisteORolRadicante_Responde422()
    {
        if (!await IniciarAsync()) return;
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
}
