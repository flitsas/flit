namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Atributos del trámite/vehículo/actores que condicionan la obligatoriedad documental
/// (HU #10521, RF30). Se resuelve desde el snapshot inmutable del trámite (RF14) y los
/// datos capturados; el motor de reglas condicionales lo evalúa para exigir cada documento
/// solo cuando corresponde. Todos por defecto <c>false</c> ⇒ sin condiciones = checklist base.
/// </summary>
/// <param name="EsImportado">Vehículo importado (⇒ documento de aduana/importación).</param>
/// <param name="EsPersonaNatural">Actor principal persona natural (⇒ identidad digital, sin cédula manual).</param>
/// <param name="EsNit">Actor principal NIT/persona jurídica (⇒ RUES + documento de identificación).</param>
/// <param name="TieneLeasing">Traspaso unilateral / leasing (⇒ contrato + declaración arrendadora).</param>
/// <param name="TienePrenda">El OT exige prenda o el vehículo tiene prenda (⇒ documentos de prenda).</param>
/// <param name="TieneTramitador">Hay un tramitador (processor) activo (⇒ poderes de mandato).</param>
/// <param name="CambioCarroceria">Cambio de carrocería (⇒ factura de carrocería).</param>
/// <param name="ServicioEspecial">Servicio especial (⇒ anexos de servicio especial).</param>
/// <param name="ExigeMandato">
/// El trámite exige mandato (ADR-0036): siempre (persona natural y jurídica).
/// ⇒ el mandato aparece en el checklist como documento autogenerado.
/// </param>
/// <param name="CancelacionCausal">
/// Causal declarada en una cancelación de matrícula (⇒ los documentos que la acreditan).
/// <para>A diferencia del resto, no es un booleano: cada causal exige documentos distintos y son
/// excluyentes entre sí, así que colapsarlas en banderas sueltas permitiría estados imposibles —una
/// cancelación que fuera judicial y voluntaria a la vez—. <see cref="ValueObjects.CancelacionCausal.Ninguna"/>
/// (el defecto) no exige nada ⇒ checklist base.</para>
/// </param>
public sealed record TramiteDocumentContext(
    bool EsImportado = false,
    bool EsPersonaNatural = false,
    bool EsNit = false,
    bool TieneLeasing = false,
    bool TienePrenda = false,
    bool TieneTramitador = false,
    bool CambioCarroceria = false,
    bool ServicioEspecial = false,
    bool ExigeMandato = false,
    CancelacionCausal CancelacionCausal = CancelacionCausal.Ninguna);
