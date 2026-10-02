// Bug #13194 (P4) — el cliente traduce el 409 `firma_pendiente` en TODAS las rutas hacia el OT
// (submit, enviar-al-ot, transition) con el mismo helper, y conserva el código en `.problem`.
// Uso de ejemplo:
//   try { await tramitesClient.submitInstance(id) } catch (e) { esErrorFirmaPendiente(e) → true }
import { afterEach, describe, expect, it } from 'vitest';
import { TramitesApiError, tramitesClient } from '../tramites-client';
import { esErrorFirmaPendiente } from '@/lib/tramites/firma-pendiente';

const originalFetch = globalThis.fetch;

function respondWith(status: number, body: unknown) {
  globalThis.fetch = (async () =>
    new Response(JSON.stringify(body), {
      status,
      headers: { 'Content-Type': 'application/problem+json' },
    })) as typeof fetch;
}

async function capturar(p: Promise<unknown>): Promise<unknown> {
  try {
    await p;
  } catch (e) {
    return e;
  }
  throw new Error('la llamada debía fallar');
}

afterEach(() => {
  globalThis.fetch = originalFetch;
});

const GENERICO = {
  title: 'firma_pendiente',
  status: 409,
  detail:
    'No se permite enviar al organismo de tránsito un trámite sin firmar: falta la identidad aprobada y vigente de alguna de las partes que firman.',
};

describe('tramites-client — 409 firma_pendiente', () => {
  it('submit: mensaje genérico de firma y código accesible', async () => {
    respondWith(409, GENERICO);
    const err = await capturar(tramitesClient.submitInstance('inst-1'));
    expect(err).toBeInstanceOf(TramitesApiError);
    expect((err as Error).message).toMatch(/^Falta la validación de identidad o firma de una de las partes/);
    expect(esErrorFirmaPendiente(err)).toBe(true);
  });

  it('enviar-al-ot: mismo mapeo', async () => {
    respondWith(409, GENERICO);
    const err = await capturar(tramitesClient.enviarAlOt('inst-1', {}));
    expect(esErrorFirmaPendiente(err)).toBe(true);
    expect((err as Error).message).toMatch(/firma de una de las partes/);
  });

  it('transition: nombra las partes del detail y lanza TramitesApiError', async () => {
    respondWith(409, {
      title: 'firma_pendiente',
      status: 409,
      detail:
        'No se permite enviar al organismo de tránsito un trámite sin firmar. Falta la firma (identidad aprobada y vigente) de: comprador.',
    });
    const err = await capturar(tramitesClient.transitionInstance('inst-1', 'preparado'));
    expect(err).toBeInstanceOf(TramitesApiError);
    expect((err as TramitesApiError).status).toBe(409);
    expect((err as Error).message).toMatch(/^Falta la firma del comprador\./);
  });

  // Contrato EXACTO del backend (FirmaGate.Detalle + extensión `partesSinFirma`).
  it('transition: detail real «parte (notificación: estado)» → nombra el correo fallido', async () => {
    respondWith(409, {
      title: 'firma_pendiente',
      status: 409,
      detail:
        'No se permite enviar al organismo de tránsito un trámite sin firmar. Falta la firma (identidad aprobada y vigente) de: comprador (notificación: enviada), vendedor (notificación: fallida).',
    });
    const err = await capturar(tramitesClient.transitionInstance('inst-1', 'preparado'));
    expect((err as Error).message).toBe(
      'Falta la firma del comprador y del vendedor. ' +
        'Enviamos el enlace de validación de identidad al correo del comprador. ' +
        'No pudimos enviar el enlace de validación al vendedor; usa «Validar identidad» en el paso 4 o la prevalidación del módulo Identidad.',
    );
  });

  it.each([
    ['submit', () => tramitesClient.submitInstance('inst-1')],
    ['enviar-al-ot', () => tramitesClient.enviarAlOt('inst-1', {})],
    ['transition', () => tramitesClient.transitionInstance('inst-1', 'entregado')],
  ])('%s: la extensión partesSinFirma manda sobre el detail', async (_ruta, llamar) => {
    respondWith(409, {
      ...GENERICO,
      partesSinFirma: [{ parte: 'comprador', notificacion: 'fallida' }],
    });
    const err = await capturar(llamar());
    expect(esErrorFirmaPendiente(err)).toBe(true);
    expect((err as Error).message).toBe(
      'Falta la firma del comprador. No pudimos enviar el enlace de validación al comprador; usa «Validar identidad» en el paso 4 o la prevalidación del módulo Identidad.',
    );
  });

  it('transition: los demás códigos conservan su copy (regresión)', async () => {
    respondWith(409, { title: 'identidad_no_aprobada', status: 409, detail: 'x' });
    await expect(tramitesClient.transitionInstance('inst-1', 'preparado')).rejects.toThrow(
      'La validación de identidad del comprador no está aprobada.',
    );
  });

  it('cuerpo no-JSON del gateway: no se confunde con firma_pendiente', async () => {
    globalThis.fetch = (async () => new Response('<html>bad gateway</html>', { status: 502 })) as typeof fetch;
    const err = await capturar(tramitesClient.transitionInstance('inst-1', 'preparado'));
    expect(esErrorFirmaPendiente(err)).toBe(false);
    expect((err as Error).message).toMatch(/no está disponible/);
  });
});

describe('tramites-client — persistOcrFields expone el código del 409 (Bug #13194 P3)', () => {
  it('soporte_soat_requerido llega en .problem.title', async () => {
    respondWith(409, {
      title: 'soporte_soat_requerido',
      status: 409,
      detail: 'Carga primero el PDF del SOAT en el trámite para registrar su lectura.',
    });
    const err = await capturar(tramitesClient.persistOcrFields('inst-1', 'soat', { fecha_vencimiento: '2027-01-01' }));
    expect(err).toBeInstanceOf(TramitesApiError);
    expect((err as TramitesApiError).problem?.title).toBe('soporte_soat_requerido');
    expect((err as Error).message).toBe('Carga primero el PDF del SOAT en el trámite para registrar su lectura.');
  });
});
