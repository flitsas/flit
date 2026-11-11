import type { OtGrupoPlaca } from '@/lib/api/types-ot';

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
  return normalizarPlacaOt(valor, 'CARRO');
}

// ── Grupo de placa por clase (registro diferido 2026-11-09, HU5–HU7, plan preasignacion.pdf) ──

/** Grupo de formato de placa. REMOLQUE y MAQUINARIA los define el catálogo del backend. */
export type GrupoPlaca = OtGrupoPlaca;

const GRUPOS_PLACA: ReadonlySet<string> = new Set(['CARRO', 'MOTO', 'MOTOCARRO', 'REMOLQUE', 'MAQUINARIA']);

/** Clases de moto (catálogo 97), ya normalizadas. Igualdad exacta. */
const CLASES_MOTO: ReadonlySet<string> = new Set(['MOTOCICLETA', 'CUATRIMOTO', 'CUADRICICLO', 'CICLOMOTOR']);

/** HU10 (D5) — clases de motocarro: placa 123ABC. Igualdad exacta. */
const CLASES_MOTOCARRO: ReadonlySet<string> = new Set(['MOTOCARRO', 'MOTOTRICICLO', 'TRICIMOTO']);

/** Clases de remolque conocidas, ya normalizadas. Solo fallback: el catálogo del backend manda. */
const CLASES_REMOLQUE: ReadonlySet<string> = new Set([
  'REMOLQUE',
  'SEMIREMOLQUE',
  'SEMIRREMOLQUE',
  'REMOLQUE Y SEMIRREMOLQUE',
]);

/**
 * Verdadero si en esta clase el dígito de preferencia es el PRIMER número de la placa: moto
 * (ABC52D) y motocarro (523ABC, HU10). En el resto es el último (ABC125).
 */
export function digitoEsPrimerNumero(clase: string | null | undefined): boolean {
  const c = normalizarClaseVehiculo(clase);
  return CLASES_MOTO.has(c) || CLASES_MOTOCARRO.has(c);
}

/**
 * Grupo por listas exactas de la clase: CARRO, MOTO, MOTOCARRO o REMOLQUE. Maquinaria no tiene lista en el
 * front (la define el catálogo del backend) → `null`, igual que clase vacía o desconocida.
 */
export function grupoPlacaDesdeClase(clase: string | null | undefined): GrupoPlaca | null {
  const c = normalizarClaseVehiculo(clase);
  if (CLASES_CARRO.has(c)) return 'CARRO';
  if (CLASES_MOTO.has(c)) return 'MOTO';
  if (CLASES_MOTOCARRO.has(c)) return 'MOTOCARRO';
  if (CLASES_REMOLQUE.has(c)) return 'REMOLQUE';
  return null;
}

/** Bandeja OT: `grupoPlaca` del backend si viene y es válido; si no, las listas por `clase`. */
export function resolverGrupoPlaca(
  grupoPlaca: string | null | undefined,
  clase: string | null | undefined,
): GrupoPlaca | null {
  const g = (grupoPlaca ?? '').trim().toUpperCase();
  if (GRUPOS_PLACA.has(g)) return g as GrupoPlaca;
  return grupoPlacaDesdeClase(clase);
}

/**
 * Lado gestor (FUR): el formato de plantilla del backend (`GET /fur/template-format`, mismo catálogo
 * que la generación del FUR) decide MAQUINARIA/REMOLQUES; AUTOMOTOR o sin formato → listas por clase.
 */
export function grupoPlacaDesdeFormatoFur(
  formato: string | null | undefined,
  clase: string | null | undefined,
): GrupoPlaca | null {
  const f = (formato ?? '').trim().toUpperCase();
  if (f === 'MAQUINARIA') return 'MAQUINARIA';
  if (f === 'REMOLQUES') return 'REMOLQUE';
  return grupoPlacaDesdeClase(clase);
}

/** Decisión D2: remolque y maquinaria no llevan dígito de preferencia (ni selector ni gate). */
export function grupoPlacaPideDigito(grupo: GrupoPlaca | null | undefined): boolean {
  return grupo !== 'REMOLQUE' && grupo !== 'MAQUINARIA';
}

/** Regla de formato y longitud máxima de cada grupo (misma regla que el backend, D1). */
export const FORMATO_PLACA_POR_GRUPO: Readonly<Record<GrupoPlaca, { pattern: RegExp; maxLength: number }>> = {
  CARRO: { pattern: PLACA_CARRO_PATTERN, maxLength: PLACA_CARRO_MAX_LENGTH },
  MOTO: { pattern: /^[A-Z]{3}\d{2}[A-Z]?$/, maxLength: 6 },
  MOTOCARRO: { pattern: /^\d{3}[A-Z]{3}$/, maxLength: 6 },
  REMOLQUE: { pattern: /^[RS]\d{5,6}$/, maxLength: 7 },
  MAQUINARIA: { pattern: /^[A-Z]{2}\d{6}$/, maxLength: 8 },
};

/** Máscara por grupo: mayúsculas, solo [A-Z0-9] y maxLength del grupo. Sin grupo, texto tal cual. */
export function normalizarPlacaOt(valor: string, grupo: GrupoPlaca | null): string {
  if (!grupo) return valor;
  return valor
    .toUpperCase()
    .replace(/[^A-Z0-9]/g, '')
    .slice(0, FORMATO_PLACA_POR_GRUPO[grupo].maxLength);
}

/**
 * Estado del campo de placa en los modales del OT («Asignar placa» y «Corregir placa»):
 * - `invalida`: hay texto y, con grupo conocido, no cumple su formato → se muestra el aviso.
 * - `lista`: se puede confirmar (texto no vacío y, con grupo, formato válido).
 * El campo vacío no es un error: solo deja el botón deshabilitado. Sin grupo, solo exige texto.
 */
export function estadoPlacaOt(valor: string, grupo: GrupoPlaca | null): { invalida: boolean; lista: boolean } {
  const conTexto = valor.trim() !== '';
  const invalida = !!grupo && conTexto && !FORMATO_PLACA_POR_GRUPO[grupo].pattern.test(valor);
  return { invalida, lista: conTexto && !invalida };
}
