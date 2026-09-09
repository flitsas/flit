namespace Flit.Admin.Application.GeneracionDocumental.Ports;

/// <summary>
/// Rol de una parte dentro del documento. Es también el <b>rótulo del bloque de firma</b> cuando esa
/// parte firma (anexo §9.1 y §9.3).
/// </summary>
public static class TransferPartyRole
{
    public const string Transferente = "TRANSFERENTE";

    public const string Adquirente = "ADQUIRENTE";

    /// <summary>
    /// Rótulo del único bloque de firma del escenario B (anexo §9.2). No es un rol nuevo: la entidad
    /// financiera comparece como TRANSFERENTE y este es el rótulo con el que el anexo la nombra.
    /// </summary>
    public const string EtiquetaEntidadFinanciera = "LA ENTIDAD FINANCIERA";

    /// <summary>Rótulos del escenario C (anexo §9.3), que distinguen quién es quién.</summary>
    public const string EtiquetaTransferenteFinanciero = "TRANSFERENTE (entidad financiera)";

    public const string EtiquetaAdquirenteTercero = "ADQUIRENTE (tercero)";
}

/// <summary>Las 13 variables de vehículo del anexo §5.1, ya resueltas a texto.</summary>
public sealed record TransferDocumentVehicle(
    string Placa,
    string? Marca,
    string? Linea,
    string? ModeloAnio,
    string? ClaseVehiculo,
    string? TipoCarroceria,
    string? Color,
    string? NoMotor,
    string? NoChasis,
    string? NoSerie,
    string? Servicio,
    string? NoLicenciaTransito,
    string? OrganismoTransito);

/// <summary>
/// Una parte compareciente, con su rol. El <see cref="Rol"/> es lo que decide el rótulo del bloque
/// de firma; el generador no infiere «el primero es el vendedor».
/// </summary>
public sealed record TransferDocumentParty(
    string Rol,
    string NombreRazonSocial,
    string TipoDoc,
    string NumeroDoc,
    string? DigitoVerificacion = null,
    string? Domicilio = null,
    string? RepresentanteLegal = null,
    string? CcRepresentanteLegal = null,
    string? RolEtiqueta = null)
{
    /// <summary>Rótulo visible del bloque de firma. Por defecto, el propio rol.</summary>
    public string Etiqueta => string.IsNullOrWhiteSpace(RolEtiqueta) ? Rol : RolEtiqueta;

    /// <summary>Identificación en una línea: <c>NIT No. 900123456, DV 8</c>.</summary>
    public string Identificacion => string.IsNullOrWhiteSpace(DigitoVerificacion)
        ? $"{TipoDoc} No. {NumeroDoc}"
        : $"{TipoDoc} No. {NumeroDoc}, DV {DigitoVerificacion}";
}

/// <summary>
/// Variables del negocio ya resueltas (anexo §5.4), incluidas las tres fiscales que imprime la
/// cláusula SEXTA de §8.1.
/// </summary>
public sealed record TransferDocumentBusiness(
    string TituloJuridico,
    string TituloRedaccion,
    string? DescripcionTitulo,
    string? PrecioLetras,
    string? PrecioNumeros,
    string? ContraprestacionDescripcion,
    string? FormaPago,
    string AsumeRetencionFuente,
    string AsumeDerechosTramite,
    string? AsumeImpuestoVehiculo);

/// <summary>
/// Antecedente de leasing del escenario B (anexo §5.5 y §8.2, cláusulas segunda, tercera y quinta).
///
/// <para><b>El locatario vive aquí y no en <c>Partes</c>.</b> Es una decisión estructural, no de
/// comodidad: <c>Partes</c> es la lista de firmantes y el locatario no firma (§9.2). Si estuviera
/// en <c>Partes</c> con una bandera «no firma», el bloque de su firma existiría y bastaría un
/// descuido en la cascada para pintarlo —exactamente lo que §10 regla #4 prohibe—. Sus datos sí
/// aparecen en las cláusulas declarativas, que es donde el anexo los quiere.</para>
/// </summary>
public sealed record TransferDocumentLeasing(
    string NoContrato,
    string TipoOpcionCompra,
    DateOnly? FechaTerminacion,
    string LocatarioNombre,
    string? LocatarioTipoDoc,
    string LocatarioNoDoc)
{
    /// <summary>Identificación del locatario en una línea, para la cláusula segunda.</summary>
    public string LocatarioIdentificacion => string.IsNullOrWhiteSpace(LocatarioTipoDoc)
        ? $"documento No. {LocatarioNoDoc}"
        : $"{LocatarioTipoDoc} No. {LocatarioNoDoc}";
}

/// <summary>Declaración de gravamen que alimenta el inciso final de la cláusula segunda (§8.1).</summary>
public sealed record TransferDocumentEncumbrance(
    bool GravamenActivo,
    bool TieneLevantamientoOAutorizacion);

/// <summary>
/// Payload completo y ya resuelto que alimenta al generador del Documento de Transferencia de
/// Dominio. Es también, campo por campo, lo que se congela en <c>document_snapshot</c> (CF-26).
///
/// <para><b><see cref="Partes"/> es una lista, no un par transferente/adquirente.</b> El anexo
/// §9.0.3 lo exige: «el escenario decide cuántos bloques de firma existen; el modo de firma solo
/// decide qué va dentro de un bloque que ya existe». En los escenarios A y C la lista trae dos
/// partes; en el <b>escenario B trae exactamente una</b> —la entidad financiera— y el bloque del
/// adquirente/locatario no puede instanciarse ni por descuido, porque no hay nada que instanciar:
/// no existe un campo opcional que dejar en blanco ni una columna que ocultar.</para>

/// <para><see cref="Negocio"/> es anulable por la misma razón: el escenario B <b>no tiene negocio
/// con precio</b> (§10 regla #3, VB-B-05), y un objeto de negocio vacío invitaría a imprimir una
/// contraprestación que el acto unilateral no tiene.</para>
/// </summary>
public sealed record TransferDocumentModel(
    string Scenario,
    string SignatureMode,
    TransferDocumentVehicle Vehiculo,
    IReadOnlyList<TransferDocumentParty> Partes,
    TransferDocumentBusiness? Negocio,
    TransferDocumentEncumbrance Gravamen,
    string CiudadFirma,
    DateOnly FechaFirma,
    string ReferenceNumber,
    TransferDocumentLeasing? Leasing = null)
{
    public TransferDocumentParty? ParteConRol(string rol) =>
        Partes.FirstOrDefault(p => string.Equals(p.Rol, rol, StringComparison.Ordinal));
}

/// <summary>
/// Puerto del generador del Documento de Transferencia de Dominio (Feature #12201, I2).
///
/// <para><b>No recibe ningún lector de firmas.</b> El modo de firma vigente es <c>MANUSCRITA</c>
/// (anexo §9.0, adenda 15.4 del diseño): líneas en blanco con nombre y documento, sin leyenda de
/// firma electrónica, sin sello del baúl y sin sello de validación de identidad, aunque la parte
/// tenga firma custodiada vigente. La ausencia del puerto es la garantía: el generador no puede
/// estampar lo que no puede consultar.</para>
/// </summary>
public interface IStandaloneTransferGenerator
{
    RenderedStandaloneDocument Render(TransferDocumentModel model);
}
