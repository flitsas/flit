using System.Text.Json;
using Flit.PersonaNatural.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Api.PersonaNatural;

/// <summary>Una parte del trámite tal como la entrega el portal (datos personales en claro: solo viajan por el RPC, nunca por logs ni por el bus).</summary>
internal sealed record ParteSolicitudPortal(
    string Rol, int Ordinal, string TipoDocumento, string NumeroDocumento, string Nombre, string Correo, string Celular, string Direccion,
    Guid? ValidacionId, string ValidacionEstado);

/// <summary>Un archivo por referencia opaca al almacenamiento (los bytes no viajan).</summary>
internal sealed record AdjuntoSolicitudPortal(Guid Id, string Tipo, string Referencia, string Sha256, long TamanoBytes, string MimeType, string NombreArchivo);

/// <summary>Bloques del trámite que no son campos del tipo (<c>datos_json</c> del RPC). Sin datos personales de personas.</summary>
internal sealed record DatosSolicitudPortal(decimal? ValorComercial, PrendaPortal? Prenda)
{
    public static readonly DatosSolicitudPortal Vacios = new(null, null);

    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    /// <summary>Lee <c>datos_json</c>; un JSON ilegible equivale a «sin bloques» (la compuerta del portal ya validó lo que el tipo exige).</summary>
    public static DatosSolicitudPortal Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Vacios;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            decimal? valor = null;
            if (raiz.TryGetProperty("comercial", out var comercial) && comercial.ValueKind == JsonValueKind.Object
                && comercial.TryGetProperty("valor", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
            {
                valor = d;
            }

            PrendaPortal? prenda = null;
            if (raiz.TryGetProperty("prenda", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                prenda = new PrendaPortal(
                    p.TryGetProperty("tiene", out var tiene) && tiene.ValueKind == JsonValueKind.True,
                    Texto(p, "acreedor"),
                    Texto(p, "nitAcreedor"));
            }

            return new DatosSolicitudPortal(valor, prenda);
        }
        catch (JsonException)
        {
            return Vacios;
        }
    }

    private static string? Texto(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
}

internal sealed record PrendaPortal(bool Tiene, string? Acreedor, string? NitAcreedor);

/// <summary>Lo que core-api necesita de una solicitud radicada para crear el trámite (HU #14050).</summary>
internal sealed record SolicitudPortal(
    Guid SolicitudId,
    string CodigoPublico,
    string TipoTramiteCodigo,
    int DefinicionVersion,
    string DefinicionHuella,
    Guid OrganismoId,
    string OrganismoNombre,
    string? Placa,
    string? Vin,
    IReadOnlyDictionary<string, string> Campos,
    DatosSolicitudPortal Datos,
    IReadOnlyList<ParteSolicitudPortal> Partes,
    IReadOnlyList<AdjuntoSolicitudPortal> Adjuntos,
    string IdempotencyKey,
    string CorrelationId);

/// <summary>
/// Resultado de leer una solicitud: la solicitud, o la razón estable por la que no se puede leer
/// (<see cref="NoEncontrada"/> o <see cref="NoRadicable"/>). Cualquier otro fallo (servicio caído, deadline) es una excepción:
/// es técnico y el mensaje se reintenta.
/// </summary>
internal sealed record LecturaSolicitudPortal(SolicitudPortal? Solicitud, string? Razon)
{
    public const string NoEncontrada = "solicitud_no_encontrada";
    public const string NoRadicable = "solicitud_no_radicable";
}

/// <summary>
/// Cliente del RPC <c>flit.personanatural.v1.RadicacionService/ObtenerSolicitudParaRadicar</c> de core-personanatural
/// (HU #14050, ADR-0075 decisión 14). Es un puerto: las pruebas lo reemplazan por un doble.
/// </summary>
internal interface IPersonaNaturalPortalClient
{
    /// <summary>Lee por pull la solicitud en cola. <paramref name="tenantId"/> viaja en <c>x-flit-tenant-id</c> y debe ser la empresa de la solicitud.</summary>
    Task<LecturaSolicitudPortal> ObtenerSolicitudParaRadicarAsync(Guid solicitudId, Guid tenantId, CancellationToken ct);
}

/// <summary>Implementación gRPC con el token de servicio de <c>svc-tramites</c> (audiencia <c>personanatural</c>, scope <c>platform.personanatural.leer</c>).</summary>
internal sealed class GrpcPersonaNaturalPortalClient(RadicacionService.RadicacionServiceClient client) : IPersonaNaturalPortalClient
{
    public async Task<LecturaSolicitudPortal> ObtenerSolicitudParaRadicarAsync(Guid solicitudId, Guid tenantId, CancellationToken ct)
    {
        try
        {
            var r = await client.ObtenerSolicitudParaRadicarAsync(
                new ObtenerSolicitudParaRadicarRequest { SolicitudId = solicitudId.ToString() },
                new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, tenantId.ToString() } },
                cancellationToken: ct).ConfigureAwait(false);
            return new LecturaSolicitudPortal(Mapear(r), null);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound && PlatformRpcErrors.Reason(ex) == LecturaSolicitudPortal.NoEncontrada)
        {
            return new LecturaSolicitudPortal(null, LecturaSolicitudPortal.NoEncontrada);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition && PlatformRpcErrors.Reason(ex) == LecturaSolicitudPortal.NoRadicable)
        {
            return new LecturaSolicitudPortal(null, LecturaSolicitudPortal.NoRadicable);
        }
    }

    internal static SolicitudPortal Mapear(ObtenerSolicitudParaRadicarResponse r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new SolicitudPortal(
            Guid.Parse(r.SolicitudId),
            r.CodigoPublico,
            r.TipoTramiteCodigo,
            r.DefinicionVersion,
            r.DefinicionHuella,
            Guid.Parse(r.OrganismoId),
            r.OrganismoNombre,
            string.IsNullOrWhiteSpace(r.Placa) ? null : r.Placa,
            string.IsNullOrWhiteSpace(r.Vin) ? null : r.Vin,
            new Dictionary<string, string>(r.FieldValues, StringComparer.Ordinal),
            DatosSolicitudPortal.Leer(r.DatosJson),
            [.. r.Partes.Select(p => new ParteSolicitudPortal(
                p.Rol, p.Ordinal, p.TipoDocumento, p.NumeroDocumento, p.Nombre, p.Correo, p.Celular, p.Direccion,
                Guid.TryParse(p.ValidacionId, out var v) ? v : null, p.ValidacionEstado))],
            [.. r.Adjuntos.Select(a => new AdjuntoSolicitudPortal(
                Guid.TryParse(a.Id, out var id) ? id : Guid.Empty, a.Tipo, a.Referencia, a.Sha256, a.TamanoBytes, a.MimeType, a.NombreArchivo))],
            r.IdempotencyKey,
            r.CorrelationId);
    }
}
