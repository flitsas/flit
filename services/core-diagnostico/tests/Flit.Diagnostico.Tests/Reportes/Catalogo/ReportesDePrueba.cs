using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Tablero;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>Documento a sembrar en <c>personas_vehiculo</c> (cifrado con el protector real del servicio).</summary>
internal sealed record DocumentoSembrado(string Tipo, string Numero);

/// <summary>
/// Vehículo a sembrar para los reportes: propietarios en su orden, locatario (del RUNT o declarado), veredicto vigente y, si se
/// da <see cref="CifradoIlegible"/>, un propietario más cuyo cifrado no se puede leer.
/// </summary>
internal sealed record VehiculoReporteSembrado(
    string Placa,
    string? Vin = null,
    DocumentoSembrado[]? Propietarios = null,
    DocumentoSembrado? Locatario = null,
    bool LocatarioDeclarado = false,
    string? Veredicto = null,
    bool CifradoIlegible = false);

/// <summary>
/// Ayudas de las pruebas del módulo de reportes con PII (HU #14579, BE-2/BE-3): arranque con el protector de prueba y la captura de
/// logs, rutas con el rango de los últimos 30 días (hora Colombia), tokens y siembra directa por el contexto de la empresa. Placas
/// ficticias del rango RPT###; documentos inventados.
/// </summary>
/// <example>
/// <code>
/// await using var api = await ReportesDePrueba.ArrancarAsync();
/// var lote = await ReportesDePrueba.LoteAsync(empresa, [new("RPT101", Propietarios: [new("CC", "1020304050")])], ct);
/// var pagina = await api.Cliente(ReportesDePrueba.Token(api, empresa.Id)).GetAsync(ReportesDePrueba.Ruta("vehiculos"), ct);
/// </code>
/// </example>
internal static class ReportesDePrueba
{
    public const string Base = Program.Prefijo + "/reportes";

    /// <summary>Hoy en Colombia (UTC−5 fijo).</summary>
    public static DateOnly Hoy => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    public static string Rango(int dias = 30) => $"desde={Hoy.AddDays(-dias):yyyy-MM-dd}&hasta={Hoy:yyyy-MM-dd}";

    /// <summary><c>/reportes/{tipo}{sufijo}?desde&amp;hasta[&amp;consulta]</c>.</summary>
    public static string Ruta(string tipo, string? consulta = null, string sufijo = "") =>
        $"{Base}/{tipo}{sufijo}?{Rango()}" + (string.IsNullOrEmpty(consulta) ? string.Empty : "&" + consulta);

    /// <summary>Arranca la API con <see cref="ProtectorDePrueba"/>, <see cref="CapturaLogs"/> y la configuración extra dada.</summary>
    public static Task<ApiDePrueba> ArrancarAsync(IDictionary<string, string?>? configuracion = null, CapturaLogs? logs = null) =>
        ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.AddSingleton<IProtectorDocumentoPersona>(sp =>
                new ProtectorDePrueba(new ProtectorDocumentoPersona(sp.GetRequiredService<IDataProtectionProvider>())));
            if (configuracion is not null)
                b.Configuration.AddInMemoryCollection(configuracion);
            if (logs is not null)
            {
                b.Logging.SetMinimumLevel(LogLevel.Debug);
                b.Logging.AddFilter((_, _) => true);
                b.Logging.AddProvider(logs);
            }
        });

    public static ProtectorDePrueba Protector(ApiDePrueba api) => (ProtectorDePrueba)api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();

    /// <summary>
    /// Usuario de la compañía: <c>diagnosticos.ver</c> y, según los indicadores, <c>reportes.ver</c>, <c>reportes.descargar</c> y
    /// <c>configuracion.editar</c> (el admin de la compañía, que ve todos los lotes con PROPIOS).
    /// </summary>
    public static string Token(ApiDePrueba api, Guid empresa, Guid? usuario = null, bool ver = true, bool descargar = false, bool admin = false, string[]? roles = null)
    {
        var permisos = new List<string> { Permisos.DiagnosticosVer };
        if (ver)
            permisos.Add(Permisos.ReportesVer);
        if (descargar)
            permisos.Add(Permisos.ReportesDescargar);
        if (admin)
            permisos.Add(Permisos.ConfiguracionEditar);
        return api.Token(empresa: empresa, permisos: permisos, roles: roles ?? [admin ? "admin_diagnostico" : "usuario_diagnostico"], sujeto: usuario?.ToString());
    }

    /// <summary>
    /// Siembra un lote con sus vehículos, una celda RUNT COMPLETADO por vehículo (origen API, o MANUAL si <paramref name="manual"/>), las
    /// personas cifradas y el dictamen vigente.
    /// </summary>
    public static async Task<LoteSembrado> LoteAsync(
        EmpresaDePrueba empresa, VehiculoReporteSembrado[] vehiculos, CancellationToken ct, string nombre = "Lote de reportes",
        Guid? creador = null, bool express = false, ModoDiagnostico modo = ModoDiagnostico.Simple, string? tipoTramite = null, bool manual = false)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        var protector = empresa.Api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-10);
        var cabecera = new CabeceraDiagnostico
        {
            Nombre = nombre,
            Alcance = [Fuente.Runt],
            Configuracion = Tablero.Tablero.Configuracion(),
            Estado = EstadoDiagnostico.Completado,
            VersionTablero = 1,
            UsuarioCreadorId = creador ?? Guid.NewGuid(),
            CreadoEn = ahora,
            CerradoEn = ahora,
            EsExpress = express,
            Modo = modo,
            TipoTramiteCodigo = tipoTramite,
        };
        var filas = new List<VehiculoDiagnostico>();
        var personas = new List<PersonaVehiculo>();
        var celdas = new List<CeldaConsulta>();
        var dictamenes = new List<DictamenVehiculo>();
        for (var i = 0; i < vehiculos.Length; i++)
        {
            var v = vehiculos[i];
            var declarado = v.Locatario is { } loc && v.LocatarioDeclarado ? protector.Proteger(loc.Numero) : null;
            var vehiculo = new VehiculoDiagnostico
            {
                DiagnosticoId = cabecera.Id,
                IdentificadorOriginal = v.Placa,
                IdentificadorNorm = v.Placa,
                TipoIdentificador = TipoIdentificador.PlacaCarro,
                Placa = v.Placa,
                Vin = v.Vin,
                Servicio = "PARTICULAR",
                OrganismoNombre = "STRIA TTO PRUEBA",
                DepartamentoCodigo = "11",
                Orden = i + 1,
                EstadoConsultas = "COMPLETADO",
                LocatarioDocTipo = declarado is null ? null : v.Locatario!.Tipo,
                LocatarioDocCifrado = declarado,
                LocatarioDocOrigen = declarado is null ? null : OrigenDocumento.DeclaradoCliente,
            };
            filas.Add(vehiculo);
            var orden = 1;
            foreach (var p in v.Propietarios ?? [])
                personas.Add(Persona(vehiculo, RolPersona.Propietario, orden++, p.Tipo, protector.Proteger(p.Numero), OrigenDocumento.Proveedor, ahora));
            if (v.CifradoIlegible)
                personas.Add(Persona(vehiculo, RolPersona.Propietario, orden++, "CC", [.. ProtectorDePrueba.Ilegible], OrigenDocumento.Proveedor, ahora));
            if (v.Locatario is { } l)
            {
                personas.Add(Persona(vehiculo, RolPersona.Locatario, 1, l.Tipo, declarado is null ? protector.Proteger(l.Numero) : [.. declarado],
                    v.LocatarioDeclarado ? OrigenDocumento.DeclaradoCliente : OrigenDocumento.Proveedor, ahora));
            }

            celdas.Add(new CeldaConsulta
            {
                DiagnosticoId = cabecera.Id,
                VehiculoId = vehiculo.Id,
                Fuente = Fuente.Runt,
                Estado = EstadoCelda.Completado,
                Origen = manual ? OrigenCelda.Manual : OrigenCelda.Api,
                UsuarioManualId = manual ? Guid.NewGuid() : null,
                Payload = "{}",
                DatoObtenidoEn = ahora.AddMinutes(1),
                DisponibleDesde = ahora,
                ActualizadoEn = ahora,
                VersionTablero = 1,
            });
            if (v.Veredicto is { } veredicto)
            {
                dictamenes.Add(new DictamenVehiculo
                {
                    VehiculoId = vehiculo.Id,
                    Version = 1,
                    Vigente = true,
                    Veredicto = veredicto,
                    Alertamientos = "[]",
                    CatalogoVersion = CatalogoReglasDictamen.Version,
                    RedactadoPor = "PLANTILLA",
                    EstadoRedaccion = "PENDIENTE",
                    CreadoEn = ahora,
                });
            }
        }

        await using var db = empresa.Db();
        db.Add(cabecera);
        db.AddRange(filas);
        db.AddRange(personas);
        db.AddRange(celdas);
        db.AddRange(dictamenes);
        await db.SaveChangesAsync(ct);
        return new LoteSembrado(cabecera, filas, celdas);
    }

    /// <summary>Muchos vehículos sin personas en un lote (topes de exportación): placas RPT00001…</summary>
    public static async Task<Guid> LoteGrandeAsync(EmpresaDePrueba empresa, int vehiculos, CancellationToken ct, Guid? creador = null)
    {
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-10);
        var cabecera = new CabeceraDiagnostico
        {
            Nombre = "Lote grande",
            Alcance = [Fuente.Runt],
            Configuracion = Tablero.Tablero.Configuracion(),
            Estado = EstadoDiagnostico.Completado,
            UsuarioCreadorId = creador ?? Guid.NewGuid(),
            CreadoEn = ahora,
            CerradoEn = ahora,
        };
        await using var db = empresa.Db();
        db.Add(cabecera);
        for (var i = 0; i < vehiculos; i++)
        {
            var placa = $"RPT{i:D5}";
            db.Add(new VehiculoDiagnostico
            {
                DiagnosticoId = cabecera.Id,
                IdentificadorOriginal = placa,
                IdentificadorNorm = placa,
                TipoIdentificador = TipoIdentificador.PlacaCarro,
                Placa = placa,
                Orden = i + 1,
            });
        }

        await db.SaveChangesAsync(ct);
        return cabecera.Id;
    }

    /// <summary>Intento de la celda con el disparador y la duración dados.</summary>
    public static async Task IntentoAsync(EmpresaDePrueba empresa, CeldaConsulta celda, DisparadorIntento disparador, int milisegundos, CancellationToken ct)
    {
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-8);
        await using var db = empresa.Db();
        db.Add(new IntentoConsulta
        {
            DiagnosticoId = celda.DiagnosticoId,
            VehiculoId = celda.VehiculoId,
            ConsultaId = celda.Id,
            Fuente = celda.Fuente,
            Servicio = ServicioConsulta.Intempo,
            Disparador = disparador,
            Inicio = inicio,
            Fin = inicio.AddMilliseconds(milisegundos),
            Estado = ResultadoIntento.Success,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Operación de derivación individual con un ítem en el estado dado (CREADO lleva trámite).</summary>
    public static async Task DerivacionAsync(EmpresaDePrueba empresa, Guid lote, Guid vehiculo, EstadoItemDerivacion estado, CancellationToken ct, string? motivo = null)
    {
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-5);
        var operacion = OperacionDerivacion.Individual(empresa.Id, lote, "TRASPASO_STANDARD", Guid.NewGuid(), ahora);
        var item = ItemDerivacion.Pendiente(operacion, vehiculo, 1, ahora);
        item.Estado = estado;
        item.MotivoCodigo = motivo;
        if (estado == EstadoItemDerivacion.Creado)
        {
            item.TramiteId = Guid.NewGuid();
            item.TramiteNumero = "TR-000123";
            operacion.Creadas = 1;
        }

        await using var db = empresa.Db();
        db.Add(operacion);
        db.Add(item);
        await db.SaveChangesAsync(ct);
    }

    public static async Task<List<AccesoReporte>> AccesosAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<AccesoReporte>().AsNoTracking().OrderBy(a => a.Fecha).ThenBy(a => a.Id).ToListAsync(ct);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

    public static async Task<string?> CodigoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        using var json = JsonDocument.Parse(texto);
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Las filas de la página como diccionarios clave → texto crudo del JSON.</summary>
    public static List<JsonElement> Filas(JsonElement pagina) => [.. pagina.GetProperty("filas").EnumerateArray()];

    public static List<string> Claves(JsonElement pagina) =>
        [.. pagina.GetProperty("columnas").EnumerateArray().Select(c => c.GetProperty("clave").GetString()!)];

    private static PersonaVehiculo Persona(VehiculoDiagnostico v, RolPersona rol, int orden, string tipo, byte[] cifrado, OrigenDocumento origen, DateTimeOffset ahora) => new()
    {
        VehiculoId = v.Id,
        Rol = rol,
        Orden = orden,
        DocTipo = tipo,
        DocCifrado = cifrado,
        Origen = origen,
        CreadoEn = ahora,
    };
}

/// <summary>
/// Protector de documentos de prueba sobre el real: cuenta los descifrados, puede retenerlos con <see cref="Compuerta"/> (exportación
/// lenta) y falla con un <see cref="CryptographicException"/> cuyo mensaje trae un documento cuando el cifrado es <see cref="Ilegible"/>.
/// </summary>
internal sealed class ProtectorDePrueba(IProtectorDocumentoPersona real) : IProtectorDocumentoPersona
{
    public const string DocumentoEnElError = "7788990011";

    public static readonly byte[] Ilegible = [7, 7, 7, 7];

    private int _llamadas;

    public int Llamadas => Volatile.Read(ref _llamadas);

    /// <summary>Si no es null, cada descifrado espera a que se libere (hasta 30 s).</summary>
    public SemaphoreSlim? Compuerta { get; set; }

    /// <summary>Se completa con el primer descifrado.</summary>
    public TaskCompletionSource PrimerDescifrado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public byte[] Proteger(string numero) => real.Proteger(numero);

    public string Desproteger(byte[] cifrado)
    {
        ArgumentNullException.ThrowIfNull(cifrado);
        Interlocked.Increment(ref _llamadas);
        PrimerDescifrado.TrySetResult();
        Compuerta?.Wait(TimeSpan.FromSeconds(30));
        if (cifrado.AsSpan().SequenceEqual(Ilegible))
            throw new CryptographicException($"No se pudo descifrar el documento {DocumentoEnElError}.");
        return real.Desproteger(cifrado);
    }

    public void Reiniciar() => Interlocked.Exchange(ref _llamadas, 0);
}

/// <summary>Proveedor de logs que guarda cada línea (mensaje formateado y excepción completa) para buscar PII.</summary>
internal sealed class CapturaLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lineas = new();

    public IReadOnlyList<string> Lineas => [.. _lineas];

    public ILogger CreateLogger(string categoryName) => new Registrador(categoryName, _lineas);

    public void Dispose()
    {
    }

    private sealed class Registrador(string categoria, ConcurrentQueue<string> lineas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lineas.Enqueue($"{logLevel} {categoria}: {formatter(state, exception)} {exception}");
    }
}
