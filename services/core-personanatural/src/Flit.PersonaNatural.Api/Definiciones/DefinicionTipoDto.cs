using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;

namespace Flit.PersonaNatural.Api.Definiciones;

public sealed record CampoDto(string FieldKey, string Etiqueta, string Tipo, bool Obligatorio, IReadOnlyList<string> Opciones, string ValidationSchema);

public sealed record PasoDto(string Clave, string Titulo, IReadOnlyList<string> SectionTypes, IReadOnlyList<CampoDto> Campos);

public sealed record ActorRolDto(string Rol, string Etiqueta, int Min, int Max, bool Biometria, bool Firma, bool PersonaNaturalPermitida);

public sealed record DocumentoRequeridoDto(string DocCode, string Nombre, bool Obligatorio, int Orden, string InstruccionCargue, string Condicion);

public sealed record FlagsTipoDto(
    string EntryMode,
    bool RequiereComercial,
    bool RequierePrenda,
    bool PermiteTransformaciones,
    bool PermitePrendaComplementaria,
    bool RequiereBiometria,
    bool RequiereSolicitudPlaca);

/// <summary>
/// Estructura del wizard de un tipo de trámite tal como la entrega FLIT 2 (HU #14078): solo estructura, sin reglas
/// ejecutables. <see cref="Version"/> es la del tipo y <see cref="Huella"/> la del contenido (la matriz documental puede
/// cambiar sin subir la versión). La solicitud congela ambas y todo lo que se valida después sale de esta estructura.
/// </summary>
public sealed record DefinicionTipoDto(
    string Code,
    string Name,
    string Family,
    int Version,
    string Huella,
    bool Disponible,
    string MotivoNoDisponible,
    IReadOnlyList<PasoDto> Pasos,
    IReadOnlyList<ActorRolDto> Actores,
    IReadOnlyList<DocumentoRequeridoDto> Documentos,
    FlagsTipoDto Flags);

/// <summary>
/// Lo que el BFF le pide a la fachada de core-api para armar un trámite (HU #14078). Interfaz hermana de
/// <see cref="IFachadaTramites"/>: tiene su propio contrato y su propia fachada falsa en las pruebas.
/// </summary>
public interface IFachadaDefiniciones
{
    Task<DefinicionTipoDto> ObtenerDefinicionAsync(string codigo, string? organismoId, CancellationToken ct);
}

internal sealed class FachadaDefinicionesNoConfigurada : IFachadaDefiniciones
{
    public Task<DefinicionTipoDto> ObtenerDefinicionAsync(string codigo, string? organismoId, CancellationToken ct) =>
        throw new FachadaException("servicio_no_disponible", "El catálogo no está disponible por ahora.", StatusCodes.Status503ServiceUnavailable);
}

/// <summary>Cliente del RPC <c>ObtenerDefinicionTipoTramite</c> con el token de servicio y la empresa de Persona Natural.</summary>
internal sealed class FachadaDefinicionesGrpc(PersonaNaturalService.PersonaNaturalServiceClient cliente) : IFachadaDefiniciones
{
    public async Task<DefinicionTipoDto> ObtenerDefinicionAsync(string codigo, string? organismoId, CancellationToken ct)
    {
        var metadata = new Metadata { { Flit.Platform.Sdk.Grpc.PlatformServiceCallInterceptor.TenantMetadata, Flit.Platform.Sdk.PlatformTenants.PersonaNatural.ToString() } };
        var r = await FachadaTramitesGrpc.Llamar(() => cliente.ObtenerDefinicionTipoTramiteAsync(
            new ObtenerDefinicionTipoTramiteRequest { Codigo = codigo, OrganismoId = organismoId ?? string.Empty }, metadata, cancellationToken: ct).ResponseAsync).ConfigureAwait(false);
        var d = r.Definicion;
        return new DefinicionTipoDto(
            d.Codigo, d.Nombre, d.Familia, d.Version, d.Huella, d.DisponiblePn, d.MotivoNoDisponible,
            [.. d.Pasos.Select(p => new PasoDto(p.Clave, p.Titulo, [.. p.SectionTypes],
                [.. p.Campos.Select(c => new CampoDto(c.FieldKey, c.Etiqueta, c.Tipo, c.Obligatorio, [.. c.Opciones], c.ValidationSchema))]))],
            [.. d.Actores.Select(a => new ActorRolDto(a.Rol, a.Etiqueta, a.Min, a.Max, a.Biometria, a.Firma, a.PersonaNaturalPermitida))],
            [.. d.Documentos.Select(x => new DocumentoRequeridoDto(x.DocCode, x.Nombre, x.Obligatorio, x.Orden, x.InstruccionCargue, x.Condicion))],
            new FlagsTipoDto(
                d.Flags.EntryMode, d.Flags.RequiereComercial, d.Flags.RequierePrenda, d.Flags.PermiteTransformaciones,
                d.Flags.PermitePrendaComplementaria, d.Flags.RequiereBiometria, d.Flags.RequiereSolicitudPlaca));
    }
}
