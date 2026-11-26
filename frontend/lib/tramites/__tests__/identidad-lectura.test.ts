import { describe, expect, it } from 'vitest';
import {
  estadoDeIdentidad,
  hitosDeIdentidad,
  transcurrido,
  transcurridoEntre,
} from '../identidad-lectura';
import type { BiometricValidation, IdentityAuditEvent } from '@/lib/api/types/procedure-runtime';

/**
 * HU #12186 — la lectura humana de la validación de identidad.
 *
 * Este módulo es la FRONTERA: lo que no sale de aquí no llega a pantalla. Por eso el test que más
 * importa no es ninguno de los de contenido, sino el que barre toda la salida buscando cualquier
 * rastro de servicio externo.
 */
function validacion(parcial: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'v1',
    partyRole: 'comprador',
    name: 'Laura Restrepo Ossa',
    documentType: 'CC',
    documentNumber: '1020998455',
    email: 'l.restrepo@correo.com',
    status: 'en_proceso',
    intentos: 0,
    maxIntentos: 3,
    score: null,
    expiresAt: '2026-08-28T11:42:00Z',
    validatedAt: null,
    expired: false,
    provider: 'kyverum',
    captureUrl: null,
    createdAt: '2026-08-27T11:42:00Z',
    ...parcial,
  } as BiometricValidation;
}

function evento(stage: string, occurredAt: string, parcial: Partial<IdentityAuditEvent> = {}): IdentityAuditEvent {
  return {
    occurredAt,
    stage,
    outcome: 'ok',
    httpStatus: 200,
    signaturePresent: null,
    secretPresent: null,
    decryptOk: null,
    providerStatus: null,
    errorType: null,
    message: null,
    ...parcial,
  };
}

describe('estadoDeIdentidad', () => {
  it('aprobada: lo dice y no pide hacer nada', () => {
    const estado = estadoDeIdentidad(validacion({ status: 'aprobado' }));
    expect(estado.label).toBe('Identidad aprobada');
    expect(estado.tono).toBe('ok');
    expect(estado.accion).toBeNull();
  });

  it('rechazada: dice el motivo Y qué hacer', () => {
    // «Rechazado» a secas obliga a llamar a soporte. Con el motivo y la salida, el gestor resuelve.
    const estado = estadoDeIdentidad(
      validacion({ status: 'rechazado', rejectionReason: 'La foto no coincide con el documento' }),
    );
    expect(estado.label).toBe('Identidad rechazada');
    expect(estado.explicacion).toBe('La foto no coincide con el documento');
    expect(estado.accion).toMatch(/enlace nuevo/i);
  });

  it('rechazada sin motivo del backend: no se inventa uno, pero sigue ofreciendo salida', () => {
    const estado = estadoDeIdentidad(validacion({ status: 'rechazado', rejectionReason: null }));
    expect(estado.explicacion).toBe('La validación no se pudo completar.');
    expect(estado.accion).not.toBeNull();
  });

  it('vencida: lo distingue de un rechazo', () => {
    const estado = estadoDeIdentidad(validacion({ expired: true }));
    expect(estado.label).toBe('Enlace vencido');
    expect(estado.tono).toBe('alerta');
  });

  it('pendiente: dice cuánto lleva esperando, no solo que está pendiente', () => {
    const estado = estadoDeIdentidad(
      validacion({ createdAt: '2026-08-25T11:42:00Z' }),
      new Date('2026-08-27T11:42:00Z'),
    );
    expect(estado.label).toBe('A la espera de la persona');
    expect(estado.explicacion).toContain('hace 2 días');
    expect(estado.accion).toMatch(/reenviar/i);
  });

  it('pendiente con un intento fallido: lo cuenta en lenguaje corriente', () => {
    const estado = estadoDeIdentidad(
      validacion({ ultimoIntentoMotivo: 'Rostro no completamente visible' }),
    );
    expect(estado.explicacion).toContain('rostro no completamente visible');
  });
});

describe('hitosDeIdentidad', () => {
  const eventos = [
    evento('send', '2026-08-27T11:42:00Z'),
    evento('send_response', '2026-08-27T11:42:03Z'),
    evento('webhook_received', '2026-08-27T11:51:00Z', { outcome: 'received' }),
    evento('webhook_applied', '2026-08-27T11:51:01Z', { outcome: 'aprobado' }),
  ];

  it('cada hito nombra a la persona, no a una etapa del sistema', () => {
    const hitos = hitosDeIdentidad(validacion(), eventos);
    expect(hitos.map((h) => h.titulo)).toContain('Laura completó la validación');
  });

  it('mide la respuesta contra el envío, no contra hoy', () => {
    const hitos = hitosDeIdentidad(validacion(), eventos);
    const respuesta = hitos.find((h) => h.titulo.includes('completó'));
    expect(respuesta?.detalle).toBe('9 minutos después de recibir el enlace');
  });

  it('el más reciente va primero', () => {
    const hitos = hitosDeIdentidad(validacion({ status: 'aprobado' }), eventos);
    expect(hitos[0].titulo).toBe('Identidad aprobada');
  });

  it('descarta las etapas de integración', () => {
    // `send_response` es una etapa de integración: no cuenta nada que el gestor pueda hacer.
    const hitos = hitosDeIdentidad(validacion(), eventos);
    expect(hitos).toHaveLength(3);
  });

  it('un rechazo lleva su motivo en el hito', () => {
    const hitos = hitosDeIdentidad(
      validacion({ status: 'rechazado', rejectionReason: 'La foto no coincide con el documento' }),
      [evento('webhook_applied', '2026-08-27T11:51:00Z', { outcome: 'rechazado' })],
    );
    expect(hitos[0].titulo).toBe('Identidad rechazada');
    expect(hitos[0].detalle).toBe('La foto no coincide con el documento');
    expect(hitos[0].tono).toBe('alerta');
  });

  it('un fallo de envío se cuenta y sugiere qué mirar', () => {
    const hitos = hitosDeIdentidad(validacion(), [
      evento('send_error', '2026-08-27T11:42:00Z', { outcome: 'error' }),
    ]);
    expect(hitos[0].titulo).toBe('No se pudo enviar el enlace');
    expect(hitos[0].detalle).toMatch(/correo de la persona/i);
  });

  it('el reenvío se distingue del envío', () => {
    const hitos = hitosDeIdentidad(validacion(), [
      evento('resend', '2026-08-27T12:00:00Z'),
      evento('send', '2026-08-27T11:42:00Z'),
    ]);
    expect(hitos[0].titulo).toBe('Se reenvió el enlace');
    expect(hitos[1].titulo).toBe('Se envió el enlace');
  });

  it('sin eventos no lanza', () => {
    expect(hitosDeIdentidad(validacion(), [])).toEqual([]);
  });

  /**
   * El test que gobierna la HU. Barre TODO lo que este módulo produce —estado, explicación, acción
   * y cada hito— buscando cualquier rastro de servicio externo o de vocabulario de integración.
   *
   * Si mañana alguien mapea una etapa nueva y arrastra el nombre del proveedor o un código HTTP,
   * este test lo detiene aquí, que es la frontera, y no en una revisión de pantalla.
   */
  it('nada de lo que sale nombra un servicio externo ni habla de integración', () => {
    const prohibido = [
      'kyverum',
      'proveedor',
      'webhook',
      'http',
      'cifrad',
      'endpoint',
      'api',
      'token',
      'firma de notificación',
      'descifrar',
    ];

    const todasLasEtapas = [
      'send',
      'send_response',
      'send_error',
      'webhook_received',
      'webhook_not_verifiable',
      'webhook_signature_invalid',
      'webhook_applied',
      'reconcile',
      'expired',
      'contact_edited',
      'resend',
    ].map((stage, i) =>
      evento(stage, `2026-08-27T1${i}:00:00Z`, {
        outcome: 'rechazado',
        providerStatus: 'KYVERUM_FAILED',
        errorType: 'decrypt_failed',
        message: 'Kyverum devolvió HTTP 502 al descifrar el webhook',
      }),
    );

    for (const status of ['en_proceso', 'aprobado', 'rechazado'] as const) {
      const v = validacion({ status, expired: status === 'en_proceso' });
      const estado = estadoDeIdentidad(v);
      const hitos = hitosDeIdentidad(v, todasLasEtapas);

      const salida = [
        estado.label,
        estado.explicacion,
        estado.accion,
        ...hitos.flatMap((h) => [h.titulo, h.detalle]),
      ]
        .filter(Boolean)
        .join(' | ')
        .toLowerCase();

      for (const palabra of prohibido) {
        expect(salida, `«${palabra}» no puede llegar a pantalla (estado ${status})`).not.toContain(
          palabra,
        );
      }
    }
  });
});

describe('tiempos en relativo', () => {
  it('cuenta la espera desde un momento hasta otro', () => {
    expect(transcurrido('2026-08-27T11:42:00Z', '2026-08-27T11:51:00Z')).toBe('hace 9 minutos');
    expect(transcurrido('2026-08-27T11:42:00Z', '2026-08-27T13:42:00Z')).toBe('hace 2 horas');
    expect(transcurrido('2026-08-25T11:42:00Z', '2026-08-27T11:42:00Z')).toBe('hace 2 días');
  });

  it('el singular no dice «1 minutos»', () => {
    expect(transcurridoEntre('2026-08-27T11:42:00Z', '2026-08-27T11:43:00Z')).toBe(
      '1 minuto después',
    );
    expect(transcurridoEntre('2026-08-27T11:42:00Z', '2026-08-27T12:42:00Z')).toBe('1 hora después');
  });

  it('lo instantáneo no se cuenta en «0 minutos»', () => {
    expect(transcurrido('2026-08-27T11:42:00Z', '2026-08-27T11:42:10Z')).toBe('hace un momento');
  });

  it('una fecha ilegible no rompe la línea', () => {
    expect(transcurridoEntre('no-es-fecha', '2026-08-27T11:42:00Z')).toBe('');
  });
});

describe('avisos repetidos del proveedor', () => {
  // Kyverum notifica varias veces el mismo hecho —reintentos de entrega, un aviso por paso— y cada
  // uno deja fila en la bitácora. En dev se veía «X completó la validación» tres veces con el mismo
  // minuto: para el gestor es UN hito, no tres.

  it('varios avisos seguidos de la misma etapa se cuentan como un solo hito', () => {
    const hitos = hitosDeIdentidad(validacion(), [
      evento('send', '2026-08-27T11:42:00Z'),
      evento('webhook_received', '2026-08-27T11:51:00Z', { outcome: 'received' }),
      evento('webhook_received', '2026-08-27T11:51:00Z', { outcome: 'received' }),
      evento('webhook_received', '2026-08-27T11:51:30Z', { outcome: 'received' }),
    ]);

    expect(hitos.filter((h) => h.titulo === 'Laura completó la validación')).toHaveLength(1);
    // El envío no se pierde por el camino.
    expect(hitos.map((h) => h.titulo)).toContain('Se envió el enlace');
  });

  it('dos respuestas separadas por un reenvío SÍ son dos intentos distintos', () => {
    // El colapso es solo de avisos consecutivos: si en medio pasó algo, son hechos distintos y los
    // dos cuentan. Colapsarlos borraría un reintento real de la persona.
    const hitos = hitosDeIdentidad(validacion(), [
      evento('send', '2026-08-27T11:42:00Z'),
      evento('webhook_received', '2026-08-27T11:51:00Z', { outcome: 'received' }),
      evento('resend', '2026-08-27T12:10:00Z'),
      evento('webhook_received', '2026-08-27T12:20:00Z', { outcome: 'received' }),
    ]);

    expect(hitos.filter((h) => h.titulo === 'Laura completó la validación')).toHaveLength(2);
    expect(hitos.map((h) => h.titulo)).toContain('Se reenvió el enlace');
  });
});


describe('flujo manual', () => {
  it('manual_activo espera las fotos y la firma, sin hablar de reenvío de terceros', () => {
    const e = estadoDeIdentidad(validacion({ status: 'manual_activo', provider: 'manual' }));
    expect(e.label).toBe('A la espera de la persona');
    expect(e.explicacion).toMatch(/fotos y su firma/);
  });

  it('pendiente_revision_manual dice que falta la decisión de FLIT', () => {
    const e = estadoDeIdentidad(validacion({ status: 'pendiente_revision_manual', provider: 'manual' }));
    expect(e.label).toBe('Pendiente de revisión');
    expect(e.tono).toBe('pendiente');
    expect(e.explicacion).toMatch(/decisión de FLIT/);
  });

  it('cuenta el recorrido manual con la persona como sujeto', () => {
    const v = validacion({ provider: 'manual', status: 'aprobado' });
    const hitos = hitosDeIdentidad(v, [
      evento('manual_activado', '2026-08-27T11:42:00Z'),
      evento('manual_consentimiento', '2026-08-27T11:45:00Z'),
      evento('manual_captura_recibida', '2026-08-27T11:51:00Z'),
      evento('manual_imagenes_consultadas', '2026-08-27T11:55:00Z'),
      evento('manual_aprobado', '2026-08-27T12:00:00Z', { outcome: 'aprobado' }),
    ]);
    expect(hitos.map((h) => h.titulo)).toEqual([
      'Identidad aprobada',
      'Laura envió sus fotos y su firma',
      'Se envió el enlace',
    ]);
    expect(hitos[0].tono).toBe('ok');
    expect(hitos[2].detalle).toBe('A l.restrepo@correo.com');
    expect(hitos[1].detalle).toBe('9 minutos después de recibir el enlace');
  });

  it('un rechazo manual lleva el motivo y el enlace regenerado se lee como reenvío', () => {
    const v = validacion({ provider: 'manual', rejectionReason: 'Firma ilegible' });
    const hitos = hitosDeIdentidad(v, [
      evento('manual_rechazado', '2026-08-27T12:00:00Z', { outcome: 'rechazado' }),
      evento('manual_enlace_regenerado', '2026-08-27T12:01:00Z'),
      evento('manual_enlace_copiado', '2026-08-27T12:02:00Z'),
      evento('manual_correo_fallido', '2026-08-27T12:03:00Z', { outcome: 'error' }),
    ]);
    expect(hitos.map((h) => h.titulo)).toEqual([
      'No se pudo enviar el enlace',
      'Se reenvió el enlace',
      'Identidad rechazada',
    ]);
    expect(hitos[2].detalle).toBe('Firma ilegible');
    expect(hitos[2].tono).toBe('alerta');
  });

  it('Bug #14634 — un rechazo manual YA reintentado conserva su motivo en el hito histórico', () => {
    // La validación VIGENTE ya se volvió a capturar y se aprobó: rejectionReason quedó en null (lo
    // borra RegistrarCapturaManual). El motivo de ESE rechazo puntual debe salir del propio evento
    // (rejectionReasonLabel), fijo en la bitácora, no de v.rejectionReason.
    const v = validacion({ provider: 'manual', status: 'aprobado', rejectionReason: null });
    const hitos = hitosDeIdentidad(v, [
      evento('manual_rechazado', '2026-08-27T12:00:00Z', {
        outcome: 'rechazado',
        rejectionReasonLabel: 'Documento ilegible o incompleto',
      }),
      evento('manual_aprobado', '2026-08-27T12:10:00Z', { outcome: 'aprobado' }),
    ]);

    const rechazo = hitos.find((h) => h.titulo === 'Identidad rechazada');
    expect(rechazo?.detalle).toBe('Documento ilegible o incompleto');
  });

  it('con revisor: «Revisado por nombre (correo, rol)» aparte del motivo', () => {
    const v = validacion({ provider: 'manual', status: 'rechazado' });
    const hitos = hitosDeIdentidad(v, [
      evento('manual_rechazado', '2026-08-27T12:00:00Z', {
        outcome: 'rechazado',
        rejectionReasonLabel: 'Firma ilegible',
        revisor: { name: 'Ana Pérez', email: 'ana@flit.co', role: 'Super Admin' },
      }),
      evento('manual_aprobado', '2026-08-27T12:10:00Z', {
        outcome: 'aprobado',
        revisor: { name: 'Luis Gómez', email: null, role: 'Super Admin' },
      }),
    ]);
    const [aprobado, rechazado] = hitos;
    expect(aprobado.revisadoPor).toBe('Revisado por Luis Gómez (Super Admin)');
    expect(rechazado.detalle).toBe('Firma ilegible');
    expect(rechazado.revisadoPor).toBe('Revisado por Ana Pérez (ana@flit.co, Super Admin)');
    expect(`${aprobado.revisadoPor} ${rechazado.revisadoPor}`).not.toMatch(/null|undefined/);
  });

  it('con activador: «Flujo manual activado por …» en el hito de activación; sin él no hay línea', () => {
    const v = validacion({ provider: 'manual', status: 'manual_activo' });
    const [con, sin] = hitosDeIdentidad(v, [
      evento('manual_activado', '2026-08-27T11:00:00Z', {
        revisor: { name: 'Ana Pérez', email: 'ana@flit.co', role: 'Super Admin' },
      }),
      evento('manual_activado', '2026-08-27T10:00:00Z'),
    ]);
    expect(con.activadoPor).toBe('Flujo manual activado por Ana Pérez (ana@flit.co, Super Admin)');
    expect(sin.activadoPor).toBeUndefined();
  });

  it('sin revisor (o vacío) no agrega la línea', () => {
    const v = validacion({ provider: 'manual', status: 'aprobado' });
    const hitos = hitosDeIdentidad(v, [
      evento('manual_aprobado', '2026-08-27T12:00:00Z', { outcome: 'aprobado' }),
      evento('manual_aprobado', '2026-08-27T12:05:00Z', {
        outcome: 'aprobado',
        revisor: { name: null, email: ' ', role: null },
      }),
    ]);
    for (const h of hitos) expect(h.revisadoPor).toBeUndefined();
  });
});
