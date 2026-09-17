import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  TramitesApiError,
  parseContentDispositionFilename,
  tramitesClient,
} from '@/lib/api/tramites-client';
import { isScopeRejection } from '@/lib/tramites/network-scope';

/**
 * HU #12411 — contrato de cliente para los documentos de un trámite de la red (contrato B5 #12410).
 *
 * Uso de ejemplo:
 *   const list = await tramitesClient.getNetworkAttachments('inst-1');
 *   const { blob, filename } = await tramitesClient.downloadNetworkAttachment('inst-1', 'att-1');
 *
 * Verifica: rutas `network/**`, SIN `X-Tenant-Id`, `filename` desde `Content-Disposition`, y que un
 * 403/404 del servidor llega como error con `status` reconocible por `isScopeRejection`.
 */

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

let calls: Array<{ url: string; init?: RequestInit }>;

beforeEach(() => {
  calls = [];
  document.cookie = 'flit_token=jwt-test; path=/';
});

afterEach(() => {
  vi.restoreAllMocks();
  document.cookie = 'flit_token=; path=/; Max-Age=0';
});

function mockFetch(handler: (url: string) => Response | Promise<Response>) {
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (url, init) => {
    calls.push({ url: String(url), init });
    return handler(String(url));
  });
}

function headerOf(init: RequestInit | undefined, name: string): string | null {
  const h = init?.headers;
  if (!h) return null;
  if (h instanceof Headers) return h.get(name);
  if (Array.isArray(h)) return h.find(([k]) => k.toLowerCase() === name.toLowerCase())?.[1] ?? null;
  const entry = Object.entries(h).find(([k]) => k.toLowerCase() === name.toLowerCase());
  return entry ? String(entry[1]) : null;
}

describe('HU #12411 — tramitesClient.getNetworkAttachments (contrato B5 #12410)', () => {
  it('pide la ruta de red sin X-Tenant-Id y devuelve el arreglo tal cual', async () => {
    const att = {
      id: 'att-1',
      tipo: 'soat',
      filename: 'soat.pdf',
      mimetype: 'application/pdf',
      sizeBytes: 10,
      sha256: 'x',
      source: 'user',
      uploadedAt: '2026-06-18T00:00:00Z',
    };
    mockFetch(() => jsonResponse([att]));
    const list = await tramitesClient.getNetworkAttachments('inst-1');
    expect(list).toEqual([att]);
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toMatch(/\/api\/v1\/tramites\/network\/instances\/inst-1\/attachments$/);
    expect(headerOf(calls[0].init, 'X-Tenant-Id')).toBeNull();
    expect(headerOf(calls[0].init, 'Authorization')).toBe('Bearer jwt-test');
    // Contrato: sin `previewUrl`.
    expect(list[0]).not.toHaveProperty('previewUrl');
  });

  it('acepta también el sobre { attachments } y devuelve [] ante cuerpo vacío', async () => {
    mockFetch(() => jsonResponse({ attachments: [] }));
    expect(await tramitesClient.getNetworkAttachments('inst-1')).toEqual([]);
    mockFetch(() => new Response(null, { status: 204 }));
    expect(await tramitesClient.getNetworkAttachments('inst-1')).toEqual([]);
  });

  it.each([
    [403, 'network_documents_disabled'],
    [403, 'network_scope_required'],
    [404, 'not_found'],
  ])('%s { error: %s } ⇒ error con status reconocido como rechazo de alcance', async (status, error) => {
    mockFetch(() => jsonResponse({ error }, status));
    const err = await tramitesClient.getNetworkAttachments('inst-1').catch((e: unknown) => e);
    expect(err).toBeInstanceOf(TramitesApiError);
    expect((err as TramitesApiError).status).toBe(status);
    expect(isScopeRejection(err)).toBe(true);
  });
});

describe('HU #12411 — tramitesClient.downloadNetworkAttachment (contrato B5 #12410)', () => {
  it('descarga el binario por la ruta de red, sin X-Tenant-Id, con filename del Content-Disposition', async () => {
    mockFetch(
      () =>
        new Response('%PDF-1.4', {
          status: 200,
          headers: {
            'content-type': 'application/pdf',
            'content-disposition': 'attachment; filename="soat vigente.pdf"',
          },
        }),
    );
    const res = await tramitesClient.downloadNetworkAttachment('inst-1', 'att-1', 'fallback.pdf');
    expect(calls[0].url).toMatch(
      /\/api\/v1\/tramites\/network\/instances\/inst-1\/attachments\/att-1\/download$/,
    );
    expect(headerOf(calls[0].init, 'X-Tenant-Id')).toBeNull();
    expect(headerOf(calls[0].init, 'Authorization')).toBe('Bearer jwt-test');
    expect(res.filename).toBe('soat vigente.pdf');
    expect(res.mimetype).toBe('application/pdf');
    expect(await res.blob.text()).toBe('%PDF-1.4');
  });

  it('usa el fallback cuando no hay Content-Disposition y el id cuando tampoco hay fallback', async () => {
    mockFetch(() => new Response('x', { status: 200 }));
    expect((await tramitesClient.downloadNetworkAttachment('i', 'att-9', 'f.pdf')).filename).toBe('f.pdf');
    expect((await tramitesClient.downloadNetworkAttachment('i', 'att-9')).filename).toBe('att-9');
  });

  it('403 al descargar ⇒ error con status 403 (rechazo de alcance, no fallo técnico)', async () => {
    mockFetch(() => jsonResponse({ error: 'network_documents_disabled' }, 403));
    const err = await tramitesClient.downloadNetworkAttachment('i', 'a').catch((e: unknown) => e);
    expect((err as { status?: number }).status).toBe(403);
    expect(isScopeRejection(err)).toBe(true);
  });
});

describe('HU #12411 — paridad: downloadAttachment (ruta propia) no cambia', () => {
  it('sigue pidiendo la ruta propia con X-Tenant-Id y decodifica filename*=UTF-8', async () => {
    mockFetch(
      () =>
        new Response('x', {
          status: 200,
          headers: { 'content-disposition': "attachment; filename*=UTF-8''fur%20final.pdf" },
        }),
    );
    const res = await tramitesClient.downloadAttachment('inst-1', 'att-1', 'tenant-9');
    expect(calls[0].url).toMatch(/\/api\/v1\/tramites\/instances\/inst-1\/attachments\/att-1\/download$/);
    expect(headerOf(calls[0].init, 'X-Tenant-Id')).toBe('tenant-9');
    expect(res.filename).toBe('fur final.pdf');
  });
});

describe('parseContentDispositionFilename', () => {
  it.each([
    ['attachment; filename="a.pdf"', 'a.pdf'],
    ['attachment; filename=a.pdf', 'a.pdf'],
    ["attachment; filename*=UTF-8''a%20b.pdf", 'a b.pdf'],
    ['attachment; filename="100%.pdf"', '100%.pdf'],
    ['inline', ''],
    ['', ''],
    [null, ''],
    [undefined, ''],
  ])('%s → %s', (cd, expected) => {
    expect(parseContentDispositionFilename(cd)).toBe(expected);
  });
});
