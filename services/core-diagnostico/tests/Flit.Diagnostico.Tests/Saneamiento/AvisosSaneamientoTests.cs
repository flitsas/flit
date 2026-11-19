using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Notificaciones;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Avisos de la campana al solicitante (plan §4.5, ADR-0087 D-8; HU #14576 AC48–AC50; seguridad SP-22 y SP-23): un aviso por
/// acción de FLIT y por par (solicitante, diagnóstico), con <c>datos</c> de solo cantidad y resultado y <c>referencia_id</c> = el
/// evento de menor id del grupo, escrito desde el backoffice sin chocar con <c>ux_notificaciones_idempotencia</c>. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~AvisosSaneamientoTests
/// </code>
/// </example>
public sealed class AvisosSaneamientoTests
{
    // AC48 + SP-23 — cada acción de FLIT avisa a solicitado_por, en el tenant del lote, con datos sin placa ni motivo.
    [Theory]
    [InlineData("SOLICITADO", "tomar", "SANEAMIENTO_TOMADO", """{"cantidad":1}""")]
    [InlineData("EN_GESTION", "cerrar_saneado", "SANEAMIENTO_CERRADO", """{"cantidad":1,"resultado":"SANEADO"}""")]
    [InlineData("EN_GESTION", "cerrar_no_procede", "SANEAMIENTO_CERRADO", """{"cantidad":1,"resultado":"NO_PROCEDE"}""")]
    [InlineData("SANEADO", "reabrir", "SANEAMIENTO_REABIERTO", """{"cantidad":1}""")]
    [InlineData("EN_GESTION", "anular", "SANEAMIENTO_ANULADO", """{"cantidad":1}""")]
    public async Task HU14576_AC48_aviso_al_solicitante_con_datos_sin_texto_libre(string desde, string accion, string tipo, string datos)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var solicitante = Guid.NewGuid();
        var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBN101", GestionSaneamientoTests.Estado(desde), ct, solicitante);
        if (accion == "cerrar_saneado")
            await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Saneado, ct);

        var respuesta = await GestionSaneamientoTests.Accion(api.Cliente(GestionDePrueba.TokenFlit(api)), empresa, s.Id, accion, await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        var avisos = await AvisosAsync(empresa, ct);
        var aviso = avisos.Should().ContainSingle().Subject;
        aviso.Tipo.Should().Be(tipo);
        aviso.UsuarioId.Should().Be(solicitante);
        aviso.TenantId.Should().Be(empresa.Id);
        aviso.DiagnosticoId.Should().Be(lote.Id);
        JsonElement.DeepEquals(JsonElement.Parse(aviso.Datos), JsonElement.Parse(datos)).Should().BeTrue(aviso.Datos);
        aviso.Datos.Should().NotContain("SBN101");
        var evento = (await GestionDePrueba.EventosAsync(empresa, s.Id, ct)).Last();
        aviso.ReferenciaId.Should().Be(evento.Id, "C18/SP-22: la referencia es el evento de la acción");
    }

    // AC49 — una toma masiva emite un aviso por par (solicitante, diagnóstico) con su cantidad y la referencia del menor evento.
    [Fact]
    public async Task HU14576_AC49_agrupacion_por_solicitante_y_diagnostico()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        Guid[] solicitantes = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var (lote1, primeros) = await GestionDePrueba.VariosAsync(empresa, ["SBN111", "SBN112", "SBN113", "SBN114"], [solicitantes[0], solicitantes[0], solicitantes[1], solicitantes[2]], ct);
        var (lote2, segundos) = await GestionDePrueba.VariosAsync(empresa, ["SBN115", "SBN116"], [solicitantes[0], solicitantes[1]], ct);
        var items = primeros.Concat(segundos).Select(s => new { empresa = empresa.Id, id = s.Id }).ToArray();

        var respuesta = await GestionDePrueba.PostAsync(api.Cliente(GestionDePrueba.TokenFlit(api)), GestionDePrueba.Base + "/tomar", new { items }, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
        (await GestionDePrueba.JsonAsync(respuesta, ct)).GetProperty("tomados").GetInt32().Should().Be(6);
        var avisos = await AvisosAsync(empresa, ct);
        avisos.Should().OnlyContain(a => a.Tipo == Notificacion.SaneamientoTomado);
        avisos.Select(a => (a.UsuarioId, a.DiagnosticoId, Cantidad(a))).Should().BeEquivalentTo(new[]
        {
            (solicitantes[0], lote1.Id, 2), (solicitantes[1], lote1.Id, 1), (solicitantes[2], lote1.Id, 1),
            (solicitantes[0], lote2.Id, 1), (solicitantes[1], lote2.Id, 1),
        });
        await using var db = empresa.Db();
        var eventos = await db.Set<Flit.Diagnostico.Api.Modulos.Saneamiento.EventoSaneamiento>().AsNoTracking()
            .Where(e => e.Evento == TipoEventoSaneamiento.Tomado).ToListAsync(ct);
        var grupoDoble = primeros.Take(2).Select(s => s.Id).ToHashSet();
        var menor = eventos.Where(e => grupoDoble.Contains(e.SaneamientoId)).Select(e => e.Id).Min();
        avisos.Single(a => a.UsuarioId == solicitantes[0] && a.DiagnosticoId == lote1.Id).ReferenciaId.Should().Be(menor);
    }

    // SP-22 — cerrar, reabrir y cerrar dejan tres avisos; dos tomas masivas del mismo lote en dos acciones dejan dos; nunca 500.
    [Fact]
    public async Task HU14576_SP22_referencia_unica_por_accion_sin_choques()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var solicitante = Guid.NewGuid();
        var (_, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBN121", EstadoSaneamiento.EnGestion, ct, solicitante);
        await GestionDePrueba.RenglonAsync(empresa, s.Id, FuenteSaneamiento.Impuesto, ResultadoRenglonSaneamiento.Saneado, ct);
        var (_, masivos) = await GestionDePrueba.VariosAsync(empresa, ["SBN122", "SBN123"], [solicitante], ct);
        using var cliente = api.Cliente(GestionDePrueba.TokenFlit(api));

        foreach (var accion in new[] { "cerrar_saneado", "reabrir", "cerrar_saneado" })
        {
            var r = await GestionSaneamientoTests.Accion(cliente, empresa, s.Id, accion, await GestionDePrueba.VersionAsync(empresa, s.Id, ct), ct);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"{accion}: {await r.Content.ReadAsStringAsync(ct)}");
        }

        foreach (var uno in masivos)
        {
            var r = await GestionDePrueba.PostAsync(cliente, GestionDePrueba.Base + "/tomar", new { items = new[] { new { empresa = empresa.Id, id = uno.Id } } }, ct);
            r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        }

        var avisos = await AvisosAsync(empresa, ct);
        avisos.Count(a => a.Tipo == Notificacion.SaneamientoCerrado).Should().Be(2);
        avisos.Count(a => a.Tipo == Notificacion.SaneamientoReabierto).Should().Be(1);
        avisos.Count(a => a.Tipo == Notificacion.SaneamientoTomado).Should().Be(2, "dos tomas del mismo lote y solicitante en dos acciones");
        avisos.Select(a => (a.Tipo, a.ReferenciaId)).Should().OnlyHaveUniqueItems();
    }

    // AC50 — la desmarcación de la compañía no avisa y ningún aviso va a un operador FLIT.
    [Fact]
    public async Task HU14576_AC50_sin_otros_canales_ni_aviso_al_desmarcar()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var usuario = Guid.NewGuid();
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SBN131", "SBN132"], ct);
        using var compania = api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id, usuario: usuario));
        (await compania.PutAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await compania.DeleteAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await compania.PutAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[1].Id), null, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var flit = Guid.NewGuid();
        await using (var db = empresa.Db())
        {
            var sid = await db.Set<Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento>().AsNoTracking()
                .Where(x => x.VehiculoId == lote.Vehiculos[1].Id && x.Estado == EstadoSaneamiento.Solicitado).Select(x => x.Id).SingleAsync(ct);
            (await api.Cliente(GestionDePrueba.TokenFlit(api, usuario: flit)).PostAsync(GestionDePrueba.Ruta(sid, empresa.Id, "/tomar"), null, ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var avisos = await AvisosAsync(empresa, ct);
        avisos.Should().ContainSingle().Which.Should().Match<Notificacion>(a => a.Tipo == Notificacion.SaneamientoTomado && a.UsuarioId == usuario);
        avisos.Should().NotContain(a => a.UsuarioId == flit);
    }

    private static int Cantidad(Notificacion aviso) => JsonElement.Parse(aviso.Datos).GetProperty("cantidad").GetInt32();

    private static async Task<List<Notificacion>> AvisosAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<Notificacion>().AsNoTracking().Where(n => n.Tipo.StartsWith("SANEAMIENTO_")).ToListAsync(ct);
    }
}
