using System.Text.Json;

namespace Flit.PersonaNatural.Api.Solicitudes;

// ── Entradas ────────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Abre el trámite. El consentimiento es obligatorio; placa o VIN según el modo de entrada del tipo.</summary>
public sealed record CrearSolicitudRequest(string? TipoCodigo, string? OrganismoId, string? Cedula, string? Placa, string? Vin, bool Consentimiento, string? ConsentimientoVersion);

public sealed record ActorRequest(string? Rol, short? Ordinal, string? TipoDocumento, string? NumeroDocumento, string? Nombre, string? Correo, string? Celular, string? Direccion);

/// <summary><c>Version</c> es la que el cliente leyó: si ya cambió, la escritura responde 409 con la vigente.</summary>
public sealed record ActoresRequest(long? Version, IReadOnlyList<ActorRequest>? Actores);

public sealed record CamposRequest(long? Version, Dictionary<string, JsonElement>? Valores);

public sealed record PrendaRequest(bool TienePrenda, string? Acreedor, string? NitAcreedor);

public sealed record ComercialRequest(long? Version, decimal? ValorComercial, PrendaRequest? Prenda);

public sealed record RequisitosRequest(long? Version, IReadOnlyList<string>? Transformaciones, string? Observaciones);

public sealed record PropietarioRequest(string? TipoDocumento, string? NumeroDocumento);

public sealed record DiagnosticoRequest(PropietarioRequest? Propietario);

// ── Salidas ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Algo que falta: un código estable, a qué se refiere (clave del campo, rol o documento) y en qué paso se corrige.</summary>
public sealed record PendienteDto(string Codigo, string? Referencia, string? Paso);

public sealed record PasoEstadoDto(string Clave, string Titulo, bool Completo, IReadOnlyList<PendienteDto> Pendientes);

/// <summary>Un archivo cargado: solo metadatos (la referencia al almacenamiento no sale del servicio).</summary>
public sealed record AdjuntoDto(Guid Id, string Tipo, string NombreArchivo, string MimeType, long TamanoBytes, string Sha256, DateTimeOffset CreadoEn);

public sealed record ActorEstadoDto(string Rol, short Ordinal, string ValidacionEstado, string? TipoDocumento, string? NumeroDocumento, string? Nombre, string? Correo, string? Celular, string? Direccion);

/// <summary>Qué cambió en la definición del tipo mientras el borrador estaba guardado y qué se hizo con los datos.</summary>
public sealed record DefinicionCambioDto(int VersionAnterior, int VersionNueva, IReadOnlyList<string> CamposDescartados, IReadOnlyList<string> CamposNuevos);

/// <summary>
/// El borrador completo (HU #14047). Los datos personales de <see cref="Actores"/> solo viajan en la rehidratación y al
/// guardar actores, a quien tiene la sesión de ESA solicitud; en las demás escrituras vienen solo rol, posición y estado.
/// </summary>
public sealed record SolicitudDto(
    Guid SolicitudId,
    string CodigoPublico,
    string Estado,
    string TipoTramiteCodigo,
    int DefinicionVersion,
    string DefinicionHuella,
    long Version,
    Guid OrganismoId,
    string OrganismoNombre,
    string? Placa,
    string? Vin,
    IReadOnlyDictionary<string, string> FieldValues,
    DatosSolicitud Datos,
    IReadOnlyList<ActorEstadoDto> Actores,
    IReadOnlyList<AdjuntoDto> Adjuntos,
    IReadOnlyList<PasoEstadoDto> Pasos,
    IReadOnlyList<PendienteDto> Pendientes,
    bool Completo,
    DefinicionCambioDto? DefinicionCambio);

public sealed record SesionCreadaDto(string Token, DateTimeOffset ExpiraEn, Guid SolicitudId, string CodigoPublico, string Estado, string TipoTramiteCodigo, int DefinicionVersion);

public sealed record RequisitoDto(string Codigo, string Descripcion, string Estado);

public sealed record DiagnosticoDto(bool PuedeContinuar, IReadOnlyList<RequisitoDto> Requisitos);

public sealed record CancelacionDto(Guid SolicitudId, string Estado);

// ── Datos del borrador que no son campos del tipo ───────────────────────────────────────────────────────────────────

public sealed record ComercialDatos(decimal? Valor);

public sealed record PrendaDatos(bool Tiene, string? Acreedor, string? NitAcreedor);

public sealed record RequisitosDatos(IReadOnlyList<string> Transformaciones, string? Observaciones);

public sealed record DiagnosticoDatos(DateTimeOffset Fecha, bool PuedeContinuar, IReadOnlyList<RequisitoDto> Requisitos);

/// <summary>
/// Lo que se guarda en <c>solicitud_tramite.datos</c> (jsonb): los bloques que el tipo pide además de los campos. Solo estos;
/// ningún dato personal (esos van cifrados en <c>persona_solicitante</c>).
/// </summary>
public sealed record DatosSolicitud(ComercialDatos? Comercial = null, PrendaDatos? Prenda = null, RequisitosDatos? Requisitos = null, DiagnosticoDatos? Diagnostico = null)
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    public static DatosSolicitud Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new DatosSolicitud();
        try
        {
            return JsonSerializer.Deserialize<DatosSolicitud>(json, Opciones) ?? new DatosSolicitud();
        }
        catch (JsonException)
        {
            return new DatosSolicitud();
        }
    }

    public string Serializar() => JsonSerializer.Serialize(this, Opciones);
}

/// <summary>
/// Estado de la solicitud sin ningún dato personal (HU #14049): lo único que la sesión de solo estado puede sondear tras radicar.
/// <c>NumeroReferencia</c>: referencia del trámite en FLIT 2; null mientras core-api no lo haya creado. No se expone el id interno.
/// </summary>
public sealed record EstadoSolicitudDto(Guid SolicitudId, string CodigoPublico, string Estado, string TipoTramiteCodigo, DateTimeOffset ActualizadoEn, string? NumeroReferencia);
