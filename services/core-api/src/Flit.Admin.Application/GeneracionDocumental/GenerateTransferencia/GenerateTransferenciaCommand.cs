using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;

/// <summary>
/// Orden de emisión del Documento de Transferencia de Dominio SIN trámite
/// (<c>docs/plantilla-transferencia-dominio.md</c>, CF-06/CF-07/CF-09/CF-26). El tenant y el autor
/// salen del JWT, no del cuerpo.
///
/// <para><b><see cref="Scenarios"/> es una lista y no un escalar a propósito.</b> VB-05 exige
/// «exactamente uno de A, B o C»: con un <c>string?</c> el caso «más de uno» sería irrepresentable y
/// el 422 correspondiente jamás podría probarse. La lista deja que el cuerpo declare cero, uno o
/// tres escenarios y que la política los rechace con su código.</para>
///
/// <para>Los tres escenarios del anexo están implementados: A (traspaso ordinario), B
/// (transferencia unilateral de leasing al locatario, art. 5.3.2.2) y C (entidad financiera a un
/// tercero, art. 5.3.2.1 sin exenciones). <see cref="Leasing"/> solo tiene sentido en B —y en C
/// únicamente para el documento del locatario histórico que alimenta VB-C-01—.</para>
/// </summary>
public sealed record GenerateTransferenciaCommand(
    Guid TenantId,
    Guid UserId,
    IReadOnlyList<string> Scenarios,
    TransferVehicleInput? Vehiculo = null,
    TransferPartyInput? Transferente = null,
    TransferPartyInput? Adquirente = null,
    TransferBusinessInput? Negocio = null,
    TransferEncumbranceInput? Gravamen = null,
    RegimenDeclarationInput? RegimenAplicable = null,
    TransferLeasingInput? Leasing = null,
    string? IdempotencyKey = null,
    Guid? BatchId = null,
    int? RowNumber = null)
{
    /// <summary>Escenario efectivo cuando hay exactamente uno; <c>null</c> si hay cero o varios.</summary>
    public string? SingleScenario =>
        Scenarios is { Count: 1 } && TransferScenario.IsKnown(Scenarios[0]) ? Scenarios[0] : null;
}

/// <summary>
/// Las 13 variables de vehículo del anexo §5.1. Todas son texto: el documento transcribe lo que
/// dice el RUNT / la licencia de tránsito, no normaliza ni interpreta.
/// </summary>
public sealed record TransferVehicleInput(
    string? Placa = null,
    string? Marca = null,
    string? Linea = null,
    string? ModeloAnio = null,
    string? ClaseVehiculo = null,
    string? TipoCarroceria = null,
    string? Color = null,
    string? NoMotor = null,
    string? NoChasis = null,
    string? NoSerie = null,
    string? Servicio = null,
    string? NoLicenciaTransito = null,
    string? OrganismoTransito = null);

/// <summary>
/// Una parte del negocio (anexo §5.2 y §5.3). <see cref="DigitoVerificacion"/> se deja entrar por
/// compatibilidad, pero el handler lo RECALCULA cuando el documento es un NIT: un DV tecleado y su
/// NIT no pueden discrepar dentro del mismo PDF.
/// </summary>
public sealed record TransferPartyInput(
    string? TipoPersona = null,
    string? NombreRazonSocial = null,
    string? TipoDoc = null,
    string? NumeroDoc = null,
    string? DigitoVerificacion = null,
    string? Domicilio = null,
    string? RepresentanteLegal = null,
    string? CcRepresentanteLegal = null);

/// <summary>
/// Variables del negocio (anexo §5.4), incluidas las tres fiscales y el modo de firma. El modo de
/// firma NO se captura: es <c>MANUSCRITA</c> fijo (§9.0) y por eso no aparece aquí.
/// </summary>
public sealed record TransferBusinessInput(
    string? TituloJuridico = null,
    string? DescripcionTitulo = null,
    string? PrecioLetras = null,
    string? PrecioNumeros = null,
    string? ContraprestacionDescripcion = null,
    string? FormaPago = null,
    string? AsumeRetencionFuente = null,
    string? AsumeDerechosTramite = null,
    string? AsumeImpuestoVehiculo = null,
    string? CiudadFirma = null,
    DateOnly? FechaFirma = null);

/// <summary>
/// Declaración de gravamen del usuario (VB-A-04, art. 5.3.2.1 num. 3.º). FLIT no consulta el
/// Registro de Garantías Mobiliarias: lo que hay es la declaración de quien genera, y la
/// verificación definitiva la hace el OT.
/// </summary>
public sealed record TransferEncumbranceInput(
    bool GravamenActivo = false,
    bool TieneLevantamientoOAutorizacion = false);

/// <summary>
/// Antecedente de leasing (anexo §5.5). En el <b>escenario B</b> es obligatorio: identifica el
/// contrato, la causal de transferencia y al locatario destinatario del acto unilateral. En el
/// <b>escenario C</b> es opcional y solo se usa para VB-C-01 —comparar el documento del adquirente
/// con el del locatario histórico, si se conoce—.
///
/// <para><b>No hay campo de precio aquí ni lo habrá</b>: el acto del art. 5.3.2.2 es unilateral y
/// no declara precio entre las partes del instrumento (§10 regla #3, VB-B-05).</para>
/// </summary>
public sealed record TransferLeasingInput(
    bool TransferenteEsEntidadFinanciera = false,
    string? NoContratoLeasing = null,
    string? TipoOpcionCompra = null,
    DateOnly? FechaTerminacion = null,
    string? LocatarioNombre = null,
    string? LocatarioTipoDoc = null,
    string? LocatarioNoDoc = null);

/// <summary>
/// Declaración de régimen aplicable (anexo §4.0, CF-24). Es un <b>gate previo</b> a la selección de
/// escenario: hasta que el usuario responda, no hay documento. La evalúa <c>VB-07</c> contra las
/// once condiciones de los arts. 5.3.2.3 a 5.3.2.13, y su resultado —con la fecha— se conserva en
/// <c>input_summary</c> sin incorporar PII (CF-26).
///
/// <para>Los tres campos son anulables a propósito: «no respondió» y «respondió que ninguna aplica»
/// son estados distintos, y solo el segundo permite generar. Un booleano no anulable haría
/// irrepresentable el primero y convertiría el silencio en un sí.</para>
/// </summary>
public sealed record RegimenDeclarationInput(
    bool? NingunaAplica = null,
    IReadOnlyList<string>? CondicionesDeclaradas = null,
    DateTimeOffset? DeclaredAt = null);

/// <summary>Desenlaces de la generación. El endpoint los traduce a códigos HTTP.</summary>
public enum GenerateTransferenciaOutcome
{
    /// <summary>Documento emitido (o recuperado por idempotencia). 200.</summary>
    Generated = 0,

    /// <summary>Cuerpo incompleto o mal formado — no es una validación normativa. 400.</summary>
    InvalidRequest = 1,

    /// <summary>Al menos una VB bloqueante falló. 422 con la lista de códigos.</summary>
    ValidationFailed = 2,

    /// <summary>
    /// Escenario reconocido por el catálogo pero sin generador registrado. Hoy no se produce —A, B y
    /// C están implementados— y se conserva porque un escenario nuevo del anexo debe fallar con un
    /// 422 explícito y no con una excepción a mitad del render.
    /// </summary>
    ScenarioNotImplemented = 3,
}

/// <summary>
/// Respuesta de la generación. <b>Nunca transporta el PDF</b> (decisión del PO): el binario se
/// descarga después por <c>GET /{id}/download</c>.
/// <para><see cref="Advisories"/> son las prevalidaciones VA (anexo §6): viajan en la respuesta 200
/// para que la interfaz las muestre como AVISO. No bloquean nunca.</para>
/// </summary>
public sealed record GenerateTransferenciaResult(
    GenerateTransferenciaOutcome Outcome,
    Guid? Id,
    string? Status,
    IReadOnlyList<TransferValidationIssue> Errors,
    IReadOnlyList<TransferValidationIssue> Advisories,
    string? ErrorField = null);
