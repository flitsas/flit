namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>Un archivo del trámite solo por identificador, tipo y huella: la referencia al almacenamiento, el nombre y el tamaño los pide core-api por RPC.</summary>
public sealed record AdjuntoRadicado(Guid Id, string Tipo, string Sha256);

/// <summary>
/// <c>data</c> del trabajo <c>tramites.persona_natural.radicacion_solicitada</c> v1 (HU #14049, contrato en
/// <c>contracts/asyncapi/tramites-events.v1.yaml</c>). Es un AVISO: lleva solo identificadores y referencias, NINGÚN dato
/// personal (ni en claro ni cifrado), ningún nombre original de archivo y ningún valor del trámite. La outbox guarda el
/// mensaje tal cual y lo que quede ahí queda para siempre en la base del servicio (Ley 1581); core-api obtiene los datos
/// por PULL con <c>RadicacionService.ObtenerSolicitudParaRadicar</c> (gRPC autenticado y auditado). La empresa y la
/// correlación de la petición viajan en el sobre.
/// </summary>
/// <param name="CorrelationId">Correlación propia de la solicitud (estable); distinta de la de la petición que viaja en el sobre.</param>
public sealed record RadicacionSolicitada(
    Guid SolicitudId,
    string CodigoPublico,
    string TipoTramiteCodigo,
    int DefinicionVersion,
    string DefinicionHuella,
    Guid OrganismoId,
    IReadOnlyList<AdjuntoRadicado> Adjuntos,
    Guid? ComprobantePagoId,
    string IdempotencyKey,
    Guid CorrelationId);
