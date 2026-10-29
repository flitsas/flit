using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13727 (ADR-0075, T6; tanda B) — <see cref="IConstructorSolicitud"/>: para cada fuente, el <c>*Request</c> que arma el
/// constructor es el que enviaba a core-consultas la <c>Fuente*Real</c> de la celda antes del bus, fijado aquí campo por campo
/// (<c>Equals</c> de proto) con el mismo vehículo y las mismas personas, y en el mismo orden: RUNT por placa o VIN; RUNT persona
/// con tipo en mayúsculas y número; RUES con el NIT sin dígito; SIMIT por placa o por documento con su tipo de persona; avalúo con
/// placa, VIN y datos técnicos; impuesto con placa y departamento. Los documentos se descifran de <c>personas_vehiculo</c> con la
/// protección de documentos y el <c>*Request</c> solo trae campos de identificación. PostgreSQL real; RUNT simulado de Intempo:
/// TST001 persona natural, TST003 leasing (propietario NIT y locatario con cédula) y un VIN de prueba que termina en 1.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ConstructoresSolicitudTests
/// </code>
/// </example>
public sealed class ConstructoresSolicitudTests
{
    internal const string Vin = "9ZZPRUEBA00000011";
    private const string Natural = "9999900001";
    private const string Nit = "999990003";
    private const string Locatario = "9999900013";

    /// <summary>Rutas de campo (proto-JSON) que puede traer el <c>*Request</c> de cada rpc: solo identificación.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> CamposPermitidos = new Dictionary<string, string[]>
    {
        [RpcConsultas.PorPlaca] = ["placa"],
        [RpcConsultas.PorVin] = ["vin"],
        // HU #14153 AC4: RUNT persona y RUES piden además la respuesta cruda del proveedor (una opción, no un dato de identificación).
        [RpcConsultas.Conductor] = ["opciones", "opciones.incluirRespuestaCruda", "documento", "documento.tipo", "documento.numero"],
        [RpcConsultas.Rues] = ["opciones", "opciones.incluirRespuestaCruda", "nit"],
        // HU #14183 T3: SIMIT también lleva opciones.incluirRespuestaCruda=true.
        [RpcConsultas.Multas] = ["opciones", "opciones.incluirRespuestaCruda", "documento", "documento.tipo", "documento.numero", "tipoPersona", "placa", "placa.valor"],
        [RpcConsultas.Avaluos] = ["opciones", "placa", "placa.valor", "vin", "vin.valor", "modelo", "cilindraje", "combustible", "pasajeros"],
        [RpcConsultas.Impuesto] = ["opciones", "placa", "placa.valor", "departamentoCodigo"],
    };

    [Fact]
    public async Task Cada_constructor_arma_el_request_de_su_fuente_y_solo_con_identificacion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteConRuntAsync(api, empresa, ct);

        var porRpc = new Dictionary<string, int>();
        await using var scope = OutboxDePrueba.Scope(api, empresa.Id);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var constructores = scope.ServiceProvider.GetServices<IConstructorSolicitud>().ToList();
        var vehiculos = await db.Set<VehiculoDiagnostico>().AsNoTracking().Where(v => v.DiagnosticoId == lote).ToDictionaryAsync(v => v.Id, ct);
        foreach (var celda in await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote).OrderBy(c => c.VehiculoId).ThenBy(c => c.Fuente).ToListAsync(ct))
        {
            var esperadas = Esperadas(vehiculos[celda.VehiculoId], celda.Fuente);
            var constructor = constructores.Should().ContainSingle(c => c.Fuente == celda.Fuente, $"hay un constructor para {celda.Fuente}").Subject;

            var construidas = await constructor.ConstruirAsync(db, celda, ct);

            construidas.Select(s => s.Rpc).Should().Equal(esperadas.Select(h => h.Rpc), $"mismos rpc en {celda.Fuente} de {vehiculos[celda.VehiculoId].IdentificadorNorm}");
            construidas.Select(s => s.Solicitud).Should().Equal(esperadas.Select(h => h.Solicitud), (a, b) => a.Equals(b), $"mismo *Request en {celda.Fuente}");
            foreach (var solicitud in construidas)
            {
                Campos(solicitud.Solicitud).Should().BeSubsetOf(CamposPermitidos[solicitud.Rpc], $"{solicitud.Rpc} solo lleva identificación");
                porRpc[solicitud.Rpc] = porRpc.GetValueOrDefault(solicitud.Rpc) + 1;
            }
        }

        porRpc.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            [RpcConsultas.PorPlaca] = 2,
            [RpcConsultas.PorVin] = 1,
            // RUNT persona: los propietarios naturales de TST001 y del VIN y el locatario de TST003; RUES: el propietario NIT de TST003.
            [RpcConsultas.Conductor] = 3,
            [RpcConsultas.Rues] = 1,
            // SIMIT por placa (3 vehículos) y por documento (3 propietarios y 1 locatario).
            [RpcConsultas.Multas] = 7,
            [RpcConsultas.Avaluos] = 3,
            [RpcConsultas.Impuesto] = 3,
        }, "cada fuente del alcance tiene su solicitud en los tres vehículos");
    }

    // HU #14153 AC4 (RUNT persona) + HU #14183 T3 (SIMIT) — RUNT_PROPIETARIO, RUNT_LOCATARIO y las tres fuentes SIMIT llevan
    // incluir_respuesta_cruda=true; RUNT del vehículo, avalúo e impuesto NO (sin la opción).
    [Fact]
    public async Task HU14183_SIMIT_y_RUNT_persona_piden_la_respuesta_cruda_y_el_resto_no()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteConRuntAsync(api, empresa, ct);
        await using var scope = OutboxDePrueba.Scope(api, empresa.Id);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var constructores = scope.ServiceProvider.GetServices<IConstructorSolicitud>().ToList();

        var porFuente = new List<(Fuente Fuente, string Rpc, bool Crudo)>();
        foreach (var celda in await db.Set<CeldaConsulta>().Where(c => c.DiagnosticoId == lote).ToListAsync(ct))
        {
            foreach (var s in await constructores.Single(c => c.Fuente == celda.Fuente).ConstruirAsync(db, celda, ct))
                porFuente.Add((celda.Fuente, s.Rpc, Opciones(s.Solicitud)?.IncluirRespuestaCruda == true));
        }

        // HU #14183 T3: las tres fuentes SIMIT también piden el crudo del proveedor.
        var fuentesConCrudo = new[]
        {
            Fuente.RuntPropietario, Fuente.RuntLocatario,
            Fuente.SimitVehiculo, Fuente.SimitPropietario, Fuente.SimitLocatario,
        };
        porFuente.Where(p => p.Crudo).Select(p => p.Fuente).Distinct()
            .Should().BeEquivalentTo(fuentesConCrudo, "RUNT persona y todas las fuentes SIMIT piden el crudo");
        porFuente.Where(p => p.Fuente is Fuente.SimitVehiculo or Fuente.SimitPropietario or Fuente.SimitLocatario)
            .Should().OnlyContain(p => p.Crudo, "toda solicitud SIMIT pide incluir_respuesta_cruda");
        porFuente.Where(p => p.Fuente is Fuente.RuntPropietario or Fuente.RuntLocatario)
            .Should().OnlyContain(p => p.Crudo, "toda solicitud de RUNT persona la pide");
        porFuente.Where(p => p.Fuente is Fuente.Runt or Fuente.Avaluo or Fuente.ImpuestoDepartamental)
            .Should().NotBeEmpty().And.OnlyContain(p => !p.Crudo, "RUNT vehículo, avalúo e impuesto NO piden crudo");
    }

    private static OpcionesConsulta? Opciones(IMessage solicitud) => solicitud switch
    {
        ConsultarConductorRequest r => r.Opciones,
        ConsultarRuesRequest r => r.Opciones,
        ConsultarMultasRequest r => r.Opciones,
        ConsultarAvaluosRequest r => r.Opciones,
        ConsultarImpuestoVehicularRequest r => r.Opciones,
        _ => null,
    };

    [Fact]
    public async Task Una_celda_de_persona_solo_pide_las_personas_que_no_respondieron()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteConRuntAsync(api, empresa, ct);
        await using var scope = OutboxDePrueba.Scope(api, empresa.Id);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.DiagnosticoId == lote && v.IdentificadorNorm == "TST003", ct);
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.VehiculoId == vehiculo.Id && c.Fuente == Fuente.SimitLocatario, ct);
        var locatario = (await DocumentosVehiculo.PersonasDelVehiculoAsync(db, empresa.Id, vehiculo.Id, ct, RolPersona.Locatario)).Single();
        var constructor = scope.ServiceProvider.GetServices<IConstructorSolicitud>().Single(c => c.Fuente == Fuente.SimitLocatario);

        (await constructor.ConstruirAsync(db, celda, ct)).Should().ContainSingle().Which.PersonaId.Should().Be(locatario.Id);
        celda.Payload = $$$"""{"personas":[{"persona_id":"{{{locatario.Id}}}","orden":1,"consulta":"SIMIT","estado":"OK","resultado":{}}]}""";

        (await constructor.ConstruirAsync(db, celda, ct)).Should().BeEmpty("la persona que ya respondió no se vuelve a consumir");
    }

    [Fact]
    public async Task SIMIT_VEHICULO_sin_propietario_del_RUNT_lleva_solo_la_placa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await MotorNa.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await LoteConRuntAsync(api, empresa, ct);
        await Motor.EjecutarAsync(api,
            $"DELETE FROM diagnostico.personas_vehiculo p USING diagnostico.vehiculos v WHERE p.vehiculo_id = v.id AND v.diagnostico_id = '{lote}' AND v.identificador_norm = 'TST001'", ct);
        await using var scope = OutboxDePrueba.Scope(api, empresa.Id);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.DiagnosticoId == lote && v.IdentificadorNorm == "TST001", ct);
        var celda = await db.Set<CeldaConsulta>().SingleAsync(c => c.VehiculoId == vehiculo.Id && c.Fuente == Fuente.SimitVehiculo, ct);
        var constructor = scope.ServiceProvider.GetServices<IConstructorSolicitud>().Single(c => c.Fuente == Fuente.SimitVehiculo);

        var solicitud = (await constructor.ConstruirAsync(db, celda, ct)).Should().ContainSingle().Subject;

        solicitud.Solicitud.Should().Be(new ConsultarMultasRequest
        {
            Opciones = new OpcionesConsulta { IncluirRespuestaCruda = true },
            Placa = new Placa { Valor = vehiculo.Placa },
        }, "sin propietario registrado, la placa sola basta y SIMIT igual pide la respuesta cruda");
    }

    /// <summary>
    /// Lote con RUNT y todas las fuentes de D3 para TST001, TST003 y el VIN de prueba; atiende solo los trabajos de la RUNT (las
    /// demás nacen diferidas y se publican al liberarse, sin respuesta todavía) y deja el departamento del impuesto soportado en
    /// los tres vehículos.
    /// </summary>
    internal static async Task<Guid> LoteConRuntAsync(ApiDePrueba api, EmpresaDePrueba empresa, CancellationToken ct)
    {
        var lote = await MotorNa.LoteAsync(api, empresa, [Lotes.Fila("TST001"), Lotes.Fila("TST003"), Lotes.Fila(Vin)],
            [Fuente.Runt, Fuente.SimitVehiculo, Fuente.Avaluo, Fuente.ImpuestoDepartamental],
            [Fuente.RuntPropietario, Fuente.SimitPropietario], [Fuente.RuntLocatario, Fuente.SimitLocatario], ct);
        (await Motor.Bus(api).ProcesarLoteAsync(ct)).Should().Be(3, "la primera vuelta solo atiende las RUNT");
        // Ningún organismo de prueba deriva un departamento: se fija el soportado de la réplica (MotorNa).
        await Motor.EjecutarAsync(api,
            $"UPDATE diagnostico.vehiculos SET departamento_codigo = '{MotorNa.DepartamentoSoportadoFicticio}' WHERE diagnostico_id = '{lote}'", ct);
        return lote;
    }

    /// <summary>
    /// El <c>*Request</c> que enviaba la <c>Fuente*Real</c> de cada celda (antes del bus) para los vehículos del lote de prueba: la
    /// placa y el VIN son los que dejó la RUNT; los documentos y los datos técnicos, los de los fixtures de Intempo.
    /// </summary>
    private static List<(string Rpc, IMessage Solicitud)> Esperadas(VehiculoDiagnostico v, Fuente fuente)
    {
        var tst003 = v.IdentificadorNorm == "TST003";
        var porVin = v.IdentificadorNorm == Vin;
        var opciones = new OpcionesConsulta();
        var conCrudo = new OpcionesConsulta { IncluirRespuestaCruda = true };
        return fuente switch
        {
            Fuente.Runt => porVin
                ? [(RpcConsultas.PorVin, new ConsultarPorVinRequest { Vin = Vin })]
                : [(RpcConsultas.PorPlaca, new ConsultarPorPlacaRequest { Placa = v.IdentificadorNorm })],
            Fuente.RuntPropietario => tst003
                ? [(RpcConsultas.Rues, new ConsultarRuesRequest { Opciones = conCrudo, Nit = Nit })]
                : [(RpcConsultas.Conductor, new ConsultarConductorRequest { Opciones = conCrudo, Documento = new DocumentoIdentidad { Tipo = "CC", Numero = Natural } })],
            Fuente.RuntLocatario => tst003
                ? [(RpcConsultas.Conductor, new ConsultarConductorRequest { Opciones = conCrudo, Documento = new DocumentoIdentidad { Tipo = "CC", Numero = Locatario } })]
                : [],
            // Bug SIMIT por placa (2026-10-27): la placa y el documento del primer propietario del RUNT (para el proveedor que lo exige),
            // sin tipo de persona: el proveedor externo se elige como por placa.
            // HU #14183 T3: las tres fuentes SIMIT usan conCrudo (IncluirRespuestaCruda=true).
            Fuente.SimitVehiculo => [(RpcConsultas.Multas, new ConsultarMultasRequest
            {
                Opciones = conCrudo,
                Placa = new Placa { Valor = v.Placa },
                Documento = tst003 ? new DocumentoIdentidad { Tipo = "NIT", Numero = Nit } : new DocumentoIdentidad { Tipo = "CC", Numero = Natural },
            })],
            Fuente.SimitPropietario => tst003
                ? [(RpcConsultas.Multas, PorDocumento("NIT", Nit, TipoPersona.Juridica, crudo: true))]
                : [(RpcConsultas.Multas, PorDocumento("CC", Natural, TipoPersona.Natural, crudo: true))],
            Fuente.SimitLocatario => tst003 ? [(RpcConsultas.Multas, PorDocumento("CC", Locatario, TipoPersona.Natural, crudo: true))] : [],
            Fuente.Avaluo => [(RpcConsultas.Avaluos, Avaluo(v, tst003))],
            Fuente.ImpuestoDepartamental => [(RpcConsultas.Impuesto, new ConsultarImpuestoVehicularRequest
            {
                Opciones = opciones,
                Placa = new Placa { Valor = v.Placa },
                DepartamentoCodigo = MotorNa.DepartamentoSoportadoFicticio,
            })],
            _ => throw new ArgumentOutOfRangeException(nameof(fuente)),
        };
    }

    private static ConsultarMultasRequest PorDocumento(string tipo, string numero, TipoPersona tipoPersona, bool crudo = false) => new()
    {
        Opciones = crudo ? new OpcionesConsulta { IncluirRespuestaCruda = true } : new OpcionesConsulta(),
        Documento = new DocumentoIdentidad { Tipo = tipo, Numero = numero },
        TipoPersona = tipoPersona,
    };

    /// <summary>Avalúo: placa y VIN de la RUNT; el leasing de prueba solo trae el modelo, la persona natural todos los datos técnicos.</summary>
    private static ConsultarAvaluosRequest Avaluo(VehiculoDiagnostico v, bool soloModelo)
    {
        var request = new ConsultarAvaluosRequest
        {
            Opciones = new OpcionesConsulta(),
            Modelo = 2020,
            Cilindraje = soloModelo ? 0 : 1598,
            Combustible = soloModelo ? string.Empty : "GASOLINA",
            Pasajeros = soloModelo ? 0 : 5,
        };
        if (!string.IsNullOrWhiteSpace(v.Placa))
            request.Placa = new Placa { Valor = v.Placa };
        if (!string.IsNullOrWhiteSpace(v.Vin))
            request.Vin = new Flit.Platform.Grpc.V1.Vin { Valor = v.Vin };
        return request;
    }

    /// <summary>Rutas de los campos presentes en el proto-JSON del mensaje (<c>documento.numero</c>, <c>placa.valor</c>…).</summary>
    internal static IEnumerable<string> Campos(IMessage mensaje)
    {
        using var json = System.Text.Json.JsonDocument.Parse(JsonFormatter.Default.Format(mensaje));
        return Rutas(json.RootElement, string.Empty).ToList();

        static IEnumerable<string> Rutas(System.Text.Json.JsonElement elemento, string prefijo)
        {
            foreach (var propiedad in elemento.EnumerateObject())
            {
                var ruta = prefijo.Length == 0 ? propiedad.Name : $"{prefijo}.{propiedad.Name}";
                yield return ruta;
                if (propiedad.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var hija in Rutas(propiedad.Value, ruta))
                        yield return hija;
                }
            }
        }
    }
}
