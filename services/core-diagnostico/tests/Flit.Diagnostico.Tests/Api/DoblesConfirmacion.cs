using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// Espía de <see cref="IEncoladorConsultas"/> (HU #13751): guarda cada entrega y, en el momento de la llamada, si había
/// una transacción abierta y cuántos vehículos del lote veía esa transacción (lo persistido aún sin confirmar). Con
/// <see cref="Fallar"/> lanza, para probar que la entrega va en la misma transacción que el guardado.
/// </summary>
/// <example>
/// <code>
/// var espia = new EncoladorEspia();
/// await using var api = await ApiDePrueba.ConBaseAsync(configurar: b => espia.Registrar(b.Services));
/// </code>
/// </example>
public sealed class EncoladorEspia
{
    private readonly ConcurrentQueue<Entrega> _entregas = new();

    public bool Fallar { get; set; }

    public IReadOnlyList<Entrega> Entregas => [.. _entregas];

    public void Registrar(IServiceCollection servicios) =>
        servicios.AddScoped<IEncoladorConsultas>(sp => new Adaptador(this, sp.GetRequiredService<DiagnosticoDb>()));

    /// <summary>Una llamada al encolador: lo recibido, si había transacción y los vehículos que veía en la base.</summary>
    public sealed record Entrega(Guid DiagnosticoId, IReadOnlyList<VehiculoAEncolar> Vehiculos, bool EnTransaccion, int VehiculosVisibles);

    private sealed class Adaptador(EncoladorEspia espia, DiagnosticoDb db) : IEncoladorConsultas
    {
        public async Task EncolarAsync(Guid diagnosticoId, IReadOnlyList<VehiculoAEncolar> vehiculos, CancellationToken ct)
        {
            var visibles = await db.Set<VehiculoDiagnostico>().CountAsync(v => v.DiagnosticoId == diagnosticoId, ct);
            espia._entregas.Enqueue(new Entrega(diagnosticoId, [.. vehiculos], db.Database.CurrentTransaction is not null, visibles));
            if (espia.Fallar)
                throw new InvalidOperationException("El encolador de prueba falla a propósito.");
        }
    }
}

/// <summary>Proveedor de logs en memoria (HU #13751): cada mensaje formateado, su excepción y su ámbito, para buscar PII.</summary>
public sealed class LogsEnMemoria : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _mensajes = new();

    public IReadOnlyCollection<string> Mensajes => _mensajes;

    public ILogger CreateLogger(string categoryName) => new Registro(_mensajes, categoryName);

    public void Dispose()
    {
    }

    private sealed class Registro(ConcurrentQueue<string> mensajes, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            mensajes.Enqueue($"{categoria} scope {state}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            mensajes.Enqueue($"{categoria} {formatter(state, exception)} {state} {exception}");
    }
}

/// <summary>Ayudas de las pruebas de validar-filas y confirmación (HU #13751).</summary>
internal static class Lotes
{
    public static string RutaValidar(Guid id) => $"{Cabeceras.Ruta}/{id}/carga/validar-filas";

    public static string RutaVehiculos(Guid id) => $"{Cabeceras.Ruta}/{id}/vehiculos";

    /// <summary>Fila del cuerpo en snake_case: identificador y, si se dan, tipo y número del documento del locatario.</summary>
    public static Dictionary<string, object?> Fila(string? identificador, string? tipo = null, string? documento = null, int? fila = null)
    {
        var json = new Dictionary<string, object?>
        {
            ["identificador"] = identificador,
            ["tipo_documento_locatario"] = tipo,
            ["documento_locatario"] = documento,
        };
        if (fila is { } n)
            json["fila"] = n;
        return json;
    }

    public static StringContent CuerpoDeFilas(string propiedad, IEnumerable<Dictionary<string, object?>> filas) =>
        new(JsonSerializer.Serialize(new Dictionary<string, object?> { [propiedad] = filas.ToList() }), System.Text.Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> ConfirmarAsync(HttpClient cliente, Guid id, IEnumerable<Dictionary<string, object?>> filas, CancellationToken ct) =>
        cliente.PostAsync(RutaVehiculos(id), CuerpoDeFilas("vehiculos", filas), ct);

    public static Task<HttpResponseMessage> ValidarAsync(HttpClient cliente, Guid id, IEnumerable<Dictionary<string, object?>> filas, CancellationToken ct) =>
        cliente.PostAsync(RutaValidar(id), CuerpoDeFilas("filas", filas), ct);

    /// <summary>Siembra la configuración y crea una cabecera SIMPLE en CREADO con el alcance dado (RUNT siempre).</summary>
    public static async Task<(HttpClient Cliente, Guid Id)> CrearAsync(
        ApiDePrueba api, EmpresaDePrueba empresa, ConfiguracionEmpresa configuracion, CancellationToken ct, string[]? porLocatario = null, bool sembrar = true)
    {
        if (sembrar)
            await SembrarAsync(empresa, configuracion, ct);
        var cliente = api.Cliente(Cabeceras.TokenUsuario(api, empresa.Id));
        var creada = await cliente.PostAsync(Cabeceras.Ruta, Cuerpo(Cabeceras.Json("Lote a confirmar", "SIMPLE", null, ["RUNT"], porLocatario: porLocatario)), ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created, await creada.Content.ReadAsStringAsync(ct));
        return (cliente, (await creada.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid());
    }

    public static async Task<List<VehiculoDiagnostico>> VehiculosAsync(ApiDePrueba api, Guid empresa, Guid id, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
        return await db.Set<VehiculoDiagnostico>().AsNoTracking().Where(v => v.DiagnosticoId == id).OrderBy(v => v.Orden).ToListAsync(ct);
    }

    /// <summary>Filas de <c>diagnostico.vehiculos</c> de todas las empresas, por SQL directo (sin filtro).</summary>
    public static async Task<long> TodosLosVehiculosAsync(ApiDePrueba api, CancellationToken ct)
    {
        await using var conexion = new Npgsql.NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var consulta = new Npgsql.NpgsqlCommand("SELECT count(*) FROM diagnostico.vehiculos", conexion);
        return (long)(await consulta.ExecuteScalarAsync(ct))!;
    }
}
