using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// Compone el detalle que acompaña a un check del diagnóstico de requisitos previos.
///
/// <para><b>Por qué existe.</b> Los tres mapeadores de vehículo (Kyverum, Verifik, Intempo) mandaban
/// <c>message = null</c> cuando el resultado era OK, así que la tarjeta del panel quedaba con un
/// título, una pastilla verde y el cuerpo vacío. El dato que la respalda —vencimiento del SOAT,
/// aseguradora, número de póliza, CDA que expidió la revisión— venía en la respuesta del proveedor y
/// se descartaba en el mapeo.</para>
///
/// <para>Para el gestor eso es la diferencia entre «el sistema dice que está bien» y «el RUNT dice
/// que la póliza 12345 de Seguros X vence el 3 de marzo de 2027»: lo segundo lo puede contrastar, y
/// es lo que se le pide cuando el organismo devuelve un trámite.</para>
///
/// <para>Los campos ausentes se omiten en silencio: cada proveedor trae un subconjunto distinto y
/// una consulta real puede no traer ninguno. Si no queda nada, devuelve <c>null</c> y el check se
/// comporta exactamente como antes.</para>
/// </summary>
public static class ConsultationCheckDetail
{
    private const string Separador = " · ";

    /// <summary>
    /// Arma la lista de datos que respaldan el check, omitiendo los que el proveedor no trae.
    ///
    /// <para>Cada par llega como (etiqueta, valor) y se descarta entero si el valor viene vacío: cada
    /// proveedor devuelve un subconjunto distinto, y una etiqueta sin valor en pantalla es peor que
    /// no mostrarla. Lista vacía ⇒ <c>null</c>, para que el check quede exactamente como antes.</para>
    /// </summary>
    public static IReadOnlyList<CheckDato>? Datos(params (string Etiqueta, string? Valor)[] pares)
    {
        ArgumentNullException.ThrowIfNull(pares);

        var datos = pares
            .Select(par => (par.Etiqueta, Valor: Normalizar(par.Valor)))
            .Where(par => par.Valor is not null)
            .Select(par => new CheckDato(par.Etiqueta, par.Valor!))
            .ToList();

        return datos.Count == 0 ? null : datos;
    }

    /// <summary>
    /// Une las partes no vacías con «·». <c>null</c> si no queda ninguna, para que el llamador pueda
    /// asignarlo directamente al mensaje del check sin comprobar nada.
    /// </summary>
    public static string? Componer(params string?[] partes)
    {
        ArgumentNullException.ThrowIfNull(partes);

        var limpias = partes
            .Select(Normalizar)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        return limpias.Count == 0 ? null : string.Join(Separador, limpias);
    }

    /// <summary>
    /// «Etiqueta valor» cuando hay valor; <c>null</c> cuando no. Evita que el llamador tenga que
    /// repetir el condicional por cada campo del proveedor.
    /// </summary>
    public static string? Campo(string etiqueta, string? valor)
    {
        var v = Normalizar(valor);
        return v is null ? null : $"{etiqueta} {v}";
    }

    /// <summary>
    /// Los mismos datos en una sola línea, como respaldo del <c>Message</c>.
    ///
    /// <para><b>Por qué se manda dos veces.</b> El respaldo vivía en <c>Message</c> y funcionaba; al
    /// moverlo a <see cref="CheckDato"/> —para que la pantalla pudiera presentarlo como filas— dejó
    /// de verse, porque un campo NUEVO tiene que atravesar entero el camino hasta el navegador y
    /// cualquier eslabón que mapee campo por campo lo pierde sin avisar. Eso ya pasó una vez.</para>
    ///
    /// <para>Mandar las dos formas cuesta una cadena por check y elimina la clase entera de fallo: la
    /// pantalla usa los datos separados cuando le llegan, y si no, el mensaje de siempre. También
    /// devuelve el respaldo a los expedientes cuyo pre-vuelo se guardó antes de este cambio.</para>
    /// </summary>
    public static string? Resumen(IReadOnlyList<CheckDato>? datos)
    {
        if (datos is null || datos.Count == 0)
            return null;
        return string.Join(Separador, datos.Select(d => $"{d.Etiqueta} {d.Valor}"));
    }

    /// <summary>
    /// Fecha del proveedor en el formato de negocio (<c>AÑO/MES/DÍA</c>, sin hora).
    ///
    /// <para>El RUNT devuelve marcas de tiempo ISO completas —<c>2027-01-23T00:00:00.000-05:00</c>—
    /// y pintarlas crudas es ilegible: milisegundos y desfase horario para una fecha de vencimiento
    /// que se lee de un vistazo. Se usa el MISMO formato que los documentos que genera el sistema
    /// (<see cref="FechaDocumento.Formato"/>), para que el gestor no tenga que traducir entre lo que
    /// ve en el panel y lo que sale impreso.</para>
    ///
    /// <para>Lo que no se puede interpretar como fecha se devuelve tal cual: un proveedor puede
    /// mandar un formato que no conocemos, y perder el dato sería peor que mostrarlo crudo.</para>
    /// </summary>
    public static string? Fecha(string? valor)
    {
        var v = Normalizar(valor);
        if (v is null) return null;
        return DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha.ToString(FechaDocumento.Formato, CultureInfo.InvariantCulture)
            : v;
    }

    /// <summary>
    /// Primer valor no vacío de la lista. Los proveedores mandan el mismo dato con nombres distintos
    /// —y a veces los dos a la vez—, así que el mapeador declara el orden de preferencia.
    /// </summary>
    public static string? Primero(params string?[] valores)
    {
        ArgumentNullException.ThrowIfNull(valores);
        return valores.Select(Normalizar).FirstOrDefault(v => v is not null);
    }

    /// <summary>
    /// Recorta y colapsa los espacios internos. El RUNT devuelve datos sucios —<c>nombreCda</c> llega
    /// con espacio inicial en capturas reales— y esto se va a imprimir en pantalla.
    /// </summary>
    private static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;
        var partes = valor.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var limpio = string.Join(' ', partes);
        return limpio.Length == 0 ? null : limpio;
    }
}
