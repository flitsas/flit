using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Radicados;
using Flit.PersonaNatural.Api.Sesiones;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// Revisión de seguridad sobre la HU #14035 — bloqueo por radicado atómico (M2), con tope por (radicado, IP) y tope mayor por
/// radicado contra la negación de servicio dirigida (I6), un único punto de política (M1) y depuración periódica de lo
/// efímero (B3). Host real y base efímera propia (sin Postgres se omite).
/// </summary>
public sealed class BloqueoYDepuracionTests
{
    private const string Radicado = "PN-2026-000777";
    private const string Cedula = "1020456789";

    [Fact]
    public async Task ConPeticionesParalelas_SoloPasanLasQueCabenEnElCupo()
    {
        await using var host = await PortalHost.IniciarAsync("blqpar");
        var clave = PiiHasher.ClaveRadicado(Radicado);
        var ip = Ip("198.51.100.1");

        var resultados = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => host.EnScopeAsync(async sp =>
            await sp.GetRequiredService<BloqueoPorRadicado>().ReservarIntentoAsync(clave, ip, TestContext.Current.CancellationToken))));

        resultados.Count(r => r is null).Should().Be(5, "el cupo por (radicado, IP) es de 5 y las demás se serializan tras el cerrojo");
        resultados.Count(r => r is not null).Should().Be(5);
        await using var db = host.NuevaDb();
        (await db.Set<IntentoReanudar>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(10, "cada intento reservado cuenta por IP y por radicado");
    }

    [Fact]
    public async Task UnAtacanteDesdeUnaIp_NoBloqueaAlDuenoDesdeOtra()
    {
        await using var host = await PortalHost.IniciarAsync("blqdos");
        await SembrarAsync(host);
        var atacante = Ip("198.51.100.1");
        var dueno = Ip("203.0.113.7");

        for (var i = 0; i < 5; i++)
            (await Reanudar(host, $"mal{i}", atacante)).Should().BeOfType<ResultadoReanudar.NoEncontrada>();
        (await Reanudar(host, "mal5", atacante)).Should().BeOfType<ResultadoReanudar.Bloqueado>("la IP que insiste queda bloqueada");
        (await Reanudar(host, Cedula, atacante)).Should().BeOfType<ResultadoReanudar.Bloqueado>("también con los datos correctos mientras dure el bloqueo");

        (await Reanudar(host, Cedula, dueno)).Should().BeOfType<ResultadoReanudar.Exito>("el dueño legítimo, desde su IP, sigue entrando");
    }

    [Fact]
    public async Task UnAtaqueDistribuido_BloqueaElRadicadoParaTodos()
    {
        await using var host = await PortalHost.IniciarAsync("blqdis");
        await SembrarAsync(host);

        // 20 IP distintas, un intento fallido cada una: ninguna pasa su cupo, pero el radicado sí llega al tope mayor.
        for (var i = 0; i < 20; i++)
            (await Reanudar(host, $"mal{i}", Ip($"198.51.100.{i + 1}"))).Should().BeOfType<ResultadoReanudar.NoEncontrada>();

        (await Reanudar(host, Cedula, Ip("203.0.113.7"))).Should().BeOfType<ResultadoReanudar.Bloqueado>();
    }

    [Fact]
    public async Task ReanudarYConsultarRadicado_ComparteElMismoCupo_PorElMismoPuntoDePolitica()
    {
        await using var host = await PortalHost.IniciarAsync("blqcom");
        var ip = Ip("198.51.100.1");
        for (var i = 0; i < 3; i++)
            (await Reanudar(host, $"mal{i}", ip)).Should().BeOfType<ResultadoReanudar.NoEncontrada>();

        // Dos fallos por «consultar radicado» completan los cinco de la misma IP sobre el mismo radicado.
        for (var i = 0; i < 2; i++)
            (await host.EnScopeAsync(sp => sp.GetRequiredService<RadicadoConsultaService>().ConsultarAsync(Radicado, $"x{i}", null, ip, TestContext.Current.CancellationToken)))
                .Should().BeOfType<ResultadoConsultaRadicado.NoEncontrado>();

        (await Reanudar(host, "mal5", ip)).Should().BeOfType<ResultadoReanudar.Bloqueado>();
    }

    [Fact]
    public async Task LaRetencion_DepuraLoEfimeroVencido_YConservaLoVigente()
    {
        await using var host = await PortalHost.IniciarAsync("blqdep");
        var ahora = host.Reloj.GetUtcNow();
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);
        var solicitud = await SembrarAsync(host);
        await using (var db = host.NuevaDb())
        {
            db.AddRange(
                new IntentoReanudar { Clave = [1], OcurrioEn = ahora.AddDays(-2) },
                new IntentoReanudar { Clave = [2], OcurrioEn = ahora.AddMinutes(-5) },
                Sesion(solicitud, "viejaVencida", ahora.AddDays(-3), revocada: false),
                Sesion(solicitud, "viejaRevocada", ahora.AddDays(1), revocada: true, creada: ahora.AddDays(-3)),
                Sesion(solicitud, "vigente", ahora.AddMinutes(30), revocada: false),
                new CuotaConsulta { Clave = "viejo", Dia = hoy.AddDays(-3), Usadas = 4 },
                new CuotaConsulta { Clave = "hoy", Dia = hoy, Usadas = 1 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var borradas = await host.EnScopeAsync(sp => sp.GetRequiredService<RetencionService>().PurgarEfimerosAsync(TestContext.Current.CancellationToken));

        borradas.Should().Be(4);
        await using var verificar = host.NuevaDb();
        (await verificar.Set<IntentoReanudar>().Select(i => i.Clave[0]).ToListAsync(TestContext.Current.CancellationToken)).Should().Equal(2);
        (await verificar.Set<SesionTramite>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        (await verificar.Set<CuotaConsulta>().Select(c => c.Clave).ToListAsync(TestContext.Current.CancellationToken)).Should().Equal("hoy");
        (await verificar.Set<SolicitudTramite>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1, "los borradores no los toca esta depuración");
        (await host.EnScopeAsync(sp => sp.GetRequiredService<RetencionService>().PurgarEfimerosAsync(TestContext.Current.CancellationToken))).Should().Be(0, "es idempotente");
    }

    private static byte[] Ip(string ip) => [.. System.Net.IPAddress.Parse(ip).GetAddressBytes()];

    private static Task<ResultadoReanudar> Reanudar(PortalHost host, string cedula, byte[] ip) =>
        host.EnScopeAsync(sp => sp.GetRequiredService<SesionService>().ReanudarAsync(Radicado, cedula, ip, TestContext.Current.CancellationToken));

    private static SesionTramite Sesion(Guid solicitud, string token, DateTimeOffset expira, bool revocada, DateTimeOffset? creada = null) => new()
    {
        Id = Guid.CreateVersion7(),
        SolicitudId = solicitud,
        TokenHash = PiiHasher.HashToken(token),
        ExpiraEn = expira,
        Revocada = revocada,
        CreatedAt = creada ?? expira.AddHours(-1),
    };

    private static async Task<Guid> SembrarAsync(PortalHost host)
    {
        var hasher = new PiiHasher(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [PiiHasher.ClaveConfig] = PortalHost.ClaveHmac }).Build());
        await using var db = host.NuevaDb();
        var solicitud = new SolicitudTramite
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.NewGuid(),
            CodigoPublico = Radicado,
            TipoTramiteCodigo = "TRASPASO_STANDARD",
            DefinicionVersion = 1,
            OrganismoId = Guid.NewGuid(),
            OrganismoNombre = "Organismo de prueba",
            Placa = "ABC123",
            Estado = EstadoSolicitud.Borrador,
            CedulaSolicitanteHash = hasher.HashCedula(Cedula),
            CorrelationId = Guid.NewGuid(),
        };
        db.Add(solicitud);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return solicitud.Id;
    }
}
