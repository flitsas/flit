import { afterEach, describe, expect, it, vi } from 'vitest';
import { tramitesClient } from '../tramites-client';

/**
 * Bug #14559 — el veredicto OCR viaja en el `register` del adjunto para que el backend lo persista
 * y la vista lo recupere al volver al paso. Solo códigos cerrados: nunca texto libre ni VINs.
 *
 * Uso de ejemplo:
 *   tramitesClient.uploadAttachment(id, 'factura', file, tenant, { status: 'rejected', reason: 'vin_mismatch' })
 *   → register { …, ocrStatus: 'rejected', ocrReason: 'vin_mismatch' }
 */
function stubFetch() {
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url: String(url), init });
    if (String(url).includes('/attachments/presign')) {
      return new Response(
        JSON.stringify({ storagePath: 'file_1', url: 'https://s3.test/up', method: 'PUT', fields: {} }),
        { status: 200, headers: { 'content-type': 'application/json' } },
      );
    }
    if (String(url) === 'https://s3.test/up') return new Response(null, { status: 200 });
    if (String(url).includes('/attachments/register')) {
      return new Response(
        JSON.stringify({
          id: 'att-1',
          tipo: 'factura',
          filename: 'doc.pdf',
          mimetype: 'application/pdf',
          sizeBytes: 3,
          sha256: 'x',
          source: 'user',
          uploadedAt: '2026-11-12T00:00:00Z',
          ocrStatus: 'rejected',
          ocrReason: 'vin_mismatch',
        }),
        { status: 201, headers: { 'content-type': 'application/json' } },
      );
    }
    throw new Error(`unexpected fetch: ${url}`);
  });
  vi.stubGlobal('fetch', fetchMock);
  const registerBody = () =>
    JSON.parse(calls.find((c) => c.url.includes('/attachments/register'))!.init!.body as string);
  return { registerBody };
}

const file = () => new File([new Uint8Array([1, 2, 3])], 'doc.pdf', { type: 'application/pdf' });

describe('tramitesClient.uploadAttachment — veredicto OCR en el register (Bug #14559)', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('incluye ocrStatus y ocrReason cuando se pasa el veredicto', async () => {
    const { registerBody } = stubFetch();
    const res = await tramitesClient.uploadAttachment('inst-1', 'factura', file(), 't-1', {
      status: 'rejected',
      reason: 'vin_mismatch',
    });
    expect(registerBody()).toMatchObject({ ocrStatus: 'rejected', ocrReason: 'vin_mismatch' });
    expect(res.ocrStatus).toBe('rejected');
    expect(res.ocrReason).toBe('vin_mismatch');
  });

  it('verified sin motivo no envía ocrReason', async () => {
    const { registerBody } = stubFetch();
    await tramitesClient.uploadAttachment('inst-1', 'factura', file(), 't-1', { status: 'verified' });
    const body = registerBody();
    expect(body.ocrStatus).toBe('verified');
    expect(body).not.toHaveProperty('ocrReason');
  });

  it('sin veredicto el body del register no cambia (retrocompatible)', async () => {
    const { registerBody } = stubFetch();
    await tramitesClient.uploadAttachment('inst-1', 'cedula', file(), 't-1');
    const body = registerBody();
    expect(body).not.toHaveProperty('ocrStatus');
    expect(body).not.toHaveProperty('ocrReason');
    expect(Object.keys(body).sort()).toEqual(
      ['filename', 'mimetype', 'sha256', 'sizeBytes', 'storagePath', 'tipo'].sort(),
    );
  });
});
