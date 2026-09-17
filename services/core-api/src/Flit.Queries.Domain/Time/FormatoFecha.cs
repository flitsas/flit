using System.Globalization;

namespace Flit.Queries.Domain.Time;

/// <summary>
/// Formato de fecha ÚNICO del producto (Épica #12552, RN-02), para todo lo que una persona lee:
/// exportables de Excel, PDF, documentos generados y reportes.
///
/// <para>
/// Igual que en el frontend, la distinción entre los dos métodos es el fondo del asunto:
/// un INSTANTE lleva hora y se convierte a la hora de Colombia; una FECHA DE CALENDARIO
/// (<see cref="DateOnly"/>) no tiene hora y no se convierte (excepción RN-08).
/// </para>
///
/// <para>
/// <b>Sin segundos</b>, por decisión del negocio: ningún punto de la plataforma los mostraba y no
/// aportan a la lectura operativa de un trámite.
/// </para>
///
/// <para>
/// <b>Esto es presentación, no serialización.</b> No debe usarse para claves de deduplicación, para
/// agrupar series temporales, para variables de plantilla documental ni para la carga útil de una
/// integración: ahí el formato es un contrato y cambiarlo rompe al consumidor (RN-09).
/// </para>
///
/// <para>
/// Los patrones van con <see cref="CultureInfo.InvariantCulture"/> a propósito. Son puramente
/// numéricos, así que el resultado es el mismo en cualquier cultura, y los proyectos compilan con
/// <c>InvariantGlobalization=true</c>: pedir una cultura concreta devolvería la invariante de todos
/// modos, sin aviso.
/// </para>
/// </summary>
public static class FormatoFecha
{
    /// <summary>Patrón de un instante: <c>17/09/2026 14:05</c>.</summary>
    public const string PatronInstante = "dd/MM/yyyy HH:mm";

    /// <summary>Patrón de una fecha de calendario: <c>17/09/2026</c>.</summary>
    public const string PatronCalendario = "dd/MM/yyyy";

    /// <summary>Instante en hora de Colombia, <c>DD/MM/YYYY HH:mm</c>.</summary>
    public static string Instante(DateTimeOffset value) =>
        ColombiaTime.From(value).ToString(PatronInstante, CultureInfo.InvariantCulture);

    /// <summary>
    /// Igual que <see cref="Instante(DateTimeOffset)"/>; devuelve <paramref name="vacio"/> si no hay
    /// valor. El respaldo es la cadena vacía porque en una celda de Excel o de un PDF un guión
    /// tabular se lee como dato.
    /// </summary>
    public static string Instante(DateTimeOffset? value, string vacio = "") =>
        value.HasValue ? Instante(value.Value) : vacio;

    /// <summary>
    /// Fecha de calendario, <c>DD/MM/YYYY</c>, sin hora y sin conversión de zona (RN-08): no hay
    /// hora que mostrar, y convertirla correría el día.
    /// </summary>
    public static string Calendario(DateOnly value) =>
        value.ToString(PatronCalendario, CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Calendario(DateOnly)"/>
    public static string Calendario(DateOnly? value, string vacio = "") =>
        value.HasValue ? Calendario(value.Value) : vacio;
}
