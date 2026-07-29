namespace Flit.Ict.Domain.Enums;

/// <summary>
/// Tipos de trámite del contrato de integración v1 (columna <c>transaction_type</c>).
/// Se conservan los códigos numéricos exactos de FLIT 1.0 por compatibilidad con los
/// clientes existentes. El mapeo a los ProcedureType de v2 vive en
/// <c>ict.procedure_type_mapping</c> (no en código).
/// </summary>
public enum TransactionType
{
    Registration = 1,          // Matrícula inicial
    RegistrationLeasing = 2,   // Matrícula leasing
    Transfer = 3,              // Traspaso
    UnilateralTransfer = 4,    // Traspaso unilateral
    ArmoredVehicle = 5,        // Blindaje
    BodyworkChange = 6,        // Cambio de carrocería
    ColorChange = 7,           // Cambio de color
    LesseeChange = 8,          // Cambio de locatario
    FuelConversion = 9,        // Conversión de combustible
    DuplicatePlate = 10,       // Duplicado de placa
    DuplicateCard = 11,        // Duplicado de tarjeta
    RegisterPledge = 12,       // Inscribir prenda
    ReleasePledge = 13,        // Levantar prenda
    RegistrationCancellation = 14, // Cancelación de matrícula
    AccountTransfer = 15,      // Traslado de cuenta
    AccountRegistration = 16,  // Radicado de cuenta
}
