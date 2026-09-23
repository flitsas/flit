/**
 * HU #12792 (Épica #12760) — componente compartido `IndicadorVigenciaConsolidado`.
 *
 * Uso de ejemplo:
 *   <IndicadorVigenciaConsolidado vigencia={item.consolidadoWizard} variante="compacta" />
 *   <IndicadorVigenciaConsolidado vigencia={detalle.consolidadoWizard}>{avisoExtra}</IndicadorVigenciaConsolidado>
 */
import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import type { ConsolidadoVigencia } from '@/lib/api/types/procedure-runtime';
import { IndicadorVigenciaConsolidado } from '@/components/shared/IndicadorVigenciaConsolidado';

const GENERADO = '2026-09-23T15:05:00Z'; // 10:05 Bogotá

function vigencia(overrides: Partial<ConsolidadoVigencia> = {}): ConsolidadoVigencia {
  return {
    estado: 'vigente',
    generadoEn: GENERADO,
    origen: 'system',
    definitivo: false,
    modo: null,
    ...overrides,
  };
}

function punto() {
  return screen.getByTestId('vigencia-consolidado-punto');
}

describe.each(['compacta', 'completa'] as const)('variante %s', (variante) => {
  it('AC1 — vigente: punto verde #70CF3A + texto + fecha/hora', () => {
    render(<IndicadorVigenciaConsolidado vigencia={vigencia()} variante={variante} />);
    const raiz = screen.getByTestId('vigencia-consolidado');
    expect(raiz).toHaveAttribute('data-estado', 'vigente');
    expect(raiz).toHaveAttribute('data-variante', variante);
    expect(punto()).toHaveStyle({ background: '#70CF3A' });
    expect(raiz).toHaveTextContent(/vigente/i);
    expect(raiz).toHaveTextContent('23/09/2026 10:05');
    expect(screen.queryByTestId('vigencia-consolidado-definitivo')).toBeNull();
  });

  it('AC1 — definitivo: vigente con la marca «Definitivo»', () => {
    render(
      <IndicadorVigenciaConsolidado
        vigencia={vigencia({ definitivo: true, modo: 'definitivo_estado_final' })}
        variante={variante}
      />,
    );
    expect(screen.getByTestId('vigencia-consolidado')).toHaveAttribute('data-estado', 'vigente');
    expect(screen.getByTestId('vigencia-consolidado-definitivo')).toHaveTextContent('Definitivo');
  });

  it('AC2 — desactualizado: punto gris #59677D + leyenda de pendiente de regenerar', () => {
    render(
      <IndicadorVigenciaConsolidado vigencia={vigencia({ estado: 'desactualizado' })} variante={variante} />,
    );
    const raiz = screen.getByTestId('vigencia-consolidado');
    expect(raiz).toHaveAttribute('data-estado', 'desactualizado');
    expect(punto()).toHaveStyle({ background: '#59677D' });
    expect(raiz).toHaveTextContent(/desactualizado/i);
    expect(raiz).toHaveTextContent('Pendiente de regenerar');
  });

  it('AC2 — leyendaDesactualizado sobrescribe el texto', () => {
    render(
      <IndicadorVigenciaConsolidado
        vigencia={vigencia({ estado: 'desactualizado' })}
        variante={variante}
        documento="maestro"
        leyendaDesactualizado="Se reconstruirá al abrirlo"
      />,
    );
    const raiz = screen.getByTestId('vigencia-consolidado');
    expect(raiz).toHaveTextContent('Se reconstruirá al abrirlo');
    expect(raiz).toHaveTextContent(/consolidado maestro/i);
    expect(raiz).not.toHaveTextContent('Pendiente de regenerar');
  });

  it('AC3 — inexistente: informa que aún no se ha generado y sin fecha', () => {
    render(
      <IndicadorVigenciaConsolidado
        vigencia={vigencia({ estado: 'inexistente', generadoEn: null, origen: null })}
        variante={variante}
      />,
    );
    const raiz = screen.getByTestId('vigencia-consolidado');
    expect(raiz).toHaveAttribute('data-estado', 'inexistente');
    expect(raiz).toHaveTextContent('Aún no se ha generado');
    expect(raiz.textContent).not.toMatch(/\d{2}\/\d{2}\/\d{4}/);
  });

  it.each([
    ['vigente', /vigente, generado el 23\/09\/2026 10:05/i],
    ['desactualizado', /desactualizado, pendiente de regenerar/i],
    ['inexistente', /aún no se ha generado/i],
  ] as const)('AC5 — %s: nombre accesible con el estado en texto', (estado, patron) => {
    render(<IndicadorVigenciaConsolidado vigencia={vigencia({ estado })} variante={variante} />);
    // Ambas variantes son `group` con `aria-label` (G4: `status` las anunciaba como región viva).
    const rol = 'group';
    const region = screen.getByRole(rol, { name: patron });
    expect(region).toHaveAttribute('aria-label');
    // El punto de color es decorativo: el estado no depende solo de él.
    expect(punto()).toHaveAttribute('aria-hidden', 'true');
  });

  it.each([undefined, null])('sin dato (%s) no pinta nada', (valor) => {
    const { container } = render(
      <IndicadorVigenciaConsolidado vigencia={valor} variante={variante} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('hueco para avisos extra (children) fuera de la región etiquetada', () => {
    render(
      <IndicadorVigenciaConsolidado vigencia={vigencia()} variante={variante}>
        <span role="alert">Falló la última regeneración</span>
      </IndicadorVigenciaConsolidado>,
    );
    const raiz = screen.getByTestId('vigencia-consolidado');
    expect(within(raiz).getByRole('alert')).toHaveTextContent('Falló la última regeneración');
    const rol = 'group';
    expect(within(screen.getByRole(rol)).queryByRole('alert')).toBeNull();
  });
});

it('AC5 — el texto del vigente no usa #70CF3A como color (contraste AA)', () => {
  render(<IndicadorVigenciaConsolidado vigencia={vigencia()} />);
  const rotulo = screen.getByText(/Consolidado: Vigente/);
  expect(rotulo.getAttribute('style') ?? '').not.toMatch(/#70CF3A|rgb\(112, 207, 58\)/i);
});

it('variante por defecto: completa', () => {
  render(<IndicadorVigenciaConsolidado vigencia={vigencia()} />);
  expect(screen.getByTestId('vigencia-consolidado')).toHaveAttribute('data-variante', 'completa');
});
