/**
 * HU #12787 (AC2) — «maestro radicado, fijo»: resolución de la fuente del maestro en la vista de
 * solo lectura del OT, aviso con la fecha de radicación en hora Colombia, conservación de los
 * campos de vigencia/radicación cuando aprobar/rechazar/revocar los devuelven en null, y
 * normalización del listado del gestor (`consolidadoWizard`, HU #12791).
 *
 * Uso de ejemplo:
 *   resolverFuenteMaestroOt({ quipuxRadicadoEn: '2026-09-20T15:30:00Z', quipuxMaestroAttachmentId: 'att-1' })
 *   // → { via: 'adjunto_radicado', attachmentId: 'att-1', radicadoEn: '2026-09-20T15:30:00Z' }
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  conservarCamposConsolidadoOt,
  resolverFuenteMaestroOt,
  textoMaestroRadicado,
} from '@/lib/tramites/consolidado-entrega-ot';
import type { OtClientProcedure } from '@/lib/api/types-ot';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import { tramitesClient } from '@/lib/api/tramites-client';

const RADICADO_EN = '2026-09-20T15:30:00Z'; // 10:30 en Bogotá (UTC−5)

const VIGENTE: ConsolidadoVigencia = {
  estado: 'vigente',
  generadoEn: '2026-09-19T12:00:00Z',
  origen: 'system',
  definitivo: false,
  modo: null,
};

const base: OtClientProcedure = {
  id: 'proc-1',
  clientTenantId: 'client-tenant-aaaa',
  procedureTypeId: 'tipo-1',
  referenceNumber: 'RAD-2026-001',
  status: 'entregado',
  createdAt: '2026-09-01T09:00:00Z',
};

describe('HU #12787 AC2 — resolverFuenteMaestroOt', () => {
  it('radicado con adjunto conocido ⇒ ese adjunto, sin pasar por la entrega', () => {
    expect(
      resolverFuenteMaestroOt({
        quipuxRadicadoEn: RADICADO_EN,
        quipuxMaestroAttachmentId: 'att-radicado',
      }),
    ).toEqual({ via: 'adjunto_radicado', attachmentId: 'att-radicado', radicadoEn: RADICADO_EN });
  });

  it('radicado SIN adjunto ⇒ entrega con soloLectura=true (no regenera)', () => {
    expect(
      resolverFuenteMaestroOt({ quipuxRadicadoEn: RADICADO_EN, quipuxMaestroAttachmentId: null }),
    ).toEqual({
      via: 'entrega',
      params: { tipo: 'consolidado_maestro', soloLectura: true },
      radicadoEn: RADICADO_EN,
    });
  });

  it('no radicado ⇒ flujo de AC1 (entrega sin soloLectura)', () => {
    const fuente = resolverFuenteMaestroOt({ quipuxRadicadoEn: null, quipuxMaestroAttachmentId: 'x' });
    expect(fuente).toEqual({
      via: 'entrega',
      params: { tipo: 'consolidado_maestro' },
      radicadoEn: null,
    });
  });

  it('procedure null/undefined o campos ausentes (backend anterior) ⇒ AC1', () => {
    expect(resolverFuenteMaestroOt(null).via).toBe('entrega');
    expect(resolverFuenteMaestroOt(undefined).radicadoEn).toBeNull();
    expect(resolverFuenteMaestroOt({})).toEqual({
      via: 'entrega',
      params: { tipo: 'consolidado_maestro' },
      radicadoEn: null,
    });
  });

  it('cadenas vacías o en blanco cuentan como ausentes', () => {
    expect(resolverFuenteMaestroOt({ quipuxRadicadoEn: '  ' }).radicadoEn).toBeNull();
    expect(
      resolverFuenteMaestroOt({ quipuxRadicadoEn: RADICADO_EN, quipuxMaestroAttachmentId: '' }),
    ).toMatchObject({ via: 'entrega', params: { soloLectura: true } });
  });
});

describe('HU #12787 AC2 — textoMaestroRadicado (hora Colombia)', () => {
  it('formatea el instante UTC en hora de Bogotá con el formato único DD/MM/YYYY HH:mm', () => {
    expect(textoMaestroRadicado(RADICADO_EN)).toBe(
      'Radicado el 20/09/2026 10:30 (hora Colombia). Se muestra el documento radicado tal cual; no se regenera.',
    );
  });

  it('un instante cerca de medianoche UTC cae en el día anterior de Colombia', () => {
    expect(textoMaestroRadicado('2026-09-21T03:00:00Z')).toContain('20/09/2026 22:00');
  });

  it('no lanza con una fecha inválida (cae al guion del formateador)', () => {
    expect(() => textoMaestroRadicado('no-es-fecha')).not.toThrow();
    expect(textoMaestroRadicado('no-es-fecha')).toContain('Radicado el —');
  });
});

describe('HU #12787 — conservarCamposConsolidadoOt (aprobar/rechazar/revocar devuelven null)', () => {
  const previo: OtClientProcedure = {
    ...base,
    consolidadoWizard: VIGENTE,
    consolidadoMaestro: VIGENTE,
    quipuxRadicadoEn: RADICADO_EN,
    quipuxMaestroAttachmentId: 'att-radicado',
  };

  it('los null de la respuesta NO pisan los valores locales', () => {
    const actualizado: OtClientProcedure = {
      ...base,
      status: 'aprobado',
      consolidadoWizard: null,
      consolidadoMaestro: null,
      quipuxRadicadoEn: null,
      quipuxMaestroAttachmentId: null,
    };
    const r = conservarCamposConsolidadoOt(previo, actualizado);
    expect(r.status).toBe('aprobado');
    expect(r.quipuxRadicadoEn).toBe(RADICADO_EN);
    expect(r.quipuxMaestroAttachmentId).toBe('att-radicado');
    expect(r.consolidadoMaestro).toEqual(VIGENTE);
    expect(r.consolidadoWizard).toEqual(VIGENTE);
  });

  it('campos ausentes en la respuesta también se conservan', () => {
    const r = conservarCamposConsolidadoOt(previo, { ...base, status: 'rechazado' });
    expect(r.quipuxRadicadoEn).toBe(RADICADO_EN);
    expect(r.status).toBe('rechazado');
  });

  it('un valor NO nulo de la respuesta gana sobre el local', () => {
    const r = conservarCamposConsolidadoOt(previo, {
      ...base,
      quipuxRadicadoEn: '2026-09-22T12:00:00Z',
      consolidadoMaestro: { ...VIGENTE, estado: 'desactualizado' },
    });
    expect(r.quipuxRadicadoEn).toBe('2026-09-22T12:00:00Z');
    expect(r.consolidadoMaestro?.estado).toBe('desactualizado');
  });

  it('no muta los objetos de entrada', () => {
    const actualizado: OtClientProcedure = { ...base, quipuxRadicadoEn: null };
    conservarCamposConsolidadoOt(previo, actualizado);
    expect(actualizado.quipuxRadicadoEn).toBeNull();
  });
});

describe('HU #12791 — normalizeInstances expone consolidadoWizard', () => {
  beforeEach(() => {
    const json = (body: unknown) =>
      new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        json({
          items: [
            { id: 'i-1', referenceNumber: 'R-1', consolidadoWizard: VIGENTE },
            { id: 'i-2', referenceNumber: 'R-2' },
          ],
        }),
      ),
    );
  });

  afterEach(() => vi.unstubAllGlobals());

  it('conserva el valor del backend y cae a null si el backend no lo trae', async () => {
    const items = await tramitesClient.listInstances();
    expect(items[0].consolidadoWizard).toEqual(VIGENTE);
    expect(items[1].consolidadoWizard).toBeNull();
  });
});
