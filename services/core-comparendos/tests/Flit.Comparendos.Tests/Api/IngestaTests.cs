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

    public ConcurrentDictionary<string, string> ProveedorUsado { get; } = new();

    /// <summary>Por qué empresa se pidió cada clave (la metadata x-flit-tenant-id hacia core-consultas).</summary>
    public ConcurrentDictionary<string, Guid> EmpresaUsada { get; } = new();

    public Task<IReadOnlyList<ProveedorFuente>> ProveedoresAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProveedorFuente>>([
            new ProveedorFuente("prueba", "Fuente de prueba", [CanalIngesta.SimitNit, CanalIngesta.SimitPlaca, CanalIngesta.MunicipalNit, CanalIngesta.MunicipalPlaca], true, null),
            new ProveedorFuente("otra", "Otra fuente de prueba", [CanalIngesta.SimitNit], true, null),
            new ProveedorFuente("sin_contrato", "Sin contrato", [CanalIngesta.SimitNit], false, "Falta el contrato."),
        ]);

    public Task<RespuestaFuente> ConsultarAsync(ClaveConsulta clave, string proveedor, Guid empresa, CancellationToken ct)
    {
        Llamadas.AddOrUpdate($"{clave.Canal}:{clave.Clave}", 1, (_, n) => n + 1);
        ProveedorUsado[$"{clave.Canal}:{clave.Clave}"] = proveedor;
        EmpresaUsada[$"{clave.Canal}:{clave.Clave}"] = empresa;
        return Task.FromResult(Respuestas.TryGetValue($"{clave.Canal}:{clave.Clave}", out var r) ? r() : RespuestaFuente.Exitosa([]));
    }
}

public class ApiConFuente : ApiDePrueba
{
    public FuenteDePrueba Fuente { get; } = new();

    /// <summary>La API de contratos simulada, salvo las placas con respuesta propia (HU #13712: Renting sin documento).</summary>
    public ConcurrentDictionary<string, Flit.Comparendos.Api.Modulos.Contraventor.RespuestaContrato> Contratos { get; } = new();

    /// <summary>Lo que responde «Probar conexión» (HU #13843).</summary>
    public Flit.Comparendos.Api.Modulos.Contraventor.PruebaContratos Prueba { get; set; } = new(true, null, null, 120, true, "buzon.pruebas@flitsas.com", false);

    protected override void Reemplazar(IServiceCollection services)
    {
        services.AddSingleton<IFuenteComparendos>(Fuente);
        services.AddScoped<Flit.Comparendos.Api.Modulos.Contraventor.IApiContratos>(_ => new ContratosDePrueba(Contratos, () => Prueba));
    }

    private sealed class ContratosDePrueba(ConcurrentDictionary<string, Flit.Comparendos.Api.Modulos.Contraventor.RespuestaContrato> propias,
        Func<Flit.Comparendos.Api.Modulos.Contraventor.PruebaContratos> prueba)
        : Flit.Comparendos.Api.Modulos.Contraventor.IApiContratos
    {
        public Task<Flit.Comparendos.Api.Modulos.Contraventor.PruebaContratos> ProbarAsync(CancellationToken ct) => Task.FromResult(prueba());

        public Task<Flit.Comparendos.Api.Modulos.Contraventor.RespuestaContrato> ConsultarAsync(Guid tenantId, string placa, DateOnly fecha, CancellationToken ct) =>
            propias.TryGetValue(placa, out var r) ? Task.FromResult(r) : new Flit.Comparendos.Api.Modulos.Contraventor.ApiContratosSimulada().ConsultarAsync(tenantId, placa, fecha, ct);
    }

    public async Task<Guid> CicloAsync(CanalIngesta canal, Guid? empresa = null)
    {
        await ConfigurarConsultasAsync();
        await using var scope = App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CicloDeIngesta>().EjecutarAsync(canal, empresa is null ? Disparo.Programado : Disparo.Manual, empresa, null, CancellationToken.None);
    }

    /// <summary>
    /// HU #13521: toda empresa que monitorea algo y no tiene configuración de consultas queda con los cuatro canales
    /// encendidos con la fuente de prueba y activa (las pruebas de ingesta no son sobre el configurador).
    /// </summary>
    public Task ConfigurarConsultasAsync() => EnDbAsync(async db =>
    {
        var conConfig = await db.ConfiguracionesConsultas.Select(c => c.TenantId).ToListAsync();
        var tenants = (await db.IdentificacionesMonitoreadas.Select(n => n.TenantId).Distinct().ToListAsync())
            .Concat(await db.PlacasMonitoreadas.Select(p => p.TenantId).Distinct().ToListAsync()).Distinct().Except(conConfig).ToList();
        foreach (var t in tenants)
        {
            db.ConfiguracionesConsultas.Add(new ConfiguracionConsultas { TenantId = t, Estado = EstadoConfiguracionConsultas.Activa, CreadoEn = DateTimeOffset.UtcNow });
            foreach (var c in new[] { CanalIngesta.SimitNit, CanalIngesta.SimitPlaca, CanalIngesta.MunicipalNit, CanalIngesta.MunicipalPlaca })
            {
                db.ConfiguracionConsultaCanales.Add(new ConfiguracionConsultaCanal
                {
                    Id = Guid.NewGuid(), TenantId = t, Canal = c, Encendido = true, Proveedor = "prueba", Horas = ["07:00"], CreadoEn = DateTimeOffset.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync();
    });
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
        // El ciclo corre en segundo plano: la empresa va explícita, una fija para la clave compartida.
        api.Fuente.EmpresaUsada["SimitNit:800197268"].Should().Be(a.CompareTo(b) < 0 ? a : b);
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
    public async Task HU13461_GuardaLaIdentificacionConsultada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900444666");
        api.Fuente.Respuestas["SimitNit:900444666"] = () => RespuestaFuente.Exitosa([Item("C-0044")]);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        (await Comparendo(empresa, "C0044"))!.NitConsultado.Should().Be("900444666", "la bandeja y el export muestran qué identificación lo trajo");
    }

    [Fact]
    public async Task HU13462_LaRespuestaSoloSeGuardaSiLaConsultaFalla_EnLaBaseYRecortada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900444888");
        api.Fuente.Respuestas["SimitNit:900444888"] = () => RespuestaFuente.Exitosa([Item("C-0888")], cuerpo: "<xml>respuesta completa</xml>");
        var exitosa = await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        api.Fuente.Respuestas["SimitNit:900444888"] = () => RespuestaFuente.Error(TipoErrorFuente.HttpNoExitoso, "La fuente respondió 502.", 502, cuerpo: new string('x', 5000));
        var fallida = await api.CicloAsync(CanalIngesta.SimitNit, empresa);

        var consultas = await api.EnDbAsync(db => db.ConsultasFuente.Where(c => c.ClaveConsultada == "900444888").ToDictionaryAsync(c => c.CicloId, c => c.RespuestaError));
        consultas[exitosa].Should().BeNull("de una consulta exitosa no se guarda la respuesta: los comparendos ya quedan con sus datos");
        consultas[fallida].Should().HaveLength(ConsultaFuente.MaxRespuestaError, "de la fallida queda el comienzo, para diagnosticar");
        (await api.EnDbAsync(db => db.Archivos.AnyAsync(a => a.Dominio == "respuesta_fuente"))).Should().BeFalse("las respuestas de las fuentes no van al file-manager");
    }

    [Fact]
    public async Task HU13467_MunicipioDesactivado_NoSeProcesa()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900444777");
        await api.EnDbAsync(async db =>
        {
            db.MunicipiosFuente.AddRange(
                new MunicipioFuente { Id = Guid.NewGuid(), CodigoFuente = "PRB-ACT", Nombre = "Municipio activo de prueba", Activo = true },
                new MunicipioFuente { Id = Guid.NewGuid(), CodigoFuente = "PRB-INA", Nombre = "Municipio desactivado de prueba", Activo = false });
            await db.SaveChangesAsync();
        });
        api.Fuente.Respuestas["MunicipalNit:900444777"] = () => new RespuestaFuente(
        [
            new ResultadoParcial("PRB-ACT", ResultadoConsulta.ExitosaConDatos, [Item("M-0001")]),
            new ResultadoParcial("PRB-INA", ResultadoConsulta.ExitosaConDatos, [Item("M-0002")]),
        ], 200, 10, null);

        await api.CicloAsync(CanalIngesta.MunicipalNit, empresa);

        (await Comparendo(empresa, "M0001")).Should().NotBeNull();
        (await Comparendo(empresa, "M0002")).Should().BeNull("el SuperAdmin desactivó ese municipio en el parametrizador");
    }

    [Fact]
    public async Task HU13467_UnMunicipioQueFallaEnTodasLasClaves_ElCicloEsParcialYEntraLoDemas()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await EmpresaConNit("900444666");
        await api.EnDbAsync(async db =>
        {
            db.MunicipiosFuente.AddRange(
                new MunicipioFuente { Id = Guid.NewGuid(), CodigoFuente = "PRB-OK", Nombre = "Municipio que responde", Activo = true },
                new MunicipioFuente { Id = Guid.NewGuid(), CodigoFuente = "PRB-SSL", Nombre = "Municipio con el certificado vencido", Activo = true });
            await db.SaveChangesAsync();
        });

        // Como Cali en la API municipal real: falla en cada clave, y el otro municipio sí trae sus comparendos.
        foreach (var variante in Flit.Comparendos.Domain.Identificadores.Nit.Variantes("900444666"))
        {
            api.Fuente.Respuestas[$"MunicipalNit:{variante}"] = () => new RespuestaFuente(
            [
                new ResultadoParcial("PRB-OK", ResultadoConsulta.ExitosaConDatos, [Item("M-0101")]),
                new ResultadoParcial("PRB-SSL", ResultadoConsulta.Error, [], TipoErrorFuente.Red, "The SSL connection could not be established"),
            ], 200, 10, null);
        }

        var ciclo = await api.CicloAsync(CanalIngesta.MunicipalNit, empresa);

        (await Comparendo(empresa, "M0101")).Should().NotBeNull("lo que respondió el otro municipio entra");
        (await api.EnDbAsync(db => db.CiclosIngesta.SingleAsync(c => c.Id == ciclo))).Resultado
            .Should().Be(ResultadoCiclo.Parcial, "todas las claves tienen un municipio con error, pero entró información: no es «Fallido»");
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

        (await super.PatchAsJsonAsync("/api/v1/comparendos/admin/ingesta/trabajos/ReporteSemanal", new { expresionCron = "99 * * * *" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var ok = await super.PatchAsJsonAsync("/api/v1/comparendos/admin/ingesta/trabajos/ReporteSemanal", new { expresionCron = "0 6,12 * * 1-5" }, ct);
        (await ok.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("expresionCron").GetString().Should().Be("0 6,12 * * 1-5");

        // HU #13521: sin el canal configurado no hay ejecución manual; con él (aun en borrador) sí.
        var empresa = await ComparendosDePrueba.EmpresaAsync(api);
        await api.EnDbAsync(db => db.ConfiguracionConsultaCanales.Where(c => c.TenantId == empresa && c.Canal == CanalIngesta.MunicipalPlaca).ExecuteDeleteAsync(ct));
        var sin = await super.PostAsJsonAsync("/api/v1/comparendos/admin/ingesta/ejecutar", new { canal = "MunicipalPlaca", empresaId = empresa }, ct);
        sin.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await sin.Content.ReadAsStringAsync(ct)).Should().Contain("CONSULTA_NO_HABILITADA");
        await ComparendosDePrueba.ConsultasActivasAsync(api, empresa);
        (await super.PostAsJsonAsync("/api/v1/comparendos/admin/ingesta/ejecutar", new { canal = "MunicipalPlaca", empresaId = empresa }, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await api.EnDbAsync(db => db.AuditoriasConfiguracion.AnyAsync(a => a.Accion == "EJECUTAR_MANUAL" && a.TenantId == empresa, ct))).Should().BeTrue();
    }

    [Fact]
    public async Task HU13815_EjecutarAMano_SoloSuperAdmin_YQuedaComoUltimaEjecucion()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid(); // sin configuración: el reintento corre y no encuentra nada que hacer
        await api.EnDbAsync(db => db.TrabajosProgramados.Where(t => t.Tipo == TipoTrabajo.ReintentoContraventor)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.UltimaEjecucionEn, (DateTimeOffset?)null), ct));
        var ruta = $"/api/v1/comparendos/admin/ingesta/trabajos/ReintentoContraventor/ejecutar?empresaId={empresa}";

        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar])).PostAsync(ruta, null, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.EnDbAsync(db => db.AuditoriasConfiguracion.AnyAsync(a => a.Accion == "EJECUTAR_MANUAL" && a.TenantId == empresa, ct))).Should().BeFalse();

        (await api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true)).PostAsync(ruta, null, ct)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        // En las pruebas los trabajos en segundo plano están apagados: se toma la solicitud de la cola y se procesa aquí.
        var cola = api.App.Services.GetRequiredService<Flit.Comparendos.Api.Modulos.Trabajos.ColaTrabajos>();
        Flit.Comparendos.Api.Modulos.Trabajos.SolicitudTrabajo? solicitud = null;
        await foreach (var s in cola.LeerAsync(ct))
        {
            if (s.Tipo == TipoTrabajo.ReintentoContraventor && s.TenantId == empresa)
            {
                solicitud = s;
                break;
            }
        }

        solicitud!.Disparo.Should().Be(Disparo.Manual);
        var procesador = ActivatorUtilities.CreateInstance<Flit.Comparendos.Api.Modulos.Trabajos.ProcesadorTrabajos>(api.App.Services);
        await procesador.ProcesarAsync(solicitud, ct);
        var ultima = await api.EnDbAsync(db => db.TrabajosProgramados.Where(t => t.Tipo == TipoTrabajo.ReintentoContraventor).Select(t => t.UltimaEjecucionEn).SingleAsync(ct));
        ultima.Should().NotBeNull("al terminar, la ejecución manual se ve en la tabla como la última");
    }
}

/// <summary>HU #13460 — el programador encola cada trabajo una sola vez por minuto aunque revisen dos réplicas.</summary>
public sealed class ProgramadorTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    /// <summary>HU #13521: la agenda de cada empresa; solo la ACTIVA con el canal encendido, una vez por minuto.</summary>
    [Fact]
    public async Task DosRevisionesDelMismoMinuto_EncolanUnaSolaVez()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Reloj.Hoy(new DateOnly(2026, 10, 13));
        var hora = Flit.Comparendos.Domain.Tiempo.ColombiaTime.From(api.Reloj.Ahora).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var (activa, borrador, apagada) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await api.EnDbAsync(async db =>
        {
            await db.TrabajosProgramados.ExecuteUpdateAsync(u => u.SetProperty(t => t.Activo, false));
            foreach (var (t, estado, encendido) in new[] { (activa, EstadoConfiguracionConsultas.Activa, true), (borrador, EstadoConfiguracionConsultas.Borrador, true), (apagada, EstadoConfiguracionConsultas.Activa, false) })
            {
                db.ConfiguracionesConsultas.Add(new Flit.Comparendos.Api.Persistence.Modelo.ConfiguracionConsultas { TenantId = t, Estado = estado, CreadoEn = api.Reloj.Ahora });
                db.ConfiguracionConsultaCanales.Add(new Flit.Comparendos.Api.Persistence.Modelo.ConfiguracionConsultaCanal
                {
                    Id = Guid.NewGuid(), TenantId = t, Canal = CanalIngesta.SimitNit, Encendido = encendido, Proveedor = "simulado", Horas = [hora, "23:59"], CreadoEn = api.Reloj.Ahora,
                });
            }

            await db.SaveChangesAsync();
        });

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

        var unica = leidas.Should().ContainSingle().Subject;
        (unica.Tipo, unica.TenantId).Should().Be((TipoTrabajo.IngestaSimitNit, (Guid?)activa));
    }
}
