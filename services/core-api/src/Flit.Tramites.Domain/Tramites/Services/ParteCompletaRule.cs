using System.Collections.Generic;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Regla única de completitud de una parte (comprador/vendedor) en los gates de actor.
/// HU #11593 — antes solo exigía nombre, documento y email; ahora exige también ciudad, dirección
/// y teléfono (los seis datos de contacto), porque sin ellos el organismo de tránsito no puede
/// notificar ni radicar el trámite. Consolida el <c>ParteCompleta</c> que estaba duplicado idéntico
/// en <see cref="TraspasoGates"/> y <see cref="MatriculaGates"/>, y expone la lista de campos
/// faltantes para que el mensaje de bloqueo del gate sea dinámico (no una frase fija).
/// </summary>
public static class ParteCompletaRule
{
    /// <summary>Campos faltantes de la parte, en el orden en que se exigen. Vacía si está completa.</summary>
    public static IReadOnlyList<string> CamposFaltantes(ParteDatos? parte)
    {
        if (parte is null)
            return ["nombre", "documento", "email", "ciudad", "dirección", "teléfono"];

        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(parte.Nombre))
            faltantes.Add("nombre");
        if (string.IsNullOrWhiteSpace(parte.Documento))
            faltantes.Add("documento");
        if (!TramiteDocumento.EmailValido(parte.Email))
            faltantes.Add("email");
        if (string.IsNullOrWhiteSpace(parte.Ciudad))
            faltantes.Add("ciudad");
        if (string.IsNullOrWhiteSpace(parte.Direccion))
            faltantes.Add("dirección");
        if (string.IsNullOrWhiteSpace(parte.Telefono))
            faltantes.Add("teléfono");
        return faltantes;
    }

    /// <summary>La parte tiene los seis datos de contacto (nombre, documento, email, ciudad, dirección, teléfono).</summary>
    public static bool EstaCompleta(ParteDatos? parte) => CamposFaltantes(parte).Count == 0;

    /// <summary>Mensaje de bloqueo dinámico enumerando los campos faltantes reales de <paramref name="rol"/>.</summary>
    public static string MensajeFaltantes(string rol, ParteDatos? parte) =>
        $"Completa {string.Join(", ", CamposFaltantes(parte))} del {rol}";
}
