import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PagoPortalBloque, medioPagoLabel } from '../PagoPortalBloque';

const base = {
  valorCop: 125500,
  medio: 'caja' as const,
  medioCaja: 'efectivo' as const,
  medioDetalle: null,
  pagadoEn: '2026-11-09T15:30:00+00:00',
};

describe('PagoPortalBloque', () => {
  it('AC1: muestra Pagado en caja con valor, medio y fecha', () => {
    render(<PagoPortalBloque pago={base} />);
    expect(screen.getByText('Pagado en caja')).toBeInTheDocument();
    expect(screen.getByText('$ 125.500')).toBeInTheDocument();
    expect(screen.getByText('Efectivo')).toBeInTheDocument();
    expect(screen.getByText('09/11/2026 10:30')).toBeInTheDocument();
  });

  it('AC2: sin pago no renderiza el bloque', () => {
    const { container, rerender } = render(<PagoPortalBloque pago={undefined} />);
    expect(container).toBeEmptyDOMElement();
    rerender(<PagoPortalBloque pago={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('sin_cobro muestra Sin cobro y no el medio', () => {
    render(
      <PagoPortalBloque pago={{ ...base, valorCop: 0, medio: 'sin_cobro', medioCaja: null }} />,
    );
    expect(screen.getByText('Sin cobro')).toBeInTheDocument();
    expect(screen.queryByText(/Medio:/)).not.toBeInTheDocument();
  });

  it('formatea los medios', () => {
    expect(medioPagoLabel({ medio: 'caja', medioCaja: 'datafono' })).toBe('Datáfono');
    expect(medioPagoLabel({ medio: 'caja', medioCaja: 'transferencia' })).toBe('Transferencia');
    expect(medioPagoLabel({ medio: 'caja', medioCaja: 'otro', medioDetalle: 'Nequi' })).toBe(
      'Otro: Nequi',
    );
  });
});
