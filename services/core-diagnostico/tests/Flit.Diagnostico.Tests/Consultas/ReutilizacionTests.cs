using System.Collections.Concurrent;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Tablero;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13739 (F03-H7), AC1-AC3 (F03-C13, D-10) — reutilización del mismo día por compañía, placa y fuente: antes de llamar a
/// la fuente, el procesador busca una celda COMPLETADO de origen API de la MISMA compañía, misma placa y misma fuente con el
/// dato obtenido hoy en hora Colombia (corte a las 00:00, UTC−5). Si existe, la celda copia el resultado (y en RUNT los datos
/// del vehículo y los documentos cifrados), queda COMPLETADO con el <c>dato_obtenido_en</c> del original y
/// <c>reutilizada_de_id</c>, y deja un intento <c>desde_cache=true</c> sin llamar a la fuente. Otra compañía o después de la
/// medianoche consulta de nuevo y lo anterior sigue en la base. PostgreSQL real (<see cref="ApiDePrueba"/>), fuente RUNT
/// simulada de Intempo con espía y reloj inyectable (<see cref="RelojManual"/>); placas ficticias TST###.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReutilizacionTests
/// </code>
/// </example>
public sealed class ReutilizacionTests
{
    private const string PropietarioNatural = "9999900001";
    private const string LocatarioLeasing = "9999900013";
    private const string LocatarioDeclarado = "9999900077";
    private const string OtroDeclarado = "9999900078";
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    // AC1 (F03-C13) — A consultó a las 08:00; un lote nuevo de A a las 15:00 no llama a la fuente, el intento queda
    // desde_cache=true y la celda copia el resultado del original.
    [Fact]
    public async Task Misma_compania_misma_placa_mismo_dia_no_llama_y_marca_desde_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, reloj, orden) = await ArrancarAsync();
        await using var _ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        IrA(reloj, 8, 0);
        var primero = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        IrA(reloj, 15, 0);
        var segundo = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST001")], ct);

        orden.Should().Equal(["RUNT PLACA_CARRO TST001"], "la segunda vez no se llama a la fuente");
        var original = (await Motor.CeldasAsync(api, empresa.Id, primero, ct)).Single();
        var reutilizada = (await Motor.CeldasAsync(api, empresa.Id, segundo, ct)).Single();
        reutilizada.Estado.Should().Be(EstadoCelda.Completado);
        reutilizada.Origen.Should().Be(OrigenCelda.Api);
        reutilizada.ReutilizadaDeId.Should().Be(original.Id);
        JsonDocument.Parse(reutilizada.Payload!).RootElement.GetRawText().Should().Be(JsonDocument.Parse(original.Payload!).RootElement.GetRawText());
        reutilizada.LeaseHasta.Should().BeNull();
        reutilizada.VersionTablero.Should().BeGreaterThan(0, "pasa por el cierre normal y sube la versión del tablero");

        var intento = (await ReintentosTests.IntentosAsync(api, empresa, segundo, ct)).Should().ContainSingle().Subject;
        intento.DesdeCache.Should().BeTrue();
        intento.Estado.Should().Be(ResultadoIntento.Success);
        intento.ConsultaId.Should().Be(reutilizada.Id);
        intento.IdentificadorConsultado.Should().Be("TST001");
        intento.Servicio.Should().BeNull("no se llamó a ningún proveedor");
        intento.TransaccionProveedorId.Should().BeNull();
        intento.Disparador.Should().Be(DisparadorIntento.Auto);
        (await ReintentosTests.IntentosAsync(api, empresa, primero, ct)).Should().ContainSingle().Which.DesdeCache.Should().BeFalse();

        // RUNT: el vehículo del lote nuevo recibe los datos del original y el documento cifrado tal cual (sin volver a cifrar).
        var vOriginal = (await Lotes.VehiculosAsync(api, empresa.Id, primero, ct)).Single();
        var vNuevo = (await Lotes.VehiculosAsync(api, empresa.Id, segundo, ct)).Single();
        vNuevo.Placa.Should().Be("TST001");
        vNuevo.Vin.Should().Be(vOriginal.Vin);
        vNuevo.Servicio.Should().Be(vOriginal.Servicio);
        vNuevo.OrganismoNombre.Should().Be(vOriginal.OrganismoNombre);
        vNuevo.DepartamentoCodigo.Should().Be(vOriginal.DepartamentoCodigo);
        vNuevo.RuntConsultadoEn.Should().BeCloseTo(original.DatoObtenidoEn!.Value, TimeSpan.FromMilliseconds(1), "el RUNT se consultó cuando el original");
        JsonDocument.Parse(vNuevo.DatosConsolidados).RootElement.GetProperty("vehiculo").GetRawText()
            .Should().Be(JsonDocument.Parse(vOriginal.DatosConsolidados).RootElement.GetProperty("vehiculo").GetRawText());
        vNuevo.PropietarioDocTipo.Should().Be(vOriginal.PropietarioDocTipo);
        vNuevo.PropietarioDocCifrado.Should().Equal(vOriginal.PropietarioDocCifrado!, "se copia el cifrado, no se descifra para copiar");
        api.App.Services.GetRequiredService<IProtectorDocumentoPersona>().Desproteger(vNuevo.PropietarioDocCifrado!).Should().Be(PropietarioNatural);

        await using var db = empresa.Db();
        (await db.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(c => c.Id == segundo, ct)).Estado
            .Should().Be(EstadoDiagnostico.Completado, "la celda reutilizada es terminal: el estado global se recalcula en su cierre");
    }

    // AC3 (F03-C13, prueba de fuga) — B consultó hoy la placa: A consulta la fuente y nunca recibe el dato de B; un lote
    // posterior de A reutiliza el de A aunque B tenga uno más nuevo.
    [Fact]
    public async Task Otra_compania_misma_placa_consulta_de_nuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, reloj, orden) = await ArrancarAsync();
        await using var _ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var dos = DosEmpresas.En(api);
        var (a, b) = (dos.A, dos.B);

        IrA(reloj, 9, 0);
        var deB = await LoteProcesadoAsync(api, b, [Lotes.Fila("TST001")], ct);
        IrA(reloj, 10, 0);
        var deA = await LoteProcesadoAsync(api, a, [Lotes.Fila("TST001")], ct);

        orden.Should().HaveCount(2, "A consulta la fuente aunque B ya tenga el dato de hoy");
        var celdaB = (await Motor.CeldasAsync(api, b.Id, deB, ct)).Single();
        var celdaA = (await Motor.CeldasAsync(api, a.Id, deA, ct)).Single();
        celdaA.ReutilizadaDeId.Should().BeNull("nunca recibe el dato de otra compañía");
        celdaA.DatoObtenidoEn.Should().BeAfter(celdaB.DatoObtenidoEn!.Value, "es un dato propio, consultado después");
        (await ReintentosTests.IntentosAsync(api, a, deA, ct)).Should().ContainSingle().Which.DesdeCache.Should().BeFalse();
        (await Lotes.VehiculosAsync(api, a.Id, deA, ct)).Single().PropietarioDocCifrado
            .Should().NotEqual((await Lotes.VehiculosAsync(api, b.Id, deB, ct)).Single().PropietarioDocCifrado!, "A cifró su propio dato");

        IrA(reloj, 10, 30);
        var deB2 = await LoteProcesadoAsync(api, b, [Lotes.Fila("TST001")], ct);
        IrA(reloj, 11, 0);
        var deA2 = await LoteProcesadoAsync(api, a, [Lotes.Fila("TST001")], ct);

        orden.Should().HaveCount(2, "cada compañía reutiliza solo lo suyo");
        (await Motor.CeldasAsync(api, b.Id, deB2, ct)).Single().ReutilizadaDeId.Should().Be(celdaB.Id);
        (await Motor.CeldasAsync(api, a.Id, deA2, ct)).Single().ReutilizadaDeId.Should().Be(celdaA.Id, "el de A, no el de B");
        (await Motor.EscalarAsync(
            api,
            "SELECT count(*) FROM diagnostico.consultas c JOIN diagnostico.consultas o ON o.id = c.reutilizada_de_id WHERE o.tenant_id <> c.tenant_id",
            ct)).Should().Be(0, "ninguna celda reutiliza la de otra empresa");
    }

    // AC2 (F03-C13) — A consultó a las 23:50; a las 00:05 del día siguiente se consulta de nuevo y la celda y el intento
    // anteriores siguen en la base. A las 00:10 se reutiliza el dato de las 00:05, no el vencido.
    [Fact]
    public async Task Tras_medianoche_consulta_de_nuevo_y_conserva_la_anterior()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, reloj, orden) = await ArrancarAsync();
        await using var _ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        IrA(reloj, 23, 50);
        var ayer = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var celdaAyer = (await Motor.CeldasAsync(api, empresa.Id, ayer, ct)).Single();
        IrA(reloj, 0, 5);
        var hoy = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST001")], ct);

        orden.Should().HaveCount(2, "tras las 00:00 hora Colombia el dato de ayer venció");
        var celdaHoy = (await Motor.CeldasAsync(api, empresa.Id, hoy, ct)).Single();
        celdaHoy.ReutilizadaDeId.Should().BeNull();
        celdaHoy.DatoObtenidoEn!.Value.ToOffset(Colombia).Hour.Should().Be(0);
        (await ReintentosTests.IntentosAsync(api, empresa, hoy, ct)).Should().ContainSingle().Which.DesdeCache.Should().BeFalse();

        var conservada = (await Motor.CeldasAsync(api, empresa.Id, ayer, ct)).Single();
        conservada.Estado.Should().Be(EstadoCelda.Completado, "lo vencido no se borra");
        conservada.DatoObtenidoEn.Should().Be(celdaAyer.DatoObtenidoEn);
        conservada.Payload.Should().Be(celdaAyer.Payload);
        (await ReintentosTests.IntentosAsync(api, empresa, ayer, ct)).Should().ContainSingle("el intento anterior queda como historial");

        IrA(reloj, 0, 10);
        var despues = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        orden.Should().HaveCount(2);
        (await Motor.CeldasAsync(api, empresa.Id, despues, ct)).Single().ReutilizadaDeId.Should().Be(celdaHoy.Id, "el dato vigente es el de las 00:05");
    }

    // AC1 (F03-C13) — la celda reutilizada guarda la fecha y hora del dato original (08:00), no la del cierre (15:00), y la
    // grilla la muestra marcada como reutilizada.
    [Fact]
    public async Task Celda_reutilizada_guarda_fecha_y_hora_del_dato_original()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, reloj, _) = await ArrancarAsync();
        await using var __ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        var lasOcho = IrA(reloj, 8, 0);
        var primero = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST001")], ct);
        var lasQuince = IrA(reloj, 15, 0);
        var segundo = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST001")], ct);

        var original = (await Motor.CeldasAsync(api, empresa.Id, primero, ct)).Single();
        var reutilizada = (await Motor.CeldasAsync(api, empresa.Id, segundo, ct)).Single();
        original.DatoObtenidoEn.Should().BeCloseTo(lasOcho, TimeSpan.FromMilliseconds(1));
        reutilizada.DatoObtenidoEn.Should().Be(original.DatoObtenidoEn, "la celda muestra la fecha y hora del dato original");
        reutilizada.ActualizadoEn.Should().BeCloseTo(lasQuince, TimeSpan.FromSeconds(1), "se cerró a las 15:00");

        var grilla = await Tablero.Tablero.JsonAsync(await empresa.Cliente().GetAsync(Tablero.Tablero.RutaGrilla(segundo), ct), ct);
        var celda = grilla.GetProperty("vehiculos")[0].GetProperty("celdas")[0];
        celda.GetProperty("reutilizada").GetBoolean().Should().BeTrue();
        celda.GetProperty("dato_obtenido_en").GetDateTimeOffset().Should().BeCloseTo(lasOcho, TimeSpan.FromMilliseconds(1));
        grilla.GetProperty("estado").GetString().Should().Be("COMPLETADO");
    }

    // RUNT (F03-C13, R-3) — al reutilizar se respeta la precedencia del locatario: el del proveedor (copiado cifrado) reemplaza
    // al declarado, que queda de respaldo; el declarado de otro vehículo nunca se copia.
    [Fact]
    public async Task Reutilizar_runt_copia_documentos_cifrados_y_respeta_el_locatario()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, reloj, orden) = await ArrancarAsync();
        await using var _ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();

        IrA(reloj, 10, 0);
        var primero = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST003"), Lotes.Fila("TST001", "CC", LocatarioDeclarado)], ct);
        IrA(reloj, 11, 0);
        var segundo = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST003", "CE", OtroDeclarado), Lotes.Fila("TST001", "CC", OtroDeclarado)], ct);

        orden.Should().HaveCount(2, "las dos placas del segundo lote se reutilizan");
        var leasingOriginal = await MotorRunt.VehiculoAsync(api, empresa, primero, "TST003", ct);
        var leasing = await MotorRunt.VehiculoAsync(api, empresa, segundo, "TST003", ct);
        leasing.LocatarioDocOrigen.Should().Be(OrigenDocumento.Proveedor, "el locatario del proveedor manda sobre el declarado");
        leasing.LocatarioDocTipo.Should().Be(leasingOriginal.LocatarioDocTipo);
        leasing.LocatarioDocCifrado.Should().Equal(leasingOriginal.LocatarioDocCifrado!);
        protector.Desproteger(leasing.LocatarioDocCifrado!).Should().Be(LocatarioLeasing);
        leasing.LocatarioDeclaradoDocTipo.Should().Be("CE", "el declarado queda de respaldo con su tipo");
        protector.Desproteger(leasing.LocatarioDeclaradoDocCifrado!).Should().Be(OtroDeclarado);
        leasing.PropietarioDocCifrado.Should().Equal(leasingOriginal.PropietarioDocCifrado!);

        var natural = await MotorRunt.VehiculoAsync(api, empresa, segundo, "TST001", ct);
        natural.LocatarioDocOrigen.Should().Be(OrigenDocumento.DeclaradoCliente, "el RUNT original no trajo locatario");
        protector.Desproteger(natural.LocatarioDocCifrado!).Should().Be(OtroDeclarado, "conserva su propio declarado, no el del otro vehículo");
        natural.LocatarioDeclaradoDocCifrado.Should().BeNull();
        (await Motor.CeldasAsync(api, empresa.Id, segundo, ct)).Should().OnlyContain(c => c.ReutilizadaDeId != null);
    }

    // RUNT por VIN (D-10, la clave es la placa) — con identificador VIN no hay reutilización de la celda RUNT.
    [Fact]
    public async Task Por_vin_no_reutiliza_la_celda_runt()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, reloj, orden) = await ArrancarAsync();
        await using var _ = api;
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        IrA(reloj, 10, 0);
        await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST003")], ct);
        IrA(reloj, 11, 0);
        var porVin = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("9ZZPRUEBA00000003")], ct);
        IrA(reloj, 12, 0);
        var porPlaca = await LoteProcesadoAsync(api, empresa, [Lotes.Fila("TST003")], ct);

        orden.Should().Equal(["RUNT PLACA_CARRO TST003", "RUNT VIN 9ZZPRUEBA00000003"], "por VIN siempre se consulta; por placa se reutiliza");
        (await Motor.CeldasAsync(api, empresa.Id, porVin, ct)).Single().ReutilizadaDeId.Should().BeNull();
        (await Lotes.VehiculosAsync(api, empresa.Id, porVin, ct)).Single().TipoIdentificador.Should().Be(TipoIdentificador.Vin);
        (await Motor.CeldasAsync(api, empresa.Id, porPlaca, ct)).Single().ReutilizadaDeId.Should().NotBeNull();
    }

    /// <summary>API con la fuente RUNT simulada espiada y un reloj manual.</summary>
    private static async Task<(ApiDePrueba Api, RelojManual Reloj, ConcurrentQueue<string> Orden)> ArrancarAsync()
    {
        var reloj = new RelojManual();
        var orden = new ConcurrentQueue<string>();
        var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            new FuenteRuntEspia(orden).Registrar(b.Services);
        });
        return (api, reloj, orden);
    }

    /// <summary>
    /// Lote confirmado de la empresa (alcance RUNT), procesado hasta vaciar la cola. La configuración de la empresa se siembra
    /// solo con su primer lote: los siguientes crean la cabecera por la API sobre la misma configuración.
    /// </summary>
    private static async Task<Guid> LoteProcesadoAsync(ApiDePrueba api, EmpresaDePrueba empresa, Dictionary<string, object?>[] filas, CancellationToken ct)
    {
        Guid id;
        if (await TieneConfiguracionAsync(empresa, ct))
        {
            var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
            var creada = await cliente.PostAsync(
                Cabeceras.Ruta, Configuracion.ConfiguracionOperativaTests.Cuerpo(Cabeceras.Json("Lote repetido", "SIMPLE", null, ["RUNT"])), ct);
            creada.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
            id = (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<JsonElement>(creada.Content, ct)).GetProperty("id").GetGuid();
            var confirmada = await Lotes.ConfirmarAsync(cliente, id, filas, ct);
            confirmada.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted, await confirmada.Content.ReadAsStringAsync(ct));
        }
        else
        {
            id = await MotorRunt.LoteAsync(api, empresa, filas, ct);
        }

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);
        return id;
    }

    private static async Task<bool> TieneConfiguracionAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<global::Flit.Diagnostico.Api.Modulos.Configuracion.ConfiguracionEmpresa>().AsNoTracking().AnyAsync(ct);
    }

    /// <summary>Avanza el reloj hasta la próxima hh:mm en hora Colombia (siempre hacia adelante) y devuelve ese instante.</summary>
    private static DateTimeOffset IrA(RelojManual reloj, int hora, int minuto)
    {
        var ahora = reloj.GetUtcNow();
        var local = ahora.ToOffset(Colombia);
        var destino = new DateTimeOffset(local.Year, local.Month, local.Day, hora, minuto, 0, Colombia);
        if (destino <= ahora)
            destino = destino.AddDays(1);
        reloj.Avanzar(destino - ahora);
        return destino;
    }
}
