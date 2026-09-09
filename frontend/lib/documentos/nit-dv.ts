/**
 * Dígito de verificación del NIT — módulo 11 DIAN (HU #12209, Feature #12201, CF-25).
 *
 * <b>En el cliente esto es solo visual.</b> El backend es la fuente de verdad: `NitVerificationDigit`
 * (`Flit.Admin.Domain/Common`) calcula el DV que se imprime en el PDF y el formulario NO envía el
 * campo — así un NIT y su DV no pueden discrepar dentro del mismo documento (anexo §5.2). Aquí se
 * calcula únicamente para mostrárselo al usuario mientras teclea, de modo que no lo capture a mano.
 *
 * Uso de ejemplo: `calcularDigitoVerificacion("901555444") // → "4"`
 */

/** Pesos DIAN, alineados de derecha a izquierda sobre los dígitos del NIT. */
const PESOS = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

/**
 * DV del NIT por módulo 11. Devuelve `null` si el valor no es un NIT utilizable (vacío, con
 * caracteres no numéricos una vez retirados puntos y guiones, o más largo que la tabla de pesos):
 * mostrar un dígito inventado sería peor que no mostrar ninguno.
 */
export function calcularDigitoVerificacion(nit: string | null | undefined): string | null {
  const digitos = (nit ?? "").replace(/[.\-\s]/g, "");
  if (!digitos || !/^\d+$/.test(digitos) || digitos.length > PESOS.length) {
    return null;
  }

  let suma = 0;
  for (let i = 0; i < digitos.length; i += 1) {
    // El dígito menos significativo pesa 3: se recorre de derecha a izquierda.
    const digito = Number(digitos[digitos.length - 1 - i]);
    suma += digito * PESOS[i];
  }

  const residuo = suma % 11;
  // Residuos 0 y 1 son el propio DV; el resto se complementa a 11. Es la regla DIAN, no una
  // simplificación: con `11 - residuo` en el caso 0 saldría un "11" imposible.
  return String(residuo <= 1 ? residuo : 11 - residuo);
}
