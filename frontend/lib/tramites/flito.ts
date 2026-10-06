/**
 * HU #13266 (Feature #13261, Épica #12741) — lo que el gestor necesita saber de lo que cargó FLITO.
 *
 * FLITO adjunta el comprobante `liquidacion_impuesto` por la API externa (HU #13263): el adjunto queda
 * con `source = user` y `provider = flito`, y al aceptarlo en preasignación, asignado o rechazado con
 * subsanación el backend marca `impuesto_departamental_pagado = true` con `source = flito` (HU #13264).
 * Gana quien carga primero (HU #13265): si FLITO cargó, el gestor no reemplaza ni borra ese adjunto
 * (409 `adjunto_bloqueado_flito` / `adjunto_protegido`) y «Enviar al OT» no desmarca el impuesto.
 *
 * Única fuente de la detección «es de FLITO» y de su copy: ningún componente compara el literal
 * `'flito'` por su cuenta.
 */
import type { FieldValue } from '@/lib/api/types/procedure-runtime';

/** Valor de `provider` (adjunto) y de `source` (field_value) con el que el backend marca lo de FLITO. */
export const ORIGEN_FLITO = 'flito';

/** Campo del trámite que FLITO marca al adjuntar el comprobante (`EnvioOtCheckFields`). */
export const CAMPO_IMPUESTO_DEPARTAMENTAL_PAGADO = 'impuesto_departamental_pagado';

/** Código del 409 de subida/presign/registro cuando FLITO ya cargó ese tipo (HU #13265). */
export const CODIGO_ADJUNTO_BLOQUEADO_FLITO = 'adjunto_bloqueado_flito';

/** Código del 409 de borrado de un adjunto protegido (sistema o FLITO). */
export const CODIGO_ADJUNTO_PROTEGIDO = 'adjunto_protegido';

/** AC2 — etiqueta del check de impuesto marcado por FLITO. */
export const ETIQUETA_IMPUESTO_PAGADO_FLITO = 'Pagado (comprobante cargado)';

/** AC4 — motivo del check deshabilitado (se anuncia con `aria-describedby`). */
export const MOTIVO_IMPUESTO_PAGADO_FLITO =
  'El pago quedó registrado al cargarse el comprobante de liquidación del impuesto. No se puede desmarcar aquí.';

/** AC3 — 409 `adjunto_bloqueado_flito` (subir o reemplazar). */
export const COPY_ADJUNTO_BLOQUEADO_FLITO =
  'Este documento ya fue cargado y no se puede reemplazar.';

/** AC3 — 409 `adjunto_protegido` sobre un adjunto de FLITO (borrar). */
export const COPY_ADJUNTO_PROTEGIDO_FLITO =
  'Este documento ya fue cargado y no se puede eliminar.';

/** ¿El `provider`/`source` es FLITO? Sin distinguir mayúsculas ni espacios de borde. */
export function esOrigenFlito(valor: string | null | undefined): boolean {
  return typeof valor === 'string' && valor.trim().toLowerCase() === ORIGEN_FLITO;
}

/** AC1 — ¿el adjunto lo cargó FLITO? (`provider = flito`). */
export function esAdjuntoDeFlito(adjunto: { provider?: string | null } | null | undefined): boolean {
  return !!adjunto && esOrigenFlito(adjunto.provider);
}

/** AC2 — ¿`impuesto_departamental_pagado` está marcado por FLITO en los field_values del trámite? */
export function esImpuestoPagadoPorFlito(fieldValues: readonly FieldValue[] | null | undefined): boolean {
  const campo = (fieldValues ?? []).find(
    (f) => f.fieldKey?.toLowerCase() === CAMPO_IMPUESTO_DEPARTAMENTAL_PAGADO,
  );
  return !!campo && campo.valueText?.trim().toLowerCase() === 'true' && esOrigenFlito(campo.source);
}

/**
 * Código de error del ProblemDetails (`extensions.error`, serializado en la raíz como `error`). Solo
 * mira el campo `error`: el `detail` es texto libre y no debe decidir nada.
 */
function codigoProblema(err: unknown): string | null {
  if (!err || typeof err !== 'object') return null;
  const problem = (err as { problem?: unknown }).problem;
  if (!problem || typeof problem !== 'object') return null;
  const codigo = (problem as { error?: unknown }).error;
  return typeof codigo === 'string' ? codigo.trim().toLowerCase() : null;
}

/**
 * AC3 — copy de FLITO para un error de adjunto, o `null` si el error no es un bloqueo de FLITO.
 *
 * - 409 `adjunto_bloqueado_flito` → {@link COPY_ADJUNTO_BLOQUEADO_FLITO}.
 * - 409 `adjunto_protegido` sobre un adjunto con `provider = flito` → {@link COPY_ADJUNTO_PROTEGIDO_FLITO}.
 *   El backend usa el mismo código (y el mismo `detail`, «lo genera el sistema») que para los
 *   documentos del sistema, así que el único que sabe que es de FLITO es quien pidió el borrado.
 */
export function mensajeErrorAdjuntoFlito(
  err: unknown,
  adjunto?: { provider?: string | null } | null,
): string | null {
  const codigo = codigoProblema(err);
  if (codigo === CODIGO_ADJUNTO_BLOQUEADO_FLITO) return COPY_ADJUNTO_BLOQUEADO_FLITO;
  if (codigo === CODIGO_ADJUNTO_PROTEGIDO && esAdjuntoDeFlito(adjunto)) return COPY_ADJUNTO_PROTEGIDO_FLITO;
  return null;
}

/**
 * Mensaje para un error al subir o borrar un adjunto: el de FLITO si aplica; si no, el que ya se
 * mostraba (`message` del error, que el cliente arma con el `detail` en español) o el `respaldo`.
 */
export function mensajeErrorAdjunto(
  err: unknown,
  respaldo: string,
  adjunto?: { provider?: string | null } | null,
): string {
  const flito = mensajeErrorAdjuntoFlito(err, adjunto);
  if (flito) return flito;
  return err instanceof Error && err.message.trim() ? err.message : respaldo;
}
