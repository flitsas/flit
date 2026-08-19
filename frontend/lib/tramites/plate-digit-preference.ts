// HU #11628 — el dígito de preferencia de placa exige una elección consciente. El valor vacío
// dejaba de servir para dos cosas indistinguibles: "no lo he tocado" y "no tengo preferencia".
// Este módulo aísla, en funciones puras, la decisión de si el gestor DEBE elegir algo antes de
// continuar y la traducción entre el valor de UI (tres estados: `''` no decidido, `'none'` sin
// preferencia declarada, `'0'..'9'` dígito) y el contrato persistido de `plate_preferred_last_digit`
// (dígito o cadena vacía — SIN CAMBIOS: es lo que lee el backend y su proyección al OT, ver
// `OtClientProcedureRepository.cs:741-743` y el test de contrato en `OtClientProcedureHandlerTests.cs`).
//
// La distinción "no decidido" vs "declaró sin preferencia" se persiste aparte, en un field_value que
// el backend NO consume: `plate_preferred_last_digit_declared` ('true' | 'false'). Solo sirve para
// rehidratar correctamente el selector entre sesiones.

/** Valor vacío en el `<select>`: placeholder "todavía no decidido". */
export const DIGITO_PLACA_NO_DECIDIDO = '';
/** Valor explícito de "sin preferencia" — distinto del placeholder de arriba. */
export const DIGITO_PLACA_SIN_PREFERENCIA = 'none';

/** Field key del contrato persistido — SIN CAMBIOS, es lo que lee el backend. */
export const PLATE_PREFERRED_LAST_DIGIT_KEY = 'plate_preferred_last_digit';
/** Field key de la señal "declarado", aparte del contrato — el backend no la consume. */
export const PLATE_PREFERRED_LAST_DIGIT_DECLARED_KEY = 'plate_preferred_last_digit_declared';

/**
 * Verdadero cuando el selector de dígito de preferencia está realmente en juego y por tanto exige
 * una elección consciente antes de continuar: paso de matrícula, el vehículo no trae placa del RUNT,
 * ya hay organismo elegido y ese organismo (o la compañía) tiene la ruta de preasignación activa.
 *
 * Con la preasignación apagada, sin organismo todavía o mientras se consulta el estado de la ruta,
 * el selector queda deshabilitado y no hay nada que exigir — bloquear ahí dejaría trámites imposibles
 * de continuar (AC4 de la HU #11628).
 */
export function isPlateDigitDecisionRequired(params: {
  muestraRadicacion: boolean;
  vehiculoConPlacaRunt: boolean;
  transitOfficeId: string;
  preasignacionActiva: boolean | null;
}): boolean {
  return (
    params.muestraRadicacion &&
    !params.vehiculoConPlacaRunt &&
    !!params.transitOfficeId &&
    params.preasignacionActiva === true
  );
}

/**
 * Verdadero cuando la decisión es exigible (`isPlateDigitDecisionRequired`) y el gestor todavía no
 * la tomó (`digitoPlacaUiValue === ''`). Gatea "Continuar y guardar" en el paso de matrícula (AC1).
 */
export function isPlateDigitUndecided(params: {
  muestraRadicacion: boolean;
  vehiculoConPlacaRunt: boolean;
  transitOfficeId: string;
  preasignacionActiva: boolean | null;
  digitoPlacaUiValue: string;
}): boolean {
  return (
    isPlateDigitDecisionRequired(params) &&
    params.digitoPlacaUiValue === DIGITO_PLACA_NO_DECIDIDO
  );
}

/**
 * Traduce el valor de UI del `<select>` a los dos field_values que se persisten juntos:
 * - `plate_preferred_last_digit`: el CONTRATO sin cambios (dígito o cadena vacía).
 * - `plate_preferred_last_digit_declared`: 'true' en cuanto el gestor eligió algo explícito
 *   (dígito o "sin preferencia"), 'false' mientras sigue sin decidir.
 */
export function toPlateDigitFieldValues(
  uiValue: string,
): { fieldKey: string; valueText: string }[] {
  const persistedDigit = uiValue === DIGITO_PLACA_SIN_PREFERENCIA ? '' : uiValue;
  const declared = uiValue === DIGITO_PLACA_NO_DECIDIDO ? 'false' : 'true';
  return [
    { fieldKey: PLATE_PREFERRED_LAST_DIGIT_KEY, valueText: persistedDigit },
    { fieldKey: PLATE_PREFERRED_LAST_DIGIT_DECLARED_KEY, valueText: declared },
  ];
}

/**
 * Reconstruye el valor de UI a partir de los field_values persistidos (rehidratación). Un dígito
 * presente gana siempre; si no hay dígito, la marca `..._declared` distingue "declaró sin
 * preferencia" (`'none'`) de "no decidido" (`''`).
 */
export function toPlateDigitUiValue(params: {
  rawDigit: string;
  declared: boolean;
}): string {
  if (params.rawDigit) return params.rawDigit;
  return params.declared ? DIGITO_PLACA_SIN_PREFERENCIA : DIGITO_PLACA_NO_DECIDIDO;
}
