/**
 * HU #12186 — la validación de identidad, contada para quien gestiona el trámite.
 *
 * <p>El dato ya existe y ya se consulta: la bitácora de auditoría. Lo que no existía era una
 * lectura. Esa bitácora está redactada para soporte —etapas de integración, columna de cifrado,
 * códigos HTTP— y su columna «Detalle» dice «Sin novedad» justo en las filas que salieron bien, que
 * son casi todas. Un gestor que solo quiere saber <b>si la persona se validó y cuándo</b> tiene que
 * traducir.</p>
 *
 * <p><b>Decisión de producto (Feature #12180): nada de servicios externos en lo que ve el
 * gestor.</b> Ni el nombre del proveedor de biometría, ni etapas de integración, ni cifrado, ni
 * códigos HTTP. Para quien gestiona el trámite, la validación la hace Flit. Todo eso sigue
 * existiendo en la bitácora técnica, un clic más adentro y para soporte — pero deja de ser lo
 * primero que se lee. Este módulo es la frontera: lo que no sale de aquí no llega a pantalla.</p>
 *
 * <p>Es PURO —recibe la validación y sus eventos, devuelve texto— para que la regla se pueda
 * probar sin montar el componente, y para que un evento nuevo del proveedor no pueda colarse a la
 * lectura humana por descuido: lo que no está mapeado, no se muestra.</p>
 */

import type { BiometricValidation, IdentityAuditEvent } from '@/lib/api/types/procedure-runtime';

export type TonoIdentidad = 'ok' | 'pendiente' | 'alerta' | 'neutro';

export interface EstadoIdentidad {
  /** Lo que se lee en la píldora de la cabecera. */
  label: string;
  tono: TonoIdentidad;
  /** Qué pasó y, si hay algo que hacer, qué. `null` cuando no hace falta explicar nada. */
  explicacion: string | null;
  /** Acción sugerida, en la voz del gestor. `null` si no hay ninguna disponible. */
  accion: string | null;
}

export interface HitoIdentidad {
  id: string;
  /** Qué pasó, nombrando a la PERSONA y no a una etapa del sistema. */
  titulo: string;
  /** Segunda línea opcional: el matiz que ayuda a entenderlo. */
  detalle: string | null;
  cuando: string;
  tono: TonoIdentidad;
}

/**
 * Etapas de la bitácora que significan algo para quien gestiona.
 *
 * <p>Las que NO están aquí —respuesta del proveedor, notificación no verificable, firma de
 * notificación inválida, reconciliación, fallos al descifrar— son de integración: no cuentan nada
 * que el gestor pueda hacer ni entender, y nombrarlas obliga a explicar el proveedor. Se quedan en
 * la bitácora técnica.</p>
 */
const ETAPAS_VISIBLES = new Set([
  'send',
  'send_error',
  'resend',
  'contact_edited',
  'expired',
  'webhook_received',
  'webhook_applied',
]);

const OUTCOMES_CORRECTOS = new Set(['ok', 'received', 'aprobado']);

/** Primer nombre, para que la línea se lea como una frase y no como un registro. */
export function primerNombre(nombre: string | null | undefined): string {
  const limpio = nombre?.trim();
  if (!limpio) return 'La persona';
  return limpio.split(/\s+/)[0];
}

/**
 * En qué quedó la validación, y qué se puede hacer.
 *
 * <p><b>Un rechazo tiene que decir por qué y qué hacer.</b> «Rechazado» a secas obliga a llamar a
 * soporte; con el motivo y la salida, el gestor resuelve solo. El motivo lo trae el backend ya
 * saneado (`rejectionReason`), así que no hay que inventarlo ni exponer el resultado crudo.</p>
 */
export function estadoDeIdentidad(v: BiometricValidation, ahora: Date = new Date()): EstadoIdentidad {
  if (v.status === 'aprobado') {
    return {
      label: 'Identidad aprobada',
      tono: 'ok',
      explicacion: 'Se verificó que la persona es quien dice ser.',
      accion: null,
    };
  }

  if (v.status === 'rechazado') {
    const motivo = v.rejectionReason?.trim();
    return {
      label: 'Identidad rechazada',
      tono: 'alerta',
      explicacion: motivo || 'La validación no se pudo completar.',
      accion: 'Se puede enviar un enlace nuevo para que lo intente otra vez.',
    };
  }

  if (v.expired) {
    return {
      label: 'Enlace vencido',
      tono: 'alerta',
      explicacion: 'El enlace caducó antes de que la persona lo usara.',
      accion: 'Se puede enviar un enlace nuevo.',
    };
  }

  // En proceso: lo que importa es cuánto lleva esperando y si ya falló algún intento.
  const desde = v.createdAt ?? null;
  const espera = desde ? transcurrido(desde, ahora.toISOString()) : null;
  const ultimoFallo = v.ultimoIntentoMotivo?.trim();

  return {
    label: 'A la espera de la persona',
    tono: 'pendiente',
    explicacion: [
      espera ? `El enlace se envió ${espera}, y todavía no ha respondido.` : 'El enlace está enviado y todavía no hay respuesta.',
      ultimoFallo ? `Su último intento no salió: ${minuscula(ultimoFallo)}.` : null,
    ]
      .filter(Boolean)
      .join(' '),
    accion: 'Se puede reenviar el enlace o corregir sus datos de contacto.',
  };
}

/**
 * El proveedor puede notificar VARIAS veces el mismo hecho —reintentos de entrega, una notificación
 * por paso— y cada aviso deja su fila en la bitácora. Para quien gestiona eso es un solo hito: la
 * persona completó la validación una vez. Sin colapsarlos, el panel repetía «X completó la
 * validación» tres veces con el mismo minuto, que es ruido de integración disfrazado de historia.
 *
 * <p>Se colapsan solo los avisos CONSECUTIVOS de la misma etapa: si entre dos hay un reenvío o un
 * resultado, son intentos distintos de verdad y los dos cuentan.</p>
 */
function colapsarRepetidos(eventos: readonly IdentityAuditEvent[]): IdentityAuditEvent[] {
  const orden = [...eventos].sort(
    (a, b) => new Date(a.occurredAt).getTime() - new Date(b.occurredAt).getTime(),
  );
  const salida: IdentityAuditEvent[] = [];
  for (const e of orden) {
    const anterior = salida[salida.length - 1];
    if (anterior && anterior.stage === e.stage && e.stage === 'webhook_received') continue;
    salida.push(e);
  }
  return salida;
}

/**
 * Los hitos, del más reciente al más antiguo.
 *
 * <p>Cada uno nombra a una persona, no a una etapa. Los tiempos van en relativo donde ayudan
 * —«9 minutos después de recibir el enlace» dice más que dos marcas de tiempo que hay que
 * restar—, y el momento en que la persona respondió se mide contra el envío, no contra hoy.</p>
 */
export function hitosDeIdentidad(
  v: BiometricValidation,
  eventos: readonly IdentityAuditEvent[],
): HitoIdentidad[] {
  const visibles = colapsarRepetidos(eventos.filter((e) => ETAPAS_VISIBLES.has(e.stage)));
  const envio = [...visibles]
    .reverse()
    .find((e) => e.stage === 'send' || e.stage === 'resend')?.occurredAt;

  const nombre = primerNombre(v.name);

  const hitos = visibles.map((e, i): HitoIdentidad => {
    const base = { id: `${e.stage}-${e.occurredAt}-${i}`, cuando: e.occurredAt };

    switch (e.stage) {
      case 'send':
      case 'resend':
        return {
          ...base,
          titulo: e.stage === 'resend' ? 'Se reenvió el enlace' : 'Se envió el enlace',
          // El correo es de la persona, no del sistema: decirlo permite detectar el típico
          // «no le llega» que en realidad es una dirección mal escrita.
          detalle: v.email?.trim() ? `A ${v.email.trim()}` : null,
          tono: 'neutro',
        };

      case 'send_error':
        return {
          ...base,
          titulo: 'No se pudo enviar el enlace',
          detalle: 'Conviene revisar el correo de la persona y volver a intentarlo.',
          tono: 'alerta',
        };

      case 'contact_edited':
        return {
          ...base,
          titulo: 'Se corrigieron sus datos de contacto',
          detalle: null,
          tono: 'neutro',
        };

      case 'expired':
        return {
          ...base,
          titulo: 'El enlace venció sin respuesta',
          detalle: null,
          tono: 'alerta',
        };

      case 'webhook_received':
        return {
          ...base,
          titulo: `${nombre} completó la validación`,
          detalle: envio ? `${capitalizar(transcurridoEntre(envio, e.occurredAt))} de recibir el enlace` : null,
          tono: 'neutro',
        };

      case 'webhook_applied':
      default: {
        const aprobado = OUTCOMES_CORRECTOS.has(e.outcome) || e.outcome === 'aprobado';
        return {
          ...base,
          titulo: aprobado ? 'Identidad aprobada' : 'Identidad rechazada',
          detalle: aprobado ? null : v.rejectionReason?.trim() || null,
          tono: aprobado ? 'ok' : 'alerta',
        };
      }
    }
  });

  return hitos.sort((a, b) => new Date(b.cuando).getTime() - new Date(a.cuando).getTime());
}

/** «hace 9 minutos» / «hace 2 días». Para contar cuánto lleva algo esperando. */
export function transcurrido(desdeIso: string, hastaIso: string): string {
  const texto = transcurridoEntre(desdeIso, hastaIso);
  return texto === 'en el mismo momento' ? 'hace un momento' : `hace ${sinDespues(texto)}`;
}

/** «9 minutos después» / «2 días después». Para relacionar dos hitos entre sí. */
export function transcurridoEntre(desdeIso: string, hastaIso: string): string {
  const desde = new Date(desdeIso).getTime();
  const hasta = new Date(hastaIso).getTime();
  if (Number.isNaN(desde) || Number.isNaN(hasta)) return '';

  const minutos = Math.round(Math.abs(hasta - desde) / 60000);
  if (minutos < 1) return 'en el mismo momento';
  if (minutos < 60) return `${minutos} ${plural(minutos, 'minuto')} después`;

  const horas = Math.round(minutos / 60);
  if (horas < 24) return `${horas} ${plural(horas, 'hora')} después`;

  const dias = Math.round(horas / 24);
  return `${dias} ${plural(dias, 'día')} después`;
}

function sinDespues(texto: string): string {
  return texto.replace(/ después$/, '');
}

function plural(n: number, palabra: string): string {
  return n === 1 ? palabra : `${palabra}s`;
}

function capitalizar(texto: string): string {
  return texto ? texto.charAt(0).toLocaleUpperCase('es') + texto.slice(1) : texto;
}

function minuscula(texto: string): string {
  return texto ? texto.charAt(0).toLocaleLowerCase('es') + texto.slice(1) : texto;
}
