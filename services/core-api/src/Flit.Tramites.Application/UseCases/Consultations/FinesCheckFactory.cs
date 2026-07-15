namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// FEATURE 05 — fábrica compartida de los checks de comparendos.
///
/// Los TRES proveedores de comparendos (verifik_simit, flit_fines, kyverum_fines) DEBEN construir
/// sus checks aquí: las claves son un contrato implícito con el gate de RADICACIÓN.
/// <see cref="ProcedureInstances.WizardStateQuery"/> deriva <c>simit_multas</c> de la clave
/// <c>{prefijo}_multas</c> (el prefijo lo añade RunProviderAsync por actor, p. ej.
/// <c>simit_comprador_multas</c>). Cambiar <see cref="KeyMultas"/> aquí rompe la radicación de
/// traspaso EN SILENCIO — ningún test de este proyecto falla si la clave deja de coincidir, solo
/// los de amarre de WizardStateHandlerTests.
///
/// AC5 del Feature: tener comparendos NO bloquea CREAR el trámite. Por eso <see cref="Multas"/>
/// emite <c>warn</c> (amarillo) y nunca <c>fail</c> (rojo). El gate de radicación al OT no cambia:
/// se deriva de la presencia de la clave, no de la severidad.
///
/// Trabaja sobre primitivas y no sobre el DTO de un proveedor concreto: cada mapper extrae los
/// datos de su propia respuesta y llama aquí, de modo que la forma del JSON de un proveedor no
/// contamina a los demás.
/// </summary>
public static class FinesCheckFactory
{
    /// <summary>Clave del check de multas. Ver nota de contrato en la doc de la clase.</summary>
    public const string KeyMultas = "multas";

    /// <summary>Clave del check de acuerdos de pago. NO debe terminar en <c>_multas</c>.</summary>
    public const string KeyAcuerdos = "acuerdos_pago";

    public const string LabelMultas = "Multas SIMIT";
    public const string LabelAcuerdos = "Acuerdos de pago SIMIT";

    private const string Ok = "ok";
    private const string Warn = "warn";
    private const string Unknown = "unknown";

    private const string Green = "green";
    private const string Yellow = "yellow";
    private const string Red = "red";

    /// <summary>
    /// Check de multas. Sin pendientes → <c>ok</c>. Con pendientes → <c>warn</c> (AC5: advierte,
    /// no bloquea la creación).
    /// </summary>
    public static ConsultationCheck Multas(string provider, int pendientes, decimal totalPagar) =>
        pendientes <= 0
            ? new ConsultationCheck(KeyMultas, LabelMultas, Ok, provider, null)
            : new ConsultationCheck(KeyMultas, LabelMultas, Warn, provider,
                $"{pendientes} multa(s) pendiente(s) por ${totalPagar:N0} COP");

    /// <summary>Check de acuerdos de pago. Sin acuerdos activos → <c>ok</c>; con acuerdos → <c>warn</c>.</summary>
    public static ConsultationCheck Acuerdos(string provider, int activos, decimal pendiente) =>
        activos <= 0
            ? new ConsultationCheck(KeyAcuerdos, LabelAcuerdos, Ok, provider, null)
            : new ConsultationCheck(KeyAcuerdos, LabelAcuerdos, Warn, provider,
                $"{activos} acuerdo(s) activo(s) por ${pendiente:N0} COP");

    /// <summary>
    /// El proveedor respondió pero sin datos utilizables (cuerpo vacío o sin la sección esperada).
    /// Dato ausente no crítico → <c>unknown</c>: no bloquea y no impide el verde global.
    /// </summary>
    public static ConsultationCheck SinDatos(string provider, string key, string label) =>
        new(key, label, Unknown, provider, "Sin datos SIMIT");

    /// <summary>
    /// Overall de un resultado de comparendos. Alineado con
    /// <see cref="ProcedureInstances.RunPreflightHandler.ComposeOverall"/>: los comparendos ya no
    /// producen <c>fail</c>, de modo que este mapa nunca devuelve rojo por multas — solo por un
    /// <c>error</c> de transporte, que construye el propio proveedor.
    /// </summary>
    public static string ComputeOverall(IReadOnlyList<ConsultationCheck> checks)
    {
        if (checks.Any(c => string.Equals(c.Status, "fail", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(c.Status, "error", StringComparison.OrdinalIgnoreCase)))
            return Red;

        if (checks.Any(c => string.Equals(c.Status, Warn, StringComparison.OrdinalIgnoreCase)))
            return Yellow;

        if (checks.Any(c => string.Equals(c.Status, Ok, StringComparison.OrdinalIgnoreCase)))
            return Green;

        return Yellow;
    }
}
