using Flit.Ict.Domain.Enums;

namespace Flit.Ict.Domain.Trazabilidad;

/// <summary>Resultado de una etapa del recorrido.</summary>
public static class ResultadoHito
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Espera = "espera";
    public const string Anulado = "anulado";
    public const string Pendiente = "pendiente";
}

/// <summary>
/// Una etapa del recorrido de un pre-trámite.
/// </summary>
/// <param name="Ocurrido">Null cuando la etapa todavía no se alcanzó.</param>
/// <param name="SegundosDesdeAnterior">
/// Tiempo consumido desde la etapa alcanzada anterior. Es EL dato de la pantalla: soporte no
/// pregunta a qué hora pasó cada cosa, pregunta dónde se atascó.
/// </param>
/// <param name="EsTramoMasLento">
/// Lo decide el servidor y no la pantalla, para que la bandeja, el detalle y una eventual
/// exportación coincidan en cuál fue el tramo lento en vez de calcularlo cada una por su cuenta.
/// </param>
public sealed record HitoTrazabilidad(
    string Etapa,
    string Titulo,
    DateTime? Ocurrido,
    long? SegundosDesdeAnterior,
    string Resultado,
    bool EsTramoMasLento,
    string? Mensaje = null);

/// <summary>
/// Tiempos agregados del recorrido, en la misma semántica que calculaba FLIT 1.0 y que se perdió en
/// la migración: todos se miden DESDE LA RECEPCIÓN, no encadenados entre sí.
/// </summary>
public sealed record TiemposRecorrido(
    long? SegundosTotal,
    long? SegundosHastaActivar,
    long? SegundosHastaCrearBorrador,
    long? SegundosSinAvanzar);

/// <summary>Marcas de tiempo crudas de un pre-trámite, tal como salen de la base.</summary>
/// <param name="Ahora">
/// Se inyecta en vez de leerse dentro del cálculo para que el resultado sea determinista y probable.
/// El endpoint pasa la hora del servidor: leer el reloj en el render del navegador ataría la cifra a
/// la zona horaria del usuario.
/// </param>
public sealed record MarcasRecorrido(
    DateTime Recibido,
    DateTime? ValidacionNegocio,
    DateTime? ConsultaFuentes,
    DateTime? BorradorCreado,
    DateTime? Anulado,
    string Estado,
    string? MensajeNovedad,
    DateTime Ahora);

/// <summary>Recorrido completo de un pre-trámite (HU #11816).</summary>
public sealed record RecorridoTramite(
    Guid Id,
    long Numero,
    string? ReferenciaCliente,
    string Placa,
    string? Vin,
    string? TipoTramite,
    string? Operacion,
    Guid ClientTenantId,
    string? Compania,
    string Estado,
    IReadOnlyList<HitoTrazabilidad> Hitos,
    TiemposRecorrido Tiempos,
    string? MensajeNovedad,
    Guid? ProcedureInstanceId,
    string? CodigoOrganismoTransito,
    string? OrganismoTransito);

/// <summary>Lectura del recorrido de un pre-trámite. Solo lectura.</summary>
public interface IRecorridoTramiteQuery
{
    /// <summary>Devuelve null cuando el trámite no existe o no pertenece al tenant consultado.</summary>
    Task<RecorridoTramite?> ConsultarAsync(long numero, Guid? tenantId, CancellationToken ct = default);
}

/// <summary>
/// Construye el recorrido a partir de las marcas crudas.
/// </summary>
/// <remarks>
/// Es una función pura y vive en el dominio a propósito: es la única parte de la HU que se puede
/// probar sin base de datos, y es donde está toda la regla de negocio. El repositorio se limita a
/// leer columnas y a llamar aquí.
/// </remarks>
public static class CalculadoraDeRecorrido
{
    private const string EtapaSinAvanzar = "sin_avanzar";

    public static (IReadOnlyList<HitoTrazabilidad> Hitos, TiemposRecorrido Tiempos) Construir(MarcasRecorrido m)
    {
        ArgumentNullException.ThrowIfNull(m);

        var esAnulado = m.Estado == IctEstado.Anulado;
        var conNovedades = m.Estado == IctEstado.ConNovedades;

        // El esqueleto son las cuatro etapas que FLIT 1.0 cronometraba. Se dibujan SIEMPRE las cuatro,
        // incluso las no alcanzadas: una etapa ausente es información («no llegó a consultar fuentes»),
        // y ocultarla dejaría al analista sin saber cuánto falta del camino.
        var pasos = new List<(string Etapa, string Titulo, DateTime? Ocurrido)>
        {
            (IctEstado.Recibido, "Recibido por la integración", m.Recibido),
            (IctEstado.EnValidacionNegocio, "Validación de negocio", m.ValidacionNegocio),
            (IctEstado.EnValidacionExterna, "Consulta a fuentes externas", m.ConsultaFuentes),
            (IctEstado.BorradorCreado, "Borrador de trámite creado", m.BorradorCreado),
        };

        if (esAnulado)
        {
            // La anulación sustituye al borrador: son desenlaces excluyentes del mismo tramo final.
            pasos[3] = (IctEstado.Anulado, "Anulado", m.Anulado);
        }

        // La novedad se cuelga de la ÚLTIMA etapa alcanzada, que es donde de verdad ocurrió. Pintarla
        // suelta al pie obligaría al analista a adivinar en qué punto se rompió.
        var indiceUltimaAlcanzada = -1;
        for (var i = 0; i < pasos.Count; i++)
        {
            if (pasos[i].Ocurrido is not null)
            {
                indiceUltimaAlcanzada = i;
            }
        }

        var hitos = new List<HitoTrazabilidad>(pasos.Count + 1);
        DateTime? anterior = null;
        var deltas = new List<(int Indice, long Segundos)>();

        for (var i = 0; i < pasos.Count; i++)
        {
            var (etapa, titulo, ocurrido) = pasos[i];

            long? delta = null;
            if (ocurrido is not null && anterior is not null)
            {
                delta = (long)(ocurrido.Value - anterior.Value).TotalSeconds;
                deltas.Add((i, delta.Value));
            }

            string resultado;
            if (ocurrido is null)
            {
                resultado = ResultadoHito.Pendiente;
            }
            else if (esAnulado && i == 3)
            {
                resultado = ResultadoHito.Anulado;
            }
            else if (conNovedades && i == indiceUltimaAlcanzada)
            {
                resultado = ResultadoHito.Error;
            }
            else
            {
                resultado = ResultadoHito.Ok;
            }

            var mensaje = resultado == ResultadoHito.Error ? m.MensajeNovedad : null;
            hitos.Add(new HitoTrazabilidad(etapa, titulo, ocurrido, delta, resultado, false, mensaje));

            if (ocurrido is not null)
            {
                anterior = ocurrido;
            }
        }

        // Un trámite que no ha terminado lleva un tiempo parado que no aparece en ningún delta, porque
        // no hay etapa siguiente contra la que medirlo. Se añade como cierre explícito: es la cifra que
        // convierte «está en validación» en «lleva cuatro horas en validación».
        var terminal = TrazabilidadEstados.EsTerminal(m.Estado);
        long? segundosSinAvanzar = null;
        if (!terminal && anterior is not null)
        {
            segundosSinAvanzar = Math.Max(0, (long)(m.Ahora - anterior.Value).TotalSeconds);
            hitos.Add(new HitoTrazabilidad(
                EtapaSinAvanzar,
                "Sin avanzar desde entonces",
                null,
                segundosSinAvanzar,
                ResultadoHito.Espera,
                false));
        }

        var hitosFinales = MarcarTramoMasLento(hitos, deltas, segundosSinAvanzar);

        var fin = m.BorradorCreado ?? m.Anulado ?? m.Ahora;
        var tiempos = new TiemposRecorrido(
            SegundosTotal: (long)(fin - m.Recibido).TotalSeconds,
            SegundosHastaActivar: Resta(m.ValidacionNegocio, m.Recibido),
            SegundosHastaCrearBorrador: Resta(m.BorradorCreado, m.Recibido),
            SegundosSinAvanzar: segundosSinAvanzar);

        return (hitosFinales, tiempos);
    }

    /// <summary>
    /// Marca un único tramo como el más lento. Empates: gana el primero, para que la pantalla no
    /// resalte dos cosas a la vez y el ojo no tenga que elegir.
    /// </summary>
    private static List<HitoTrazabilidad> MarcarTramoMasLento(
        List<HitoTrazabilidad> hitos, List<(int Indice, long Segundos)> deltas, long? segundosSinAvanzar)
    {
        var indiceGanador = -1;
        var mayor = long.MinValue;

        foreach (var (indice, segundos) in deltas)
        {
            if (segundos > mayor)
            {
                mayor = segundos;
                indiceGanador = indice;
            }
        }

        // La espera actual compite con los tramos ya cerrados: si un trámite lleva cuatro horas parado,
        // ese es el tramo lento, no el minuto que tardó la validación de negocio.
        if (segundosSinAvanzar is { } espera && espera > mayor)
        {
            mayor = espera;
            indiceGanador = hitos.Count - 1;
        }

        // Un único tramo no es «el más lento», es el único: resaltarlo no aporta nada.
        if (indiceGanador < 0 || (deltas.Count + (segundosSinAvanzar is null ? 0 : 1)) < 2)
        {
            return hitos;
        }

        hitos[indiceGanador] = hitos[indiceGanador] with { EsTramoMasLento = true };
        return hitos;
    }

    private static long? Resta(DateTime? fin, DateTime inicio) =>
        fin is null ? null : (long)(fin.Value - inicio).TotalSeconds;
}
