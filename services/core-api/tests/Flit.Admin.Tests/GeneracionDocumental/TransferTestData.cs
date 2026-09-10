using Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Datos SINTÉTICOS de los tres escenarios, compartidos por los tests de HU #12207 y #12208. Ningún
/// dato es real: nombres inventados, NIT y cédulas de prueba, placa que no corresponde a ningún
/// vehículo del entorno.
///
/// <para><b>El comando por defecto declara «ninguna condición especial aplica»</b> (VB-07). No es
/// decoración: desde HU-06 la declaración de régimen es un gate previo y su ausencia bloquea, así
/// que un payload sin ella ya no es un payload válido.</para>
/// </summary>
internal static class TransferTestData
{
    public static TransferVehicleInput Vehiculo(string placa = "ABC123", string clase = "AUTOMOVIL") => new(
        Placa: placa,
        Marca: "MARCA DE PRUEBA",
        Linea: "LINEA DE PRUEBA",
        ModeloAnio: "2020",
        ClaseVehiculo: clase,
        TipoCarroceria: "SEDAN",
        Color: "BLANCO",
        NoMotor: "MOT0000001",
        NoChasis: "CHA0000001",
        NoSerie: "SER0000001",
        Servicio: "PARTICULAR",
        NoLicenciaTransito: "11223344",
        OrganismoTransito: "SECRETARIA DE MOVILIDAD DE PRUEBA");

    public static TransferPartyInput Transferente(string documento = "900123456") => new(
        TipoPersona: "PJ",
        NombreRazonSocial: "EMPRESA TRANSFERENTE SAS",
        TipoDoc: "NIT",
        NumeroDoc: documento,
        DigitoVerificacion: null,
        Domicilio: "CIUDAD DE PRUEBA",
        RepresentanteLegal: "REPRESENTANTE DE PRUEBA",
        CcRepresentanteLegal: "10000001");

    public static TransferPartyInput Adquirente(string documento = "10000002") => new(
        TipoPersona: "PN",
        NombreRazonSocial: "PERSONA ADQUIRENTE DE PRUEBA",
        TipoDoc: "CC",
        NumeroDoc: documento,
        DigitoVerificacion: null,
        Domicilio: "OTRA CIUDAD DE PRUEBA",
        RepresentanteLegal: null,
        CcRepresentanteLegal: null);

    public static TransferBusinessInput Negocio(
        string titulo = TransferJuridicalTitle.Compraventa,
        string? precioLetras = "VEINTE MILLONES DE PESOS",
        string? precioNumeros = "20.000.000") => new(
        TituloJuridico: titulo,
        DescripcionTitulo: null,
        PrecioLetras: precioLetras,
        PrecioNumeros: precioNumeros,
        ContraprestacionDescripcion: null,
        FormaPago: "Transferencia electronica al momento de la entrega",
        AsumeRetencionFuente: "TRANSFERENTE",
        AsumeDerechosTramite: "COMPARTIDOS",
        AsumeImpuestoVehiculo: "ADQUIRENTE",
        CiudadFirma: "CIUDAD DE PRUEBA",
        FechaFirma: new DateOnly(2026, 9, 9));

    /// <summary>
    /// Declaración de régimen aplicable (§4.0). Por defecto, la respuesta que permite generar.
    /// </summary>
    public static RegimenDeclarationInput Regimen(
        bool? ningunaAplica = true,
        IReadOnlyList<string>? condiciones = null) => new(
        NingunaAplica: ningunaAplica,
        CondicionesDeclaradas: condiciones ?? [],
        DeclaredAt: new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Antecedente de leasing válido del escenario B (§5.5). Sin precio: no existe.
    ///
    /// <para><b>El nombre del destinatario no contiene la palabra «LOCATARIO»</b> a propósito. La
    /// verificación de §9.2 busca el RÓTULO <c>LOCATARIO</c> en el bloque de firmas; con una razón
    /// social que llevara esa palabra, la aserción más amplia —la de todo el documento— fallaría por
    /// el nombre y no por un rótulo, y habría que debilitarla justo donde interesa que sea fuerte.</para>
    /// </summary>
    public static TransferLeasingInput Leasing(
        bool esEntidadFinanciera = true,
        string? contrato = "LSG-2020-000123",
        string? tipoOpcion = TransferPurchaseOption.Ejercida,
        string? locatarioNombre = "COMPANIA DESTINATARIA DE PRUEBA SAS",
        string? locatarioTipoDoc = "NIT",
        string? locatarioNoDoc = "901555444") => new(
        TransferenteEsEntidadFinanciera: esEntidadFinanciera,
        NoContratoLeasing: contrato,
        TipoOpcionCompra: tipoOpcion,
        FechaTerminacion: new DateOnly(2026, 8, 31),
        LocatarioNombre: locatarioNombre,
        LocatarioTipoDoc: locatarioTipoDoc,
        LocatarioNoDoc: locatarioNoDoc);

    /// <summary>
    /// Negocio del escenario B: <b>el que no hay</b>. Solo ciudad y fecha de firma —sin título
    /// jurídico, sin precio y sin catálogos fiscales—, porque el acto unilateral no los tiene.
    /// </summary>
    public static TransferBusinessInput NegocioSinPrecio() => new(
        TituloJuridico: null,
        DescripcionTitulo: null,
        PrecioLetras: null,
        PrecioNumeros: null,
        ContraprestacionDescripcion: null,
        FormaPago: null,
        AsumeRetencionFuente: null,
        AsumeDerechosTramite: null,
        AsumeImpuestoVehiculo: null,
        CiudadFirma: "CIUDAD DE PRUEBA",
        FechaFirma: new DateOnly(2026, 9, 9));

    /// <summary>Comando válido de escenario B (unilateral de leasing, art. 5.3.2.2).</summary>
    public static GenerateTransferenciaCommand ComandoEscenarioB(
        TransferLeasingInput? leasing = null,
        TransferBusinessInput? negocio = null,
        TransferPartyInput? transferente = null,
        RegimenDeclarationInput? regimen = null) => Comando(
        escenarios: [TransferScenario.UnilateralLeasing],
        transferente: transferente ?? Transferente(),
        // El adquirente NO viaja en el escenario B: no hay parte que reciba y firme.
        sinAdquirente: true,
        negocio: negocio ?? NegocioSinPrecio(),
        regimen: regimen ?? Regimen(),
        leasing: leasing ?? Leasing());

    /// <summary>Comando válido de escenario C (financiera a tercero, art. 5.3.2.1 sin exenciones).</summary>
    public static GenerateTransferenciaCommand ComandoEscenarioC(
        TransferPartyInput? adquirente = null,
        TransferLeasingInput? leasing = null) => Comando(
        escenarios: [TransferScenario.FinancieraATercero],
        adquirente: adquirente ?? Adquirente(),
        leasing: leasing);

    /// <summary>Comando válido de escenario A. Cada test muta solo lo que quiere romper.</summary>
    public static GenerateTransferenciaCommand Comando(
        Guid? tenantId = null,
        Guid? userId = null,
        IReadOnlyList<string>? escenarios = null,
        TransferVehicleInput? vehiculo = null,
        TransferPartyInput? transferente = null,
        TransferPartyInput? adquirente = null,
        TransferBusinessInput? negocio = null,
        TransferEncumbranceInput? gravamen = null,
        RegimenDeclarationInput? regimen = null,
        TransferLeasingInput? leasing = null,
        bool sinAdquirente = false,
        string? idempotencyKey = null) => new(
        tenantId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
        userId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"),
        escenarios ?? [TransferScenario.TraspasoOrdinario],
        vehiculo ?? Vehiculo(),
        transferente ?? Transferente(),
        adquirente ?? (sinAdquirente ? null : Adquirente()),
        negocio ?? Negocio(),
        gravamen,
        regimen ?? Regimen(),
        leasing,
        idempotencyKey);

    /// <summary>
    /// Modelo ya resuelto del <b>escenario B</b>: UNA parte —la entidad financiera— y ningún
    /// negocio. El locatario vive en <c>Leasing</c> y por eso su bloque de firma no puede existir.
    /// </summary>
    public static TransferDocumentModel ModeloEscenarioB(
        TransferDocumentLeasing? leasing = null,
        string tipoOpcion = TransferPurchaseOption.Ejercida) => Modelo() with
    {
        Scenario = TransferScenario.UnilateralLeasing,
        Partes =
        [
            new TransferDocumentParty(
                TransferPartyRole.Transferente, "EMPRESA TRANSFERENTE SAS", "NIT", "900123456", "8",
                "CIUDAD DE PRUEBA", "REPRESENTANTE DE PRUEBA", "10000001",
                TransferPartyRole.EtiquetaEntidadFinanciera),
        ],
        Negocio = null,
        Leasing = leasing ?? new TransferDocumentLeasing(
            "LSG-2020-000123",
            tipoOpcion,
            new DateOnly(2026, 8, 31),
            "COMPANIA DESTINATARIA DE PRUEBA SAS",
            "NIT",
            "901555444"),
    };

    /// <summary>Modelo ya resuelto del <b>escenario C</b>: dos partes con los rótulos del §9.3.</summary>
    public static TransferDocumentModel ModeloEscenarioC() => Modelo() with
    {
        Scenario = TransferScenario.FinancieraATercero,
        Partes =
        [
            new TransferDocumentParty(
                TransferPartyRole.Transferente, "EMPRESA TRANSFERENTE SAS", "NIT", "900123456", "8",
                "CIUDAD DE PRUEBA", "REPRESENTANTE DE PRUEBA", "10000001",
                TransferPartyRole.EtiquetaTransferenteFinanciero),
            new TransferDocumentParty(
                TransferPartyRole.Adquirente, "PERSONA ADQUIRENTE DE PRUEBA", "CC", "10000002", null,
                "OTRA CIUDAD DE PRUEBA", null, null, TransferPartyRole.EtiquetaAdquirenteTercero),
        ],
    };

    /// <summary>Modelo ya resuelto de escenario A, para probar el generador sin pasar por el handler.</summary>
    public static TransferDocumentModel Modelo(
        IReadOnlyList<TransferDocumentParty>? partes = null,
        string clase = "AUTOMOVIL") => new(
        TransferScenario.TraspasoOrdinario,
        TransferSignatureMode.Manuscrita,
        new TransferDocumentVehicle(
            "ABC123", "MARCA DE PRUEBA", "LINEA DE PRUEBA", "2020", clase, "SEDAN", "BLANCO",
            "MOT0000001", "CHA0000001", "SER0000001", "PARTICULAR", "11223344",
            "SECRETARIA DE MOVILIDAD DE PRUEBA"),
        partes ??
        [
            new TransferDocumentParty(
                TransferPartyRole.Transferente, "EMPRESA TRANSFERENTE SAS", "NIT", "900123456", "8",
                "CIUDAD DE PRUEBA", "REPRESENTANTE DE PRUEBA", "10000001"),
            new TransferDocumentParty(
                TransferPartyRole.Adquirente, "PERSONA ADQUIRENTE DE PRUEBA", "CC", "10000002", null,
                "OTRA CIUDAD DE PRUEBA"),
        ],
        new TransferDocumentBusiness(
            TransferJuridicalTitle.Compraventa,
            TransferJuridicalTitle.Wording(TransferJuridicalTitle.Compraventa),
            null,
            "VEINTE MILLONES DE PESOS",
            "20.000.000",
            null,
            "Transferencia electronica al momento de la entrega",
            "TRANSFERENTE",
            "COMPARTIDOS",
            "ADQUIRENTE"),
        new TransferDocumentEncumbrance(false, false),
        "CIUDAD DE PRUEBA",
        new DateOnly(2026, 9, 9),
        "GD-0a1b2c3d");
}
