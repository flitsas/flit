// Píldora «Quipux · …» de la columna Marcas: texto por estado y salida para el Excel.
import { describe, expect, it } from 'vitest';
import { marcasLabel, quipuxMarca } from '../tramites-row-labels';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

const fila = (parcial: Partial<InstanceSummary>) =>
  ({ id: '1', referenceNumber: 'FT1-1', modalidad: 'TRASPASO', ...parcial }) as InstanceSummary;

describe('quipuxMarca / marcasLabel', () => {
  it.each([
    ["en_cola", "QX · En cola", "neutral"],
    ["radicado", "QX · Radicado", "info"],
    ["fallido", "QX · Falló", "danger"],
  ] as const)('%s → «%s»', (estado, texto, tono) => {
    const m = quipuxMarca(fila({ quipuxEstado: estado }));
    expect(m?.texto).toBe(texto);
    expect(m?.tono).toBe(tono);
    expect(m?.ayuda.length).toBeGreaterThan(10);
    expect(marcasLabel(fila({ quipuxEstado: estado }))).toBe(texto);
  });

  it('aprobado y rechazado no se pintan, ni van al Excel', () => {
    for (const e of ['aprobado', 'rechazado'] as const) {
      expect(quipuxMarca(fila({ quipuxEstado: e }))).toBeNull();
      expect(marcasLabel(fila({ quipuxEstado: e }))).toBe('');
    }
  });

  it('null, ausente o desconocido: sin píldora', () => {
    expect(quipuxMarca(fila({ quipuxEstado: null }))).toBeNull();
    expect(quipuxMarca(fila({}))).toBeNull();
    expect(quipuxMarca(fila({ quipuxEstado: 'otro' as never }))).toBeNull();
    expect(marcasLabel(fila({}))).toBe('');
  });

  it('va después de las demás marcas en el Excel', () => {
    expect(marcasLabel(fila({ tienePrenda: true, quipuxEstado: 'fallido' }))).toBe(
      'prenda, QX · Falló',
    );
  });
});
