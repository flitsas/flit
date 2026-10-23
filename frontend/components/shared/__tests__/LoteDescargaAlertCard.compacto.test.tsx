import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { LoteDescargaAlertCard, porcentajeLote } from '@/components/shared/LoteDescargaAlertCard';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

/**
 * Aviso de la descarga masiva compacto y minimizable (registro diferido, extiende HU #13382/#13388).
 * AC1 — compacto: cabecera en una línea, título solo como nombre accesible/tooltip, contador y
 * «Cancelar» en la misma fila bajo la barra, partes en filas sin caja con scroll.
 *
 * Uso de ejemplo:
 *   <LoteDescargaAlertCard lote={lote} expirado={false} onDescargarParte={descargar}
 *     onCancelar={cancelar} onMinimizar={() => setMinimizado(true)} minimizado={false} />
 */

/** Datos sintéticos según `LoteConsolidados` del contrato §5. */
const LOTE: LoteConsolidados = {
  id: 'lote-compacto',
  estado: 'en_proceso',
  tipoDocumento: 'consolidado',
  total: 1200,
  procesados: 450,
  incluidos: 450,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  partes: [],
};
const COMPLETADO: LoteConsolidados = {
  ...LOTE,
  estado: 'completado',
  procesados: 1200,
  incluidos: 1200,
  terminadoEn: '2026-10-07T16:00:00Z',
  expiraEn: '2026-10-08T16:00:00Z',
  partes: [1, 2, 3, 4].map((numero) => ({
    numero,
    nombreArchivo: `consolidados_parte-0${numero}-de-04.zip`,
    pdfs: 300,
    omitidos: 0,
    bytes: 1024,
  })),
};

function montar(over: Partial<Parameters<typeof LoteDescargaAlertCard>[0]> = {}) {
  const props = { lote: LOTE, expirado: false, onDescargarParte: vi.fn(), onCancelar: vi.fn(), ...over };
  return { ...props, ...render(<LoteDescargaAlertCard {...props} />) };
}
const card = () => screen.getByTestId('lote-descarga-card');

describe('porcentajeLote', () => {
  it('lote activo: porcentaje entero truncado (no anuncia 100 % antes de terminar)', () => {
    expect(porcentajeLote(LOTE, false)).toBe(37);
    expect(porcentajeLote({ ...LOTE, procesados: 1199 }, false)).toBe(99);
  });
  it('terminado, expirado o sin total: null (sin %)', () => {
    expect(porcentajeLote(COMPLETADO, false)).toBeNull();
    expect(porcentajeLote(LOTE, true)).toBeNull();
    expect(porcentajeLote({ ...LOTE, total: 0, procesados: 0 }, false)).toBeNull();
  });
});

describe('LoteDescargaAlertCard compacto — AC1', () => {
  it('el título queda como nombre accesible (encabezado sr-only + tooltip), no como texto visible', () => {
    montar();
    const h = screen.getByRole('heading', { name: 'Descarga masiva de consolidados' });
    expect(h.className).toContain('sr-only');
    expect(card()).toHaveAttribute('title', 'Descarga masiva de consolidados');
    expect(card()).toHaveAttribute('aria-label', 'Descarga masiva de consolidados');
  });

  it('cabecera en una línea con estado y % del avance; barra fina h-1', () => {
    montar();
    const cabecera = screen.getByTestId('lote-descarga-cabecera');
    expect(within(cabecera).getByRole('status')).toHaveTextContent('Descarga en proceso');
    expect(cabecera).toHaveTextContent('37%');
    expect(screen.getByRole('progressbar').className).toContain('h-1');
  });

  it('contador y «Cancelar» (texto rojo, sin caja) comparten la fila bajo la barra', () => {
    montar();
    const pie = screen.getByTestId('lote-descarga-pie');
    expect(pie).toHaveTextContent('450 / 1.200');
    const cancelar = within(pie).getByRole('button', { name: 'Cancelar la descarga' });
    expect(cancelar.getAttribute('style')).toContain('var(--badge-danger-fg)');
    expect(cancelar.getAttribute('style') ?? '').not.toContain('border');
  });

  it('tarjeta compacta: max-w-xs al usarse en el seguimiento y padding px-3 py-2', () => {
    montar({ className: 'max-w-xs' });
    expect(card().className).toContain('px-3');
    expect(card().className).toContain('py-2');
  });

  it('partes en filas sin caja, con divisor y alto máximo con scroll', () => {
    const { container } = montar({ lote: COMPLETADO });
    const lista = screen.getByRole('list', { name: 'Partes de la descarga' });
    expect(lista.className).toContain('divide-y');
    expect(lista.className).toContain('max-h-24');
    expect(lista.className).toContain('overflow-y-auto');
    expect(within(lista).getAllByRole('listitem')).toHaveLength(4);
    expect(container.innerHTML).not.toContain('bg-white');
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    expect(container.innerHTML).not.toContain('text-[10px]');
  });
});

describe('LoteDescargaAlertCard — botón minimizar (AC2)', () => {
  it('sin onMinimizar no se pinta el botón', () => {
    montar();
    expect(screen.queryByRole('button', { name: 'Minimizar la descarga masiva' })).not.toBeInTheDocument();
  });

  it('con onMinimizar: <button> con aria-label, aria-expanded=true, aria-controls a la tarjeta y foco visible', async () => {
    const onMinimizar = vi.fn();
    montar({ onMinimizar });
    const b = screen.getByRole('button', { name: 'Minimizar la descarga masiva' });
    expect(b).toHaveAttribute('type', 'button');
    expect(b).toHaveAttribute('aria-expanded', 'true');
    expect(b.getAttribute('aria-controls')).toBe(card().id);
    expect(b.className).toContain('focus-visible:ring-2');
    await userEvent.click(b);
    expect(onMinimizar).toHaveBeenCalledTimes(1);
  });

  it('minimizado: la tarjeta sale de la vista y queda inerte, pero la región viva sigue accesible (AC6)', () => {
    montar({ onMinimizar: vi.fn(), minimizado: true });
    expect(card()).toHaveAttribute('data-minimizado', 'true');
    expect(card().className).toContain('pointer-events-none');
    expect(screen.getByRole('status')).toHaveTextContent('Descarga en proceso');
    expect(screen.queryAllByRole('button')).toHaveLength(0);
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('transición de ~200 ms solo con movimiento permitido (prefers-reduced-motion)', () => {
    montar({ onMinimizar: vi.fn() });
    expect(card().className).toContain('motion-safe:duration-200');
    expect(card().className).toContain('motion-reduce:transition-none');
  });
});
