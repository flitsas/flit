import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { LoteDescargaAlertCard, TEXTO_DESCARGA_CANCELADA } from '@/components/shared/LoteDescargaAlertCard';
import { MENSAJE_NO_SE_PUDO_CANCELAR } from '@/lib/api/consolidado-lotes-client';
import type { EstadoLoteConsolidados, LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

// Uso de ejemplo (HU #13388):
//   <LoteDescargaAlertCard lote={lote} expirado={expirado} onDescargarParte={descargar}
//     onCancelar={() => void cancelar()} cancelando={cancelando} errorCancelacion={errorCancelacion} />

/** Datos sintéticos según `LoteConsolidados` del contrato. */
const LOTE: LoteConsolidados = {
  id: 'lote-cancelar',
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
const PARTES = [1, 2].map((numero) => ({
  numero,
  nombreArchivo: `consolidados_parte-0${numero}-de-02.zip`,
  pdfs: 600,
  omitidos: 0,
  bytes: 1024,
}));
const CANCELADO: LoteConsolidados = {
  ...LOTE,
  estado: 'cancelado',
  terminadoEn: '2026-10-07T16:00:00Z',
  expiraEn: '2026-10-07T16:00:00Z',
};

function montar(over: Partial<Parameters<typeof LoteDescargaAlertCard>[0]> = {}) {
  const props = { lote: LOTE, expirado: false, onDescargarParte: vi.fn(), onCancelar: vi.fn(), ...over };
  const view = render(<LoteDescargaAlertCard {...props} />);
  return { ...props, ...view };
}
const botonCancelar = () => screen.queryByRole('button', { name: 'Cancelar la descarga' });
const card = () => screen.getByTestId('lote-descarga-card');

describe('LoteDescargaAlertCard — cancelar (HU #13388)', () => {
  it.each<EstadoLoteConsolidados>(['en_cola', 'en_proceso', 'empaquetando'])(
    'AC1 — en curso (%s) muestra «Cancelar» con nombre accesible «Cancelar la descarga»',
    (estado) => {
      montar({ lote: { ...LOTE, estado } });
      const b = botonCancelar();
      expect(b).toBeInTheDocument();
      expect(b).toHaveTextContent('Cancelar');
      expect(b).toBeEnabled();
    },
  );

  it.each<EstadoLoteConsolidados>(['completado', 'completado_con_omitidos', 'fallido', 'expirado', 'cancelado'])(
    'AC1 — en un lote %s no se muestra «Cancelar»',
    (estado) => {
      const partes = estado.startsWith('completado') ? PARTES : [];
      montar({ lote: { ...LOTE, estado, partes, terminadoEn: '2026-10-07T16:00:00Z' } });
      expect(botonCancelar()).not.toBeInTheDocument();
    },
  );

  it('AC1 — un lote activo que el reloj ya marcó expirado no muestra «Cancelar»', () => {
    montar({ expirado: true });
    expect(botonCancelar()).not.toBeInTheDocument();
  });

  it('AC1 — sin onCancelar no se pinta el botón (no lanza)', () => {
    expect(() => montar({ onCancelar: undefined })).not.toThrow();
    expect(botonCancelar()).not.toBeInTheDocument();
  });

  it('AC2 — un clic llama a onCancelar una vez, sin diálogo de confirmación', async () => {
    const { onCancelar } = montar();
    await userEvent.click(botonCancelar()!);
    expect(onCancelar).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('AC2 — cancelando: el botón queda deshabilitado y ocupado', async () => {
    const { onCancelar } = montar({ cancelando: true });
    const b = botonCancelar()!;
    expect(b).toBeDisabled();
    expect(b).toHaveAttribute('aria-busy', 'true');
    await userEvent.click(b);
    expect(onCancelar).not.toHaveBeenCalled();
  });

  it('AC3 — cancelado: tono neutro, «Descarga cancelada» en la región viva, sin partes ni botones', () => {
    montar({ lote: { ...CANCELADO, partes: PARTES } });
    expect(card()).toHaveAttribute('data-tone', 'neutral');
    expect(screen.getByRole('status')).toHaveTextContent(TEXTO_DESCARGA_CANCELADA);
    expect(TEXTO_DESCARGA_CANCELADA).toBe('Descarga cancelada');
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Partes de la descarga' })).not.toBeInTheDocument();
    expect(screen.queryAllByRole('button')).toHaveLength(0);
  });

  it('AC3 — cancelado prevalece sobre expirado (el servidor fija expiraEn = instante de cancelación)', () => {
    montar({ lote: CANCELADO, expirado: true });
    expect(screen.getByRole('status')).toHaveTextContent(TEXTO_DESCARGA_CANCELADA);
    expect(card()).toHaveAttribute('data-tone', 'neutral');
  });

  it('AC7 — el paso a «Descarga cancelada» se anuncia en la región aria-live="polite" del aviso', () => {
    const { rerender, onDescargarParte, onCancelar } = montar();
    const region = screen.getByRole('status');
    expect(region).toHaveAttribute('aria-live', 'polite');
    expect(region).toHaveTextContent('Descarga en proceso');
    rerender(
      <LoteDescargaAlertCard lote={CANCELADO} expirado={false} onDescargarParte={onDescargarParte} onCancelar={onCancelar} />,
    );
    expect(screen.getByRole('status')).toBe(region);
    expect(region).toHaveTextContent('Descarga cancelada');
  });

  it('AC6 — error de cancelación: «No se pudo cancelar la descarga» en tono error y el botón sigue operable', () => {
    montar({ errorCancelacion: MENSAJE_NO_SE_PUDO_CANCELAR });
    const alerta = screen.getByRole('alert');
    expect(alerta).toHaveTextContent('No se pudo cancelar la descarga');
    expect(alerta.getAttribute('style')).toContain('var(--badge-danger-fg)');
    expect(botonCancelar()).toBeEnabled();
  });

  it('AC7 — «Cancelar» es un <button> nativo operable con teclado y con foco visible', async () => {
    const { onCancelar } = montar();
    const b = botonCancelar()!;
    expect(b.tagName).toBe('BUTTON');
    expect(b).toHaveAttribute('type', 'button');
    expect(b.className).toContain('focus-visible:ring-2');
    b.focus();
    expect(b).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard(' ');
    expect(onCancelar).toHaveBeenCalledTimes(2);
  });

  it('AC7 — tonos con tokens semánticos del tema, sin hex en el botón ni en el error', () => {
    montar({ errorCancelacion: MENSAJE_NO_SE_PUDO_CANCELAR });
    const hex = /#[0-9a-f]{3,8}\b/i;
    const b = botonCancelar()!;
    expect(`${b.className} ${b.getAttribute('style') ?? ''}`).not.toMatch(hex);
    expect(b.getAttribute('style')).toContain('var(--badge-danger-');
    expect(screen.getByRole('alert').getAttribute('style') ?? '').not.toMatch(hex);
  });

  it.each<[EstadoLoteConsolidados, boolean]>([
    ['en_cola', false],
    ['en_proceso', false],
    ['empaquetando', false],
    ['completado', false],
    ['completado_con_omitidos', false],
    ['fallido', false],
    ['cancelado', false],
    ['completado', true],
    ['expirado', true],
  ])('sin cierre manual — %s (expirado=%s) no pinta «Cerrar aviso de descarga»', (estado, expirado) => {
    // Aunque un llamador antiguo siga pasando `onCerrar`, la tarjeta ya no lo admite: el aviso solo
    // desaparece solo (cancelado a los 8 s, expirado a las 24 h).
    const legado = { onCerrar: vi.fn() } as object;
    render(
      <LoteDescargaAlertCard
        lote={{ ...LOTE, estado, partes: estado.startsWith('completado') ? PARTES : [] }}
        expirado={expirado}
        onDescargarParte={vi.fn()}
        onCancelar={vi.fn()}
        {...legado}
      />,
    );
    expect(screen.queryByRole('button', { name: 'Cerrar aviso de descarga' })).not.toBeInTheDocument();
  });
});
