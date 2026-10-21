using System.Collections.Concurrent;
using System.Text;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Trabajos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13730 (F03-H4), AC1-AC4 — el ejecutor real de la celda RUNT sobre la fuente simulada de Intempo (fixtures inventados de
/// #13725, escenario por el último carácter del identificador): RUNT es la primera celda del vehículo, guarda placa definitiva,
/// VIN, servicio y organismo, cifra el documento del propietario y resuelve el del locatario (proveedor sobre declarado), sin
/// nombres ni documentos en claro en la base ni en los logs. PostgreSQL real (<see cref="ApiDePrueba"/>).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~CeldaRuntTests
/// </code>
/// </example>
public sealed class CeldaRuntTests
{
    // Documentos y nombres de los fixtures (rango ficticio de FixturesSinPiiTests) y un declarado inventado.
    private const string PropietarioLeasing = "999990003";
    private const string LocatarioLeasing = "9999900013";
    private const string PropietarioNatural = "9999900001";
    private const string LocatarioDeclarado = "9999900077";
    private static readonly string[] Nombres = ["LEASING FICTICIO SA", "LOCATARIO PRUEBA DOS", "PERSONA PRUEBA UNO"];

    // AC1 (F03-C4) — por placa: RUNT es la primera celda que se ejecuta; las demás esperan y se liberan al completarla.
    [Fact]
    public async Task Por_placa_guarda_placa_vin_servicio_y_organismo()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        var espia = new FuenteRuntEspia(orden);
        var simit = new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden);
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            espia.Registrar(b.Services);
            simit.Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo);
        var procesador = Motor.Procesador(api);

        (await procesador.ProcesarLoteAsync(ct)).Should().Be(1, "solo la celda RUNT está disponible al principio");
        orden.Should().Equal(["RUNT PLACA_CARRO TST001"], "la primera llamada es RUNT, por placa y sin documento");
        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        var runt = celdas.Single(c => c.Fuente == Fuente.Runt);
        var simitCelda = celdas.Single(c => c.Fuente == Fuente.SimitVehiculo);
        runt.Estado.Should().Be(EstadoCelda.Completado);
        runt.DatoObtenidoEn.Should().NotBeNull();
        simitCelda.Estado.Should().Be(EstadoCelda.Pendiente);
        simitCelda.DisponibleDesde.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(1), "RUNT completó y liberó las demás celdas");
        simitCelda.VersionTablero.Should().Be(runt.VersionTablero, "la liberación va en la misma transacción que cierra RUNT");

        var vehiculo = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single();
        vehiculo.Placa.Should().Be("TST001");
        vehiculo.Vin.Should().Be("9ZZPRUEBA00000001");
        vehiculo.Servicio.Should().Be("PARTICULAR");
        vehiculo.OrganismoNombre.Should().Be("SECRETARIA DE TRANSITO DE PRUEBA");
        vehiculo.RuntConsultadoEn.Should().NotBeNull();
        vehiculo.DatosConsolidados.Should().Contain("TST001").And.Contain("PARTICULAR");

        await MotorRunt.ProcesarTodoAsync(procesador, ct);
        orden.Should().Equal(["RUNT PLACA_CARRO TST001", $"SIMIT_VEHICULO {vehiculo.Id}"], "las demás fuentes van después de RUNT");
    }

    // AC2 (F03-C4) — por VIN: la consulta va por VIN y la placa definitiva es la que devuelve la fuente.
    [Fact]
    public async Task Por_vin_consulta_por_vin_y_guarda_placa_definitiva()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        var espia = new FuenteRuntEspia(orden);
        await using var api = await MotorRunt.ArrancarAsync(b => espia.Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("9ZZPRUEBA00000003")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        orden.Should().Equal(["RUNT VIN 9ZZPRUEBA00000003"]);
        var vehiculo = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single();
        vehiculo.TipoIdentificador.Should().Be(TipoIdentificador.Vin);
        vehiculo.Vin.Should().Be("9ZZPRUEBA00000003");
        vehiculo.Placa.Should().Be("TST003", "la placa definitiva es la del RUNT");
        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Single().Estado.Should().Be(EstadoCelda.Completado);
    }

    // AC3 (F03-C5) — leasing con locatario y un declarado: queda el del proveedor, cifrado y con origen PROVEEDOR.
    [Fact]
    public async Task Locatario_del_proveedor_reemplaza_al_declarado_cifrado_con_origen_PROVEEDOR()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST003", "CC", LocatarioDeclarado)], ct);
        (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single().LocatarioDocOrigen.Should().Be(OrigenDocumento.DeclaradoCliente);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        var vehiculo = (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single();
        vehiculo.LocatarioDocOrigen.Should().Be(OrigenDocumento.Proveedor);
        vehiculo.LocatarioDocTipo.Should().Be("CC");
        protector.Desproteger(vehiculo.LocatarioDocCifrado!).Should().Be(LocatarioLeasing);
        vehiculo.PropietarioDocTipo.Should().Be("NIT");
        protector.Desproteger(vehiculo.PropietarioDocCifrado!).Should().Be(PropietarioLeasing);
    }

    // AC4 (F03-C5) — sin locatario en la respuesta: se conserva el declarado con origen DECLARADO_CLIENTE.
    [Fact]
    public async Task Sin_locatario_del_proveedor_conserva_el_declarado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001", "CC", LocatarioDeclarado), Lotes.Fila("TST011")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        var vehiculos = await Lotes.VehiculosAsync(api, empresa.Id, id, ct);
        var conDeclarado = vehiculos.Single(v => v.IdentificadorNorm == "TST001");
        conDeclarado.RuntConsultadoEn.Should().NotBeNull("el RUNT se ejecutó");
        conDeclarado.LocatarioDocOrigen.Should().Be(OrigenDocumento.DeclaradoCliente);
        conDeclarado.LocatarioDocTipo.Should().Be("CC");
        protector.Desproteger(conDeclarado.LocatarioDocCifrado!).Should().Be(LocatarioDeclarado);
        protector.Desproteger(conDeclarado.PropietarioDocCifrado!).Should().Be(PropietarioNatural);
        var sinNinguno = vehiculos.Single(v => v.IdentificadorNorm == "TST011");
        sinNinguno.RuntConsultadoEn.Should().NotBeNull();
        sinNinguno.LocatarioDocOrigen.Should().BeNull();
        sinNinguno.LocatarioDocCifrado.Should().BeNull();
        sinNinguno.LocatarioDocTipo.Should().BeNull();
    }

    // AC3 (F03-C5) — en bruto: ni el documento ni el nombre aparecen en vehiculos ni en consultas.payload.
    [Fact]
    public async Task Documento_propietario_queda_cifrado_y_sin_nombre()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorRunt.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST003", "CC", LocatarioDeclarado)], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(
            "SELECT v.propietario_doc_cifrado, v.locatario_doc_cifrado, row_to_json(v)::text, coalesce(c.payload::text, '') " +
            "FROM diagnostico.vehiculos v JOIN diagnostico.consultas c ON c.vehiculo_id = v.id AND c.fuente = 'RUNT' WHERE v.diagnostico_id = @id",
            conexion);
        consulta.Parameters.AddWithValue("id", id);
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        (await lector.ReadAsync(ct)).Should().BeTrue();
        var propietario = (byte[])lector[0];
        var locatario = (byte[])lector[1];
        var fila = lector.GetString(2);
        var payload = lector.GetString(3);

        propietario.Should().NotBeEmpty();
        Encoding.UTF8.GetString(propietario).Should().NotContain(PropietarioLeasing);
        Encoding.Latin1.GetString(propietario).Should().NotContain(PropietarioLeasing);
        Encoding.UTF8.GetString(locatario).Should().NotContain(LocatarioLeasing);
        payload.Should().NotBeEmpty("la celda RUNT guarda su resultado normalizado");
        foreach (var texto in new[] { fila, payload })
        {
            texto.Should().NotContain(PropietarioLeasing).And.NotContain(LocatarioLeasing).And.NotContain(LocatarioDeclarado);
            foreach (var nombre in Nombres)
                texto.Should().NotContain(nombre, "el nombre no se guarda");
        }
    }

    // Seguridad (F03-C5) — ningún documento aparece en los logs, ni con RUNT completado ni con RUNT en error.
    [Fact]
    public async Task Ningun_documento_aparece_en_logs()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST003", "CC", LocatarioDeclarado), Lotes.Fila("TST004"), Lotes.Fila("TST008")], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        celdas.Should().Contain(c => c.Estado == EstadoCelda.Completado).And.Contain(c => c.Estado == EstadoCelda.Error);
        logs.Mensajes.Should().Contain(m => celdas.Any(c => m.Contains(c.Id.ToString(), StringComparison.Ordinal)), "el ejecutor deja su log por celda");
        string[] prohibidos = [PropietarioLeasing, LocatarioLeasing, LocatarioDeclarado, "999990004", "999990014", .. Nombres, "EMPRESA LOCATARIA FICTICIA SAS"];
        logs.Mensajes.Should().NotContain(m => prohibidos.Any(p => m.Contains(p, StringComparison.Ordinal)));
    }
}

/// <summary>
/// Fuente RUNT de prueba (HU #13730): delega en la simulada de Intempo (o en la que se le dé) y anota cada llamada como
/// «RUNT tipo identificador» en un registro de orden compartido con los ejecutores de prueba.
/// </summary>
public sealed class FuenteRuntEspia(ConcurrentQueue<string> orden, IFuenteRunt? interna = null) : IFuenteRunt
{
    private readonly IFuenteRunt _interna = interna ?? new FuenteRuntIntempoSimulada();

    public void Registrar(IServiceCollection servicios) => servicios.AddSingleton<IFuenteRunt>(this);

    public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct)
    {
        orden.Enqueue($"RUNT {CodigosDeEnum.Codigo(identificador.Tipo)} {identificador.Valor}");
        return _interna.ConsultarVehiculoAsync(empresa, identificador, ct);
    }
}

/// <summary>Ejecutor de prueba de una fuente distinta de RUNT (HU #13730): anota «FUENTE vehiculo» y completa la celda.</summary>
public sealed class EjecutorFuenteDePrueba(Fuente fuente, ConcurrentQueue<string> orden) : IEjecutorCelda
{
    public Fuente Fuente => fuente;

    public void Registrar(IServiceCollection servicios) => servicios.AddSingleton<IEjecutorCelda>(this);

    public Task<ResultadoCelda> EjecutarAsync(ContextoCelda contexto, CancellationToken ct)
    {
        orden.Enqueue($"{CodigosDeEnum.Codigo(fuente)} {contexto.Celda.VehiculoId}");
        return Task.FromResult(ResultadoCelda.Completado("{}", DateTimeOffset.UtcNow));
    }
}

/// <summary>Ayudas de las pruebas de la celda RUNT (HU #13730).</summary>
internal static class MotorRunt
{
    /// <summary>API con base y la fuente RUNT simulada de Intempo (la configuración del servicio la deja no conectada).</summary>
    public static Task<ApiDePrueba> ArrancarAsync(Action<WebApplicationBuilder>? configurar = null) =>
        ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.AddSingleton<IFuenteRunt, FuenteRuntIntempoSimulada>();
            configurar?.Invoke(b);
        });

    /// <summary>Lote confirmado con RUNT y las fuentes por vehículo dadas (habilitadas en la configuración de la empresa).</summary>
    public static Task<Guid> LoteAsync(ApiDePrueba api, EmpresaDePrueba empresa, Dictionary<string, object?>[] filas, CancellationToken ct, params Fuente[] otras)
    {
        Fuente[] alcance = [Fuente.Runt, .. otras];
        return Motor.LoteConfirmadoAsync(api, empresa, filas, ct, [.. alcance.Select(CodigosDeEnum.Codigo)], alcance);
    }

    /// <summary>Procesa vueltas hasta que no quede celda tomable (las diferidas o en error no lo son).</summary>
    public static async Task ProcesarTodoAsync(ProcesadorConsultas procesador, CancellationToken ct)
    {
        for (var vuelta = 0; vuelta < 50; vuelta++)
        {
            if (await procesador.ProcesarLoteAsync(ct) == 0)
                return;
        }

        throw new InvalidOperationException("La cola no se vació en 50 vueltas.");
    }

    /// <summary>«FUENTE ESTADO TIPO_ERROR» de cada celda del vehículo, leído por SQL directo.</summary>
    public static async Task<List<string>> CeldasDelVehiculoAsync(ApiDePrueba api, Guid vehiculo, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(
            "SELECT fuente || ' ' || estado || ' ' || coalesce(tipo_error, '-') FROM diagnostico.consultas WHERE vehiculo_id = @v ORDER BY fuente", conexion);
        consulta.Parameters.AddWithValue("v", vehiculo);
        var filas = new List<string>();
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
            filas.Add(lector.GetString(0));
        return filas;
    }

    /// <summary>Vehículo del lote por su identificador normalizado.</summary>
    public static async Task<VehiculoDiagnostico> VehiculoAsync(ApiDePrueba api, EmpresaDePrueba empresa, Guid id, string identificador, CancellationToken ct) =>
        (await Lotes.VehiculosAsync(api, empresa.Id, id, ct)).Single(v => v.IdentificadorNorm == identificador);
}
