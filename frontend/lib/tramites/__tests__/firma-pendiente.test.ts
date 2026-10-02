// Bug #13194 (P4) — mapeo único del 409 `firma_pendiente` (gate de firma hacia el OT).
// Uso de ejemplo:
//   mensajeFirmaPendiente({ title: 'firma_pendiente', partes: [{ parte: 'comprador', notificacion: 'enviada' }] })
//   → 'Falta la firma del comprador. Enviamos el enlace de validación de identidad a su correo.'
import { describe, expect, it } from 'vitest';
import {
  esErrorFirmaPendiente,
  esFirmaPendiente,
  mensajeFirmaPendiente,
  partesFirmaPendiente,
} from '../firma-pendiente';

const DETAIL_TRANSITION =
  'No se permite enviar al organismo de tránsito un trámite sin firmar. Falta la firma ' +
  '(identidad aprobada y vigente) de: comprador, vendedor.';
const DETAIL_GENERICO =
  'No se permite enviar al organismo de tránsito un trámite sin firmar: falta la identidad aprobada y ' +
  'vigente de alguna de las partes que firman.';

describe('esFirmaPendiente', () => {
  it.each(['title', 'code', 'errorCode', 'error'])('reconoce el código en `%s`', (k) => {
    expect(esFirmaPendiente({ [k]: 'firma_pendiente' })).toBe(true);
  });

  it('no confunde otros códigos de identidad', () => {
    expect(esFirmaPendiente({ title: 'identidad_no_aprobada' })).toBe(false);
    expect(esFirmaPendiente(null)).toBe(false);
    expect(esFirmaPendiente(undefined)).toBe(false);
  });

  it('esErrorFirmaPendiente lee el `problem` del TramitesApiError', () => {
    expect(esErrorFirmaPendiente({ status: 409, problem: { title: 'firma_pendiente' } })).toBe(true);
    expect(esErrorFirmaPendiente(new Error('x'))).toBe(false);
    expect(esErrorFirmaPendiente(null)).toBe(false);
  });
});

describe('partesFirmaPendiente', () => {
  it('extrae las partes del detail de /transition (contrato actual)', () => {
    expect(partesFirmaPendiente({ title: 'firma_pendiente', detail: DETAIL_TRANSITION })).toEqual([
      { parte: 'comprador', notificacion: null },
      { parte: 'vendedor', notificacion: null },
    ]);
  });

  it('el detail genérico de /submit y /enviar-al-ot no inventa partes', () => {
    expect(partesFirmaPendiente({ title: 'firma_pendiente', detail: DETAIL_GENERICO })).toEqual([]);
  });

  it('acepta lista de objetos con notificación y lista de textos + mapa de notificaciones', () => {
    expect(
      partesFirmaPendiente({
        code: 'firma_pendiente',
        partes: [{ parte: 'Comprador', notificacion: 'enviada' }, { rol: 'vendedor', notificacionVid: 'fallida' }],
      }),
    ).toEqual([
      { parte: 'comprador', notificacion: 'enviada' },
      { parte: 'vendedor', notificacion: 'fallida' },
    ]);
    expect(
      partesFirmaPendiente({
        title: 'firma_pendiente',
        partesSinFirma: ['comprador'],
        notificaciones: { comprador: 'ya_en_curso' },
      }),
    ).toEqual([{ parte: 'comprador', notificacion: 'ya_en_curso' }]);
  });

  it('descarta roles desconocidos y estados de notificación fuera del contrato (nada crudo del servidor)', () => {
    expect(
      partesFirmaPendiente({
        title: 'firma_pendiente',
        partes: ['Juan Pérez 123', { parte: 'comprador', notificacion: 'algo_raro' }],
      }),
    ).toEqual([{ parte: 'comprador', notificacion: null }]);
  });
});

// Contrato EXACTO del backend (FirmaGate.Detalle + extensión `partesSinFirma`, review 4.ª vuelta PR #510).
const DETAIL_BACKEND_CON_NOTIFICACION =
  'No se permite enviar al organismo de tránsito un trámite sin firmar. Falta la firma ' +
  '(identidad aprobada y vigente) de: comprador (notificación: enviada), vendedor (notificación: fallida).';

describe('contrato del backend (FirmaGate)', () => {
  it('detail con «parte (notificación: estado)»: extrae parte y estado', () => {
    expect(partesFirmaPendiente({ title: 'firma_pendiente', detail: DETAIL_BACKEND_CON_NOTIFICACION })).toEqual([
      { parte: 'comprador', notificacion: 'enviada' },
      { parte: 'vendedor', notificacion: 'fallida' },
    ]);
  });

  it('detail con ya_en_curso y no_configurada', () => {
    expect(
      partesFirmaPendiente({
        title: 'firma_pendiente',
        detail:
          'No se permite enviar al organismo de tránsito un trámite sin firmar. Falta la firma ' +
          '(identidad aprobada y vigente) de: comprador (notificación: ya_en_curso), vendedor (notificación: no_configurada).',
      }),
    ).toEqual([
      { parte: 'comprador', notificacion: 'ya_en_curso' },
      { parte: 'vendedor', notificacion: 'no_configurada' },
    ]);
  });

  it('el mensaje del detail real avisa que el correo del vendedor falló (antes caía al genérico)', () => {
    expect(mensajeFirmaPendiente({ title: 'firma_pendiente', detail: DETAIL_BACKEND_CON_NOTIFICACION })).toBe(
      'Falta la firma del comprador y del vendedor. ' +
        'Enviamos el enlace de validación de identidad al correo del comprador. ' +
        'No pudimos enviar el enlace de validación al vendedor; usa «Validar identidad» en el paso 4 o la prevalidación del módulo Identidad.',
    );
  });

  it('la extensión `partesSinFirma` (forma exacta) manda sobre el detail', () => {
    const problem = {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
      title: 'firma_pendiente',
      status: 409,
      detail: DETAIL_BACKEND_CON_NOTIFICACION,
      partesSinFirma: [{ parte: 'comprador', notificacion: 'ya_en_curso' }],
    };
    expect(partesFirmaPendiente(problem)).toEqual([{ parte: 'comprador', notificacion: 'ya_en_curso' }]);
    expect(mensajeFirmaPendiente(problem)).toBe(
      'Falta la firma del comprador. Ya hay una validación en curso para el comprador.',
    );
  });

  it('extensión vacía: cae al detail', () => {
    expect(
      partesFirmaPendiente({ title: 'firma_pendiente', detail: DETAIL_BACKEND_CON_NOTIFICACION, partesSinFirma: [] }),
    ).toHaveLength(2);
  });
});

describe('mensajeFirmaPendiente', () => {
  it('genérico sin partes ni notificación (contrato actual de /submit y /enviar-al-ot)', () => {
    const msg = mensajeFirmaPendiente({ title: 'firma_pendiente', detail: DETAIL_GENERICO });
    expect(msg).toMatch(/^Falta la validación de identidad o firma de una de las partes/);
    expect(msg).toMatch(/paso 4 «Validación de Identidad»/);
  });

  it('nombra las partes del detail', () => {
    expect(mensajeFirmaPendiente({ title: 'firma_pendiente', detail: DETAIL_TRANSITION })).toMatch(
      /^Falta la firma del comprador y del vendedor\./,
    );
  });

  it('una parte con correo enviado', () => {
    expect(
      mensajeFirmaPendiente({ title: 'firma_pendiente', partes: [{ parte: 'comprador', notificacion: 'enviada' }] }),
    ).toBe('Falta la firma del comprador. Enviamos el enlace de validación de identidad al correo del comprador.');
  });

  it('validación ya en curso', () => {
    expect(
      mensajeFirmaPendiente({ title: 'firma_pendiente', partes: [{ parte: 'comprador', notificacion: 'ya_en_curso' }] }),
    ).toBe('Falta la firma del comprador. Ya hay una validación en curso para el comprador.');
  });

  it.each(['fallida', 'no_configurada'])('correo %s: no pudimos enviar el enlace → paso 4 o módulo Identidad', (estado) => {
    expect(
      mensajeFirmaPendiente({ title: 'firma_pendiente', partes: [{ parte: 'vendedor', notificacion: estado }] }),
    ).toBe(
      'Falta la firma del vendedor. No pudimos enviar el enlace de validación al vendedor; usa «Validar identidad» en el paso 4 o la prevalidación del módulo Identidad.',
    );
  });

  it('varias partes: cada notificación nombra a su parte; no_requerida no agrega frase', () => {
    const msg = mensajeFirmaPendiente({
      title: 'firma_pendiente',
      partes: [
        { parte: 'vendedor', notificacion: 'enviada' },
        { parte: 'comprador', notificacion: 'no_requerida' },
      ],
    });
    expect(msg).toBe(
      'Falta la firma del vendedor y del comprador. Enviamos el enlace de validación de identidad al correo del vendedor.',
    );
  });

  it('en asignado: sin notificación remite al módulo Identidad o al baúl del RL, no al paso 4', () => {
    const msg = mensajeFirmaPendiente({ title: 'firma_pendiente' }, 'asignado');
    expect(msg).not.toMatch(/paso 4/);
    expect(msg).toMatch(/módulo Identidad/);
    expect(msg).toMatch(/baúl de firmas del representante legal/);
    expect(msg).toMatch(/se envía automáticamente/);
    const fallida = mensajeFirmaPendiente(
      { title: 'firma_pendiente', partes: [{ parte: 'comprador', notificacion: 'fallida' }] },
      'asignado',
    );
    expect(fallida).toContain(
      'No pudimos enviar el enlace de validación al comprador; usa «Validar identidad» en el paso 4 o la prevalidación del módulo Identidad.',
    );
    expect(fallida).not.toMatch(/se envía automáticamente/);
  });

  it('nunca devuelve vacío, ni con problem nulo', () => {
    expect(mensajeFirmaPendiente(null).length).toBeGreaterThan(0);
  });
});
