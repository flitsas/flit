namespace Flit.Admin.Application.Plataforma.Mandatos;

/// <summary>
/// Escenario a simular (Feature #11702, HU #11706). Todo lo que NO está aquí —mandante, placa,
/// trámite— sale como marcador visible: el simulador nunca inventa datos de personas.
/// </summary>
/// <param name="OfficeId">Organismo cuya parametrización se quiere ver aplicada.</param>
/// <param name="PersonType">
/// <c>natural</c> | <c>juridica</c> — tipo de persona del MANDANTE. Decide si el contrato lleva el
/// bloque de representante legal, razón social y NIT. No lo infiere el generador: en el trámite sale
/// del actor, y aquí lo elige quien simula.
/// </param>
/// <param name="AssignmentMode">
/// <c>signer</c> | <c>institutional</c> | <c>open</c>. Nulo ⇒ el modo configurado para el organismo.
/// </param>
/// <param name="MandateSignerId">
/// Mandatario concreto. Nulo ⇒ marcador. Solo aplica en modo <c>signer</c>: en institucional no hay
/// bloque de mandatario y en abierto el bloque va con líneas a propósito.
/// </param>
public sealed record MandateSimulationRequest(
    Guid OfficeId,
    string? PersonType,
    string? AssignmentMode,
    Guid? MandateSignerId,
    /// <summary>
    /// Código de catálogo o alias wizard. Nulo ⇒ traspaso. Preferir <see cref="ProcedureTypeCode"/>.
    /// </summary>
    string? Tipologia = null,
    /// <summary>Código de <c>tramites.procedure_types</c> (p. ej. <c>MATRICULA_NUEVA</c>).</summary>
    string? ProcedureTypeCode = null,
    /// <summary><c>ninguna</c> | <c>inscripcion</c> | <c>levantamiento</c> | <c>ambas</c>. Mismo vocabulario que el simulador FUR.</summary>
    string? Prenda = null,
    bool CambioColor = false,
    bool CambioCombustible = false,
    bool CambioCarroceria = false,
    bool Blindaje = false);

/// <summary>
/// Escenario + destinatario del envío.
///
/// <para><b>El envío por correo está OCULTO en la interfaz</b> (decisión de producto del 2026-08-21:
/// el simulador solo sirve para ver cómo queda el documento). El contrato y su implementación se
/// conservan intactos para no perder el trabajo si vuelve a pedirse; hoy no hay ninguna pantalla que
/// los invoque.</para>
/// </summary>
public sealed record MandateSimulationSendRequest(
    Guid OfficeId,
    string? PersonType,
    string? AssignmentMode,
    Guid? MandateSignerId,
    string? ToEmail,
    string? ToName,
    string? Tipologia = null,
    string? ProcedureTypeCode = null,
    string? Prenda = null,
    bool CambioColor = false,
    bool CambioCombustible = false,
    bool CambioCarroceria = false,
    bool Blindaje = false);

/// <summary>Tipologías simulables: lo que cambia es el objeto del contrato.</summary>
public static class MandateSimulationTipologias
{
    public const string MatriculaInicial = "matricula_inicial";
    public const string Traspaso = "traspaso_standard";

    /// <summary>
    /// Acepta tanto el código de tipología (<c>traspaso_standard</c>) como el nombre corto que usa la
    /// pantalla (<c>traspaso</c>). Desconocido o ausente ⇒ traspaso.
    /// </summary>
    public static string Resolve(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        return v is MatriculaInicial or "matricula" ? MatriculaInicial : Traspaso;
    }
}

/// <summary>Códigos de <see cref="MandateSimulationRequest.PersonType"/>.</summary>
public static class MandateSimulationPersonTypes
{
    public const string Natural = "natural";
    public const string Juridica = "juridica";

    /// <summary>Desconocido o ausente ⇒ <see cref="Juridica"/> (el bloque más completo).</summary>
    public static string Resolve(string? value) =>
        string.Equals(value?.Trim(), Natural, StringComparison.OrdinalIgnoreCase) ? Natural : Juridica;

    public static bool IsJuridica(string? value) => Resolve(value) == Juridica;
}

/// <summary>
/// Catálogo CERRADO de desenlaces de una simulación. <see cref="Ok"/> es el único de éxito.
/// </summary>
public enum MandateSimulationOutcome
{
    Ok,

    /// <summary>El organismo no existe o no está dado de alta en FLIT.</summary>
    OfficeNotFound,

    /// <summary>El modo de asignación no pertenece al catálogo.</summary>
    InvalidAssignmentMode,

    /// <summary>El mandatario indicado no existe, está inactivo o no aplica a ese organismo.</summary>
    SignerNotFound,

    /// <summary>Destinatario ausente o con formato inválido. No se intenta enviar.</summary>
    InvalidRecipient,

    /// <summary>
    /// El intento llegó al transporte de correo y este reportó un fallo (ver el catálogo cerrado
    /// <c>EmailSendOutcome</c> del puerto <c>IEmailSender</c>). El motivo llega en lenguaje de negocio,
    /// sin host, credenciales ni el mensaje crudo del proveedor.
    /// </summary>
    SendFailed,
}

/// <summary>PDF simulado listo para abrir o adjuntar.</summary>
public sealed record MandateSimulationResult(
    MandateSimulationOutcome Outcome,
    string Message,
    byte[]? Content = null,
    string? FileName = null)
{
    public bool Success => Outcome == MandateSimulationOutcome.Ok;
}

/// <summary>Un mandatario ofrecible en el simulador para un organismo.</summary>
public sealed record MandateSimulatorSignerOption(
    Guid Id,
    string FullName,
    string DocumentNumber,
    bool IdentityVigente,
    bool TieneFirmaEnBaul);

/// <summary>
/// Simulador de mandatos de Plataforma → Mandatos (HU #11706). Genera el contrato que emitiría un
/// organismo con las condiciones indicadas y lo envía por correo, SIN tocar ningún trámite ni dejar
/// el documento en un expediente.
/// </summary>
public interface IMandateSimulatorService
{
    /// <summary>Mandatarios habilitados en el organismo, para armar el escenario.</summary>
    Task<IReadOnlyList<MandateSimulatorSignerOption>> ListSignersAsync(
        Guid officeId,
        CancellationToken ct = default);

    Task<MandateSimulationResult> PreviewAsync(
        MandateSimulationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Envía la simulación por correo. <b>Sin consumidores en la interfaz</b>: la función quedó oculta
    /// (ver <see cref="MandateSimulationSendRequest"/>).
    /// </summary>
    Task<MandateSimulationResult> SendAsync(
        MandateSimulationSendRequest request,
        CancellationToken ct = default);
}
