using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Ejecucion;
using Flit.Impuestos.Tests.Usuarios;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Impuestos.Tests.Reportes;

/// <summary>Una placa sembrada para el dashboard y los reportes (M14): estado, resultado, monto, departamento y propietario.</summary>
internal sealed record ItemSemilla(
    string Placa,
    EstadoItem Estado = EstadoItem.Exitoso,
    string? ResultadoStatus = null,
    decimal? Total = null,
    bool Parcial = false,
    bool ConCallback = false,
    string? Departamento = null,
    string? DepartamentoNombre = null,
    string? PropietarioNombre = null,
    string? DocumentoTipo = null,
    string? DocumentoNumero = null,
    string? ErrorCodigo = null,
    ClaseError? ErrorClase = null,
    string? ErrorMensaje = null,
    string? CampoFaltante = null,
    IReadOnlyList<string>? Vigencias = null);

/// <summary>
/// Entorno de las pruebas de M14 (HU #14694; dashboard y reportes, impuestos.v1.yaml 1.1.0): la API de verdad sobre una base
/// efímera con los dobles de la plataforma de usuarios (nombres de «usuario») y del directorio de compañías (nombres de
/// <c>empresa</c> y del ranking). Siembra cabeceras y placas con su estado y resultado directamente en la base.
/// </summary>
/// <example>
/// <code>
/// var (lote, items) = await EntornoReportes.SembrarAsync(api, empresa, TipoLiquidacion.Masiva, creador, ct, null,
///     new ItemSemilla("ABC123", ResultadoStatus: "con_deuda", Total: 100m));
/// var json = await EntornoReportes.GetJsonAsync(api.Cliente(token), "/api/v1/impuestos/dashboard", ct);
/// </code>
/// </example>
internal static class EntornoReportes
{
    public const string Dashboard = Program.Prefijo + "/dashboard";
    public const string Reportes = Program.Prefijo + "/reportes";
    public const string Catalogos = Program.Prefijo + "/catalogos/reportes";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static readonly string[] Ver = [Permisos.DashboardVer, Permisos.ReportesVer];
    public static readonly string[] Exportar = [Permisos.ReportesVer, Permisos.ReportesExportar];

    public static async Task SembrarEmpresaAsync(ApiDePrueba api, Guid empresa, CancellationToken ct, EstadoAprovisionamiento estado = EstadoAprovisionamiento.Listo) =>
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct, estado);

    /// <summary>Una cabecera con sus placas; los contadores de la cabecera salen de los estados sembrados.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068", Justification = "Las semillas van como params al final para leer la prueba como una tabla.")]
    public static async Task<(Guid Lote, IReadOnlyList<Guid> Items)> SembrarAsync(
        ApiDePrueba api, Guid empresa, TipoLiquidacion tipo, Guid creadaPor, CancellationToken ct, DateTimeOffset? creadaEn, params ItemSemilla[] semillas) =>
        await SembrarAsync(api, empresa, tipo, creadaPor, null, ct, creadaEn, semillas);

    /// <summary>Como la otra sobrecarga, con <c>creada_por_nombre</c> (1.2.0: el nombre del token al crear).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1068", Justification = "Las semillas van como params al final para leer la prueba como una tabla.")]
    public static async Task<(Guid Lote, IReadOnlyList<Guid> Items)> SembrarAsync(
        ApiDePrueba api, Guid empresa, TipoLiquidacion tipo, Guid creadaPor, string? creadaPorNombre, CancellationToken ct, DateTimeOffset? creadaEn,
        params ItemSemilla[] semillas)
    {
        var proteccion = api.App.Services.GetRequiredService<IProteccionImpuestos>();
        await using var db = api.Contexto(empresa);
        var lote = new Liquidacion
        {
            TenantId = empresa,
            Tipo = tipo,
            Estado = semillas.All(s => EstadosItem.EsFinal(s.Estado)) ? EstadoLote.CompletadoConNovedades : EstadoLote.EnProceso,
            TotalItems = semillas.Length,
            CExitosas = semillas.Count(s => s.Estado == EstadoItem.Exitoso),
            CError = semillas.Count(s => s.Estado == EstadoItem.Error),
            CPendientes = semillas.Count(s => !EstadosItem.EsFinal(s.Estado)),
            ConfigCopia = "{\"maxRegistros\":5000,\"departamentosHabilitados\":null,\"concurrencia\":2}",
            CreadaPor = creadaPor,
            CreadaPorNombre = creadaPorNombre,
            CreadaEn = creadaEn ?? DateTimeOffset.UtcNow,
            NombreArchivo = tipo == TipoLiquidacion.Masiva ? "placas.csv" : null,
        };
        db.Add(lote);
        var items = new List<LiquidacionItem>(semillas.Length);
        for (var i = 0; i < semillas.Length; i++)
        {
            var s = semillas[i];
            var item = new LiquidacionItem
            {
                TenantId = empresa,
                LiquidacionId = lote.Id,
                Fila = tipo == TipoLiquidacion.Masiva ? i + 2 : null,
                Placa = s.Placa,
                Estado = s.Estado,
                ResultadoStatus = s.ResultadoStatus,
                TotalAPagar = s.Total,
                Parcial = s.Parcial,
                ConCallback = s.ConCallback,
                DepartamentoCodigo = s.Departamento,
                DepartamentoNombre = s.DepartamentoNombre,
                DocumentoTipo = s.DocumentoTipo,
                DocumentoNumeroCifrado = s.DocumentoNumero is null ? null : proteccion.ProtegerPii(s.DocumentoNumero),
                PropietarioNombreCifrado = s.PropietarioNombre is null ? null : proteccion.ProtegerPii(s.PropietarioNombre),
                ErrorCodigo = s.ErrorCodigo,
                ErrorClase = s.ErrorClase,
                ErrorMensaje = s.ErrorMensaje,
                CampoFaltante = s.CampoFaltante,
                ActualizadoEn = DateTimeOffset.UtcNow,
            };
            items.Add(item);
            foreach (var vigencia in s.Vigencias ?? [])
                db.Add(new LiquidacionPeriodo { ItemId = item.Id, TenantId = empresa, Periodo = vigencia, TotalAPagar = 1m });
        }

        db.AddRange(items);
        await db.SaveChangesAsync(ct);
        return (lote.Id, [.. items.Select(i => i.Id)]);
    }

    public static async Task<JsonElement> GetJsonAsync(HttpClient cliente, string ruta, CancellationToken ct)
    {
        using var r = await cliente.GetAsync(ruta, ct);
        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, $"{ruta}: {texto}");
        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.Clone();
    }

    /// <summary>Las filas de un reporte de la compañía cuya placa está en <paramref name="placas"/>.</summary>
    public static List<JsonElement> Filas(JsonElement pagina, params string[] placas) =>
        [.. pagina.GetProperty("items").EnumerateArray().Where(f => placas.Length == 0 || (f.TryGetProperty("placa", out var p) && placas.Contains(p.GetString())))];

    /// <summary>
    /// Todo el texto de un JSON ya decodificado (nombres de propiedad, cadenas y números, separados por «|»): la respuesta escapa los
    /// caracteres no ASCII (<c>é</c>), así que buscar «Pérez» en el cuerpo crudo no prueba nada.
    /// </summary>
    public static string TextoDe(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var partes = new List<string>();
        void Visitar(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        partes.Add(p.Name);
                        Visitar(p.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray())
                        Visitar(x);
                    break;
                case JsonValueKind.String:
                    partes.Add(e.GetString()!);
                    break;
                default:
                    partes.Add(e.GetRawText());
                    break;
            }
        }

        Visitar(doc.RootElement);
        return string.Join("|", partes);
    }

    public static string Hoy(int menosDias = 0) =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).DateTime).AddDays(-menosDias).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Una API con base para la clase, con los dobles de la plataforma de usuarios y del directorio de compañías (M14).</summary>
public sealed class ApiReportesFixture : IAsyncLifetime
{
    public UsuariosPlataformaDePrueba Plataforma { get; } = new();

    public DirectorioEmpresasDePrueba Directorio { get; } = new();

    public ApiDePrueba Api { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Api = await ApiDePrueba.ConBaseAsync(b =>
    {
        b.Services.AddSingleton<IUsuariosPlataforma>(Plataforma);
        b.Services.AddSingleton<IDirectorioEmpresas>(Directorio);
    });

    public async ValueTask DisposeAsync() => await Api.DisposeAsync();
}
