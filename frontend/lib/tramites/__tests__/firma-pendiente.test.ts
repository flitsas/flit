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
    ).toBe('Falta la firma del comprador. Enviamos el enlace de validación de identidad a su correo.');
  });

  it('validación ya en curso y correo fallido (asistente → paso 4)', () => {
    expect(
      mensajeFirmaPendiente({ title: 'firma_pendiente', partes: [{ parte: 'comprador', notificacion: 'ya_en_curso' }] }),
    ).toMatch(/Ya hay una validación de identidad en curso; espera a que termine\./);
    expect(
      mensajeFirmaPendiente({ title: 'firma_pendiente', partes: [{ parte: 'vendedor', notificacion: 'fallida' }] }),
    ).toMatch(/No se pudo enviar el correo de validación: usa «Validar identidad» en el paso 4\./);
  });

  it('varias partes: cada notificación nombra a su parte', () => {
    const msg = mensajeFirmaPendiente({
      title: 'firma_pendiente',
      partes: [
        { parte: 'vendedor', notificacion: 'enviada' },
        { parte: 'comprador', notificacion: 'no_requerida' },
      ],
    });
    expect(msg).toMatch(/^Falta la firma del vendedor y del comprador\./);
    expect(msg).toMatch(/al correo del vendedor\./);
  });

  it('en asignado no remite al paso 4: prevalidación del módulo Identidad o baúl del RL', () => {
    const msg = mensajeFirmaPendiente({ title: 'firma_pendiente' }, 'asignado');
    expect(msg).not.toMatch(/paso 4/);
    expect(msg).toMatch(/módulo Identidad/);
    expect(msg).toMatch(/baúl de firmas del representante legal/);
    expect(msg).toMatch(/se envía automáticamente/);
    const fallida = mensajeFirmaPendiente(
      { title: 'firma_pendiente', partes: [{ parte: 'comprador', notificacion: 'fallida' }] },
      'asignado',
    );
    expect(fallida).toMatch(/inicia la prevalidación desde el módulo Identidad/);
    expect(fallida).not.toMatch(/se envía automáticamente/);
  });

  it('nunca devuelve vacío, ni con problem nulo', () => {
    expect(mensajeFirmaPendiente(null).length).toBeGreaterThan(0);
  });
});
