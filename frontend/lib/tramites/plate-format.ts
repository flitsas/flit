// Formato de placa de carro (registro diferido 2026-11-09, HU 2) — en el modal «Asignar placa al
// trámite» del OT, los carros solo admiten 3 letras + 3 números (ABC123). El resto de clases (motos,
// maquinaria, remolques, clase vacía o desconocida) conserva los formatos actuales.
//
// La clase se normaliza IGUAL que `FurClassificationNormalizer.Normalize` del backend
// (`FurTemplateResolver.cs`): trim, mayúsculas, espacios colapsados y sin tildes (Ñ→N, Ç→C). La
// comparación es por IGUALDAD EXACTA: nunca prefijo ni contains, porque «CAMIÓN GRÚA» y las demás
// clases CAMION* de maquinaria no son carro.

/** Clases de carro, ya normalizadas. */
const CLASES_CARRO: ReadonlySet<string> = new Set([
  'AUTOMOVIL',
  'CAMIONETA',
  'CAMPERO',
  'BUS',
  'BUSETA',
  'MICROBUS',
  'CAMION',
  'TRACTOCAMION',
  'VOLQUETA',
]);

/** 3 letras y 3 números, ya en mayúsculas. */
export const PLACA_CARRO_PATTERN = /^[A-Z]{3}\d{3}$/;

/** Longitud máxima de una placa de carro. */
export const PLACA_CARRO_MAX_LENGTH = 6;

/** Normaliza una clase de vehículo como el backend: trim, mayúsculas, espacios colapsados, sin tildes. */
export function normalizarClaseVehiculo(clase: string | null | undefined): string {
  return (clase ?? '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toUpperCase()
    .replace(/\s+/g, ' ')
    .trim();
}

/** Verdadero solo si la clase normalizada es exactamente una de las clases de carro. */
export function esClaseCarro(clase: string | null | undefined): boolean {
  return CLASES_CARRO.has(normalizarClaseVehiculo(clase));
}

/** Mayúsculas, solo [A-Z0-9] (sin espacios ni guiones) y como máximo 6 caracteres. */
export function normalizarPlacaCarro(valor: string): string {
  return valor
    .toUpperCase()
    .replace(/[^A-Z0-9]/g, '')
    .slice(0, PLACA_CARRO_MAX_LENGTH);
}

/**
 * Estado del campo de placa en los modales del OT («Asignar placa» y «Corregir placa»):
 * - `invalida`: hay texto y, siendo carro, no cumple ABC123 → se muestra el aviso.
 * - `lista`: se puede confirmar (texto no vacío y, si es carro, formato válido).
 * El campo vacío no es un error: solo deja el botón deshabilitado.
 */
export function estadoPlacaOt(valor: string, esCarro: boolean): { invalida: boolean; lista: boolean } {
  const conTexto = valor.trim() !== '';
  const invalida = esCarro && conTexto && !PLACA_CARRO_PATTERN.test(valor);
  return { invalida, lista: conTexto && !invalida };
}
