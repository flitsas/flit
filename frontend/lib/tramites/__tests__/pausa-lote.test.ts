import { describe, expect, it } from 'vitest';

import {
  NOTA_PAUSA_MODO_FILTRO,
  PAUSA_LOTE_EN_MODO_FILTRO,
  accionPausaLote,
  esBorradorIct,
  partirSeleccionParaPausa,
  pausaLoteDisponibleEnModo,
  seleccionTieneBorradorIct,
  textoExcluidosNoIct,
  type ItemPausaLote,
} from '@/lib/tramites/pausa-lote';

// Uso de ejemplo:
//   const p = partirSeleccionParaPausa(lote.modelo.ids, (id) => porId.get(id), tenantActual);
//   accionPausaLote(p.pausables) → 'pausar' | 'reanudar' | null

const PROPIO = 'tenant-propio';
const HIJA = 'tenant-hija';

const item = (over: Partial<ItemPausaLote> & { id: string }): ItemPausaLote => ({
  origin: 'ict',
  estado: 'borrador',
  isPaused: false,
  tenantId: PROPIO,
  ...over,
});

describe('pausa-lote — HU #13380 (una sola casilla por fila)', () => {
  it('happy path — solo los borradores ICT propios son pausables', () => {
    const filas = new Map(
      [
        item({ id: 'ict-propio' }),
        item({ id: 'ict-red', tenantId: HIJA }),
        item({ id: 'dashboard', origin: 'dashboard', estado: 'preparado' }),
        item({ id: 'ict-radicado', estado: 'preparado' }),
      ].map((it) => [it.id, it] as const),
    );
    const p = partirSeleccionParaPausa(
      ['ict-propio', 'ict-red', 'dashboard', 'ict-radicado'],
      (id) => filas.get(id),
      PROPIO,
    );
    expect(p.pausables.map((it) => it.id)).toEqual(['ict-propio']);
    expect(p.excluidosRed).toBe(1);
    expect(p.excluidosNoIct).toBe(2);
    expect(seleccionTieneBorradorIct(p)).toBe(true);
  });

  it('edge — un id sin fila conocida no se puede verificar y cuenta como no ICT', () => {
    const p = partirSeleccionParaPausa(['desconocido'], () => undefined, PROPIO);
    expect(p).toEqual({ pausables: [], excluidosRed: 0, excluidosNoIct: 1 });
    expect(seleccionTieneBorradorIct(p)).toBe(false);
  });

  it('edge — selección sin borradores ICT: la sección de pausa no aparece', () => {
    const p = partirSeleccionParaPausa(
      ['a'],
      () => item({ id: 'a', origin: 'dashboard' }),
      PROPIO,
    );
    expect(seleccionTieneBorradorIct(p)).toBe(false);
  });

  it('Bug #13109 — ninguno pausado → pausar; todos → reanudar; mezcla o vacío → ninguna', () => {
    expect(accionPausaLote([{ isPaused: false }, { isPaused: false }])).toBe('pausar');
    expect(accionPausaLote([{ isPaused: true }, { isPaused: true }])).toBe('reanudar');
    expect(accionPausaLote([{ isPaused: true }, { isPaused: false }])).toBeNull();
    expect(accionPausaLote([])).toBeNull();
  });

  it('contrato — en modo «todos los del filtro» la pausa no se ofrece y hay nota', () => {
    expect(PAUSA_LOTE_EN_MODO_FILTRO).toBe(false);
    expect(pausaLoteDisponibleEnModo('ids')).toBe(true);
    expect(pausaLoteDisponibleEnModo('filtro')).toBe(false);
    expect(NOTA_PAUSA_MODO_FILTRO).toBe('La pausa en lote aplica a trámites marcados uno a uno');
  });

  it('contrato — textos de exclusión y borrador ICT', () => {
    expect(textoExcluidosNoIct(0)).toBeNull();
    expect(textoExcluidosNoIct(1)).toBe('1 excluido de la pausa: no son borradores ICT');
    expect(textoExcluidosNoIct(3)).toBe('3 excluidos de la pausa: no son borradores ICT');
    expect(esBorradorIct({ origin: 'ict', estado: 'borrador' })).toBe(true);
    expect(esBorradorIct({ origin: 'ict', estado: 'preparado' })).toBe(false);
  });
});
