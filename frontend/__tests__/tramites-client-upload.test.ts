import { afterEach, describe, expect, it, vi } from 'vitest';
import { tramitesClient } from '@/lib/api/tramites-client';

/**
 * uploadAttachment: subida directa navegador→S3 (presigned). Verifica el flujo de 3 pasos
 * (presign → POST a S3 → register), el orden de los campos firmados antes del 'file', y que el
 * sha256 se calcula en el navegador y viaja en el register (el binario NO pasa por el API).
 */
describe('tramitesClient.uploadAttachment (presigned)', () => {
  afterEach(() => vi.restoreAllMocks());

  it('hace presign, sube a S3 y registra la metadata con el sha256 del cliente', async () => {
    const calls: Array<{ url: string; init?: RequestInit }> = [];
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      calls.push({ url: String(url), init });
      if (String(url).includes('/attachments/presign')) {
        return new Response(
          JSON.stringify({
            storagePath: 'file_xyz',
            url: 'https://s3.test/upload',
            fields: { key: 'tramites/k/obj', policy: 'pol' },
          }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }
      if (String(url) === 'https://s3.test/upload') {
        return new Response(null, { status: 204 }); // S3 POST policy OK
      }
      if (String(url).includes('/attachments/register')) {
        return new Response(
          JSON.stringify({
            id: 'att-1',
            tipo: 'factura',
            filename: 'doc.pdf',
            mimetype: 'application/pdf',
            sizeBytes: 5,
            sha256: 'x',
            source: 'user',
            uploadedAt: '2026-06-26T00:00:00Z',
          }),
          { status: 201, headers: { 'content-type': 'application/json' } },
        );
      }
      throw new Error(`unexpected fetch: ${url}`);
    });
    vi.stubGlobal('fetch', fetchMock);

    const file = new File([new Uint8Array([1, 2, 3, 4, 5])], 'doc.pdf', { type: 'application/pdf' });
    const result = await tramitesClient.uploadAttachment('inst-1', 'factura', file, 'tenant-1');

    expect(result.id).toBe('att-1');
    expect(fetchMock).toHaveBeenCalledTimes(3);

    // 1) presign con la metadata del archivo
    const presign = calls.find((c) => c.url.includes('/attachments/presign'))!;
    expect(JSON.parse(presign.init!.body as string)).toMatchObject({
      tipo: 'factura',
      filename: 'doc.pdf',
      mimetype: 'application/pdf',
      sizeBytes: 5,
    });

    // 2) POST a S3: los campos firmados van ANTES del 'file'
    const s3 = calls.find((c) => c.url === 'https://s3.test/upload')!;
    const form = s3.init!.body as FormData;
    const keys = [...form.keys()];
    expect(keys).toEqual(['key', 'policy', 'file']);
    expect(keys.indexOf('file')).toBe(keys.length - 1);

    // 3) register con storagePath y sha256 (64 hex calculado en el navegador)
    const register = calls.find((c) => c.url.includes('/attachments/register'))!;
    const body = JSON.parse(register.init!.body as string);
    expect(body.storagePath).toBe('file_xyz');
    expect(body.sha256).toMatch(/^[0-9a-f]{64}$/);
  });

  it('lanza si la subida a S3 falla (no llega a register)', async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (String(url).includes('/attachments/presign')) {
        return new Response(
          JSON.stringify({ storagePath: 'f', url: 'https://s3.test/upload', fields: {} }),
          { status: 200, headers: { 'content-type': 'application/json' } },
        );
      }
      if (String(url) === 'https://s3.test/upload') {
        return new Response('AccessDenied', { status: 403 });
      }
      throw new Error('register no debería llamarse');
    });
    vi.stubGlobal('fetch', fetchMock);

    const file = new File([new Uint8Array([1])], 'doc.pdf', { type: 'application/pdf' });
    await expect(
      tramitesClient.uploadAttachment('inst-1', 'factura', file, 'tenant-1'),
    ).rejects.toThrow(/almacenamiento/i);
  });
});
