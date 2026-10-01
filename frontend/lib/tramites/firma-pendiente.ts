/**
 * Bug #13194 (P4) — gate de firma: toda transición hacia el organismo de tránsito (radicar,
 * re-radicar, «Enviar al OT», cambio de estado por `/transition`) responde 409 con el código
 * `firma_pendiente` cuando alguna parte que firma no tiene identidad aprobada y vigente.
 *
 * Mapeo ÚNICO de ese error a texto de UI. Es tolerante a la forma del cuerpo porque el contrato no
 * está cerrado:
 *  - el código puede venir en `title` (ProblemDetails, lo que hace hoy el backend), `code`,
 *    `errorCode` o `error`;
 *  - las partes pueden venir como lista (`partes`, `partesSinFirma`, `partesFaltantes`, `parties`)
 *    de textos u objetos `{ parte, notificacion }`, o solo dentro del `detail`
 *    («… Falta la firma (identidad aprobada y vigente) de: comprador, vendedor.»);
 *  - la notificación del correo de validación de identidad por parte puede faltar (paso posterior).
 *
 * Sin partes ni notificación el mensaje es genérico. Solo se nombran ROLES conocidos: nada que venga
 * del servidor se pinta tal cual (no hay PII en el mensaje).
 */

export const FIRMA_PENDIENTE_CODE = 'firma_pendiente';

/** Estado del correo de validación de identidad (VID) que el backend reporta por parte. */
export type NotificacionVid = 'no_requerida' | 'enviada' | 'ya_en_curso' | 'fallida';

export interface ParteFirmaPendiente {
  /** Rol normalizado (`comprador`, `vendedor`, …). */
  parte: string;
  notificacion: NotificacionVid | null;
}

/**
 * Dónde se muestra el mensaje: en el asistente (borrador/preparado/subsanación) el gestor puede ir al
 * paso 4; en `asignado` el asistente ya no es editable y la salida es otra.
 */
export type FirmaPendienteContexto = 'wizard' | 'asignado';

const PARTE_LABEL: Record<string, string> = {
  comprador: 'comprador',
  vendedor: 'vendedor',
  locatario: 'locatario',
  propietario: 'propietario',
  titular: 'titular',
  acreedor: 'acreedor',
};

const NOTIFICACIONES: readonly NotificacionVid[] = ['no_requerida', 'enviada', 'ya_en_curso', 'fallida'];

const CODE_KEYS = ['title', 'code', 'errorCode', 'error'] as const;
const LIST_KEYS = ['partes', 'partesSinFirma', 'partesFaltantes', 'parties', 'missingParties'] as const;
const PARTE_KEYS = ['parte', 'rol', 'role', 'party'] as const;
const NOTIF_KEYS = ['notificacion', 'notificacionVid', 'estadoNotificacion', 'notification'] as const;

type Problem = Record<string, unknown> | null | undefined;

function asRecord(v: unknown): Record<string, unknown> | null {
  return v && typeof v === 'object' && !Array.isArray(v) ? (v as Record<string, unknown>) : null;
}

/** ¿El ProblemDetails es el del gate de firma? */
export function esFirmaPendiente(problem: Problem): boolean {
  const p = asRecord(problem);
  if (!p) return false;
  return CODE_KEYS.some(
    (k) => typeof p[k] === 'string' && (p[k] as string).trim().toLowerCase() === FIRMA_PENDIENTE_CODE,
  );
}

/** Igual que {@link esFirmaPendiente} pero sobre el error lanzado por el cliente (`TramitesApiError`). */
export function esErrorFirmaPendiente(err: unknown): boolean {
  const e = asRecord(err);
  return !!e && esFirmaPendiente(asRecord(e.problem));
}

function normalizarParte(raw: unknown): string | null {
  if (typeof raw !== 'string') return null;
  const key = raw.trim().toLowerCase();
  return PARTE_LABEL[key] ? key : null;
}

function normalizarNotificacion(raw: unknown): NotificacionVid | null {
  if (typeof raw !== 'string') return null;
  const key = raw.trim().toLowerCase() as NotificacionVid;
  return NOTIFICACIONES.includes(key) ? key : null;
}

function primerValor(o: Record<string, unknown>, keys: readonly string[]): unknown {
  for (const k of keys) if (o[k] !== undefined && o[k] !== null) return o[k];
  return undefined;
}

/** Partes que faltan por firmar, en el orden en que las reporta el backend y sin repetir. */
export function partesFirmaPendiente(problem: Problem): ParteFirmaPendiente[] {
  const p = asRecord(problem);
  if (!p) return [];
  const out: ParteFirmaPendiente[] = [];
  const add = (parte: string | null, notificacion: NotificacionVid | null) => {
    if (!parte) return;
    const prev = out.find((x) => x.parte === parte);
    if (prev) {
      prev.notificacion ??= notificacion;
      return;
    }
    out.push({ parte, notificacion });
  };

  // Mapa aparte de notificaciones por parte ({ comprador: 'enviada' }), si el backend lo manda así.
  const mapaNotif = asRecord(p.notificaciones);

  const lista = primerValor(p, LIST_KEYS);
  if (Array.isArray(lista)) {
    for (const item of lista) {
      const obj = asRecord(item);
      if (obj) {
        add(normalizarParte(primerValor(obj, PARTE_KEYS)), normalizarNotificacion(primerValor(obj, NOTIF_KEYS)));
      } else {
        const parte = normalizarParte(item);
        add(parte, parte && mapaNotif ? normalizarNotificacion(mapaNotif[parte]) : null);
      }
    }
  }

  // Contrato actual de `/transition`: las partes solo viajan en el `detail` («… de: comprador, vendedor.»).
  if (out.length === 0 && typeof p.detail === 'string') {
    const m = /de:\s*([^.]+)\.?\s*$/i.exec(p.detail);
    if (m) {
      for (const token of m[1].split(/,|\by\b/)) {
        const parte = normalizarParte(token);
        add(parte, parte && mapaNotif ? normalizarNotificacion(mapaNotif[parte]) : null);
      }
    }
  }

  if (out.length === 0 && mapaNotif) {
    for (const [k, v] of Object.entries(mapaNotif)) add(normalizarParte(k), normalizarNotificacion(v));
  }
  return out;
}

function listaDel(partes: string[]): string {
  const labels = partes.map((p) => `del ${PARTE_LABEL[p]}`);
  if (labels.length <= 1) return labels.join('');
  return `${labels.slice(0, -1).join(', ')} y ${labels[labels.length - 1]}`;
}

/**
 * Texto de UI del 409 `firma_pendiente`. Nunca devuelve vacío: sin partes ni notificación cae al
 * genérico.
 */
export function mensajeFirmaPendiente(
  problem: Problem,
  contexto: FirmaPendienteContexto = 'wizard',
): string {
  const partes = partesFirmaPendiente(problem);
  const frases: string[] = [];

  if (partes.length === 0) {
    frases.push(
      'Falta la validación de identidad o firma de una de las partes: no se puede enviar al organismo de tránsito un trámite sin firmar.',
    );
  } else {
    frases.push(`Falta la firma ${listaDel(partes.map((x) => x.parte))}.`);
  }

  const una = partes.length === 1;
  let hayNotificacion = false;
  for (const { parte, notificacion } of partes) {
    const quien = una ? '' : ` del ${PARTE_LABEL[parte]}`;
    switch (notificacion) {
      case 'enviada':
        hayNotificacion = true;
        frases.push(
          una
            ? 'Enviamos el enlace de validación de identidad a su correo.'
            : `Enviamos el enlace de validación de identidad al correo${quien}.`,
        );
        break;
      case 'ya_en_curso':
        hayNotificacion = true;
        frases.push(`Ya hay una validación de identidad en curso${quien}; espera a que termine.`);
        break;
      case 'fallida':
        hayNotificacion = true;
        frases.push(
          contexto === 'asignado'
            ? `No se pudo enviar el correo de validación${quien}: inicia la prevalidación desde el módulo Identidad.`
            : `No se pudo enviar el correo de validación${quien}: usa «Validar identidad» en el paso 4.`,
        );
        break;
      default:
        break;
    }
  }

  if (contexto === 'asignado') {
    frases.push(
      'El trámite ya no es editable: completa la identidad con la prevalidación del módulo Identidad o renovando el baúl de firmas del representante legal.',
    );
    if (!hayNotificacion) {
      frases.push('Si falta una validación de identidad, el enlace se envía automáticamente al correo de la parte.');
    }
  } else if (!hayNotificacion) {
    frases.push('Complétala en el paso 4 «Validación de Identidad» y vuelve a intentarlo.');
  }

  return frases.join(' ');
}
