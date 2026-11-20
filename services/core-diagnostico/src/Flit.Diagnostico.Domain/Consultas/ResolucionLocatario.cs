using Flit.Diagnostico.Domain.Vehiculos;

namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>Qué hacer con el documento del locatario guardado tras la respuesta del RUNT.</summary>
/// <param name="Reemplazar">Verdadero si se guarda el documento que trajo el proveedor en lugar del que había.</param>
/// <param name="Origen">Origen que queda en <c>locatario_doc_origen</c>; null si el vehículo queda sin documento de locatario.</param>
public sealed record DecisionLocatario(bool Reemplazar, OrigenDocumento? Origen);

/// <summary>
/// Precedencia del documento del locatario (HU #13730, F03-C5, R-3, H-INT): si la respuesta del RUNT trae locatario, su
/// documento reemplaza al guardado (el declarado por el cliente en la captura) con origen <see cref="OrigenDocumento.Proveedor"/>;
/// si no lo trae, se conserva el guardado con su origen; sin ninguno de los dos, el vehículo queda sin documento de locatario.
/// El declarado que el proveedor reemplaza no se pierde: queda de respaldo (<see cref="RespaldarDeclarado"/>, HU #13730b).
/// Regla pura: no ve el número del documento.
/// </summary>
/// <example>
/// <code>
/// var decision = ResolucionLocatario.Resolver(vehiculo.LocatarioDocOrigen, proveedorLoTrae: runt.Locatarios.Count > 0);
/// if (decision.Reemplazar) { /* cifrar y guardar el del proveedor con decision.Origen */ }
/// </code>
/// </example>
public static class ResolucionLocatario
{
    /// <param name="guardado">Origen del documento que ya tiene el vehículo; null si no tiene.</param>
    /// <param name="proveedorLoTrae">Verdadero si la respuesta del RUNT trae al menos un locatario con documento.</param>
    public static DecisionLocatario Resolver(OrigenDocumento? guardado, bool proveedorLoTrae) =>
        proveedorLoTrae ? new(true, OrigenDocumento.Proveedor) : new(false, guardado);

    /// <summary>
    /// Verdadero si el documento guardado es el declarado por el cliente y el proveedor lo va a reemplazar: entonces se copia
    /// al respaldo (<c>locatario_declarado_doc_*</c>, decisión del usuario «Guardar el respaldo», 2026-10-21) ANTES de
    /// reemplazarlo. Sin proveedor el declarado sigue vigente y no se duplica; si el guardado ya es del proveedor (una
    /// segunda consulta al RUNT), el respaldo que hubiera no se pisa.
    /// </summary>
    public static bool RespaldarDeclarado(OrigenDocumento? guardado, bool proveedorLoTrae) =>
        proveedorLoTrae && guardado == OrigenDocumento.DeclaradoCliente;
}
