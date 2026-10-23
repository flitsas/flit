import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';

import { LoteDescargaAlertCard, etiquetaAlcanceLote } from '@/components/shared/LoteDescargaAlertCard';
import type { LoteConsolidados } from '@/lib/api/types-consolidado-lotes';

// Uso de ejemplo (HU #13419 AC6):
//   <LoteDescargaAlertCard lote={lote} nombreHija="Concesionario Hijo SAS" expirado={false}
//     onDescargarParte={descargar} />
//   // lote.alcanceRed 'red' → «Red»; 'hija' → «Red · Concesionario Hijo SAS» (sin nombre: «Red · compañía
//   // de la red»); ausente → sin rótulo.

/** Datos sintéticos según `LoteConsolidados` del contrato (HU #13417). */
const LOTE: LoteConsolidados = {
  id: 'lote-red',
  estado: 'en_proceso',
  tipoDocumento: 'consolidado',
  total: 57,
  procesados: 10,
  incluidos: 10,
  omitidos: 0,
  creadoEn: '2026-10-07T15:00:00Z',
  terminadoEn: null,
  expiraEn: null,
  partes: [],
};

const montar = (lote: LoteConsolidados, nombreHija?: string | null) =>
  render(<LoteDescargaAlertCard lote={lote} nombreHija={nombreHija} expirado={false} onDescargarParte={vi.fn()} />);
const card = () => screen.getByTestId('lote-descarga-card');

describe('etiquetaAlcanceLote — HU #13419 AC6', () => {
  it.each([
    [undefined, null, null],
    [null, 'X', null],
    ['red', null, 'Red'],
    ['red', 'Ignorada', 'Red'],
    ['hija', 'Concesionario Hijo SAS', 'Red · Concesionario Hijo SAS'],
    ['hija', null, 'Red · compañía de la red'],
    ['hija', '   ', 'Red · compañía de la red'],
  ] as const)('alcanceRed=%s nombreHija=%s ⇒ %s', (alcanceRed, nombreHija, esperado) => {
    expect(etiquetaAlcanceLote({ ...LOTE, alcanceRed }, nombreHija)).toBe(esperado);
  });
});

describe('LoteDescargaAlertCard — rótulo de red (HU #13419 AC6)', () => {
  it('lote de toda la red: el aviso indica «Red»', () => {
    montar({ ...LOTE, alcanceRed: 'red' });
    expect(within(card()).getByTestId('lote-alcance-red')).toHaveTextContent(/^Alcance:\s*Red$/);
  });

  it('lote acotado a una hija: «Red · nombre de la hija»', () => {
    montar({ ...LOTE, alcanceRed: 'hija' }, 'Concesionario Hijo SAS');
    expect(within(card()).getByTestId('lote-alcance-red')).toHaveTextContent('Red · Concesionario Hijo SAS');
  });

  it('borde — lote de hija sin nombre resoluble (hija fuera de la red, lista no cargada): «Red · compañía de la red»', () => {
    montar({ ...LOTE, alcanceRed: 'hija' }, null);
    expect(within(card()).getByTestId('lote-alcance-red')).toHaveTextContent(/Red · compañía de la red$/);
  });

  it('contrato — lote propio (sin `alcanceRed`): no hay rótulo de red', () => {
    montar(LOTE);
    expect(within(card()).queryByTestId('lote-alcance-red')).not.toBeInTheDocument();
    expect(screen.queryByText(/^Red/)).not.toBeInTheDocument();
  });

  it('a11y — el rótulo no depende del color: texto «Alcance:» para lector de pantalla', () => {
    montar({ ...LOTE, alcanceRed: 'red' });
    const rotulo = within(card()).getByTestId('lote-alcance-red');
    expect(rotulo.textContent).toMatch(/Alcance:/);
  });
});
