using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Ingesta;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Fuente de prueba: cada clave responde lo que la prueba le diga y cuenta las llamadas.</summary>
public sealed class FuenteDePrueba : IFuenteComparendos
{
    public ConcurrentDictionary<string, Func<RespuestaFuente>> Respuestas { get; } = new();
    public ConcurrentDictionary<string, int> Llamadas { get; } = new();

    public bool Atiende(CanalIngesta canal) => true;

    public Task<RespuestaFuente> ConsultarAsync(ClaveConsulta clave, CancellationToken ct)
    {
        Llamadas.AddOrUpdate($"{clave.Canal}:{clave.Clave}", 1, (_, n) => n + 1);
        return Task.FromResult(Respuestas.TryGetValue($"{clave.Canal}:{clave.Clave}", out var r) ? r() : RespuestaFuente.Exitosa([]));
    }
}

public class ApiConFuente : ApiDePrueba
{
    public FuenteDePrueba Fuente { get; } = new();

    protected override void Reemplazar(IServiceCollection services) => services.AddSingleton<IFuenteComparendos>(Fuente);

    public async Task<Guid> CicloAsync(CanalIngesta canal, Guid? empresa = null)
    {
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CicloDeIngesta>().EjecutarAsync(canal, empresa is null ? Disparo.Programado : Disparo.Manual, empresa, null, CancellationToken.None);
    }
}

/// <summary>HU #13460 (trabajos y ciclos) y HU #13461 (reconciliación).</summary>
public sealed class IngestaTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static ItemFuente Item(string numero, decimal valor = 604100m, DateOnly? notificacion = null, string estado = "PENDIENTE") => new()
    {
        Numero = numero,
        Placa = "ABC123",
        CodigoInfraccion = "C29",
        FechaImposicion = new DateOnly(2026, 9, 25),
        FechaNotificacion = notificacion ?? new DateOnly(2026, 10, 1),
        EsFotomulta = true,
        Secretaria = "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA",
        ValorTotal = valor,
        EstadoFuente = estado,
    };

    private async Task<Guid> EmpresaConNit(string nit)
    {
        var empresa = Guid.NewGuid();
        var r = await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar]))
            .PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = nit }, TestContext.Current.CancellationToken);
        r.EnsureSuccessStatusCode();
        return empresa;
    }

    private Task<Comparendo?> Comparendo(Guid empresa, string numero) =>
        api.EnDbAsync(db => db.Comparendos.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == empresa && c.NumeroNormalizado == numero));

    [Fact]
    public async Task HU13460_ClaveCompartida_SeConsultaUnaVezPorVariante_YLlegaAAmbas()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var a = await EmpresaConNit("800197268");
        var b = await EmpresaConNit("800197268-4");
        api.Fuente.Respuestas["SimitNit:800197268"] = () => RespuestaFuente.Exitosa([Item("11001000000000001")]);

        await api.CicloAsync(CanalIngesta.SimitNit);

        api.Fuente.Llamadas["SimitNit:800197268"].Should().Be(1);
        api.Fuente.Llamadas["SimitNit:8001972684"].Should().Be(1);
        (await Comparendo(a, "11001000000000001")).Should().NotBeNull();
        (await Comparendo(b, "11001000000000001")).Should().NotBeNull();
    }

    [Fact]
    public async Task HU13461_AC1_Nuevo_SeCreaConPlazosOtYTimeline()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900111222");
        api.Fuente.Respuestas["SimitNit:900111222"] = () => RespuestaFuente.Exitosa([Item("C-0001")]);

        var ciclo = await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        var registro = await api.EnDbAsync(db => db.CiclosIngesta.SingleAsync(x => x.Id == ciclo));
        registro.MensajeError.Should().BeNull();

        var c = await Comparendo(empresa, "C0001");
        c.Should().NotBeNull();
        c!.EstadoGestion.Should().Be(EstadoGestion.SinGestion);
        c.FechaLimiteGestion.Should().Be(new DateOnly(2026, 10, 19));
        c.OrganismoTransitoId.Should().Be(Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001"));
        var eventos = await api.EnDbAsync(db => db.Timeline.Where(t => t.ComparendoId == c.Id).Select(t => t.Tipo).ToListAsync());
        eventos.Should().Contain([TipoEventoTimeline.Ingreso, TipoEventoTimeline.PlazosCalculados]);
        var log = await api.EnDbAsync(db => db.LogMigracion.SingleAsync(l => l.CicloId == ciclo && l.TenantId == empresa));
        log.CantidadNuevas.Should().Be(1);
        log.Resultado.Should().Be(ResultadoCiclo.Exitoso);
    }

    [Fact]
    public async Task HU13461_AC2_CambioDeCampos_SeSobrescribeConHistorialYRecalculaPlazos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900222333");
        api.Fuente.Respuestas["SimitNit:900222333"] = () => RespuestaFuente.Exitosa([Item("C-0002")]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);

        api.Fuente.Respuestas["SimitNit:900222333"] = () => RespuestaFuente.Exitosa([Item("C-0002", 700000m, new DateOnly(2026, 10, 5), "COBRO COACTIVO")]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);

        var c = (await Comparendo(empresa, "C0002"))!;
        c.ValorTotal.Should().Be(700000m);
        c.FechaNotificacion.Should().Be(new DateOnly(2026, 10, 5));
        c.FechaLimiteGestion.Should().Be(new DateOnly(2026, 10, 21));
        c.EnCobroCoactivo.Should().BeTrue();
        var campos = await api.EnDbAsync(db => db.ComparendoCambios.Where(x => x.ComparendoId == c.Id).Select(x => x.Campo).ToListAsync());
        campos.Should().Contain(["valor_total", "fecha_notificacion", "estado_fuente"]);
        var eventos = await api.EnDbAsync(db => db.Timeline.Where(t => t.ComparendoId == c.Id).Select(t => t.Tipo).ToListAsync());
        eventos.Should().Contain([TipoEventoTimeline.DatosActualizados, TipoEventoTimeline.CobroCoactivoDetectado]);
        eventos.Count(e => e == TipoEventoTimeline.PlazosCalculados).Should().Be(2);
    }

    [Fact]
    public async Task HU13461_AC3_ErrorDeFuente_NoTocaNada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900333444");
        api.Fuente.Respuestas["SimitNit:900333444"] = () => RespuestaFuente.Exitosa([Item("C-0003")]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);

        api.Fuente.Respuestas["SimitNit:900333444"] = () => RespuestaFuente.Error(TipoErrorFuente.HttpNoExitoso, "500", 500);
        var ciclo = await api.CicloAsync(CanalIngesta.SimitNit, empresa);

        (await Comparendo(empresa, "C0003"))!.Activo.Should().BeTrue();
        var log = await api.EnDbAsync(db => db.LogMigracion.SingleAsync(l => l.CicloId == ciclo && l.TenantId == empresa));
        log.Resultado.Should().Be(ResultadoCiclo.Parcial, "una de las dos variantes falló: no se reconcilia");
    }

    [Fact]
    public async Task HU13461_AC4_DesapareceConDpEnviado_InactivoYDpFavorable()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900444555");
        api.Fuente.Respuestas["SimitNit:900444555"] = () => RespuestaFuente.Exitosa([Item("C-0004")]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        var c = (await Comparendo(empresa, "C0004"))!;
        await api.EnDbAsync(async db =>
        {
            db.DerechosPeticion.Add(new DerechoPeticion
            {
                Id = Guid.NewGuid(), TenantId = empresa, ComparendoId = c.Id, TipologiaId = Guid.NewGuid(), PlantillaVersionId = Guid.NewGuid(),
                Radicado = $"DP-2026-{Random.Shared.Next(999999):D6}", OrganismoTransitoId = Guid.NewGuid(), DocumentoArchivoId = Guid.NewGuid(),
                EstadoLegalAlRadicar = "EnTiempo", Estado = EstadoDp.Enviado, EnviadoPor = Guid.NewGuid(), CreadoEn = DateTimeOffset.UtcNow,
            });
            await db.Comparendos.Where(x => x.Id == c.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.EstadoGestion, EstadoGestion.DpEnviado));
            await db.SaveChangesAsync();
        });

        api.Fuente.Respuestas["SimitNit:900444555"] = () => RespuestaFuente.Exitosa([]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);

        c = (await Comparendo(empresa, "C0004"))!;
        c.Activo.Should().BeFalse();
        c.MotivoInactivacion.Should().Be(MotivoInactivacion.DesaparecidoFuente);
        c.EstadoGestion.Should().Be(EstadoGestion.RespuestaOt);
        var dp = await api.EnDbAsync(db => db.DerechosPeticion.SingleAsync(d => d.ComparendoId == c.Id));
        dp.RespuestaSentido.Should().Be(SentidoRespuestaOt.Favorable);
        dp.RespuestaOrigen.Should().Be(OrigenRespuestaOt.AutomaticaDesaparicion);
    }

    [Fact]
    public async Task HU13461_Reaparece_SeReactivaSalvoInactivadoPorOperador()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900555666");
        api.Fuente.Respuestas["SimitNit:900555666"] = () => RespuestaFuente.Exitosa([Item("C-0005"), Item("C-0006")]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        api.Fuente.Respuestas["SimitNit:900555666"] = () => RespuestaFuente.Exitosa([]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        var operador = (await Comparendo(empresa, "C0006"))!;
        await api.EnDbAsync(db => db.Comparendos.Where(x => x.Id == operador.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.MotivoInactivacion, MotivoInactivacion.InactivadoOperador)));

        api.Fuente.Respuestas["SimitNit:900555666"] = () => RespuestaFuente.Exitosa([Item("C-0005"), Item("C-0006")]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);

        (await Comparendo(empresa, "C0005"))!.Activo.Should().BeTrue();
        (await Comparendo(empresa, "C0006"))!.Activo.Should().BeFalse();
        (await api.EnDbAsync(db => db.Timeline.AnyAsync(t => t.ComparendoId == operador.Id && t.Tipo == TipoEventoTimeline.ReaparecioInactivoOperador))).Should().BeTrue();
    }

    [Fact]
    public async Task HU13461_OtroCanal_EsFuenteAdicionalYNoPisaLaPrincipal()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900666777");
        await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar]))
            .PostAsJsonAsync("/api/v1/comparendos/monitoreo/placas", new { placas = new[] { "ABC123" } }, TestContext.Current.CancellationToken);
        api.Fuente.Respuestas["SimitNit:900666777"] = () => RespuestaFuente.Exitosa([Item("C-0007")]);
        api.Fuente.Respuestas["SimitPlaca:ABC123"] = () => RespuestaFuente.Exitosa([Item("C-0007", 999999m)]);

        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        await api.CicloAsync(CanalIngesta.SimitPlaca, empresa);

        var c = (await Comparendo(empresa, "C0007"))!;
        c.FuentePrincipal.Should().Be(CanalIngesta.SimitNit);
        c.ValorTotal.Should().Be(604100m);
        (await api.EnDbAsync(db => db.ComparendoFuentes.CountAsync(f => f.ComparendoId == c.Id))).Should().Be(2);
    }

    [Fact]
    public async Task HU13460_Trabajos_CronInvalidoSeRechazaYManualQuedaAuditado()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

        (await super.PatchAsJsonAsync("/api/v1/comparendos/admin/ingesta/trabajos/IngestaSimitNit", new { expresionCron = "99 * * * *" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var ok = await super.PatchAsJsonAsync("/api/v1/comparendos/admin/ingesta/trabajos/IngestaSimitNit", new { expresionCron = "0 6,12 * * 1-5" }, ct);
        (await ok.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("expresionCron").GetString().Should().Be("0 6,12 * * 1-5");

        var empresa = Guid.NewGuid();
        (await super.PostAsJsonAsync("/api/v1/comparendos/admin/ingesta/ejecutar", new { canal = "MunicipalPlaca", empresaId = empresa }, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await api.EnDbAsync(db => db.AuditoriasConfiguracion.AnyAsync(a => a.Accion == "EJECUTAR_MANUAL" && a.TenantId == empresa, ct))).Should().BeTrue();
    }
}

/// <summary>HU #13460 — el programador encola cada trabajo una sola vez por minuto aunque revisen dos réplicas.</summary>
public sealed class ProgramadorTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    [Fact]
    public async Task DosRevisionesDelMismoMinuto_EncolanUnaSolaVez()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await api.EnDbAsync(db => db.TrabajosProgramados.Where(t => t.Tipo == TipoTrabajo.IngestaSimitNit)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.ExpresionCron, "* * * * *").SetProperty(t => t.UltimaEjecucionEn, (DateTimeOffset?)null)));
        await api.EnDbAsync(db => db.TrabajosProgramados.Where(t => t.Tipo != TipoTrabajo.IngestaSimitNit)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Activo, false)));

        var cola = api.App.Services.GetRequiredService<Flit.Comparendos.Api.Modulos.Trabajos.ColaTrabajos>();
        var replica1 = ActivatorUtilities.CreateInstance<Flit.Comparendos.Api.Modulos.Trabajos.ProgramadorTrabajos>(api.App.Services);
        var replica2 = ActivatorUtilities.CreateInstance<Flit.Comparendos.Api.Modulos.Trabajos.ProgramadorTrabajos>(api.App.Services);
        await replica1.RevisarAsync(TestContext.Current.CancellationToken);
        await replica2.RevisarAsync(TestContext.Current.CancellationToken);

        using var corte = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var leidas = new List<Flit.Comparendos.Api.Modulos.Trabajos.SolicitudTrabajo>();
        try
        {
            await foreach (var s in cola.LeerAsync(corte.Token))
                leidas.Add(s);
        }
        catch (OperationCanceledException)
        {
        }

        leidas.Should().ContainSingle().Which.Tipo.Should().Be(TipoTrabajo.IngestaSimitNit);
    }
}
