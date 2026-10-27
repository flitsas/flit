namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>Un archivo del trámite, por referencia: el consumidor lo registra, no recibe los bytes.</summary>
public sealed record AdjuntoRadicado(Guid Id, string Tipo, string NombreArchivo, string MimeType, long TamanoBytes, string Sha256, string Referencia);

/// <summary>Parte del trámite sin dato personal: rol, posición y el estado de su validación de identidad en esta solicitud.</summary>
public sealed record ActorRadicado(string Rol, short Ordinal, string TipoDocumento, Guid? ValidacionId, string ValidacionEstado);

/// <summary>
/// <c>data</c> del trabajo <c>tramites.persona_natural.radicacion_solicitada</c> v1 (HU #14049, contrato en
/// <c>contracts/asyncapi/tramites-events.v1.yaml</c>). Lleva identificadores, referencias y los datos NO personales del
/// trámite. Los datos personales de las partes (documento, nombre, correo, celular, dirección) viajan SOLO dentro de
/// <see cref="ActoresCifrados"/>: la outbox guarda el mensaje tal cual, y un dato personal en claro ahí quedaría para siempre
/// en la base del servicio (Ley 1581).
/// </summary>
/// <param name="ActoresCifrados">Base64 de <c>CifradoPii.Cifrar(json de DatosPartes, "radicacion.actores")</c>; solo lo abre quien comparta la llave <c>Pii:Cifrado</c>.</param>
public sealed record RadicacionSolicitada(
    Guid SolicitudId,
    string CodigoPublico,
    string TipoTramiteCodigo,
    int DefinicionVersion,
    string DefinicionHuella,
    Guid OrganismoId,
    string? Placa,
    string? Vin,
    IReadOnlyDictionary<string, string> FieldValues,
    Solicitudes.DatosSolicitud Datos,
    IReadOnlyList<ActorRadicado> Actores,
    string ActoresCifrados,
    IReadOnlyList<AdjuntoRadicado> Adjuntos,
    Guid? ComprobantePagoId);

/// <summary>Los datos personales de una parte tal como se cifran dentro del evento.</summary>
public sealed record DatosParteRadicada(string Rol, short Ordinal, string TipoDocumento, string Documento, string? Nombre, string? Correo, string? Celular, string? Direccion);
