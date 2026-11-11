import { afterEach, describe, expect, it } from 'vitest';
import { TramitesApiError, tramitesClient } from '../tramites-client';
import {
  ESTADOS_SIN_REGENERAR_MANDATO,
  mensajeErrorRegenerarMandato,
} from '@/lib/tramites/regenerar-mandato';

/**
 * HU #14547 — «Regenerar mandato y consolidado» (solo SuperAdmin).
 *
 * Uso de ejemplo: `await tramitesClient.regenerarMandatoYConsolidado('inst-1', 't-1')`
 * → `POST /api/v1/admin/tramites/inst-1/mandato/regenerar` con `X-Tenant-Id: t-1`.
 */
describe('tramites-client — regenerarMandatoYConsolidado', () => {
  const originalFetch = globalThis.fetch;
  const calls: { url: string; init?: RequestInit }[] = [];

  afterEach(() => {
    globalThis.fetch = originalFetch;
    calls.length = 0;
  });

  function respond(status: number, body: unknown) {
    globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ url: String(input), init });
      return new Response(JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' },
      });
    }) as typeof fetch;
  }

  const ok = {
    mandato: { attachmentId: 'm-1', firmanteAnteriorId: 'f-0', firmanteNuevoId: 'f-1', sinFirmar: false },
    consolidado: { attachmentId: 'c-1', generadoEn: '2026-11-11T10:00:00Z' },
    maestro: { estado: 'radicado_conservado', attachmentId: 'x-1', generadoEn: null },
    avisos: [{ codigo: 'maestro_radicado_conservado', mensaje: 'El OT conserva el expediente radicado.' }],
  };

  it('hace POST a /admin/tramites/{id}/mandato/regenerar con el X-Tenant-Id de la fila', async () => {
    respond(200, ok);
    const res = await tramitesClient.regenerarMandatoYConsolidado('inst-1', 't-1');
    expect(new URL(calls[0].url, 'http://x').pathname).toBe('/api/v1/admin/tramites/inst-1/mandato/regenerar');
    expect(calls[0].init?.method).toBe('POST');
    const headers = new Headers(calls[0].init?.headers);
    expect(headers.get('X-Tenant-Id')).toBe('t-1');
    expect(res.avisos[0].mensaje).toBe('El OT conserva el expediente radicado.');
    expect(res.maestro.estado).toBe('radicado_conservado');
  });

  it('un 409 llega como TramitesApiError con status y problem', async () => {
    respond(409, { title: 'Conflict', detail: 'estado_no_permite_regenerar_mandato', status: 409 });
    const err = await tramitesClient.regenerarMandatoYConsolidado('inst-1', 't-1').catch((e) => e);
    expect(err).toBeInstanceOf(TramitesApiError);
    expect((err as TramitesApiError).status).toBe(409);
  });
});

describe('mensajeErrorRegenerarMandato — textos de negocio, nunca códigos crudos', () => {
  const e = (status: number, problem: Record<string, unknown> | null, message = 'x') =>
    new TramitesApiError(status, message, problem);

  it.each([
    ['estado final', e(409, { title: 'Conflict', detail: 'estado_no_permite_regenerar_mandato' }, 'estado_no_permite_regenerar_mandato'), /aprobado, anulado o revocado/],
    ['código en `code`', e(409, { title: 'Conflict', code: 'estado_no_permite_regenerar_mandato', detail: 'Conflict' }), /aprobado, anulado o revocado/],
    ['migrado', e(409, { title: 'Conflict', detail: 'migrado_solo_lectura' }, 'migrado_solo_lectura'), /solo lectura/],
    ['organismo', e(409, { title: 'organismo_requerido' }, 'organismo_requerido'), /organismo de tránsito/],
    ['403', e(403, null, '403 Forbidden'), /Solo un SuperAdmin/],
    ['503', e(503, null, 'El servicio no está disponible'), /almacenamiento de archivos/],
    ['storage_unavailable', e(503, { title: 'storage_unavailable' }, 'storage_unavailable'), /almacenamiento de archivos/],
    ['404', e(404, { title: 'Not Found', detail: 'Procedure instance not found.' }), /No se encontró el trámite/],
    ['400', e(400, { title: 'Bad Request', detail: 'Falta header X-Tenant-Id' }), /compañía del trámite/],
  ])('%s', (_caso, err, esperado) => {
    const msg = mensajeErrorRegenerarMandato(err);
    expect(msg).toMatch(esperado);
    expect(msg).not.toMatch(/[a-z]+_[a-z_]+/);
  });

  it('un 409 con detalle legible del backend se muestra tal cual', () => {
    const msg = mensajeErrorRegenerarMandato(
      e(409, { title: 'Conflict', detail: 'Un adjunto del expediente no está disponible en almacenamiento.' }, 'Un adjunto del expediente no está disponible en almacenamiento.'),
    );
    expect(msg).toBe('Un adjunto del expediente no está disponible en almacenamiento.');
  });

  it('un código desconocido o un error que no es de la API cae al texto genérico', () => {
    expect(mensajeErrorRegenerarMandato(e(409, { title: 'otro_codigo' }, 'otro_codigo'))).toBe(
      'No se pudo regenerar el mandato y el consolidado.',
    );
    expect(mensajeErrorRegenerarMandato(new Error('boom'))).toBe('No se pudo regenerar el mandato y el consolidado.');
    expect(mensajeErrorRegenerarMandato(undefined)).toBe('No se pudo regenerar el mandato y el consolidado.');
  });

  it('los estados que bloquean son exactamente aprobado, anulado y revocado', () => {
    expect([...ESTADOS_SIN_REGENERAR_MANDATO].sort()).toEqual(['anulado', 'aprobado', 'revocado']);
  });
});
