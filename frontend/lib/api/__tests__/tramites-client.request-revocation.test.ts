import { afterEach, describe, expect, it } from 'vitest';
import { TramitesApiError, tramitesClient } from '../tramites-client';

/**
 * HU #12574 (Feature #12565) — cliente de `POST /instances/{id}/revocation-requests` (HU #12572):
 * multipart con reason/confirmAccuracy/confirmConsequences/file, mismo patrón que
 * `adminCargarConsolidado`/`analyzeDocument` (fetch directo + `TramitesApiError` en error).
 */
describe('tramites-client — requestRevocation', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('arma el multipart con los 4 campos (checks como texto "true"/"false") y devuelve el 201 parseado', async () => {
    let capturedUrl: string | undefined;
    let capturedInit: RequestInit | undefined;
    globalThis.fetch = (async (url: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(url);
      capturedInit = init;
      return new Response(
        JSON.stringify({
          id: 'rev-1',
          procedureInstanceId: 'inst-1',
          attemptNumber: 1,
          status: 'solicitada',
          requestedAt: '2026-09-15T12:00:00Z',
        }),
        { status: 201, headers: { 'Content-Type': 'application/json' } },
      );
    }) as typeof fetch;

    const file = new File(['%PDF-1.4'], 'soporte.pdf', { type: 'application/pdf' });
    const result = await tramitesClient.requestRevocation('inst-1', {
      reason: 'El vehículo no cumplía los requisitos.',
      confirmAccuracy: true,
      confirmConsequences: false,
      file,
    });

    expect(capturedUrl).toContain('/api/v1/tramites/instances/inst-1/revocation-requests');
    expect(capturedInit?.method).toBe('POST');
    const form = capturedInit?.body as FormData;
    expect(form.get('reason')).toBe('El vehículo no cumplía los requisitos.');
    expect(form.get('confirmAccuracy')).toBe('true');
    expect(form.get('confirmConsequences')).toBe('false');
    expect(form.get('file')).toBe(file);
    // Sin Content-Type manual: el navegador fija el boundary del multipart.
    const headers = capturedInit?.headers as Record<string, string> | undefined;
    expect(headers?.['Content-Type']).toBeUndefined();

    expect(result).toEqual({
      id: 'rev-1',
      procedureInstanceId: 'inst-1',
      attemptNumber: 1,
      status: 'solicitada',
      requestedAt: '2026-09-15T12:00:00Z',
    });
  });

  it('un 422 con title=motivo_requerido llega como TramitesApiError con status y problem.title', async () => {
    globalThis.fetch = (async () =>
      new Response(
        JSON.stringify({
          title: 'motivo_requerido',
          status: 422,
          detail: 'Debe indicar el motivo de la solicitud de revocatoria.',
        }),
        { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
      )) as typeof fetch;

    const file = new File(['%PDF-1.4'], 'soporte.pdf', { type: 'application/pdf' });
    await expect(
      tramitesClient.requestRevocation('inst-1', {
        reason: '',
        confirmAccuracy: true,
        confirmConsequences: true,
        file,
      }),
    ).rejects.toMatchObject({
      status: 422,
      message: 'Debe indicar el motivo de la solicitud de revocatoria.',
      problem: { title: 'motivo_requerido' },
    });
  });

  it('un 409 solicitud_activa_existente se propaga como TramitesApiError instanceof', async () => {
    globalThis.fetch = (async () =>
      new Response(
        JSON.stringify({
          title: 'solicitud_activa_existente',
          status: 409,
          detail: 'Ya existe una solicitud de revocatoria activa para este trámite.',
        }),
        { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
      )) as typeof fetch;

    const file = new File(['%PDF-1.4'], 'soporte.pdf', { type: 'application/pdf' });
    try {
      await tramitesClient.requestRevocation('inst-1', {
        reason: 'motivo',
        confirmAccuracy: true,
        confirmConsequences: true,
        file,
      });
      expect.unreachable('debía lanzar');
    } catch (err) {
      expect(err).toBeInstanceOf(TramitesApiError);
      expect((err as TramitesApiError).status).toBe(409);
    }
  });
});
