import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * HU #14303 (Épica #14271) — qué ofrece la UI sobre el certificado de una validación de identidad.
 *  - `descargable`: botón de descarga (Kyverum/mock aprobadas como siempre; manual aprobada con carta generada).
 *  - `en_preparacion`: validación MANUAL aprobada cuya carta aún no se genera — aviso, sin botón ni error.
 *  - `ninguno`: no hay certificado que ofrecer (no aprobada).
 * Las validaciones que no son manuales no cambian.
 */
export type EstadoCertificado = 'descargable' | 'en_preparacion' | 'ninguno';

export const TEXTO_CERTIFICADO_EN_PREPARACION = 'Certificado en preparación';

export function estadoCertificado(
  v: Pick<BiometricValidation, 'status' | 'provider' | 'cartaDisponible'> | null | undefined,
): EstadoCertificado {
  if (!v || v.status !== 'aprobado') return 'ninguno';
  if (v.provider !== 'manual') return 'descargable';
  return v.cartaDisponible === true ? 'descargable' : 'en_preparacion';
}
