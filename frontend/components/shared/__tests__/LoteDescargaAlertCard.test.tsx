import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import {
  LoteDescargaAlertCard,
  TEXTO_DESCARGA_EXPIRADA,
  toneLoteConsolidados,
} from '@/components/shared/LoteDescargaAlertCard';
import { MENSAJE_REINTENTAR_DESCARGA } from '@/lib/api/consolidado-lotes-client';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

// Uso de ejemplo:
//   <LoteDescargaAlertCard lote={lote} expirado={false} onDescargarParte={(n) => descargar(n)}
//     errorConsulta={false} />

/** Datos sintéticos según `LoteConsolidados` del contrato §5. */
const LOTE: LoteConsolidados = {
  id: 'lote-card',
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
const PARTES = [1, 2, 3].map((numero) => ({
  numero,
  nombreArchivo: `consolidados_20261007_1000_parte-0${numero}-de-03.zip`,
  pdfs: 400,
  omitidos: 0,
  bytes: 1024,
}));
const COMPLETADO: LoteConsolidados = {
  ...LOTE,
  estado: 'completado',
  procesados: 1200,
  incluidos: 1200,
  terminadoEn: '2026-10-07T16:00:00Z',
  expiraEn: '2026-10-08T16:00:00Z',
  partes: PARTES,
};

function montar(over: Partial<Parameters<typeof LoteDescargaAlertCard>[0]> = {}) {
  const props = { lote: LOTE, expirado: false, onDescargarParte: vi.fn(), ...over };
  const view = render(<LoteDescargaAlertCard {...props} />);
  return { ...props, ...view };
}
const card = () => screen.getByTestId('lote-descarga-card');

describe('toneLoteConsolidados — HU #13382 AC2', () => {
  it.each([
    [{ estado: 'en_cola', omitidos: 0 }, false, 'info'],
    [{ estado: 'en_proceso', omitidos: 0 }, false, 'info'],
    [{ estado: 'empaquetando', omitidos: 0 }, false, 'info'],
    [{ estado: 'en_proceso', omitidos: 2 }, false, 'warning'],
    [{ estado: 'completado_con_omitidos', omitidos: 2 }, false, 'warning'],
    [{ estado: 'completado', omitidos: 1 }, false, 'warning'],
    [{ estado: 'completado', omitidos: 0 }, false, 'success'],
    [{ estado: 'fallido', omitidos: 0 }, false, 'danger'],
    [{ estado: 'expirado', omitidos: 0 }, false, 'neutral'],
    [{ estado: 'cancelado', omitidos: 0 }, false, 'neutral'],
    [{ estado: 'completado', omitidos: 0 }, true, 'neutral'],
  ] as const)('%o (expirado=%s) → %s', (over, expirado, tone) => {
    expect(toneLoteConsolidados({ ...LOTE, ...over } as LoteConsolidados, expirado)).toBe(tone);
  });
});

describe('LoteDescargaAlertCard — HU #13382', () => {
  it('AC1 — muestra «procesados / total» y «En cola» para en_cola', () => {
    montar({ lote: { ...LOTE, estado: 'en_cola', procesados: 0, incluidos: 0 } });
    expect(card()).toHaveTextContent('0 / 1.200');
    expect(screen.getByRole('status')).toHaveTextContent('En cola');
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuemax', '1200');
  });

  it('AC1 — en proceso: progreso con miles es-CO y barra con el valor actual', () => {
    montar();
    expect(card()).toHaveTextContent('450 / 1.200');
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '450');
  });

  it('AC2 — el color sale del token del tono (var(--badge-*)) y ningún color está en hex', () => {
    const { container } = montar({ lote: { ...LOTE, omitidos: 3 } });
    expect(card()).toHaveAttribute('data-tone', 'warning');
    expect(card().getAttribute('style')).toContain('var(--badge-warning-bg)');
    expect(card().getAttribute('style')).toContain('var(--badge-warning-border)');
    expect(container.innerHTML).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
  });

  it('AC2 — fallido: tono error y mensaje de reintento, sin botones de parte', () => {
    montar({ lote: { ...LOTE, estado: 'fallido', terminadoEn: '2026-10-07T16:00:00Z' } });
    expect(card()).toHaveAttribute('data-tone', 'danger');
    expect(card()).toHaveTextContent(MENSAJE_REINTENTAR_DESCARGA);
    expect(screen.queryByRole('button', { name: /^Descargar/ })).not.toBeInTheDocument();
  });

  it('AC2/AC4 — expirado: tono neutro, «Descarga expirada» y sin botones', () => {
    montar({ lote: COMPLETADO, expirado: true });
    expect(card()).toHaveAttribute('data-tone', 'neutral');
    expect(screen.getByRole('status')).toHaveTextContent(TEXTO_DESCARGA_EXPIRADA);
    expect(screen.queryByRole('button', { name: /^Descargar/ })).not.toBeInTheDocument();
  });

  it('AC2 — completado sin omitidos: success', () => {
    montar({ lote: COMPLETADO });
    expect(card()).toHaveAttribute('data-tone', 'success');
  });

  it('AC3 — lista las 3 partes con un botón «Descargar» cada una', async () => {
    const { onDescargarParte } = montar({ lote: COMPLETADO });
    const lista = screen.getByRole('list', { name: /Partes de la descarga/ });
    expect(within(lista).getAllByRole('listitem')).toHaveLength(3);
    const botones = within(lista).getAllByRole('button');
    expect(botones.map((b) => b.textContent?.trim())).toEqual(['Descargar', 'Descargar', 'Descargar']);
    await userEvent.click(screen.getByRole('button', { name: 'Descargar parte 2 de 3' }));
    expect(onDescargarParte).toHaveBeenCalledWith(2);
  });

  it('AC6 — región aria-live="polite" con el estado y botones operables con teclado', async () => {
    const user = userEvent.setup();
    const { onDescargarParte } = montar({ lote: COMPLETADO });
    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite');
    await user.tab();
    expect(screen.getByRole('button', { name: 'Descargar parte 1 de 3' })).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(onDescargarParte).toHaveBeenCalledWith(1);
    await user.tab();
    expect(screen.getByRole('button', { name: 'Descargar parte 2 de 3' })).toHaveFocus();
    await user.keyboard(' ');
    expect(onDescargarParte).toHaveBeenCalledWith(2);
  });

  it('AC6 — el texto del estado cambia en la misma región al pasar a terminal', () => {
    const { rerender, onDescargarParte } = montar();
    const region = screen.getByRole('status');
    expect(region).toHaveTextContent(/en proceso/i);
    rerender(
      <LoteDescargaAlertCard lote={COMPLETADO} expirado={false} onDescargarParte={onDescargarParte} />,
    );
    expect(screen.getByRole('status')).toBe(region);
    expect(region).toHaveTextContent(/lista/i);
  });

  it('AC5 — error de consulta: conserva los datos y avisa que reintenta', () => {
    montar({ errorConsulta: true });
    expect(card()).toHaveTextContent('450 / 1.200');
    expect(card()).toHaveTextContent(/reintentando/i);
  });

  it('parte descargándose: su botón queda deshabilitado; error de descarga visible', () => {
    montar({ lote: COMPLETADO, descargandoParte: 1, errorDescarga: MENSAJE_REINTENTAR_DESCARGA });
    expect(screen.getByRole('button', { name: 'Descargar parte 1 de 3' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Descargar parte 2 de 3' })).toBeEnabled();
    expect(screen.getByRole('alert')).toHaveTextContent(MENSAJE_REINTENTAR_DESCARGA);
  });

  it('a11y — todos los botones tienen nombre accesible', () => {
    montar({ lote: COMPLETADO });
    screen.getAllByRole('button').forEach((b) => {
      expect(b.getAttribute('aria-label') || b.textContent?.trim()).toBeTruthy();
    });
  });
});
