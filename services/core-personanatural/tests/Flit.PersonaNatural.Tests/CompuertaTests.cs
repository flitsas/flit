using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Radicacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.PersonaNatural.Tests;

/// <summary>
/// HU #14046 (Feature C, Épica #14021) — la compuerta de radicación: ninguna solicitud sale hacia FLIT 2 con partes obligadas
/// sin identidad aprobada. Sin vigencia en días y sin reutilizar lo que la misma cédula validó en otro trámite (decisión del
/// PO, 2026-10-27). Sobre el host real con la fachada de FLIT 2 falsa.
/// </summary>
public sealed class CompuertaTests : IdentidadBase
{
    [Fact]
    public async Task ConTodasLasPartesObligadasAprobadas_Permite()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");
        await PonerEstadoAsync(solicitud, "comprador", "aprobada");

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeTrue();
        r.Motivo.Should().Be(MotivosCompuerta.Ok);
        r.Partes.Should().BeEmpty();
    }

    [Fact]
    public async Task ConUnaPartePendiente_Bloquea_ConElRolQueFalta()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");
        await PonerEstadoAsync(solicitud, "comprador", "pendiente");

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeFalse();
        r.Motivo.Should().Be(MotivosCompuerta.IdentidadPendiente);
        r.Partes.Should().ContainSingle().Which.Should().Be(new ParteFaltante("comprador", 1, "pendiente"));
    }

    [Theory]
    [InlineData("sin_solicitar")]
    [InlineData("pendiente_envio")]
    [InlineData("vencida")]
    public async Task UnaParteSinPedirPorEnviarOVencida_BloqueaComoPendiente(string estado)
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");
        await PonerEstadoAsync(solicitud, "comprador", estado);

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeFalse();
        r.Motivo.Should().Be(MotivosCompuerta.IdentidadPendiente);
        r.Partes.Should().ContainSingle().Which.Estado.Should().Be(estado);
    }

    [Fact]
    public async Task ConUnaParteRechazada_BloqueaConElMotivoDeRechazo()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "rechazada");
        await PonerEstadoAsync(solicitud, "comprador", "pendiente");

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeFalse();
        r.Motivo.Should().Be(MotivosCompuerta.IdentidadRechazada);
        r.Partes.Select(p => p.Parte).Should().BeEquivalentTo("vendedor", "comprador");
    }

    [Fact]
    public async Task UnRolObligadoSinPersonaRegistrada_AunFalta()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync(conPersonas: false);

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeFalse();
        r.Partes.Should().BeEquivalentTo([new ParteFaltante("vendedor", 1, "sin_solicitar"), new ParteFaltante("comprador", 1, "sin_solicitar")]);
    }

    [Fact]
    public async Task UnaAprobadaSinIdDeValidacion_NoCuenta()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");
        await PonerEstadoAsync(solicitud, "comprador", "aprobada");
        await EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<PersonaNaturalDb>().Set<PersonaSolicitante>().Where(p => p.SolicitudId == solicitud && p.Parte == "comprador")
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ValidacionId, _ => (Guid?)null), TestContext.Current.CancellationToken);
            return 0;
        });

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeFalse();
        r.Partes.Should().ContainSingle().Which.Parte.Should().Be("comprador");
    }

    [Fact]
    public async Task LaValidacionAprobadaDeOtraSolicitudDeLaMismaCedula_NoCuenta()
    {
        if (!await IniciarAsync()) return;
        var (ayer, _) = await SembrarAsync();
        await PonerEstadoAsync(ayer, "vendedor", "aprobada");
        await PonerEstadoAsync(ayer, "comprador", "aprobada");
        (await EvaluarAsync(ayer)).Permite.Should().BeTrue("la solicitud de ayer sí está completa");

        var (hoy, _) = await SembrarAsync(); // mismas cédulas, trámite nuevo

        var r = await EvaluarAsync(hoy);

        r.Permite.Should().BeFalse("cada trámite vuelve a validar a cada parte");
        r.Partes.Select(p => p.Parte).Should().BeEquivalentTo("vendedor", "comprador");
        r.Partes.Should().OnlyContain(p => p.Estado == "sin_solicitar");
    }

    [Fact]
    public async Task UnaAprobadaNoCaducaPorElPaso_DelTiempo_NiPorUnaVigenciaEnElPasado()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync();
        await PonerEstadoAsync(solicitud, "vendedor", "aprobada");
        await PonerEstadoAsync(solicitud, "comprador", "aprobada");
        await EnScopeAsync(async sp =>
        {
            await sp.GetRequiredService<PersonaNaturalDb>().Set<PersonaSolicitante>().Where(p => p.SolicitudId == solicitud)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ValidacionVigenteHasta, _ => (DateTimeOffset?)Reloj.GetUtcNow().AddDays(-400)), TestContext.Current.CancellationToken);
            return 0;
        });
        Reloj.Avanzar(TimeSpan.FromDays(120));

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeTrue("la validación vale para esta solicitud y no tiene vigencia en días");
    }

    [Fact]
    public async Task SiElEventoNoLlego_LaCompuertaPreguntaAFlit2_YPermiteSiYaEstabaAprobada()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        await SolicitarAsync(solicitud, token, "comprador");
        foreach (var parte in new[] { "vendedor", "comprador" })
            Fachada.Estados[(await PersonaAsync(solicitud, parte)).ValidacionId!.Value] = "aprobada";

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeTrue();
        (await PersonaAsync(solicitud, "vendedor")).ValidacionEstado.Should().Be("aprobada");
    }

    [Fact]
    public async Task ConFlit2Caido_LaCompuertaNoAdivina_YBloqueaLoPendiente()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, token) = await SembrarAsync();
        await SolicitarAsync(solicitud, token, "vendedor");
        await SolicitarAsync(solicitud, token, "comprador");
        foreach (var parte in new[] { "vendedor", "comprador" })
            Fachada.Estados[(await PersonaAsync(solicitud, parte)).ValidacionId!.Value] = "aprobada";
        Fachada.Caida = true;

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeFalse();
        r.Motivo.Should().Be(MotivosCompuerta.IdentidadPendiente);
    }

    [Fact]
    public async Task UnTipoSinRolesConIdentidad_PermiteSinPreguntarANadie()
    {
        if (!await IniciarAsync()) return;
        var (solicitud, _) = await SembrarAsync(conPersonas: false, snapshot: """{"actores":[{"rol":"titular","min":1,"biometria":false}]}""");

        var r = await EvaluarAsync(solicitud);

        r.Permite.Should().BeTrue();
        Fachada.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task UnaSolicitudInexistente_NoSePuedeRadicar()
    {
        if (!await IniciarAsync()) return;

        var r = await EvaluarAsync(Guid.NewGuid());

        r.Permite.Should().BeFalse();
    }

    private Task<ResultadoCompuerta> EvaluarAsync(Guid solicitud) =>
        EnScopeAsync(sp => sp.GetRequiredService<ICompuertaDeRadicacion>().EvaluarAsync(solicitud, TestContext.Current.CancellationToken));
}
