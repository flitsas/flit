import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { UseConsolidadoLoteActual } from '@/hooks/useConsolidadoLoteActual';
import { CLAVE_LOTE_MINIMIZADO } from '@/hooks/useLoteDescargaMinimizado';
import { LoteDescargaTracker, LoteDescargaTrackerProvider } from '@/components/shared/LoteDescargaTracker';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

/**
 * Aviso de la descarga masiva compacto y minimizable (registro diferido, extiende HU #13382/#13388).
 * AC2 minimizar → pestaña · AC3 restaurar + foco · AC4 persistencia por loteId · AC5 opción a ·
 * AC6 región viva montada minimizada. El seguimiento (`useConsolidadoLoteActual`) se sustituye por un
 * doble controlable: el estado minimizado es UI y no toca el polling (AC7).
 *
 * Uso de ejemplo:
 *   <LoteDescargaTrackerProvider habilitado><LoteDescargaTracker /></LoteDescargaTrackerProvider>
 *   // clic «Minimizar la descarga masiva» ⇒ pestaña «Mostrar la descarga masiva — Descarga en proceso, 37 %»
 */

const seguimiento = vi.hoisted(() => ({ s: null as unknown }));
vi.mock('@/hooks/useConsolidadoLoteActual', () => ({ useConsolidadoLoteActual: () => seguimiento.s }));
vi.mock('@/hooks/useNombreHijaLote', () => ({ useNombreHijaLote: () => null }));

/** Datos sintéticos según `LoteConsolidados` del contrato §5. */
const LOTE: LoteConsolidados = {
  id: 'lote-min-1',
  estado: 'en_proceso',
  tipoDocumento: 'consolidado',
  total: 120,
  procesados: 45,
  incluidos: 45,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  partes: [],
};
const COMPLETADO: LoteConsolidados = {
  ...LOTE,
  estado: 'completado',
  procesados: 120,
  incluidos: 120,
  terminadoEn: '2026-10-07T16:00:00Z',
  expiraEn: '2026-10-08T16:00:00Z',
  partes: [{ numero: 1, nombreArchivo: 'consolidados.zip', pdfs: 120, omitidos: 0, bytes: 1024 }],
};

function fijar(lote: LoteConsolidados, over: Partial<UseConsolidadoLoteActual> = {}) {
  seguimiento.s = {
    lote,
    errorConsulta: false,
    expirado: false,
    oculto: false,
    mostrarLote: vi.fn(),
    descargarParte: vi.fn(async () => undefined),
    descargandoParte: null,
    errorDescarga: null,
    cancelar: vi.fn(async () => undefined),
    cancelando: false,
    errorCancelacion: null,
    ...over,
  } satisfies UseConsolidadoLoteActual;
}
const arbol = () => (
  <LoteDescargaTrackerProvider habilitado>
    <LoteDescargaTracker />
  </LoteDescargaTrackerProvider>
);
const card = () => screen.getByTestId('lote-descarga-card');
const minimizar = () => screen.getByRole('button', { name: 'Minimizar la descarga masiva' });
const pestana = () => screen.queryByTestId('lote-descarga-pestana');

beforeEach(() => {
  window.localStorage.clear();
  fijar(LOTE);
});
afterEach(() => {
  vi.restoreAllMocks();
  window.localStorage.clear();
});

describe('LoteDescargaTracker minimizable', () => {
  it('AC1 — compacto en el Shell: max-w-xs, título accesible, contador y Cancelar; arranca abierta', () => {
    render(arbol());
    expect(card().className).toContain('max-w-xs');
    expect(screen.getByRole('heading', { name: 'Descarga masiva de consolidados' })).toBeInTheDocument();
    expect(screen.getByTestId('lote-descarga-pie')).toHaveTextContent('45 / 120');
    expect(screen.getByRole('button', { name: 'Cancelar la descarga' })).toBeInTheDocument();
    expect(minimizar()).toHaveAttribute('aria-expanded', 'true');
    expect(pestana()).not.toBeInTheDocument();
  });

  it('AC2 — minimizar: la tarjeta sale de la vista y queda una pestaña al borde derecho con icono y % del tono', async () => {
    render(arbol());
    await userEvent.click(minimizar());
    expect(card()).toHaveAttribute('data-minimizado', 'true');
    expect(screen.queryByRole('button', { name: 'Cancelar la descarga' })).not.toBeInTheDocument();
    const p = screen.getByRole('button', { name: 'Mostrar la descarga masiva — Descarga en proceso, 37 %' });
    expect(p).toBe(pestana());
    expect(p.tagName).toBe('BUTTON');
    expect(p).toHaveAttribute('aria-expanded', 'false');
    expect(p).toHaveAttribute('data-tone', 'info');
    expect(p).toHaveTextContent('37%');
    expect(p.className).toContain('right-0');
    expect(p.className).toContain('w-10');
    expect(p.className).toContain('h-14');
    expect(p.getAttribute('style')).toContain('var(--badge-info-');
  });

  it('AC3 — foco: a la pestaña al minimizar y de vuelta al botón de la tarjeta al restaurar (teclado)', async () => {
    const user = userEvent.setup();
    render(arbol());
    minimizar().focus();
    await user.keyboard('{Enter}');
    expect(pestana()).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(pestana()).not.toBeInTheDocument();
    expect(card()).toHaveAttribute('data-minimizado', 'false');
    expect(minimizar()).toHaveFocus();
  });

  it('AC3 — restaurar con clic vuelve a mostrar contador y Cancelar', async () => {
    render(arbol());
    await userEvent.click(minimizar());
    await userEvent.click(pestana()!);
    expect(screen.getByRole('button', { name: 'Cancelar la descarga' })).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
  });

  it('AC4 — se recuerda por loteId: al recargar sigue minimizada y sin robar el foco; un lote nuevo aparece abierto', async () => {
    const primera = render(arbol());
    await userEvent.click(minimizar());
    expect(window.localStorage.getItem(CLAVE_LOTE_MINIMIZADO)).toBe('lote-min-1');
    primera.unmount();

    const segunda = render(arbol());
    expect(pestana()).toBeInTheDocument();
    expect(card()).toHaveAttribute('data-minimizado', 'true');
    expect(pestana()).not.toHaveFocus();
    segunda.unmount();

    fijar({ ...LOTE, id: 'lote-min-2' });
    render(arbol());
    expect(pestana()).not.toBeInTheDocument();
    expect(card()).toHaveAttribute('data-minimizado', 'false');
  });

  it('AC4 — restaurar borra la marca del lote', async () => {
    render(arbol());
    await userEvent.click(minimizar());
    await userEvent.click(pestana()!);
    expect(window.localStorage.getItem(CLAVE_LOTE_MINIMIZADO)).toBeNull();
  });

  it('AC4 — storage que lanza: arranca abierta y minimizar sigue funcionando en memoria', async () => {
    window.localStorage.setItem(CLAVE_LOTE_MINIMIZADO, 'lote-min-1');
    const original = Object.getOwnPropertyDescriptor(window, 'localStorage')!;
    // Navegador con storage bloqueado (política / modo privado): el propio acceso lanza SecurityError.
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get() {
        throw new DOMException('bloqueado', 'SecurityError');
      },
    });
    try {
      render(arbol());
      expect(card()).toHaveAttribute('data-minimizado', 'false');
      await userEvent.click(minimizar());
      expect(pestana()).toBeInTheDocument();
    } finally {
      Object.defineProperty(window, 'localStorage', original);
    }
  });

  it('AC5 — opción a: si el lote termina minimizado sigue minimizado; cambia tono/icono y muestra el punto de aviso', async () => {
    const { rerender } = render(arbol());
    await userEvent.click(minimizar());
    expect(screen.queryByTestId('lote-descarga-pestana-aviso')).not.toBeInTheDocument();

    fijar(COMPLETADO);
    rerender(arbol());
    const p = pestana()!;
    expect(card()).toHaveAttribute('data-minimizado', 'true');
    expect(p).toHaveAttribute('data-tone', 'success');
    expect(p).toHaveAccessibleName('Mostrar la descarga masiva — Descarga lista');
    expect(p).not.toHaveTextContent('%');
    expect(p.getAttribute('style')).toContain('var(--badge-success-');
    expect(screen.getByTestId('lote-descarga-pestana-aviso')).toBeInTheDocument();
  });

  it.each([
    [{ ...LOTE, estado: 'fallido' as const, terminadoEn: '2026-10-07T16:00:00Z' }, 'danger', 'La descarga falló'],
    [{ ...COMPLETADO, omitidos: 3, estado: 'completado_con_omitidos' as const }, 'warning', 'Descarga lista, con trámites omitidos'],
    [{ ...LOTE, estado: 'cancelado' as const }, 'neutral', 'Descarga cancelada'],
  ])('AC5 — terminado minimizado (%#) → pestaña %s con aviso', async (terminal, tone, estado) => {
    const { rerender } = render(arbol());
    await userEvent.click(minimizar());
    fijar(terminal);
    rerender(arbol());
    expect(pestana()).toHaveAttribute('data-tone', tone);
    expect(pestana()).toHaveAccessibleName(`Mostrar la descarga masiva — ${estado}`);
    expect(screen.getByTestId('lote-descarga-pestana-aviso')).toBeInTheDocument();
  });

  it('AC6 — la región role=status aria-live sigue montada minimizada y anuncia el cambio de estado', async () => {
    const { rerender } = render(arbol());
    const region = screen.getByRole('status');
    await userEvent.click(minimizar());
    expect(screen.getByRole('status')).toBe(region);
    expect(region).toHaveAttribute('aria-live', 'polite');
    fijar(COMPLETADO);
    rerender(arbol());
    expect(screen.getByRole('status')).toBe(region);
    expect(region).toHaveTextContent('Descarga lista');
  });

  it('AC7 — oculto (cancelado tras 8 s) sigue sin pintar nada, ni tarjeta ni pestaña', async () => {
    const { rerender } = render(arbol());
    await userEvent.click(minimizar());
    fijar({ ...LOTE, estado: 'cancelado' }, { oculto: true });
    rerender(arbol());
    expect(screen.queryByTestId('lote-descarga-card')).not.toBeInTheDocument();
    expect(pestana()).not.toBeInTheDocument();
  });
});
