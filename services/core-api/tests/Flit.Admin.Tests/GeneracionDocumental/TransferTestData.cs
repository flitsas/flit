using Flit.Admin.Application.GeneracionDocumental.GenerateTransferencia;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Datos SINTÉTICOS del escenario A compartidos por los tests de HU #12207. Ningún dato es real:
/// nombres inventados, NIT y cédulas de prueba, placa que no corresponde a ningún vehículo del
/// entorno.
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
        string? idempotencyKey = null) => new(
        tenantId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
        userId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"),
        escenarios ?? [TransferScenario.TraspasoOrdinario],
        vehiculo ?? Vehiculo(),
        transferente ?? Transferente(),
        adquirente ?? Adquirente(),
        negocio ?? Negocio(),
        gravamen,
        regimen,
        idempotencyKey);

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
