// Contrato REAL de la revisión manual (Épica #13202, Feature C): el cliente HTTP contra las rutas y la forma JSON que devuelve el
// backend (services/core-api: ManualIdentityReviewEndpoints + ManualListItem/ManualDetail/AprobarValidacionManualResult/
// RechazarValidacionManualResult). Los cuerpos de ejemplo replican la serialización camelCase de esos records.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../types';
import { manualReviewHttpClient } from '../manual-review-client';
import { createMockManualReviewClient } from '../manual-review-mock';

const originalFetch = global.fetch;
afterEach(() => {
  global.fetch = originalFetch;
});

interface Llamada {
  url: URL;
  method: string;
  body: unknown;
}

function falsoFetch(status: number, body: unknown, contentType = 'application/json') {
  const llamadas: Llamada[] = [];
  global.fetch = vi.fn(async (url: string | URL, init?: RequestInit) => {
    llamadas.push({
      url: new URL(url.toString()),
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : init?.body,
    });
    return new Response(typeof body === 'string' ? body : JSON.stringify(body), {
      status,
      headers: { 'Content-Type': contentType },
    });
  }) as never;
  return llamadas;
}

const BASE = '/api/v1/tramites/biometric-validations';
const ID = '0199c300-0000-7000-8000-000000000001';

describe('listManual → GET /biometric-validations/manual', () => {
  it('usa los nombres de query del backend (page, pageSize, status, origin, q) y omite los vacíos', async () => {
    const llamadas = falsoFetch(200, { items: [], total: 0, page: 2, pageSize: 25 });

    await manualReviewHttpClient.listManual({
      page: 2,
      pageSize: 25,
      status: 'pendiente_revision_manual',
      origin: 'mandatario',
      q: '  Ana ',
    });
    await manualReviewHttpClient.listManual({ page: 1, pageSize: 10, status: '', origin: '', q: '' });

    expect(llamadas[0].method).toBe('GET');
    expect(llamadas[0].url.pathname).toBe(`${BASE}/manual`);
    expect(Object.fromEntries(llamadas[0].url.searchParams)).toEqual({
      page: '2',
      pageSize: '25',
      status: 'pendiente_revision_manual',
      origin: 'mandatario',
      q: 'Ana',
    });
    expect(Object.fromEntries(llamadas[1].url.searchParams)).toEqual({ page: '1', pageSize: '10' });
  });

  it('lee ManualListItem tal cual lo serializa el backend (waitingMinutes y activatedAt pueden ser null)', async () => {
    falsoFetch(200, {
      items: [
        {
          id: ID,
          fullName: 'Ana Gómez',
          documentNumber: '1001',
          tenantName: 'Compañía A',
          origin: 'tramite',
          status: 'pendiente_revision_manual',
          activatedAt: '2026-10-05T12:00:00+00:00',
          waitingMinutes: 125,
        },
        {
          id: '0199c300-0000-7000-8000-000000000002',
          fullName: 'Beto Ruiz',
          documentNumber: '2002',
          tenantName: 'Compañía B',
          origin: 'prevalidacion',
          status: 'rechazado',
          activatedAt: null,
          waitingMinutes: null,
        },
      ],
      total: 2,
      page: 1,
      pageSize: 20,
    });

    const res = await manualReviewHttpClient.listManual({ page: 1, pageSize: 20 });

    expect(res.total).toBe(2);
    expect(res.items[0]).toMatchObject({ status: 'pendiente_revision_manual', waitingMinutes: 125 });
    expect(res.items[1]).toMatchObject({ status: 'rechazado', waitingMinutes: null, activatedAt: null });
  });

  it('403 super_admin_required se propaga como ApiError 403', async () => {
    falsoFetch(403, { code: 'super_admin_required', message: 'Solo el Super Admin puede revisar validaciones manuales.' });
    await expect(manualReviewHttpClient.listManual({ page: 1, pageSize: 10 })).rejects.toMatchObject({ status: 403 });
  });
});

describe('getManualDetail → GET /biometric-validations/{id}/manual-detail', () => {
  it('lee ManualDetail con images, revisión y linkExpiresAt', async () => {
    const llamadas = falsoFetch(200, {
      id: ID,
      fullName: 'Beto Ruiz',
      documentNumber: '2002',
      tenantName: 'Compañía B',
      origin: 'prevalidacion',
      status: 'rechazado',
      activatedAt: '2026-10-05T12:00:00+00:00',
      waitingMinutes: null,
      consentAt: '2026-10-05T12:30:00+00:00',
      consentTextVersion: 'manual-ley1581-v1',
      images: [
        { kind: 'rostro', available: true },
        { kind: 'anverso', available: true },
        { kind: 'reverso', available: true },
        { kind: 'firma', available: true },
      ],
      reviewedAt: '2026-10-05T14:00:00+00:00',
      reviewedBy: 'Super Admin',
      rejectionReasonCode: 'imagen_borrosa',
      linkExpiresAt: '2026-10-06T14:00:00+00:00',
    });

    const d = await manualReviewHttpClient.getManualDetail(ID);

    expect(llamadas[0].url.pathname).toBe(`${BASE}/${ID}/manual-detail`);
    expect(d.images.map((i) => i.kind)).toEqual(['rostro', 'anverso', 'reverso', 'firma']);
    expect(d).toMatchObject({
      rejectionReasonCode: 'imagen_borrosa',
      linkExpiresAt: '2026-10-06T14:00:00+00:00',
      waitingMinutes: null,
    });
  });

  it('404 not_found → ApiError 404', async () => {
    falsoFetch(404, { code: 'not_found', message: 'Validación manual no encontrada.' });
    await expect(manualReviewHttpClient.getManualDetail(ID)).rejects.toMatchObject({ status: 404 });
  });
});

describe('getManualImage → GET /biometric-validations/{id}/manual-images/{kind}', () => {
  it('pide la ruta con el kind y devuelve el Blob', async () => {
    const llamadas = falsoFetch(200, 'bytes', 'image/jpeg');
    const blob = await manualReviewHttpClient.getManualImage(ID, 'firma');
    expect(llamadas[0].url.pathname).toBe(`${BASE}/${ID}/manual-images/firma`);
    expect(blob.size).toBeGreaterThan(0);
  });

  it('404 → ApiError 404', async () => {
    falsoFetch(404, { code: 'not_found', message: 'Imagen no encontrada.' });
    await expect(manualReviewHttpClient.getManualImage(ID, 'rostro')).rejects.toBeInstanceOf(ApiError);
  });
});

describe('approveManual → POST /biometric-validations/{id}/manual-approve', () => {
  it('POST sin cuerpo y devuelve AprobarValidacionManualResult', async () => {
    const llamadas = falsoFetch(200, {
      validationId: ID,
      tenantId: '0199c300-0000-7000-8000-0000000000aa',
      procedureInstanceId: null,
      status: 'aprobado',
      approvalOrigin: 'manual',
      validatedAt: '2026-10-05T15:00:00+00:00',
      validUntil: '2026-11-04T15:00:00+00:00',
      reviewedAt: '2026-10-05T15:00:00+00:00',
    });

    const res = await manualReviewHttpClient.approveManual(ID);

    expect(llamadas[0].method).toBe('POST');
    expect(llamadas[0].body).toBeUndefined();
    expect(llamadas[0].url.pathname).toBe(`${BASE}/${ID}/manual-approve`);
    expect(res).toMatchObject({ status: 'aprobado', approvalOrigin: 'manual', validUntil: '2026-11-04T15:00:00+00:00' });
  });

  it.each(['estado_invalido', 'tramite_inactivo'])('409 %s conserva el code en ApiError.body', async (code) => {
    falsoFetch(409, { code, message: 'x' });
    const err = await manualReviewHttpClient.approveManual(ID).catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect((err as ApiError).status).toBe(409);
    expect((err as ApiError).body).toMatchObject({ code });
  });
});

describe('rejectManual → POST /biometric-validations/{id}/manual-reject', () => {
  it('manda { reasonCode } y devuelve RechazarValidacionManualResult', async () => {
    const llamadas = falsoFetch(200, {
      validationId: ID,
      tenantId: '0199c300-0000-7000-8000-0000000000aa',
      procedureInstanceId: null,
      status: 'rechazado',
      rejectionReasonCode: 'imagen_borrosa',
      reviewedAt: '2026-10-05T15:00:00+00:00',
      linkExpiresAt: '2026-10-06T15:00:00+00:00',
      emailEnviado: true,
    });

    const res = await manualReviewHttpClient.rejectManual(ID, 'imagen_borrosa');

    expect(llamadas[0].method).toBe('POST');
    expect(llamadas[0].url.pathname).toBe(`${BASE}/${ID}/manual-reject`);
    expect(llamadas[0].body).toEqual({ reasonCode: 'imagen_borrosa' });
    expect(res).toMatchObject({ status: 'rechazado', emailEnviado: true, linkExpiresAt: '2026-10-06T15:00:00+00:00' });
  });

  it('400 motivo_invalido → ApiError 400 con el code', async () => {
    falsoFetch(400, { code: 'motivo_invalido', message: 'El motivo de rechazo es obligatorio y debe ser uno de la lista.' });
    const err = await manualReviewHttpClient.rejectManual(ID, 'otro').catch((e: unknown) => e);
    expect((err as ApiError).status).toBe(400);
    expect((err as ApiError).body).toMatchObject({ code: 'motivo_invalido' });
  });
});

describe('el adaptador simulado respeta la misma forma que el real', () => {
  it('aprobar y rechazar devuelven los mismos campos que el backend', async () => {
    const mock = createMockManualReviewClient({ delayMs: 0 });
    const aprobada = await mock.approveManual('mock-manual-01');
    expect(Object.keys(aprobada).sort()).toEqual(
      [
        'approvalOrigin',
        'procedureInstanceId',
        'reviewedAt',
        'status',
        'tenantId',
        'validUntil',
        'validatedAt',
        'validationId',
      ].sort(),
    );
    const rechazada = await mock.rejectManual('mock-manual-03', 'imagen_borrosa');
    expect(Object.keys(rechazada).sort()).toEqual(
      [
        'emailEnviado',
        'linkExpiresAt',
        'procedureInstanceId',
        'rejectionReasonCode',
        'reviewedAt',
        'status',
        'tenantId',
        'validationId',
      ].sort(),
    );
  });
});
