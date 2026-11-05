using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Dictamen;

/// <summary>
/// Cola de redacción (corte 6). El cierre, la tomabilidad y el cierre de una toma no abren Postgres. La toma con
/// <c>FOR UPDATE SKIP LOCKED</c> usa el mismo Postgres de <c>ConnectionStrings__Core</c> que <see cref="ColaEvidenciasTests"/>.
/// </summary>
public sealed class ColaRedaccionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 3, 30, 0, TimeSpan.Zero);

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static int _placa = 800;

    private const string Alertas = """[{"codigo":"ESTADO_VEHICULO","texto":"plantilla del cierre","efecto":"ADVIERTE","fuente":"RUNT"},{"codigo":"IMPUESTO","texto":"plantilla del impuesto","efecto":"ADVIERTE","fuente":"IMPUESTO_DEPARTAMENTAL"}]""";

    [Fact]
    public void El_cierre_con_proveedor_deja_la_plantilla_tomable_y_no_llama_al_modelo()
    {
        var tipo = "TRASPASO_STANDARD";
        var evaluado = new DictamenEvaluado("BLOQUEADO",
        [
            new ReglaEvaluada("SOAT_NO_VIGENTE", "RUNT", "BLOQUEA", null),
            new ReglaEvaluada("IMPUESTO", "IMPUESTO_DEPARTAMENTAL", "ADVIERTE", 150_000),
        ]);

        var fila = EmisorDictamen.FilaAlCerrar(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, tipo, evaluado, redaccionSuspendida: false, proveedorConfigurado: true, Ahora);

        fila.EstadoRedaccion.Should().Be(ColaRedaccion.Pendiente);
        fila.RedactadoPor.Should().Be("PLANTILLA");
        fila.RedaccionIntentos.Should().Be(0);
        fila.RedaccionLeaseHasta.Should().BeNull();
        fila.RedaccionDisponibleDesde.Should().Be(Ahora);
        fila.Veredicto.Should().Be("BLOQUEADO");
        Texto(fila.Alertamientos, 0).Should().Be(Plantilla(tipo, "SOAT_NO_VIGENTE"));
        Texto(fila.Alertamientos, 1).Should().Be(Plantilla(tipo, "IMPUESTO"));

        var ruta = Path.Combine(
            IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "src", "Flit.Diagnostico.Api", "Modulos", "Dictamen", "EmisorDictamen.cs");
        var fuente = File.ReadAllText(ruta);
        var inicio = fuente.IndexOf("Task EmitirAsync", StringComparison.Ordinal);
        var fin = fuente.IndexOf("private async Task<CabeceraDiagnostico>", inicio, StringComparison.Ordinal);
        fuente[inicio..fin].Should().NotContain("RedactarAsync");
        typeof(EmisorDictamen).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
            .Should().NotContain(typeof(ServicioRedactor));
    }

    [Fact]
    public void Suspension_o_proveedor_apagado_no_dejan_fecha_de_toma_en_el_cierre()
    {
        var evaluado = new DictamenEvaluado("VIABLE", []);
        EmisorDictamen.FilaAlCerrar(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "TRASPASO_STANDARD", evaluado, true, true, Ahora)
            .RedaccionDisponibleDesde.Should().BeNull();
        EmisorDictamen.FilaAlCerrar(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "TRASPASO_STANDARD", evaluado, false, false, Ahora)
            .RedaccionDisponibleDesde.Should().BeNull();
        EmisorDictamen.DisponibleAlCerrar(false, true, Ahora).Should().Be(Ahora);
    }

    [Fact]
    public void Solo_es_tomable_la_pendiente_vigente_con_fecha_cumplida_y_lease_libre()
    {
        ColaRedaccion.EsTomable(ColaRedaccion.Pendiente, true, null, null, Ahora).Should().BeFalse("sin fecha no entra");
        ColaRedaccion.EsTomable(ColaRedaccion.Pendiente, true, Ahora.AddSeconds(1), null, Ahora).Should().BeFalse("la fecha todavía no llega");
        ColaRedaccion.EsTomable(ColaRedaccion.Pendiente, true, Ahora, Ahora.AddSeconds(1), Ahora).Should().BeFalse("el lease sigue vigente");
        ColaRedaccion.EsTomable(ColaRedaccion.Pendiente, false, Ahora, null, Ahora).Should().BeFalse("no vigente");
        ColaRedaccion.EsTomable(ColaRedaccion.Completada, true, Ahora, null, Ahora).Should().BeFalse("ya completada");

        ColaRedaccion.EsTomable(ColaRedaccion.Pendiente, true, Ahora, null, Ahora).Should().BeTrue();
        ColaRedaccion.EsTomable(ColaRedaccion.Pendiente, true, Ahora.AddMinutes(-1), null, Ahora).Should().BeTrue();
        ColaRedaccion.EsTomable(ColaRedaccion.Pendiente, true, Ahora, Ahora, Ahora).Should().BeTrue("el lease vence en este instante");
        ColaRedaccion.EsTomable(ColaRedaccion.Pendiente, true, Ahora, Ahora.AddSeconds(-1), Ahora).Should().BeTrue();
    }

    [Fact]
    public void Un_fallo_tecnico_agenda_otra_fecha_y_pasado_el_tope_ya_no()
    {
        var fallo = new ResultadoRedaccion("Redacción IA pendiente.", "PLANTILLA", ColaRedaccion.Pendiente, 1, "claude", TipoFalloRedactor.Timeout, TimeSpan.FromSeconds(15));

        var reintento = RedactorEnCola.Decidir("BLOQUEADO", Alertas, intentos: 2, fallo, maxReintentos: 2, Ahora);
        reintento.EstadoRedaccion.Should().Be(ColaRedaccion.Pendiente);
        reintento.RedactadoPor.Should().Be("PLANTILLA");
        reintento.Alertamientos.Should().Be(Alertas);
        reintento.LeaseHasta.Should().BeNull();
        reintento.DisponibleDesde.Should().Be(Ahora.AddSeconds(15));
        reintento.InvalidarPdf.Should().BeFalse();

        var agotado = RedactorEnCola.Decidir("BLOQUEADO", Alertas, intentos: 3, fallo, maxReintentos: 2, Ahora);
        agotado.DisponibleDesde.Should().BeNull("el intento 3 pasa de MaxReintentos 2");
        agotado.LeaseHasta.Should().BeNull();
        agotado.EstadoRedaccion.Should().Be(ColaRedaccion.Pendiente);
        agotado.InvalidarPdf.Should().BeFalse();
    }

    [Fact]
    public void Suspension_al_tomar_contradiccion_o_proveedor_apagado_no_reintentan()
    {
        foreach (var resultado in new ResultadoRedaccion?[]
        {
            null,
            new("Redacción IA pendiente.", "PLANTILLA", ColaRedaccion.Pendiente, 2, "claude", null),
            new("Redacción IA pendiente.", "PLANTILLA", ColaRedaccion.Pendiente, 1, "no-configurado", TipoFalloRedactor.NoConfigurado),
            new("Redacción IA pendiente.", "PLANTILLA", ColaRedaccion.Pendiente, 1, "claude", TipoFalloRedactor.Ilegible),
        })
        {
            var cierre = RedactorEnCola.Decidir("VIABLE", Alertas, 1, resultado, 2, Ahora);
            cierre.DisponibleDesde.Should().BeNull();
            cierre.LeaseHasta.Should().BeNull();
            cierre.EstadoRedaccion.Should().Be(ColaRedaccion.Pendiente);
            cierre.RedactadoPor.Should().Be("PLANTILLA");
            cierre.Alertamientos.Should().Be(Alertas);
            cierre.InvalidarPdf.Should().BeFalse();
        }
    }

    [Fact]
    public void Completada_copia_el_texto_no_cambia_el_veredicto_e_invalida_solo_el_pdf()
    {
        const string veredicto = "BLOQUEADO";
        var cierre = RedactorEnCola.Decidir(
            veredicto, Alertas, 1, new ResultadoRedaccion("El modelo explicó el bloqueo.", "LLM", ColaRedaccion.Completada, 1, "claude", null), 2, Ahora);

        veredicto.Should().Be("BLOQUEADO");
        cierre.EstadoRedaccion.Should().Be(ColaRedaccion.Completada);
        cierre.RedactadoPor.Should().Be("LLM");
        cierre.DisponibleDesde.Should().BeNull();
        cierre.LeaseHasta.Should().BeNull();
        cierre.InvalidarPdf.Should().BeTrue();
        Texto(cierre.Alertamientos, 0).Should().Be("El modelo explicó el bloqueo.");
        Texto(cierre.Alertamientos, 1).Should().Be("El modelo explicó el bloqueo.");
        Codigo(cierre.Alertamientos, 1).Should().Be("IMPUESTO");

        var vehiculo = Guid.CreateVersion7();
        var pdf = EvidenciaDe(TipoEvidencia.Pdf, vehiculo);
        var excel = EvidenciaDe(TipoEvidencia.Xlsx, null);
        var zip = EvidenciaDe(TipoEvidencia.Zip, null);
        var ajeno = EvidenciaDe(TipoEvidencia.Pdf, Guid.CreateVersion7());
        RedactorEnCola.InvalidarPdfVigente([pdf, excel, zip, ajeno], vehiculo);
        pdf.Vigente.Should().BeFalse();
        excel.Vigente.Should().BeTrue();
        zip.Vigente.Should().BeTrue();
        ajeno.Vigente.Should().BeTrue();
        var sinPdf = () => RedactorEnCola.InvalidarPdfVigente([], vehiculo);
        sinPdf.Should().NotThrow();
    }

    [Fact]
    public void Rearmar_solo_pone_monto_en_impuesto_y_la_huella_suma_el_dictamen_si_lo_hay()
    {
        var pedido = RedactorEnCola.Rearmar(
            "BLOQUEADO", Alertas, new DatosBien("ABC123", null, null, null, null, null), suspendida: false, montoImpuesto: 150_000);
        pedido.Veredicto.Should().Be("BLOQUEADO");
        pedido.Reglas.Select(r => r.Codigo).Should().Equal("ESTADO_VEHICULO", "IMPUESTO");
        pedido.Reglas[0].MontoPesos.Should().BeNull();
        pedido.Reglas[1].MontoPesos.Should().Be(150_000);
        pedido.RedaccionSuspendida.Should().BeFalse();

        var encabezado = new EncabezadoReporte(Guid.Empty, "Lote", "Empresa", "Ana", Ahora, "Con análisis IA", "ABC123", "PLACA_CARRO", "ABC123", null);
        var sin = new ContenidoReporteVehiculo(encabezado, []);
        sin.ParaHuella().Keys.Should().NotContain(k => k.StartsWith("dictamen/", StringComparison.Ordinal));
        var con = sin with { Dictamen = new DictamenEnHuella("BLOQUEADO", ColaRedaccion.Completada, ContenidoReporteVehiculo.TextoDeAlertamientos(Alertas)) };
        con.ParaHuella()["dictamen/veredicto"].Should().Be("BLOQUEADO");
        con.ParaHuella()["dictamen/estado"].Should().Be(ColaRedaccion.Completada);
        con.ParaHuella()["dictamen/texto"].Should().Be("plantilla del cierre\nplantilla del impuesto");
    }

    [Fact]
    public async Task Trabajador_de_redaccion_solo_se_registra_con_la_bandera()
    {
        await using (var apagado = await ApiDePrueba.SinBaseAsync())
            apagado.App.Services.GetServices<IHostedService>().Should().NotContain(s => s is TrabajadorRedaccion);

        await using var encendido = await ApiDePrueba.SinBaseAsync(configurar: b => b.Configuration["Diagnostico:Redaccion:Habilitado"] = "true");
        encendido.App.Services.GetServices<IHostedService>().Should().ContainSingle(s => s is TrabajadorRedaccion);
    }

    [Fact]
    public async Task Dos_trabajadores_no_toman_el_mismo_dictamen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var deA = await PendientesAsync(empresas.A, 4, ct);
        var deB = await PendientesAsync(empresas.B, 4, ct);
        var empresaDe = deA.ToDictionary(id => id, _ => empresas.A.Id).Concat(deB.ToDictionary(id => id, _ => empresas.B.Id)).ToDictionary();

        var tomadas = new ConcurrentBag<RedaccionTomada>();
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            while (await TomarAsync(api, ct) is { } tomada)
                tomadas.Add(tomada);
        }, ct)));

        tomadas.Select(t => t.Id).Should().OnlyHaveUniqueItems("ningún dictamen se toma dos veces").And.BeEquivalentTo(empresaDe.Keys);
        tomadas.Should().OnlyContain(t => t.TenantId == empresaDe[t.Id] && t.Intentos == 1);

        var libre = (await PendientesAsync(empresas.A, 2, ct)).ToList();
        await using var otra = new NpgsqlConnection(api.BaseDeDatos);
        await otra.OpenAsync(ct);
        await using (var transaccion = await otra.BeginTransactionAsync(ct))
        {
            await using var bloqueo = new NpgsqlCommand("SELECT id FROM diagnostico.dictamenes WHERE id = @id FOR UPDATE", otra, transaccion);
            bloqueo.Parameters.AddWithValue("id", libre[0]);
            await bloqueo.ExecuteScalarAsync(ct);

            (await TomarAsync(api, ct))!.Id.Should().Be(libre[1], "la bloqueada se salta sin esperar");
            (await TomarAsync(api, ct)).Should().BeNull();
            await transaccion.RollbackAsync(ct);
        }

        (await TomarAsync(api, ct))!.Id.Should().Be(libre[0], "liberada, se toma");

        await using var indice = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'diagnostico' AND indexname = 'ix_redaccion_pendiente'", otra);
        var definicion = (string)(await indice.ExecuteScalarAsync(ct))!;
        definicion.Should().Contain("redaccion_disponible_desde").And.Contain("PENDIENTE").And.Contain("vigente").And.NotContain("tenant_id");
    }

    [Fact]
    public async Task Fecha_futura_no_se_toma_y_el_lease_vencido_se_retoma()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<TimeProvider>(reloj));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var futura = (await PendientesAsync(empresa, 1, ct, disponible: reloj.GetUtcNow().AddMinutes(10))).Single();

        (await TomarAsync(api, ct)).Should().BeNull("la fecha de toma no ha llegado");
        reloj.Avanzar(TimeSpan.FromMinutes(10));
        var llegada = await TomarAsync(api, ct);
        llegada!.Id.Should().Be(futura);

        var id = (await PendientesAsync(empresa, 1, ct, disponible: reloj.GetUtcNow())).Single();
        var primera = await TomarAsync(api, ct);
        primera!.Id.Should().Be(id);
        primera.Intentos.Should().Be(1);
        primera.LeaseHasta.Should().BeCloseTo(reloj.GetUtcNow() + Lease, TimeSpan.FromMilliseconds(1));

        reloj.Avanzar(Lease - TimeSpan.FromSeconds(1));
        (await TomarAsync(api, ct)).Should().BeNull("el lease de la segunda sigue vigente y el de la primera todavía no vence");

        reloj.Avanzar(TimeSpan.FromSeconds(2));
        var segunda = await TomarAsync(api, ct);
        segunda!.Id.Should().Be(futura, "su lease es el más antiguo y ya venció");
        segunda.Intentos.Should().Be(2);
        (await TomarAsync(api, ct))!.Id.Should().Be(id);
    }

    [Fact]
    public async Task Completar_copia_el_texto_e_invalida_solo_el_pdf()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos:
        [
            new("TST701", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
            new("TST702", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
        ]);
        var conPdf = lote.Vehiculos[0].Id;
        var sinPdf = lote.Vehiculos[1].Id;
        await SembrarDictamenAsync(empresa, conPdf, Alertas, "BLOQUEADO", ct);
        await SembrarDictamenAsync(empresa, sinPdf, Alertas, "VIABLE", ct);
        await using (var db = empresa.Db())
        {
            db.Add(EvidenciaDe(TipoEvidencia.Pdf, conPdf, lote.Id));
            db.Add(EvidenciaDe(TipoEvidencia.Xlsx, null, lote.Id));
            db.Add(EvidenciaDe(TipoEvidencia.Zip, null, lote.Id));
            await db.SaveChangesAsync(ct);
        }

        var vueltas = 0;
        while (await ProcesarAsync(api, ct) && vueltas < 5)
            vueltas++;
        vueltas.Should().Be(2);

        await using var lectura = empresa.Db();
        var filas = await lectura.Set<DictamenVehiculo>().AsNoTracking().Where(d => d.TenantId == empresa.Id).ToListAsync(ct);
        filas.Should().OnlyContain(d => d.EstadoRedaccion == ColaRedaccion.Completada && d.RedactadoPor == "LLM" && d.RedaccionDisponibleDesde == null && d.RedaccionLeaseHasta == null);
        filas.Single(d => d.VehiculoId == conPdf).Veredicto.Should().Be("BLOQUEADO");
        Texto(filas.Single(d => d.VehiculoId == conPdf).Alertamientos, 0).Should().Be("El dictamen del bien es BLOQUEADO.");
        filas.Single(d => d.VehiculoId == sinPdf).Veredicto.Should().Be("VIABLE");
        Texto(filas.Single(d => d.VehiculoId == sinPdf).Alertamientos, 0).Should().Be("El dictamen del bien es VIABLE.");
        var evidencias = await lectura.Set<Evidencia>().AsNoTracking().Where(e => e.DiagnosticoId == lote.Id).ToListAsync(ct);
        evidencias.Single(e => e.Tipo == TipoEvidencia.Pdf).Vigente.Should().BeFalse();
        evidencias.Single(e => e.Tipo == TipoEvidencia.Xlsx).Vigente.Should().BeTrue();
        evidencias.Single(e => e.Tipo == TipoEvidencia.Zip).Vigente.Should().BeTrue();
        (await OutboxDePrueba.EventosAsync(api, DictamenEmitido.Tipo, ct)).Should().BeEmpty("completar el texto no publica otro evento");
    }

    [Fact]
    public async Task Suspension_o_proveedor_apagado_al_tomar_dejan_la_fecha_en_null()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var apagado = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(apagado.Omitir is not null, apagado.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(apagado).A;
        var id = (await PendientesAsync(empresa, 1, ct, alertas: Alertas))[0];
        (await ProcesarAsync(apagado, ct)).Should().BeTrue();
        var fila = await FilaAsync(empresa, id, ct);
        fila.EstadoRedaccion.Should().Be(ColaRedaccion.Pendiente);
        fila.RedactadoPor.Should().Be("PLANTILLA");
        fila.RedaccionDisponibleDesde.Should().BeNull();
        fila.RedaccionLeaseHasta.Should().BeNull();
        Texto(fila.Alertamientos, 0).Should().Be("plantilla del cierre");

        await using var suspendido = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(suspendido.Omitir is not null, suspendido.Omitir ?? string.Empty);
        var otra = DosEmpresas.En(suspendido).A;
        var idSuspendido = (await PendientesAsync(otra, 1, ct, alertas: Alertas))[0];
        await using (var db = otra.Db())
        {
            db.Add(new ConfiguracionEmpresa { RedaccionIaSuspendida = true });
            await db.SaveChangesAsync(ct);
        }

        (await ProcesarAsync(suspendido, ct)).Should().BeTrue();
        var quedo = await FilaAsync(otra, idSuspendido, ct);
        quedo.EstadoRedaccion.Should().Be(ColaRedaccion.Pendiente);
        quedo.RedaccionDisponibleDesde.Should().BeNull();
        quedo.RedaccionLeaseHasta.Should().BeNull();
        Texto(quedo.Alertamientos, 0).Should().Be("plantilla del cierre", "suspendida no llama al modelo");
    }

    [Fact]
    public async Task Un_lease_ajeno_no_pisa_el_texto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: Simulado);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = (await PendientesAsync(empresa, 1, ct, alertas: Alertas, veredicto: "BLOQUEADO"))[0];
        var tomada = (await TomarAsync(api, ct))!;
        var ajeno = tomada.LeaseHasta.AddMinutes(30);
        await using (var db = empresa.Db())
        {
            var fila = await db.Set<DictamenVehiculo>().SingleAsync(d => d.Id == id, ct);
            fila.RedaccionLeaseHasta = ajeno;
            await db.SaveChangesAsync(ct);
        }

        await using var scope = api.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RedactorEnCola>().AplicarAsync(tomada, ct);

        var despues = await FilaAsync(empresa, id, ct);
        despues.EstadoRedaccion.Should().Be(ColaRedaccion.Pendiente);
        despues.RedactadoPor.Should().Be("PLANTILLA");
        Texto(despues.Alertamientos, 0).Should().Be("plantilla del cierre");
        despues.RedaccionLeaseHasta.Should().BeCloseTo(ajeno, TimeSpan.FromMilliseconds(1));
    }

    private static void Simulado(WebApplicationBuilder builder)
    {
        builder.Configuration["Diagnostico:Ambiente"] = "LOCAL";
        builder.Configuration["Diagnostico:Anthropic:Simulado"] = "true";
    }

    private static async Task<RedaccionTomada?> TomarAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<ColaRedaccion>(scope.ServiceProvider).TomarAsync(Lease, ct);
    }

    private static async Task<bool> ProcesarAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RedactorEnCola>().ProcesarSiguienteAsync(ct);
    }

    private static async Task<IReadOnlyList<Guid>> PendientesAsync(
        EmpresaDePrueba empresa, int cantidad, CancellationToken ct, DateTimeOffset? disponible = null, string? alertas = null, string veredicto = "VIABLE")
    {
        var vehiculos = Enumerable.Range(0, cantidad)
            .Select(_ => new Flit.Diagnostico.Tests.Tablero.VehiculoSembrado($"TST{Interlocked.Increment(ref _placa):000}", [new(Fuente.Runt, EstadoCelda.Completado, 1)]))
            .ToArray();
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: vehiculos);
        var ahora = disponible ?? DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        await using var db = empresa.Db();
        for (var i = 0; i < cantidad; i++)
        {
            var fila = Fila(lote.Vehiculos[i].Id, ahora.AddMilliseconds(i), alertas ?? "[]", veredicto);
            db.Add(fila);
            ids.Add(fila.Id);
        }

        await db.SaveChangesAsync(ct);
        return ids;
    }

    private static async Task SembrarDictamenAsync(EmpresaDePrueba empresa, Guid vehiculoId, string alertas, string veredicto, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(Fila(vehiculoId, DateTimeOffset.UtcNow, alertas, veredicto));
        await db.SaveChangesAsync(ct);
    }

    private static DictamenVehiculo Fila(Guid vehiculoId, DateTimeOffset disponible, string alertas, string veredicto) => new()
    {
        VehiculoId = vehiculoId,
        Version = 1,
        Vigente = true,
        Veredicto = veredicto,
        Alertamientos = alertas,
        CatalogoVersion = CatalogoReglasDictamen.Version,
        RedactadoPor = "PLANTILLA",
        EstadoRedaccion = ColaRedaccion.Pendiente,
        RedaccionIntentos = 0,
        RedaccionDisponibleDesde = disponible,
        CreadoEn = disponible,
    };

    private static async Task<DictamenVehiculo> FilaAsync(EmpresaDePrueba empresa, Guid id, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<DictamenVehiculo>().AsNoTracking().SingleAsync(d => d.Id == id, ct);
    }

    private static Evidencia EvidenciaDe(TipoEvidencia tipo, Guid? vehiculoId, Guid? diagnosticoId = null) => new()
    {
        DiagnosticoId = diagnosticoId ?? Guid.CreateVersion7(),
        VehiculoId = vehiculoId,
        Tipo = tipo,
        Huella = Convert.ToHexStringLower(SHA256.HashData(Guid.NewGuid().ToByteArray())),
        Estado = EstadoEvidencia.Lista,
        ArchivoRef = "db:prueba",
        TamanoBytes = 4,
        GeneradoEn = Ahora,
        Vigente = true,
        SolicitadoPor = Guid.NewGuid(),
        CreadoEn = Ahora,
    };

    private static string Plantilla(string tipo, string codigo) =>
        CatalogoReglasDictamen.Filas.Single(f => f.TipoTramiteCodigo == tipo && f.Codigo == codigo).PlantillaTexto;

    private static JsonElement Item(string json, int indice)
    {
        using var documento = JsonDocument.Parse(json);
        return documento.RootElement[indice].Clone();
    }

    private static string? Texto(string json, int indice) => Item(json, indice).GetProperty("texto").GetString();

    private static string? Codigo(string json, int indice) => Item(json, indice).GetProperty("codigo").GetString();
}
