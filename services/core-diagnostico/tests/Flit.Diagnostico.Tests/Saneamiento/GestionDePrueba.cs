using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Tablero;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Ayudas de la gestión FLIT del saneamiento (HU #14576, plan §6.2): rutas del backoffice, token del operador FLIT con
/// <c>diagnostico.saneamiento.gestionar</c>, siembra de saneamientos y renglones y lectura de la versión. Placas ficticias SBO###.
/// </summary>
/// <example>
/// <code>
/// var (lote, s) = await GestionDePrueba.SaneamientoAsync(empresa, "SBO101", EstadoSaneamiento.Solicitado, ct);
/// var r = await api.Cliente(GestionDePrueba.TokenFlit(api)).PostAsync(GestionDePrueba.Ruta(s.Id, empresa.Id, "/tomar"), null, ct);
/// </code>
/// </example>
internal static class GestionDePrueba
{
    public const string Base = Program.Prefijo + "/backoffice/saneamientos";

    /// <summary>Un PDF mínimo válido: <c>%PDF-</c> al inicio y <c>%%EOF</c> al final.</summary>
    public static readonly byte[] PdfValido = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    public static string Ruta(Guid sid, Guid empresa, string sufijo = "") => $"{Base}/{sid}{sufijo}?empresa={empresa}";

    public static string RutaRenglon(Guid sid, Guid rid, Guid empresa, string sufijo = "") => $"{Base}/{sid}/renglones/{rid}{sufijo}?empresa={empresa}";

    /// <summary>Operador de la compañía FLIT con backoffice.ver, saneamiento.gestionar y reportes.descargar.</summary>
    public static string TokenFlit(ApiDePrueba api, bool gestionar = true, Guid? usuario = null, string? sujeto = null) =>
        api.Token(
            permisos: gestionar
                ? [Permisos.BackofficeVer, Permisos.SaneamientoGestionar, Permisos.ReportesDescargar]
                : [Permisos.BackofficeVer, Permisos.ReportesDescargar],
            roles: ["admin_diagnostico"], tipoEmpresa: "FLIT", sujeto: sujeto ?? (usuario ?? Guid.NewGuid()).ToString());

    /// <summary>Un lote con un vehículo terminado y su saneamiento en <paramref name="estado"/>.</summary>
    public static async Task<(LoteSembrado Lote, SaneamientoEntidad Saneamiento)> SaneamientoAsync(
        EmpresaDePrueba empresa, string placa, EstadoSaneamiento estado, CancellationToken ct, Guid? solicitante = null, string nombreLote = "Lote del tablero")
    {
        var lote = await Tablero.Tablero.SembrarAsync(empresa, ct, version: 5, nombre: nombreLote, vehiculos:
            [new(placa, [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 2)])]);
        var saneamiento = await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[0].Id, estado, ct, solicitante);
        return (lote, saneamiento);
    }

    /// <summary>Saneamientos SOLICITADO de varios vehículos de UN lote, cada uno con su solicitante.</summary>
    public static async Task<(LoteSembrado Lote, List<SaneamientoEntidad> Saneamientos)> VariosAsync(
        EmpresaDePrueba empresa, string[] placas, Guid[] solicitantes, CancellationToken ct)
    {
        var lote = await SaneamientoDePrueba.LoteAsync(empresa, placas, ct);
        var lista = new List<SaneamientoEntidad>();
        for (var i = 0; i < placas.Length; i++)
            lista.Add(await SaneamientoDePrueba.SembrarAsync(empresa, lote.Id, lote.Vehiculos[i].Id, EstadoSaneamiento.Solicitado, ct, solicitantes[i % solicitantes.Length]));
        return (lote, lista);
    }

    /// <summary>Un renglón no quitado del saneamiento.</summary>
    public static async Task<RenglonSaneamiento> RenglonAsync(
        EmpresaDePrueba empresa, Guid sid, FuenteSaneamiento fuente, ResultadoRenglonSaneamiento resultado, CancellationToken ct, bool automatico = true)
    {
        var renglon = new RenglonSaneamiento
        {
            TenantId = empresa.Id,
            SaneamientoId = sid,
            Fuente = fuente,
            // ck_saneamiento_renglones_hallazgo_fuente: SOAT y RTM solo NO_VIGENTE; SIMIT e impuesto, DEUDA.
            Hallazgo = fuente switch
            {
                FuenteSaneamiento.Otro => null,
                FuenteSaneamiento.Soat or FuenteSaneamiento.Rtm => HallazgoSaneamiento.NoVigente,
                _ => HallazgoSaneamiento.Deuda,
            },
            Descripcion = fuente == FuenteSaneamiento.Otro ? "Embargo registrado" : null,
            Automatico = automatico && fuente != FuenteSaneamiento.Otro,
            Resultado = resultado,
            CreadoPor = Guid.NewGuid(),
            CreadoEn = DateTimeOffset.UtcNow.AddMinutes(-20),
        };
        await using var db = empresa.Db();
        db.Add(renglon);
        await db.SaveChangesAsync(ct);
        return renglon;
    }

    /// <summary>La versión (<c>xmin</c>) vigente del saneamiento.</summary>
    public static async Task<uint> VersionAsync(EmpresaDePrueba empresa, Guid sid, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<SaneamientoEntidad>().AsNoTracking().Where(s => s.Id == sid).Select(s => s.Xmin).SingleAsync(ct);
    }

    public static async Task<uint> VersionRenglonAsync(EmpresaDePrueba empresa, Guid rid, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<RenglonSaneamiento>().AsNoTracking().Where(r => r.Id == rid).Select(r => r.Xmin).SingleAsync(ct);
    }

    public static async Task<SaneamientoEntidad> LeerAsync(EmpresaDePrueba empresa, Guid sid, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<SaneamientoEntidad>().AsNoTracking().SingleAsync(s => s.Id == sid, ct);
    }

    public static async Task<List<EventoSaneamiento>> EventosAsync(EmpresaDePrueba empresa, Guid sid, CancellationToken ct)
    {
        await using var db = empresa.Db();
        return await db.Set<EventoSaneamiento>().AsNoTracking().Where(e => e.SaneamientoId == sid).OrderBy(e => e.OcurridoEn).ThenBy(e => e.Id).ToListAsync(ct);
    }

    public static StringContent Json(object cuerpo) => new(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");

    public static Task<HttpResponseMessage> PostAsync(HttpClient cliente, string ruta, object? cuerpo, CancellationToken ct) =>
        cliente.PostAsync(ruta, cuerpo is null ? null : Json(cuerpo), ct);

    public static Task<HttpResponseMessage> PutAsync(HttpClient cliente, string ruta, object cuerpo, CancellationToken ct) =>
        cliente.PutAsync(ruta, Json(cuerpo), ct);

    /// <summary>Multipart con un archivo en el campo <paramref name="campo"/>.</summary>
    public static MultipartFormDataContent Archivo(byte[] contenido, string nombre = "pago.pdf", string tipo = "application/pdf", string campo = "archivo")
    {
        var multipart = new MultipartFormDataContent();
        var parte = new ByteArrayContent(contenido);
        parte.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo);
        multipart.Add(parte, campo, nombre);
        return multipart;
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
}
