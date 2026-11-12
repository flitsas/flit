using System.Collections.Concurrent;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Tablero;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TableroPrueba = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Derivacion;

/// <summary>
/// Doble del cliente gRPC de <c>flit.tramites.v1.DerivacionTramitesService</c> (Punto 4, incremento 2): registra cada solicitud
/// con su metadata y responde con <see cref="Responder"/> (por defecto, <c>creado</c> con un id nuevo) o falla con
/// <see cref="Falla"/>. El servicio lo consume por el adaptador real <see cref="ClienteDerivacionTramitesGrpc"/>.
/// </summary>
/// <example>
/// <code>
/// var falso = new DerivacionTramitesFalso { Falla = StatusCode.Unavailable };
/// await using var api = await DerivacionDePrueba.ApiAsync(falso);
/// </code>
/// </example>
public sealed class DerivacionTramitesFalso : DerivacionTramitesService.DerivacionTramitesServiceClient
{
    public ConcurrentQueue<(CrearBorradorDesdeDiagnosticoRequest Solicitud, Metadata? Metadata, DateTime? Deadline)> Llamadas { get; } = new();

    public Func<CrearBorradorDesdeDiagnosticoRequest, CrearBorradorDesdeDiagnosticoResponse> Responder { get; set; } = _ => Creado(Guid.NewGuid(), "TR-000123");

    public StatusCode? Falla { get; set; }

    public static CrearBorradorDesdeDiagnosticoResponse Creado(Guid id, string numero, bool yaExistia = false) => new()
    {
        Creado = new BorradorCreado { ProcedureInstanceId = id.ToString(), Numero = numero, Estado = "borrador", YaExistia = yaExistia },
    };

    public static CrearBorradorDesdeDiagnosticoResponse Rechazo(string codigo, string motivo) => new()
    {
        Rechazo = new RechazoBorrador { Codigo = codigo, Motivo = motivo },
    };

    public override AsyncUnaryCall<CrearBorradorDesdeDiagnosticoResponse> CrearBorradorDesdeDiagnosticoAsync(
        CrearBorradorDesdeDiagnosticoRequest request, CallOptions options)
    {
        Llamadas.Enqueue((request, options.Headers, options.Deadline));
        var respuesta = Falla is { } codigo
            ? Task.FromException<CrearBorradorDesdeDiagnosticoResponse>(new RpcException(new Status(codigo, "falla de prueba")))
            : Task.FromResult(Responder(request));
        return new AsyncUnaryCall<CrearBorradorDesdeDiagnosticoResponse>(respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }

    /// <summary>Trámites que core-api «ya tiene» por referencia externa (incremento 3, reconciliador).</summary>
    public ConcurrentDictionary<string, (Guid Id, string Numero)> PorReferencia { get; } = new();

    /// <summary>Si <c>ObtenerTramitesPorReferencia</c> falla con este status.</summary>
    public StatusCode? FallaReferencias { get; set; }

    public ConcurrentQueue<ObtenerTramitesPorReferenciaRequest> ConsultasReferencia { get; } = new();

    public override AsyncUnaryCall<ObtenerTramitesPorReferenciaResponse> ObtenerTramitesPorReferenciaAsync(
        ObtenerTramitesPorReferenciaRequest request, CallOptions options)
    {
        ConsultasReferencia.Enqueue(request);
        var respuesta = new ObtenerTramitesPorReferenciaResponse();
        foreach (var referencia in request.ExternalRefs)
        {
            if (PorReferencia.TryGetValue(referencia, out var t))
                respuesta.Tramites.Add(new TramitePorReferencia { ExternalRef = referencia, ProcedureInstanceId = t.Id.ToString(), Numero = t.Numero, Estado = "borrador" });
        }

        var tarea = FallaReferencias is { } codigo
            ? Task.FromException<ObtenerTramitesPorReferenciaResponse>(new RpcException(new Status(codigo, "falla de prueba")))
            : Task.FromResult(respuesta);
        return new AsyncUnaryCall<ObtenerTramitesPorReferenciaResponse>(tarea, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}

/// <summary>
/// Ayudas de las pruebas de la derivación (Punto 4, incremento 2): la API con la derivación encendida y el doble del rpc, y la
/// siembra de un vehículo listo para derivar (celdas, organismo, documentos cifrados con el protector real y dictamen).
/// Placas TST4##, documentos ficticios del rango 99999###.
/// </summary>
internal static class DerivacionDePrueba
{
    public const string UrlTramites = "https://tramites.prueba";

    public static async Task<ApiDePrueba> ApiAsync(DerivacionTramitesFalso falso, bool habilitada = true, Dictionary<string, string?>? extra = null) =>
        await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            var configuracion = new Dictionary<string, string?>
            {
                [$"{OpcionesDerivacion.Seccion}:Habilitada"] = habilitada ? "true" : "false",
                [$"{OpcionesDerivacion.Seccion}:UrlTramites"] = UrlTramites,
                [$"{CatalogoTiposTramiteGrpc.ClaveHabilitados}:0"] = "TRASPASO_STANDARD",
                [$"{CatalogoTiposTramiteGrpc.ClaveHabilitados}:1"] = "TRASPASO_UNILATERAL",
            };
            foreach (var (clave, valor) in extra ?? [])
                configuracion[clave] = valor;
            b.Configuration.AddInMemoryCollection(configuracion);
            b.Services.AddSingleton<DerivacionTramitesService.DerivacionTramitesServiceClient>(falso);
            b.Services.AddScoped<IClienteDerivacionTramites, ClienteDerivacionTramitesGrpc>();
        });

    public static string Ruta(Guid lote, Guid vehiculo) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{lote}/vehiculos/{vehiculo}/tramite";

    /// <summary><c>POST /diagnosticos/{id}/tramites</c> (masivo, incremento 3).</summary>
    public static string RutaMasivo(Guid lote) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{lote}/tramites";

    public static string RutaOperaciones(Guid lote) => $"{RutaMasivo(lote)}/operaciones";

    public static string RutaOperacion(Guid lote, Guid operacion) => $"{RutaOperaciones(lote)}/{operacion}";

    public static string RutaReintentar(Guid lote, Guid operacion) => $"{RutaOperacion(lote, operacion)}/reintentar-fallidas";

    /// <summary>Los eventos <c>diagnostico.derivacion.solicitada</c> de la outbox de la empresa (data), en orden.</summary>
    public static async Task<List<(Guid Empresa, Flit.Diagnostico.Api.Modulos.Derivacion.Bus.DerivacionSolicitada Data, string Payload)>> MensajesAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        await using var db = empresa.Db();
        var filas = await db.Set<Flit.Platform.Sdk.Messaging.OutboxMessage>().AsNoTracking()
            .Where(m => m.RoutingKey == Flit.Diagnostico.Api.Modulos.Derivacion.Bus.DerivacionSolicitada.Tipo)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync(ct);
        return [.. filas
            .Select(m => (Sobre: Flit.Platform.Sdk.Messaging.EventEnvelope.FromJson(System.Text.Encoding.UTF8.GetBytes(m.Payload)), m.Payload))
            .Where(x => x.Sobre.TenantId == empresa.Id)
            .Select(x => (x.Sobre.TenantId, x.Sobre.DataAs<Flit.Diagnostico.Api.Modulos.Derivacion.Bus.DerivacionSolicitada>(), x.Payload))];
    }

    /// <summary>
    /// Entrega un <c>diagnostico.derivacion.solicitada</c> al consumidor como el SDK: scope propio, transacción dentro de la estrategia
    /// de ejecución (<c>PlatformConsumer</c>: el contexto usa <c>EnableRetryOnFailure</c> y no admite transacciones fuera de ella),
    /// efecto y guardado; si el efecto lanza, revierte y relanza (la señal de reintento incluida).
    /// </summary>
    public static async Task ConsumirAsync(ApiDePrueba api, Guid empresa, Flit.Diagnostico.Api.Modulos.Derivacion.Bus.DerivacionSolicitada mensaje, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api);
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Flit.Diagnostico.Api.Persistence.DiagnosticoDb>();
        var consumidor = scope.ServiceProvider.GetRequiredService<Flit.Diagnostico.Api.Modulos.Derivacion.Bus.ConsumidorDerivacion>();
        var sobre = new Flit.Platform.Sdk.Messaging.EventEnvelope(
            Guid.CreateVersion7(), Flit.Diagnostico.Api.Modulos.Derivacion.Bus.DerivacionSolicitada.Tipo, 1, DateTimeOffset.UtcNow, empresa, "diagnostico", "corr-prueba",
            System.Text.Json.JsonSerializer.SerializeToElement(mensaje, Flit.Platform.Sdk.Messaging.EventEnvelope.JsonOptions));
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await consumidor.HandleAsync(sobre, mensaje, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>Token de un usuario de la compañía con <c>diagnostico.derivacion.crear</c> y lectura.</summary>
    public static string TokenDerivar(ApiDePrueba api, EmpresaDePrueba empresa) =>
        api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear, Permisos.DerivacionCrear], roles: ["usuario_diagnostico"]);

    /// <summary>
    /// Siembra un lote con un vehículo por placa: RUNT COMPLETADO (o PENDIENTE si no está terminado), organismo del RUNT,
    /// propietario y locatario cifrados con el protector real, datos del vehículo y, si se da, dictamen vigente.
    /// </summary>
    public static async Task<LoteSembrado> SembrarAsync(
        EmpresaDePrueba empresa, CancellationToken ct, ModoDiagnostico modo = ModoDiagnostico.ConAnalisis, string? tipo = "TRASPASO_STANDARD",
        params (string Placa, bool Terminado, string? Veredicto)[] vehiculos)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        var lote = await TableroPrueba.SembrarAsync(empresa, ct, modo: modo, tipoTramiteCodigo: modo == ModoDiagnostico.ConAnalisis ? tipo : null,
            vehiculos: [.. vehiculos.Select(v => new VehiculoSembrado(v.Placa, [new CeldaSembrada(Fuente.Runt, v.Terminado ? EstadoCelda.Completado : EstadoCelda.Pendiente, 1)]))]);
        var protector = empresa.Api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        await using var db = empresa.Db();
        foreach (var (placa, _, veredicto) in vehiculos)
        {
            var vehiculo = await db.Set<VehiculoDiagnostico>().SingleAsync(v => v.DiagnosticoId == lote.Id && v.IdentificadorNorm == placa, ct);
            vehiculo.OrganismoNombre = "STRIA TTO PRUEBA";
            vehiculo.Servicio = "PARTICULAR";
            vehiculo.PropietarioDocTipo = "C";
            vehiculo.PropietarioDocCifrado = protector.Proteger("99999123");
            vehiculo.DatosConsolidados = """{"vehiculo":{"clase":"AUTOMOVIL","marca":"MARCA FICTICIA","modelo":2020}}""";
            vehiculo.RuntConsultadoEn = DateTimeOffset.UtcNow.AddMinutes(-10);
            if (veredicto is not null)
            {
                db.Add(new DictamenVehiculo
                {
                    VehiculoId = vehiculo.Id,
                    Version = 1,
                    Vigente = true,
                    Veredicto = veredicto,
                    Alertamientos = "[]",
                    CatalogoVersion = 1,
                    RedactadoPor = "PLANTILLA",
                    EstadoRedaccion = "PENDIENTE",
                    CreadoEn = DateTimeOffset.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return lote;
    }

    /// <summary>Organismo de la réplica global con el nombre del RUNT sembrado (para <c>organismo_codigo</c>).</summary>
    public static async Task OrganismoAsync(EmpresaDePrueba empresa, CancellationToken ct, string codigo = "99001")
    {
        ArgumentNullException.ThrowIfNull(empresa);
        await using var db = empresa.Db();
        db.Add(new Organismo { Codigo = codigo, Nombre = "Stria Tto Prueba", NombreNorm = "STRIA TTO PRUEBA", DepartamentoCodigo = "99", DepartamentoNombre = "PRUEBA" });
        await db.SaveChangesAsync(ct);
    }

    public static async Task<List<ItemDerivacion>> ItemsAsync(EmpresaDePrueba empresa, Guid vehiculo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        await using var db = empresa.Db();
        return await db.Set<ItemDerivacion>().AsNoTracking().Where(i => i.VehiculoId == vehiculo).ToListAsync(ct);
    }

    public static async Task<VehiculoDiagnostico> VehiculoAsync(EmpresaDePrueba empresa, Guid vehiculo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        await using var db = empresa.Db();
        return await db.Set<VehiculoDiagnostico>().AsNoTracking().SingleAsync(v => v.Id == vehiculo, ct);
    }
}
