using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Datos que los bloques del párrafo 23 ligados al tipo pueden necesitar.
///
/// <para>Es un contexto y no una lista de parámetros porque CADA campo lo usa UN SOLO tipo —la causal
/// es de la cancelación, el destino del radicado y del traslado, la placa del traslado— y la firma
/// venía creciendo con uno nuevo por trámite. Con cinco posicionales opcionales dejaba de leerse
/// quién recibe qué, y equivocarse de orden entre dos <c>string?</c> seguidos no da error de
/// compilación: imprime el dato equivocado en un formulario oficial.</para>
/// </summary>
/// <param name="CancelacionCausal">Causal declarada. Solo la mira <c>CANCELACION_MATRICULA</c>.</param>
/// <param name="OrganismoDestino">
/// Organismo al que va la cuenta. Lo miran <c>RADICADO_CUENTA</c> (donde es el organismo del propio
/// trámite) y <c>TRASLADO_CUENTA</c> (donde es un dato declarado, porque el trámite lo expide el de
/// origen).
/// </param>
/// <param name="Placa">Placa del vehículo. Solo la mira <c>TRASLADO_CUENTA</c>.</param>
public sealed record FurTramiteObservationContext(
    string? CancelacionCausal = null,
    string? OrganismoDestino = null,
    string? Placa = null);

/// <summary>
/// Bloques de observaciones del párrafo 23 ligados al tipo (leasing / unilateral / causal de
/// cancelación / cuenta). Fuente: <c>docs/ot/fur/REGLAS-NUMERAL-3-TRES-CAPAS.md</c>.
/// </summary>
public static class FurTramiteObservation
{
    public static string? Compose(
        string? tipologiaCodigo,
        IReadOnlyList<DocumentParte> partes,
        FurTramiteObservationContext? contexto = null)
    {
        var ctx = contexto ?? new FurTramiteObservationContext();
        var cancelacionCausal = ctx.CancelacionCausal;
        var organismoDestino = ctx.OrganismoDestino;
        var code = tipologiaCodigo?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code is "MATRICULA_LEASING")
            return ComposeLeasing(partes);
        if (code is "TRASPASO_UNILATERAL")
            return ComposeUnilateral(partes);
        if (code is "CAMBIO_LOCATARIO")
            return ComposeCambioLocatario(partes);
        if (code == CancelacionCausales.TipoCodigo)
            return ComposeCancelacion(cancelacionCausal);
        if (code is "RADICADO_CUENTA")
            return ComposeRadicadoCuenta(organismoDestino);
        if (code is "TRASLADO_CUENTA")
            return ComposeTrasladoCuenta(ctx.Placa, organismoDestino);
        return null;
    }

    /// <summary>
    /// Casilla 13: POR QUÉ se cancela la matrícula.
    ///
    /// <para>La casilla es una sola para cuatro trámites que el organismo tramita distinto —lo ordena
    /// un juez, el vehículo se destruyó, o el propietario lo saca de circulación— y el formulario no
    /// tiene dónde distinguirlos salvo aquí. Sin la causal escrita, un expediente por pérdida total
    /// llega idéntico a uno voluntario y el organismo tiene que deducirla de los anexos.</para>
    ///
    /// <para>Sin causal declarada devuelve <c>null</c>: regla del artefacto —faltan datos, sí casilla,
    /// no se inventa el texto—. Escribir una por defecto declararía ante el organismo un motivo de
    /// cancelación que nadie eligió, y de él dependen los documentos que acreditan el trámite.</para>
    /// </summary>
    private static string? ComposeCancelacion(string? valorPersistido) =>
        CancelacionCausales.Parse(valorPersistido) switch
        {
            CancelacionCausal.DecisionJudicial => "CANCELACIÓN POR DECISIÓN JUDICIAL.",
            CancelacionCausal.PerdidaTotalFuerzaMayor => "CANCELACIÓN POR PÉRDIDA TOTAL - FUERZA MAYOR.",
            CancelacionCausal.PerdidaTotalAccidente => "CANCELACIÓN POR PÉRDIDA TOTAL - ACCIDENTE.",
            CancelacionCausal.DecisionVoluntaria => "CANCELACIÓN POR DECISIÓN VOLUNTARIA.",
            _ => null,
        };

    /// <summary>
    /// Casilla 18 (Otros): quién deja de ser arrendatario del vehículo y quién pasa a serlo.
    /// <code>CAMBIO DE LOCATARIO por Leasing de {PROPIETARIO} a {LOCATARIO}, TIPO DE DOCUMENTO {TIPO},
    /// NÚMERO DE DOCUMENTO {NUMERO}.</code>
    /// </summary>
    /// <remarks>
    /// «Leasing de» es texto fijo de la plantilla, no parte de la razón social: es el mismo conector
    /// que ya usa <see cref="ComposeLeasing"/>, de modo que los dos trámites de leasing se leen igual
    /// en el formulario. El propietario va solo con su nombre; el tipo y el número de documento
    /// acompañan únicamente al locatario, que es la parte que entra.
    /// </remarks>
    private static string? ComposeCambioLocatario(IReadOnlyList<DocumentParte> partes)
    {
        var propietario = Find(partes, "comprador");
        var locatario = Find(partes, "locatario");

        // Sin las DOS partes no se compone: aquí no cabe el fallback al comprador que sí usan leasing y
        // unilateral, porque el trámite es precisamente el cambio de una por otra y con una sola parte
        // la frase diría que alguien se sustituye a sí mismo. Regla del artefacto: faltan datos ⇒ sí
        // casilla, sí tipo, NO se inventa el texto.
        if (propietario is null || locatario is null)
            return null;
        if (string.IsNullOrWhiteSpace(propietario.Nombre) || string.IsNullOrWhiteSpace(locatario.Nombre))
            return null;

        var tipo = string.IsNullOrWhiteSpace(locatario.DocumentType) ? "-" : locatario.DocumentType.Trim();
        var numero = string.IsNullOrWhiteSpace(locatario.Documento) ? "-" : locatario.Documento.Trim();
        return $"CAMBIO DE LOCATARIO por Leasing de {propietario.Nombre.Trim()} a {locatario.Nombre.Trim()}, "
             + $"TIPO DE DOCUMENTO {tipo}, NÚMERO DE DOCUMENTO {numero}.";
    }

    private static string? ComposeLeasing(IReadOnlyList<DocumentParte> partes)
    {
        var propietario = Find(partes, "comprador");
        var locatario = Find(partes, "locatario") ?? Find(partes, "comprador");
        if (propietario is null || locatario is null)
            return null;
        if (string.IsNullOrWhiteSpace(propietario.Nombre) || string.IsNullOrWhiteSpace(locatario.Nombre))
            return null;
        if (ReferenceEquals(propietario, locatario) && Find(partes, "locatario") is null)
            return null;

        var tipo = string.IsNullOrWhiteSpace(locatario.DocumentType) ? "-" : locatario.DocumentType.Trim();
        var numero = string.IsNullOrWhiteSpace(locatario.Documento) ? "-" : locatario.Documento.Trim();
        return $"Matrícula con locatario por Leasing de {propietario.Nombre.Trim()} a {locatario.Nombre.Trim()} TIPO DE DOCUMENTO {tipo}, NÚMERO DE DOCUMENTO {numero}";
    }

    private static string? ComposeUnilateral(IReadOnlyList<DocumentParte> partes)
    {
        var locatario = Find(partes, "locatario") ?? Find(partes, "comprador");
        if (locatario is null || string.IsNullOrWhiteSpace(locatario.Nombre))
            return null;

        var tipo = string.IsNullOrWhiteSpace(locatario.DocumentType) ? "-" : locatario.DocumentType.Trim();
        var numero = string.IsNullOrWhiteSpace(locatario.Documento) ? "-" : locatario.Documento.Trim();
        return $"Traspaso unilateral por leasing a {locatario.Nombre.Trim()}., tipo de documento {tipo}, número de documento {numero}.";
    }

    /// <summary>
    /// Casilla 18 (Otros): a qué organismo se radica la cuenta.
    /// <code>Radicado de cuenta en {ORGANISMO DESTINO}</code>
    /// </summary>
    /// <remarks>
    /// El encabezado del FUR lleva el organismo donde el vehículo está matriculado HOY, así que sin
    /// esta línea el formulario no dice a dónde va la cuenta — que es justamente el trámite. Sin
    /// destino capturado no se compone: regla del artefacto, sí casilla y sí tipo, pero no se inventa
    /// el texto.
    /// </remarks>
    private static string? ComposeRadicadoCuenta(string? organismoDestino)
    {
        var destino = organismoDestino?.Trim();
        return string.IsNullOrEmpty(destino) ? null : $"Radicado de cuenta en {destino.ToUpperInvariant()}";
    }

    /// <summary>
    /// Casilla 18 (Otros): a qué organismo se traslada la cuenta.
    /// <code>Traslado de cuenta del Vehículo con placa {PLACA} para la nueva secretaria de {DESTINO}</code>
    /// </summary>
    /// <remarks>
    /// A diferencia del radicado, este trámite lo expide el organismo de ORIGEN —él valida el paz y
    /// salvo y da salida a la cuenta—, así que el encabezado del FUR lleva el de origen y el destino
    /// solo puede declararse aquí.
    /// <para>La placa se repite aunque el FUR ya la imprima en su casilla propia: es el literal que
    /// pidió el organismo. Cuesta ~110 de los 500 caracteres del recuadro, que comparte con el texto
    /// libre del gestor.</para>
    /// <para>Sin destino no se compone: sí casilla, sí tipo, no se inventa el texto.</para>
    /// </remarks>
    private static string? ComposeTrasladoCuenta(string? placa, string? organismoDestino)
    {
        var destino = organismoDestino?.Trim();
        if (string.IsNullOrEmpty(destino))
            return null;

        var placaTexto = string.IsNullOrWhiteSpace(placa) ? "-" : placa.Trim().ToUpperInvariant();
        return $"Traslado de cuenta del Vehículo con placa {placaTexto} para la nueva secretaria de "
             + destino.ToUpperInvariant();
    }

    private static DocumentParte? Find(IReadOnlyList<DocumentParte> partes, string rol) =>
        partes.FirstOrDefault(p => string.Equals(p.Rol, rol, StringComparison.OrdinalIgnoreCase));
}
