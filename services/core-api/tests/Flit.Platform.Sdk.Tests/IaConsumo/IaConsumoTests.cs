using System.Security.Claims;
using Flit.Platform.Sdk.IaConsumo;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Platform.Sdk.Tests.IaConsumo;

/// <summary>
/// Contrato ia-consumo v1 (ADR-0090) sin base de datos: ámbito ambiental, armado de la fila, banderas, costo y el
/// escritor en segundo plano. La regla que más importa: pase lo que pase con la cola o con la base, la llamada a la
/// IA no se entera.
/// </summary>
public sealed class IaConsumoTests
{
    private static IaConsumoOpciones Opciones(Dictionary<string, string?>? valores = null, int capacidad = 10_000) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(valores ?? []).Build(), "flit", "core-api")
        {
            CapacidadCola = capacidad,
            TopeVaciado = TimeSpan.FromSeconds(2),
        };

    private static IaConsumoRegistrador Registrador(IaConsumoOpciones opciones, IaConsumoCola cola, HttpContext? http = null) =>
        new(opciones, cola, IaCallContext.Instancia, new HttpContextAccessor { HttpContext = http }, TimeProvider.System,
            NullLogger<IaConsumoRegistrador>.Instance);

    private static IaLlamada Llamada(string resultado = IaResultados.Exito) => new()
    {
        Proveedor = IaProveedores.Anthropic,
        TipoTareaPorDefecto = IaTiposTarea.Ocr,
        FechaInicio = DateTimeOffset.UtcNow.AddMilliseconds(-250),
        Resultado = resultado,
        HttpStatus = 200,
        ModeloSolicitado = "claude-haiku-4-5",
        ModeloRespondido = "claude-haiku-4-5-20251001",
        TokensEntrada = 1000,
        TokensSalida = 100,
        PromptTexto = "lee la factura",
        RespuestaTexto = "{\"ok\":true}",
    };

    private static List<IaConsumoPendiente> Drenar(IaConsumoCola cola)
    {
        var lista = new List<IaConsumoPendiente>();
        while (cola.Lector.TryRead(out var p))
            lista.Add(p);
        return lista;
    }

    // ── Ámbito ambiental ─────────────────────────────────────────────────────

    [Fact]
    public void ElAmbitoAnidadoHeredaLoQueNoRedefineYConservaLaCorrelacion()
    {
        var ctx = IaCallContext.Instancia;
        ctx.Actual.Should().BeNull();

        using (ctx.Iniciar(IaProcesosFlit.ModuloTramites, IaProcesosFlit.OcrLote, IaTiposTarea.Ocr, placa: "abc-123"))
        {
            var afuera = ctx.Actual!;
            using (ctx.Iniciar(new IaAmbito { Proceso = IaProcesosFlit.ClasificarLote, Pasada = 2 }))
            {
                var adentro = ctx.Actual!;
                adentro.CorrelacionId.Should().Be(afuera.CorrelacionId);
                adentro.Ambito.Modulo.Should().Be(IaProcesosFlit.ModuloTramites);
                adentro.Ambito.Proceso.Should().Be(IaProcesosFlit.ClasificarLote);
                adentro.Ambito.Placa.Should().Be("abc-123");
                adentro.Ambito.Pasada.Should().Be((short)2);
            }

            ctx.Actual!.Ambito.Proceso.Should().Be(IaProcesosFlit.OcrLote, "al cerrar el de adentro vuelve el de afuera");
        }

        ctx.Actual.Should().BeNull();
    }

    [Fact]
    public async Task LasTareasHijasHeredanElAmbitoYLoQueAbrenNoSeFiltraHaciaAfuera()
    {
        var ctx = IaCallContext.Instancia;
        using var _ = ctx.Iniciar(IaProcesosFlit.ModuloTramites, IaProcesosFlit.OcrLote);
        var correlacion = ctx.Actual!.CorrelacionId;

        var vistas = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            await Task.Yield();
            using var hijo = ctx.Iniciar(new IaAmbito { PromptCodigo = $"tipo{i}" });
            await Task.Delay(5, TestContext.Current.CancellationToken);
            return ctx.Actual!;
        }));

        vistas.Should().OnlyContain(v => v.CorrelacionId == correlacion);
        vistas.Select(v => v.Ambito.PromptCodigo).Should().BeEquivalentTo(["tipo0", "tipo1", "tipo2", "tipo3"]);
        ctx.Actual!.Ambito.PromptCodigo.Should().BeNull();
    }

    // ── Armado de la fila ───────────────────────────────────────────────────

    [Fact]
    public void SinAmbitoLaFilaQuedaDesconocidaYNoFalla()
    {
        var opciones = Opciones();
        var cola = new IaConsumoCola(opciones);
        Registrador(opciones, cola).Registrar(Llamada());

        var fila = Drenar(cola).Single().Fila;
        fila.Modulo.Should().Be("desconocido");
        fila.Proceso.Should().Be("desconocido");
        fila.TipoTarea.Should().Be(IaTiposTarea.Ocr);
        fila.Origen.Should().Be(IaOrigenes.Job, "sin usuario en la petición (criterio común con FLITO)");
        fila.Aplicativo.Should().Be("flit");
        fila.Servicio.Should().Be("core-api");
        fila.Ambiente.Should().Be("local");
        fila.Intento.Should().Be((short)1);
        fila.Pasada.Should().Be((short)1);
        fila.LatenciaMs.Should().BeGreaterThanOrEqualTo(200);
        fila.FechaInicio.Offset.Should().Be(TimeSpan.Zero, "fechas en UTC");
        fila.PromptHash.Should().HaveLength(64);
        fila.Id.Should().NotBe(fila.CorrelacionId);
    }

    [Fact]
    public void ConAmbitoYUsuarioLaFilaLlevaElNegocioYLaPeticion()
    {
        var opciones = Opciones(new() { ["IA_CONSUMO_AMBIENTE"] = "PDN" });
        var cola = new IaConsumoCola(opciones);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "u-1"), new Claim("tenant_id", "t-1"), new Claim("preferred_username", "ana@flit.test")], "jwt")),
        };

        using (IaCallContext.Instancia.Iniciar(IaProcesosFlit.ModuloTramites, IaProcesosFlit.OcrLicenciaTransito,
                   placa: " abc-12 3 ", entidadTipo: "tramite", entidadId: "42", promptCodigo: "tarjeta_propiedad"))
        {
            Registrador(opciones, cola, http).Registrar(Llamada());
        }

        var fila = Drenar(cola).Single().Fila;
        fila.Modulo.Should().Be("tramites");
        fila.Proceso.Should().Be("ocr_licencia_transito");
        fila.Placa.Should().Be("ABC123", "MAYÚSCULAS, sin espacios ni guiones");
        fila.EntidadTipo.Should().Be("tramite");
        fila.EntidadId.Should().Be("42");
        fila.PromptCodigo.Should().Be("tarjeta_propiedad");
        fila.UsuarioId.Should().Be("u-1");
        fila.TenantId.Should().Be("t-1");
        fila.UsuarioLogin.Should().Be("ana@flit.test");
        fila.Origen.Should().Be(IaOrigenes.Usuario);
        fila.Ambiente.Should().Be("pdn");
    }

    [Fact]
    public void ElTenantSaleDeLaCabeceraCuandoElTokenNoLoTrae()
    {
        var opciones = Opciones();
        var cola = new IaConsumoCola(opciones);
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Tenant-Id"] = "t-cabecera";

        Registrador(opciones, cola, http).Registrar(Llamada());

        Drenar(cola).Single().Fila.TenantId.Should().Be("t-cabecera");
    }

    [Fact]
    public void ConLaBanderaMaestraApagadaNoSeRegistraNada()
    {
        var opciones = Opciones(new() { ["IA_CONSUMO_LOG"] = "false" });
        var cola = new IaConsumoCola(opciones);
        var registrador = Registrador(opciones, cola);

        registrador.Activo.Should().BeFalse();
        registrador.Registrar(Llamada());

        Drenar(cola).Should().BeEmpty();
    }

    [Fact]
    public void ElDetalleSoloSeGuardaConSuBanderaYSeRecortaA64KB()
    {
        var sinDetalle = Opciones();
        var cola1 = new IaConsumoCola(sinDetalle);
        Registrador(sinDetalle, cola1).Registrar(Llamada());
        Drenar(cola1).Single().Detalle.Should().BeNull("por defecto IA_CONSUMO_DETALLE=false");

        var conDetalle = Opciones(new() { ["IA_CONSUMO_DETALLE"] = "true" });
        var cola2 = new IaConsumoCola(conDetalle);
        Registrador(conDetalle, cola2).Registrar(Llamada() with
        {
            PromptTexto = new string('p', 70_000),
            ErrorMensaje = new string('e', 900),
        });

        var pendiente = Drenar(cola2).Single();
        pendiente.Detalle.Should().NotBeNull();
        pendiente.Detalle!.ConsumoId.Should().Be(pendiente.Fila.Id);
        pendiente.Detalle.PromptTexto.Should().HaveLength(64 * 1024);
        pendiente.Detalle.RespuestaTexto.Should().Be("{\"ok\":true}");
        pendiente.Fila.ErrorMensaje.Should().HaveLength(500);
    }

    [Fact]
    public void ConLaColaLlenaSeDescartaSeCuentaYLaLlamadaSigue()
    {
        var opciones = Opciones(capacidad: 2);
        var cola = new IaConsumoCola(opciones);
        var registrador = Registrador(opciones, cola);

        var registrar = () =>
        {
            for (var i = 0; i < 5; i++)
                registrador.Registrar(Llamada());
        };

        registrar.Should().NotThrow();
        cola.Descartados.Should().Be(3);
        Drenar(cola).Should().HaveCount(2);
    }

    [Fact]
    public void UnaLlamadaMalArmadaNoRompeAlQueLlama()
    {
        var opciones = Opciones();
        var registrador = Registrador(opciones, new IaConsumoCola(opciones));

        var registrar = () => registrador.Registrar(null!);

        registrar.Should().NotThrow();
    }

    [Theory]
    [InlineData(429, IaResultados.RateLimit)]
    [InlineData(500, IaResultados.ErrorProveedor)]
    [InlineData(529, IaResultados.ErrorProveedor)]
    [InlineData(401, IaResultados.ErrorProveedor)]
    [InlineData(400, IaResultados.Rechazado)]
    [InlineData(413, IaResultados.Rechazado)]
    [InlineData(408, IaResultados.ErrorProveedor)]
    [InlineData(404, IaResultados.ErrorProveedor)]
    [InlineData(415, IaResultados.Rechazado)]
    [InlineData(200, IaResultados.Exito)]
    public void ClasificaElStatusHttpSegunElCatalogo(int status, string esperado) =>
        IaResultados.DesdeHttp(status).Should().Be(esperado);

    // ── Costo ────────────────────────────────────────────────────────────────

    private static readonly IaTarifaModelo[] Tarifas =
    [
        new() { Proveedor = "anthropic", Modelo = "claude-haiku-4-5", VigenteDesde = new DateOnly(2026, 1, 1),
                PrecioEntradaMtok = 1m, PrecioSalidaMtok = 5m, PrecioCacheLecturaMtok = 0.1m, PrecioCacheEscrituraMtok = 1.25m },
        new() { Proveedor = "anthropic", Modelo = "claude-haiku-4-5", VigenteDesde = new DateOnly(2027, 1, 1),
                PrecioEntradaMtok = 2m, PrecioSalidaMtok = 10m },
    ];

    [Fact]
    public void ElCostoUsaLaTarifaVigenteYElModeloSinSufijoDeFecha()
    {
        var fila = new IaConsumoLog
        {
            Proveedor = "anthropic",
            ModeloSolicitado = "claude-haiku-4-5",
            ModeloRespondido = "claude-haiku-4-5-20251001",
            FechaInicio = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero),
            TokensEntrada = 1_000_000,
            TokensSalida = 100_000,
            TokensCacheLectura = 2_000_000,
            TokensCacheEscritura = 400_000,
        };

        // 1×1 + 0,1×5 + 2×0,1 + 0,4×1,25 = 1 + 0,5 + 0,2 + 0,5
        IaCalculadoraCosto.Calcular(Tarifas, fila).Should().Be(2.2m);
    }

    [Fact]
    public void SinTarifaElCostoEsNullYNoCero()
    {
        var fila = new IaConsumoLog { Proveedor = "openai", ModeloSolicitado = "gpt-4o", FechaInicio = DateTimeOffset.UtcNow, TokensEntrada = 10 };
        IaCalculadoraCosto.Calcular(Tarifas, fila).Should().BeNull();

        var antes = new IaConsumoLog
        {
            Proveedor = "anthropic", ModeloSolicitado = "claude-haiku-4-5",
            FechaInicio = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), TokensEntrada = 10,
        };
        IaCalculadoraCosto.Calcular(Tarifas, antes).Should().BeNull("ninguna tarifa estaba vigente todavía");
    }

    // ── Escritor en segundo plano ───────────────────────────────────────────

    private sealed class AlmacenFalso : IIaConsumoAlmacen
    {
        public bool Falla { get; set; }
        public List<IaConsumoPendiente> Guardados { get; } = [];
        public int Intentos { get; private set; }

        public Task GuardarAsync(IReadOnlyList<IaConsumoPendiente> lote, CancellationToken ct)
        {
            Intentos++;
            if (Falla)
                throw new InvalidOperationException("la base está caída");
            Guardados.AddRange(lote);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IaTarifaModelo>> TarifasAsync(CancellationToken ct) =>
            Falla ? throw new InvalidOperationException("caída") : Task.FromResult<IReadOnlyList<IaTarifaModelo>>(Tarifas);

        public Task<int> PurgarDetalleAsync(DateTimeOffset limite, CancellationToken ct) => Task.FromResult(0);
    }

    private static (IaConsumoEscritor Escritor, IaConsumoCola Cola, AlmacenFalso Almacen, IaConsumoRegistrador Registrador) Escritor()
    {
        var opciones = Opciones();
        var cola = new IaConsumoCola(opciones);
        var almacen = new AlmacenFalso();
        var sp = new ServiceCollection().AddSingleton<IIaConsumoAlmacen>(almacen).BuildServiceProvider();
        var escritor = new IaConsumoEscritor(cola, sp.GetRequiredService<IServiceScopeFactory>(),
            new IaCalculadoraCosto(opciones, TimeProvider.System), opciones, NullLogger<IaConsumoEscritor>.Instance);
        return (escritor, cola, almacen, Registrador(opciones, cola));
    }

    [Fact]
    public async Task ElEscritorGuardaPorLotesConElCostoCalculado()
    {
        var (escritor, cola, almacen, registrador) = Escritor();
        registrador.Registrar(Llamada());
        registrador.Registrar(Llamada());

        await escritor.VaciarAsync([]);

        almacen.Guardados.Should().HaveCount(2);
        // 1000×1 + 100×5 por millón
        almacen.Guardados.Should().OnlyContain(p => p.Fila.CostoUsdEstimado == 0.0015m);
        cola.Lector.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task SiLaBaseCaeElLoteSeDescartaSinLanzarYElSiguienteSeGuarda()
    {
        var (escritor, _, almacen, registrador) = Escritor();
        almacen.Falla = true;
        registrador.Registrar(Llamada());

        var vaciar = () => escritor.VaciarAsync([]);
        await vaciar.Should().NotThrowAsync();
        almacen.Guardados.Should().BeEmpty();

        almacen.Falla = false;
        registrador.Registrar(Llamada());
        await escritor.VaciarAsync([]);
        almacen.Guardados.Should().HaveCount(1);
    }

    [Fact]
    public async Task AlApagarElServicioSeVaciaLaCola()
    {
        var (escritor, _, almacen, registrador) = Escritor();
        await escritor.StartAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < 3; i++)
            registrador.Registrar(Llamada());

        await escritor.StopAsync(TestContext.Current.CancellationToken);

        almacen.Guardados.Should().HaveCount(3);
    }

    [Fact]
    public async Task SiSeApagaAntesDeQueElBucleArranqueIgualSeVaciaLaCola()
    {
        // Reproduce el CI con carga: el host se detiene antes de que ExecuteAsync llegue a correr.
        var (escritor, _, almacen, registrador) = Escritor();
        for (var i = 0; i < 3; i++)
            registrador.Registrar(Llamada());

        await escritor.StopAsync(TestContext.Current.CancellationToken);

        almacen.Guardados.Should().HaveCount(3);
    }

    [Fact]
    public void ElMensajeDeErrorSeSaneaSinCedulasNiCorreos()
    {
        IaConsumoTexto.SanearError("documento 1.020.304.050 de ana.p@flit.test rechazado; http 429")
            .Should().Be("documento [numero] de [correo] rechazado; http 429");
        IaConsumoTexto.SanearError(null).Should().BeNull();
    }

    [Fact]
    public void LaPlacaSeNormaliza()
    {
        IaConsumoTexto.NormalizarPlaca(" abc-123 ").Should().Be("ABC123");
        IaConsumoTexto.NormalizarPlaca(" - ").Should().BeNull();
        IaConsumoTexto.NormalizarPlaca(null).Should().BeNull();
        IaConsumoTexto.BytesDeBase64(Convert.ToBase64String(new byte[10])).Should().Be(10);
    }
}
