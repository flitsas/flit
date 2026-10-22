using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>Celda a sembrar: fuente, estado y versión del tablero en que cambió.</summary>
internal sealed record CeldaSembrada(Fuente Fuente, EstadoCelda Estado, long Version);

/// <summary>Vehículo a sembrar con sus celdas; <c>ConLocatario</c> le pone un documento de locatario declarado (cifrado ficticio).</summary>
internal sealed record VehiculoSembrado(string Placa, CeldaSembrada[] Celdas, bool ConLocatario = false);

/// <summary>Lo que quedó en la base: la cabecera, sus vehículos y sus celdas, por placa y fuente.</summary>
internal sealed record LoteSembrado(CabeceraDiagnostico Cabecera, IReadOnlyList<VehiculoDiagnostico> Vehiculos, IReadOnlyList<CeldaConsulta> Celdas)
{
    public Guid Id => Cabecera.Id;

    public CeldaConsulta Celda(string placa, Fuente fuente) =>
        Celdas.Single(c => c.Fuente == fuente && c.VehiculoId == Vehiculos.Single(v => v.IdentificadorNorm == placa).Id);
}

/// <summary>
/// Ayudas del tablero (HU #13743): rutas y siembra directa por el contexto de la empresa (sin pasar por el procesador), con
/// versiones del tablero controladas. Placas ficticias del rango TST###; los documentos son bytes inventados.
/// </summary>
/// <example>
/// <code>
/// var lote = await Tablero.SembrarAsync(empresa, ct, version: 12, vehiculos:
///     [new VehiculoSembrado("TST101", [new(Fuente.Runt, EstadoCelda.Completado, 3)])]);
/// var grilla = await empresa.Cliente().GetAsync(Tablero.RutaGrilla(lote.Id, desde: 10), ct);
/// </code>
/// </example>
internal static class Tablero
{
    public static string RutaGrilla(Guid id, long? desde = null) =>
        $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/grilla" + (desde is { } d ? $"?desde={d}" : string.Empty);

    public static string RutaCierrePanel(Guid id) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/cerrar-panel";

    public static string RutaHistorial(Guid id) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/historial";

    /// <summary>Detalle de una celda (HU #13798): <c>/diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}</c>.</summary>
    public static string RutaDetalle(Guid id, Guid vehiculo, string fuente) =>
        $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{id}/vehiculos/{vehiculo}/consultas/{fuente}";

    /// <summary>
    /// Deja la celda con el estado y el payload dados (HU #13798): COMPLETADO con origen API, o MANUAL con un usuario de
    /// backoffice si <paramref name="manual"/>; ERROR con <paramref name="tipoError"/>.
    /// </summary>
    public static async Task<CeldaConsulta> PayloadAsync(
        EmpresaDePrueba empresa, CeldaConsulta celda, string? payload, CancellationToken ct,
        EstadoCelda estado = EstadoCelda.Completado, string? tipoError = null, bool manual = false)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        ArgumentNullException.ThrowIfNull(celda);
        await using var db = empresa.Db();
        var guardada = await db.Set<CeldaConsulta>().SingleAsync(c => c.Id == celda.Id, ct);
        guardada.Estado = estado;
        guardada.Payload = payload;
        guardada.TipoError = estado == EstadoCelda.Error ? tipoError ?? "TIMEOUT" : null;
        guardada.MotivoNa = estado == EstadoCelda.Na ? MotivoNa.SinDocumentoLocatario : null;
        guardada.Origen = payload is null ? null : manual ? OrigenCelda.Manual : OrigenCelda.Api;
        guardada.UsuarioManualId = manual ? Guid.NewGuid() : null;
        guardada.DatoObtenidoEn = payload is null ? null : DateTimeOffset.UtcNow.AddMinutes(-3);
        await db.SaveChangesAsync(ct);
        return guardada;
    }

    /// <summary>Usuario (claim <c>sub</c>) de un token de prueba.</summary>
    public static Guid Usuario(string token) => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject);

    /// <summary>Copia de configuración mínima de la cabecera con el <c>estimado_consultas</c> dado.</summary>
    public static string Configuracion(string estimado = "NO_MOSTRAR") =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { ["estimado_consultas"] = estimado, ["visibilidad_usuario"] = "EMPRESA" });

    /// <summary>
    /// Siembra un lote con sus vehículos y celdas en la empresa. Las celdas COMPLETADO llevan origen API (o MANUAL con un
    /// usuario de backoffice si <paramref name="manual"/>), payload y fecha del dato; las NA, motivo; las ERROR, tipo de error.
    /// </summary>
    public static async Task<LoteSembrado> SembrarAsync(
        EmpresaDePrueba empresa, CancellationToken ct, long version = 1,
        EstadoDiagnostico estado = EstadoDiagnostico.EnProceso, Guid? creador = null, string estimado = "NO_MOSTRAR",
        Fuente[]? alcance = null, bool manual = false, string nombre = "Lote del tablero", VehiculoSembrado[]? vehiculos = null)
    {
        ArgumentNullException.ThrowIfNull(vehiculos);
        var ahora = DateTimeOffset.UtcNow;
        var cabecera = new CabeceraDiagnostico
        {
            Nombre = nombre,
            Alcance = [.. alcance ?? vehiculos.SelectMany(v => v.Celdas).Select(c => c.Fuente).Prepend(Fuente.Runt).Distinct()],
            Configuracion = Configuracion(estimado),
            Estado = estado,
            VersionTablero = version,
            UsuarioCreadorId = creador ?? Guid.NewGuid(),
            CreadoEn = ahora,
            CerradoEn = estado is EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto ? ahora : null,
        };
        var filas = vehiculos.Select((v, i) => new VehiculoDiagnostico
        {
            DiagnosticoId = cabecera.Id,
            IdentificadorOriginal = v.Placa,
            IdentificadorNorm = v.Placa,
            TipoIdentificador = TipoIdentificador.PlacaCarro,
            Placa = v.Placa,
            Orden = i + 1,
            LocatarioDocTipo = v.ConLocatario ? "C" : null,
            LocatarioDocCifrado = v.ConLocatario ? [1, 2, 3, 4] : null,
            LocatarioDocOrigen = v.ConLocatario ? OrigenDocumento.DeclaradoCliente : null,
        }).ToList();
        var celdas = vehiculos.SelectMany((v, i) => v.Celdas.Select(c => Celda(cabecera.Id, filas[i].Id, c, ahora, manual))).ToList();

        await using var db = empresa.Db();
        db.Add(cabecera);
        db.AddRange(filas);
        db.AddRange(celdas);
        await db.SaveChangesAsync(ct);
        return new LoteSembrado(cabecera, filas, celdas);
    }

    /// <summary>Inserta un intento de la celda (para el consumo frente al estimado).</summary>
    public static async Task IntentoAsync(EmpresaDePrueba empresa, CeldaConsulta celda, bool desdeCache, CancellationToken ct, DisparadorIntento disparador = DisparadorIntento.Auto)
    {
        var ahora = DateTimeOffset.UtcNow;
        await using var db = empresa.Db();
        db.Add(new IntentoConsulta
        {
            DiagnosticoId = celda.DiagnosticoId,
            VehiculoId = celda.VehiculoId,
            ConsultaId = celda.Id,
            Fuente = celda.Fuente,
            Servicio = desdeCache ? null : ServicioConsulta.Intempo,
            Disparador = disparador,
            Inicio = ahora,
            Fin = ahora,
            Estado = ResultadoIntento.Success,
            DesdeCache = desdeCache,
        });
        await db.SaveChangesAsync(ct);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

    /// <summary>Todas las celdas de la respuesta de la grilla, aplanadas.</summary>
    public static List<JsonElement> CeldasDe(JsonElement grilla) =>
        [.. grilla.GetProperty("vehiculos").EnumerateArray().SelectMany(v => v.GetProperty("celdas").EnumerateArray())];

    private static CeldaConsulta Celda(Guid diagnostico, Guid vehiculo, CeldaSembrada c, DateTimeOffset ahora, bool manual) => new()
    {
        DiagnosticoId = diagnostico,
        VehiculoId = vehiculo,
        Fuente = c.Fuente,
        Estado = c.Estado,
        MotivoNa = c.Estado == EstadoCelda.Na ? MotivoNa.SinDocumentoLocatario : null,
        TipoError = c.Estado == EstadoCelda.Error ? "TIMEOUT" : null,
        Origen = c.Estado == EstadoCelda.Completado ? (manual ? OrigenCelda.Manual : OrigenCelda.Api) : null,
        UsuarioManualId = c.Estado == EstadoCelda.Completado && manual ? Guid.NewGuid() : null,
        Payload = c.Estado == EstadoCelda.Completado ? """{"documento_propietario":"99999123"}""" : null,
        DatoObtenidoEn = c.Estado == EstadoCelda.Completado ? ahora.AddMinutes(-5) : null,
        DisponibleDesde = ahora,
        ActualizadoEn = ahora,
        VersionTablero = c.Version,
    };
}
