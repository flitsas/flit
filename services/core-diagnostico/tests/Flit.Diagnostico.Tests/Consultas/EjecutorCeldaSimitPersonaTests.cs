using System.Collections.Concurrent;
using System.Net;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13794 (F05-H5), AC1-AC5 (F05-C5, F05-C6, F05-C9, F05-C10, F05-C12, F05-C14, decisión Q1 y decisión 4 del gate) —
/// celdas SIMIT_PROPIETARIO y SIMIT_LOCATARIO (<see cref="EjecutorCeldaSimitPersona"/>): una llamada <c>ConsultarMultas</c>
/// por persona del rol, por documento, con <c>tipo_persona</c> JURIDICA para el NIT (sin dígito de verificación) y NATURAL en
/// otro caso; un intento por persona; fallo parcial con reproceso solo de la persona en error; sin locatario la celda nace NA
/// sin llamar; del SIMIT solo se guardan semáforo, cantidad y número, fecha, valor, estado y organismo de cada comparendo; y
/// ningún log, intento ni payload contiene un documento. Adaptador real sobre un cliente gRPC falso
/// (<see cref="MultasPersonaGrpcFalso"/>) o fuente programable (<see cref="SimitPersonaProgramable"/>). PostgreSQL real;
/// placas TST4## y documentos del rango ficticio 99999####.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EjecutorCeldaSimitPersonaTests
/// </code>
/// </example>
public sealed class EjecutorCeldaSimitPersonaTests
{
    // AC1 (F05-C6, F05-C10) — dos locatarios naturales en leasing: dos llamadas NATURAL por documento (sin placa), dos intentos
    // con su persona y la celda COMPLETADO con dos resultados SIMIT. La solicitud gRPC por CC es NATURAL y sin placa.
    [Fact]
    public async Task Una_consulta_por_persona_con_su_tipo_persona()
    {
        var porCc = FuenteSimitReal.Solicitud(SolicitudSimit.PorDocumento(new DocumentoPersona("cc", RuntSimitPersonas.Locatario1)));
        (porCc.TipoPersona, porCc.Documento.Tipo, porCc.Documento.Numero).Should().Be((TipoPersona.Natural, "CC", RuntSimitPersonas.Locatario1));
        porCc.Placa.Should().BeNull("por persona no va la placa");

        var ct = TestContext.Current.CancellationToken;
        var grpc = new MultasPersonaGrpcFalso();
        await using var api = await ArrancarAsync(b => grpc.RegistrarAdaptadorReal(b));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST401")], [Fuente.Runt], null, [Fuente.SimitLocatario], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Select(l => (l.Tenant, l.Tipo, l.Numero, l.TipoPersona, l.ConPlaca)).Should().BeEquivalentTo(
        [
            (empresa.Id.ToString(), "CC", RuntSimitPersonas.Locatario1, TipoPersona.Natural, false),
            (empresa.Id.ToString(), "CE", RuntSimitPersonas.Locatario2, TipoPersona.Natural, false),
        ]);
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST401", Fuente.SimitLocatario, ct);
        (celda.Estado, celda.TipoError).Should().Be((EstadoCelda.Completado, (string?)null));
        var personas = MotorPersonas.PersonasDelPayload(celda.Payload);
        personas.Should().HaveCount(2).And.OnlyContain(p =>
            p.GetProperty("consulta").GetString() == "SIMIT" && p.GetProperty("estado").GetString() == "OK"
            && p.GetProperty("resultado").GetProperty("cantidad_comparendos").GetInt32() == 2);
        var locatarios = await MotorPersonas.PersonasAsync(empresa, celda.VehiculoId, RolPersona.Locatario, ct);
        personas.Select(p => p.GetProperty("persona_id").GetGuid()).Should().Equal(locatarios.Select(l => l.Id));
        var intentos = await MotorPersonas.IntentosAsync(empresa, celda.Id, ct);
        intentos.Should().HaveCount(2).And.OnlyContain(i =>
            i.Estado == ResultadoIntento.Success && i.IdentificadorConsultado == null && i.Fuente == Fuente.SimitLocatario && i.Servicio != null);
        intentos.Select(i => i.PersonaId).Should().BeEquivalentTo(locatarios.Select(l => (Guid?)l.Id));
    }

    // AC2 (F05-C6, decisión 4) — la financiera propietaria con NIT (y dígito de verificación): una llamada JURIDICA, tipo NIT y
    // el número sin dígito de verificación. La solicitud gRPC por NIT es JURIDICA.
    [Fact]
    public async Task NIT_consulta_con_tipo_persona_juridica()
    {
        FuenteSimitReal.Solicitud(SolicitudSimit.PorDocumento(new DocumentoPersona("NIT", RuntSimitPersonas.Financiera)))
            .TipoPersona.Should().Be(TipoPersona.Juridica);

        var ct = TestContext.Current.CancellationToken;
        var grpc = new MultasPersonaGrpcFalso();
        await using var api = await ArrancarAsync(b => grpc.RegistrarAdaptadorReal(b));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST402")], [Fuente.Runt], [Fuente.SimitPropietario], null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Select(l => (l.Tenant, l.Tipo, l.Numero, l.TipoPersona, l.ConPlaca)).Should().ContainSingle()
            .Which.Should().Be((empresa.Id.ToString(), "NIT", RuntSimitPersonas.Financiera, TipoPersona.Juridica, false), "el NIT va sin dígito de verificación");
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST402", Fuente.SimitPropietario, ct);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        MotorPersonas.PersonasDelPayload(celda.Payload).Should().ContainSingle().Which.GetProperty("consulta").GetString().Should().Be("SIMIT");
    }

    // AC2 (F05-C9) — la financiera con muchos comparendos (150, con infractor, infracción y repetidos) guarda solo semáforo,
    // cantidad y número, fecha, valor, estado y organismo; nunca nombres ni el NIT. El simulado con NIT terminado en 6 devuelve
    // muchos comparendos (prueba de tamaño).
    [Fact]
    public async Task NIT_con_muchos_comparendos_guarda_solo_campos_minimos()
    {
        var ct = TestContext.Current.CancellationToken;
        var simulado = await new FuenteSimitSimulada().ConsultarAsync(Guid.NewGuid(), SolicitudSimit.PorDocumento(new DocumentoPersona("NIT", "999990456")), ct);
        simulado.Resultado.Should().NotBeNull();
        simulado.Resultado!.CantidadComparendos.Should().BeGreaterThanOrEqualTo(50, "el 6 simula una persona jurídica con muchos comparendos");
        simulado.Resultado.Comparendos.Select(c => c.Numero).Should().OnlyHaveUniqueItems();
        simulado.Resultado.Semaforo.Should().Be("ROJO");

        var grpc = new MultasPersonaGrpcFalso();
        grpc.ComparendosPor[RuntSimitPersonas.Financiera] = 150;
        await using var api = await ArrancarAsync(b => grpc.RegistrarAdaptadorReal(b));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST402")], [Fuente.Runt], [Fuente.SimitPropietario], null, ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST402", Fuente.SimitPropietario, ct);
        celda.Estado.Should().Be(EstadoCelda.Completado);
        celda.Payload.Should().NotContainAny(MultasPersonaGrpcFalso.Infractor, MultasPersonaGrpcFalso.Infraccion, RuntSimitPersonas.Financiera, RuntSimitPersonas.FinancieraConDv);
        var resultado = MotorPersonas.PersonasDelPayload(celda.Payload).Should().ContainSingle().Subject.GetProperty("resultado");
        resultado.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["semaforo", "cantidad_comparendos", "comparendos"]);
        resultado.GetProperty("cantidad_comparendos").GetInt32().Should().Be(150, "los repetidos por número se cuentan una vez");
        var comparendos = resultado.GetProperty("comparendos").EnumerateArray().ToList();
        comparendos.Should().HaveCount(150);
        comparendos.Should().OnlyContain(c => c.EnumerateObject().Select(p => p.Name).All(n => n == "numero" || n == "fecha" || n == "valor" || n == "estado" || n == "organismo"));
    }

    // AC3 (F05-C5) — dos propietarios, el segundo con Timeout hasta agotar el reintento: ERROR PERSONAS_CON_ERROR con el
    // resultado del primero guardado; el reproceso del admin consulta solo al que falló y la celda queda COMPLETADO.
    [Fact]
    public async Task Fallo_parcial_y_reproceso_solo_de_las_fallidas()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojManual();
        var simit = new SimitPersonaProgramable();
        simit.Responder(RuntSimitPersonas.Dueno2, SimitPersonaProgramable.Timeout, SimitPersonaProgramable.Timeout);
        await using var api = await ArrancarAsync(b =>
        {
            b.Services.AddSingleton<TimeProvider>(reloj);
            simit.Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST403")], [Fuente.Runt], [Fuente.SimitPropietario], null, ct);
        var procesador = Motor.Procesador(api, maxReintentos: 1);

        await MotorRunt.ProcesarTodoAsync(procesador, ct);
        reloj.Avanzar(PoliticaReintentos.Espera(1));
        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        var agotada = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST403", Fuente.SimitPropietario, ct);
        (agotada.Estado, agotada.TipoError).Should().Be((EstadoCelda.Error, EjecutorPorPersonas.PersonasConError), "agotado el reintento del Timeout");
        var parcial = MotorPersonas.PersonasDelPayload(agotada.Payload);
        parcial.Select(p => p.GetProperty("estado").GetString()).Should().Equal("OK", "ERROR");
        parcial[0].GetProperty("resultado").GetProperty("semaforo").GetString().Should().Be("VERDE");
        parcial[1].GetProperty("tipo_error").GetString().Should().Be("TIMEOUT");
        simit.LlamadasA(RuntSimitPersonas.Dueno1).Should().Be(1, "el reintento no vuelve a consumir a quien respondió");
        simit.LlamadasA(RuntSimitPersonas.Dueno2).Should().Be(2);

        var respuesta = await api.Cliente(MotorPersonas.Admin(api, empresa)).PostAsync($"{Program.Prefijo}/consultas/{agotada.Id}/reprocesar", null, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        await MotorRunt.ProcesarTodoAsync(procesador, ct);

        simit.LlamadasA(RuntSimitPersonas.Dueno1).Should().Be(1, "el reproceso tampoco vuelve a consumir a quien respondió");
        simit.LlamadasA(RuntSimitPersonas.Dueno2).Should().Be(3);
        simit.Llamadas.Should().OnlyContain(l => l.Placa == null && l.Empresa == empresa.Id);
        var final = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST403", Fuente.SimitPropietario, ct);
        (final.Estado, final.TipoError).Should().Be((EstadoCelda.Completado, (string?)null));
        MotorPersonas.PersonasDelPayload(final.Payload).Should().HaveCount(2).And.OnlyContain(p => p.GetProperty("estado").GetString() == "OK");
        var intentos = await MotorPersonas.IntentosAsync(empresa, final.Id, ct);
        intentos.Should().HaveCount(4, "dos en la primera ejecución, uno en el reintento y uno en el reproceso");
        intentos[^1].Disparador.Should().Be(DisparadorIntento.Reproceso);
    }

    // AC4 (F05-C12) — vehículo sin ningún locatario (ni del proveedor ni declarado), con el adaptador real: SIMIT_LOCATARIO nace
    // NA SIN_DOCUMENTO_LOCATARIO, sin intento, y core-consultas no recibe llamadas.
    [Fact]
    public async Task Sin_locatario_no_llama_al_adaptador_real()
    {
        var ct = TestContext.Current.CancellationToken;
        var grpc = new MultasPersonaGrpcFalso();
        await using var api = await ArrancarAsync(b => grpc.RegistrarAdaptadorReal(b));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST404")], [Fuente.Runt], null, [Fuente.SimitLocatario], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Should().BeEmpty("sin locatario no se consulta");
        var celda = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST404", Fuente.SimitLocatario, ct);
        (celda.Estado, celda.MotivoNa, celda.Payload).Should().Be((EstadoCelda.Na, (MotivoNa?)MotivoNa.SinDocumentoLocatario, (string?)null));
        (await MotorPersonas.IntentosAsync(empresa, celda.Id, ct)).Should().BeEmpty("una celda NA no deja intento");
        using var scope = api.App.Services.CreateScope();
        scope.ServiceProvider.GetServices<IEjecutorCelda>()
            .Should().Contain(e => e.Fuente == Fuente.SimitLocatario, "la celda tiene ejecutor: el NA es por la regla, no por falta de adaptador");
    }

    // AC5 (F05-C14, R-3) — el locatario declarado y el NIT del propietario: core-consultas rechaza con un mensaje que trae el
    // número; la celda queda en ERROR y ni los logs, ni intentos_consulta (mensaje_error incluido), ni el payload lo contienen.
    [Fact]
    public async Task Ningun_log_ni_intento_contiene_el_documento()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new LogsEnMemoria();
        var grpc = new MultasPersonaGrpcFalso { RechazarConDocumento = true };
        await using var api = await ArrancarAsync(b =>
        {
            b.Logging.AddProvider(logs);
            b.Logging.AddFilter<LogsEnMemoria>(null, LogLevel.Trace);
            grpc.RegistrarAdaptadorReal(b);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST405", "CC", RuntSimitPersonas.Declarado)],
            [Fuente.Runt], [Fuente.SimitPropietario], [Fuente.SimitLocatario], ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        grpc.Llamadas.Select(l => l.Numero).Should().BeEquivalentTo([RuntSimitPersonas.Financiera, RuntSimitPersonas.Declarado]);
        var locatario = await MotorPersonas.CeldaAsync(api, empresa, lote, "TST405", Fuente.SimitLocatario, ct);
        (locatario.Estado, locatario.TipoError).Should().Be((EstadoCelda.Error, "ESTADO_NO_OK"));
        var intento = (await MotorPersonas.IntentosAsync(empresa, locatario.Id, ct)).Should().ContainSingle().Subject;
        intento.MensajeError.Should().NotBeNullOrEmpty();

        string[] documentos = [RuntSimitPersonas.Declarado, RuntSimitPersonas.Financiera, RuntSimitPersonas.FinancieraConDv];
        logs.Mensajes.Should().NotBeEmpty().And.OnlyContain(m => !documentos.Any(d => m.Contains(d, StringComparison.Ordinal)));
        var filas = (await FilasAsync(api, "SELECT row_to_json(i)::text FROM diagnostico.intentos_consulta i WHERE fuente IN ('SIMIT_PROPIETARIO', 'SIMIT_LOCATARIO')", ct))
            .Concat(await FilasAsync(api, "SELECT coalesce(payload::text, '') FROM diagnostico.consultas WHERE fuente IN ('SIMIT_PROPIETARIO', 'SIMIT_LOCATARIO')", ct)).ToList();
        filas.Should().HaveCount(4, "dos intentos y dos celdas");
        filas.Should().OnlyContain(f => !documentos.Any(d => f.Contains(d, StringComparison.Ordinal)));
    }

    /// <summary>API con el RUNT de <see cref="RuntSimitPersonas"/>; el ejecutor SIMIT por persona es el real del servicio.</summary>
    private static Task<ApiDePrueba> ArrancarAsync(Action<WebApplicationBuilder> configurar) =>
        MotorRunt.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<IFuenteRunt>(new RuntSimitPersonas());
            configurar(b);
        });

    private static async Task<List<string>> FilasAsync(ApiDePrueba api, string sql, CancellationToken ct)
    {
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand(sql, conexion);
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        var filas = new List<string>();
        while (await lector.ReadAsync(ct))
            filas.Add(lector.GetString(0));
        return filas;
    }
}

/// <summary>
/// RUNT de las pruebas de SIMIT por persona (HU #13794), sin nombres y con documentos del rango ficticio. TST401: leasing (dueño
/// NIT, dos locatarios naturales CC y CE). TST402: financiera propietaria con NIT y dígito de verificación. TST403: dos
/// propietarios naturales. TST404 y TST405: un propietario sin locatario (en TST405 el NIT de la financiera; su locatario lo
/// declara la fila).
/// </summary>
public sealed class RuntSimitPersonas : IFuenteRunt
{
    public const string Locatario1 = "9999900401";
    public const string Locatario2 = "9999900402";
    public const string DuenoLeasing = "999990406";
    public const string FinancieraConDv = "999990426-7";
    public const string Financiera = "999990426";
    public const string Dueno1 = "9999900431";
    public const string Dueno2 = "9999900432";
    public const string Unico = "9999900441";
    public const string Declarado = "9999900477";

    public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identificador);
        LocatarioRunt[] locatarios = [new("CC", Locatario1), new("CE", Locatario2)];
        return Task.FromResult(identificador.Valor switch
        {
            "TST401" => Vehiculo("TST401", [new PropietarioRunt("NIT", DuenoLeasing, TipoPropiedad.Leasing, locatarios)], locatarios),
            "TST402" or "TST405" => Vehiculo(identificador.Valor, [RuntConPersonas.Propio("NIT", FinancieraConDv)], []),
            "TST403" => Vehiculo("TST403", [RuntConPersonas.Propio("CC", Dueno1), RuntConPersonas.Propio("CC", Dueno2)], []),
            _ => Vehiculo(identificador.Valor, [RuntConPersonas.Propio("CC", Unico)], []),
        });
    }

    private static ResultadoRunt Vehiculo(string placa, IReadOnlyList<PropietarioRunt> propietarios, IReadOnlyList<LocatarioRunt> locatarios) =>
        ResultadoRunt.Exito(new VehiculoRunt(
            placa, "9ZZPRUEBA00000" + placa[3..], "PARTICULAR", "AUTOMOVIL", "SECRETARIA DE TRANSITO DE PRUEBA", propietarios, locatarios,
            "INTEMPO_SIMULADO", "TX-PRUEBA-" + placa));
}

/// <summary>
/// SIMIT programable por persona (HU #13794): anota empresa, placa y documento de cada llamada y responde sin comparendos salvo
/// que se programe una cola de errores por número de documento (la respuesta n consume el elemento n; agotada, OK).
/// </summary>
public sealed class SimitPersonaProgramable : IFuenteSimit
{
    public static readonly ErrorConsulta Timeout = new("TIMEOUT", "El proveedor no respondió a tiempo.");

    private readonly ConcurrentQueue<(Guid Empresa, string? Placa, string? Numero)> _llamadas = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ErrorConsulta>> _errores = new();

    public IReadOnlyCollection<(Guid Empresa, string? Placa, string? Numero)> Llamadas => _llamadas;

    public int LlamadasA(string numero) => _llamadas.Count(l => l.Numero == numero);

    public void Responder(string numero, params ErrorConsulta[] errores) => _errores[numero] = new ConcurrentQueue<ErrorConsulta>(errores);

    public void Registrar(IServiceCollection servicios) => servicios.AddSingleton<IFuenteSimit>(this);

    public Task<RespuestaFuente<ResultadoSimit>> ConsultarAsync(Guid empresa, SolicitudSimit solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var numero = solicitud.Documento?.Numero;
        _llamadas.Enqueue((empresa, solicitud.Placa, numero));
        return Task.FromResult(numero is not null && _errores.TryGetValue(numero, out var cola) && cola.TryDequeue(out var error)
            ? RespuestaFuente<ResultadoSimit>.Fallo(error, ServicioConsulta.Smartcloud)
            : RespuestaFuente<ResultadoSimit>.Exito(new ResultadoSimit("VERDE", 0, []), ServicioConsulta.Smartcloud, "TX-SIMIT"));
    }
}

/// <summary>
/// Cliente gRPC falso de core-consultas para SIMIT por persona (HU #13794): responde <c>ConsultarMultas</c> con los comparendos
/// pedidos por documento (dos por defecto; cada uno repetido una vez y con infracción e infractor inventados, que el adaptador
/// debe descartar) y anota la empresa de la metadata, el documento, el tipo de persona y si vino placa. Con
/// <see cref="RechazarConDocumento"/> lanza <c>InvalidArgument</c> con un detalle que trae el número.
/// </summary>
public sealed class MultasPersonaGrpcFalso : ConsultasService.ConsultasServiceClient
{
    public const string Infractor = "PERSONA PRUEBA INFRACTORA";
    public const string Infraccion = "INFRACCION FICTICIA C02";

    private readonly ConcurrentQueue<(string? Tenant, string Tipo, string Numero, TipoPersona TipoPersona, bool ConPlaca)> _llamadas = new();

    public IReadOnlyCollection<(string? Tenant, string Tipo, string Numero, TipoPersona TipoPersona, bool ConPlaca)> Llamadas => _llamadas;

    /// <summary>Cantidad de comparendos distintos por número de documento (por defecto, dos).</summary>
    public ConcurrentDictionary<string, int> ComparendosPor { get; } = new();

    public bool RechazarConDocumento { get; init; }

    /// <summary>Registra el adaptador real de SIMIT sobre este cliente (antes que Program: gana al TryAdd).</summary>
    public void RegistrarAdaptadorReal(WebApplicationBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);
        b.Services.AddSingleton(new ClienteConsultas(this, NullLogger<ClienteConsultas>.Instance));
        b.Services.AddScoped<IFuenteSimit, FuenteSimitReal>();
    }

    public override AsyncUnaryCall<ConsultarMultasResponse> ConsultarMultasAsync(ConsultarMultasRequest request, CallOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        var numero = request.Documento?.Numero ?? string.Empty;
        _llamadas.Enqueue((options.Headers?.GetValue(PlatformServiceCallInterceptor.TenantMetadata), request.Documento?.Tipo ?? string.Empty, numero,
            request.TipoPersona, request.Placa is not null));
        if (RechazarConDocumento)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"El proveedor no reconoce el documento {numero} del infractor {Infractor}."));

        var resultado = new ResultadoConsulta { Proveedor = "smartcloud_simit", Semaforo = Semaforo.Rojo };
        resultado.Campos.Add(new Campo { Clave = "person_full_name", ValorTexto = Infractor });
        resultado.Campos.Add(new Campo { Clave = "person_document", ValorTexto = numero });
        var multas = new Chequeo { Clave = "multas", Etiqueta = "Multas", Estado = EstadoChequeo.Fail, Fuente = "SIMIT" };
        var cantidad = ComparendosPor.TryGetValue(numero, out var n) ? n : 2;
        for (var i = 1; i <= cantidad; i++)
        {
            for (var copia = 0; copia < 2; copia++)
            {
                multas.Comparendos.Add(new Comparendo
                {
                    Numero = $"C-{i:D4}",
                    Fecha = "2026-01-10",
                    Valor = "604100.00",
                    Estado = "PENDIENTE",
                    Organismo = "TRANSITO FICTICIO",
                    Infraccion = Infraccion,
                });
            }
        }

        resultado.Chequeos.Add(multas);
        return new AsyncUnaryCall<ConsultarMultasResponse>(
            Task.FromResult(new ConsultarMultasResponse { Resultado = resultado }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}
