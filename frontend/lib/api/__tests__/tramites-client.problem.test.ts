import { describe, expect, it } from 'vitest';
import { tramitesClient } from '../tramites-client';

/**
 * El mensaje que ve el gestor cuando el backend rechaza una acción viene del `detail` del
 * ProblemDetails. Sin esto, la alerta bloqueante del SOAT llegaría a la pantalla como un
 * "Error 409" sin explicar qué pasó ni qué hacer.
 */
describe('tramites-client — mensajes de error del backend', () => {
  const originalFetch = globalThis.fetch;

  function respondWith(status: number, body: unknown) {
    globalThis.fetch = (async () =>
      new Response(JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/problem+json' },
      })) as typeof fetch;
  }

  it('propaga el detalle del 409 de SOAT no vigente', async () => {
    const detail =
      'El RUNT no reporta un SOAT vigente para el vehículo. La compañía exige validar el SOAT ' +
      'ante el RUNT para procesar: registra un SOAT vigente y vuelve a intentarlo.';
    respondWith(409, { title: 'soat_no_vigente', status: 409, detail });

    await expect(
      tramitesClient.completePlateFlow('inst-1', { soatPagado: true }),
    ).rejects.toThrow(detail);

    globalThis.fetch = originalFetch;
  });

  it('cae al title cuando el problema no trae detalle', async () => {
    respondWith(409, { title: 'plate_flow_not_asignado', status: 409 });

    await expect(
      tramitesClient.completePlateFlow('inst-1', { soatPagado: true }),
    ).rejects.toThrow('plate_flow_not_asignado');

    globalThis.fetch = originalFetch;
  });
});
