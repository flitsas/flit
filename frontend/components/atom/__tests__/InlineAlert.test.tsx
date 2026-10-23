import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { InlineAlert } from '../InlineAlert';

describe('InlineAlert', () => {
  it('anuncia error, aviso y pendiente como alert assertive (interrumpen una acción del usuario)', () => {
    const { rerender } = render(<InlineAlert tone="error">Algo falló</InlineAlert>);
    const error = screen.getByRole('alert');
    expect(error).toHaveTextContent('Algo falló');
    expect(error).toHaveAttribute('aria-live', 'assertive');

    rerender(<InlineAlert tone="warning">Revisa el SOAT</InlineAlert>);
    expect(screen.getByRole('alert')).toHaveAttribute('aria-live', 'assertive');

    rerender(<InlineAlert tone="pending">Pendiente por aprobación del OT</InlineAlert>);
    const pending = screen.getByRole('alert');
    expect(pending).toHaveAttribute('aria-live', 'assertive');
    expect(pending).toHaveTextContent('Pendiente por aprobación del OT');
    expect(pending.getAttribute('style')).toContain('--badge-pending-bg');
  });

  // Uso de ejemplo: INLINE_ALERT_TONES contracts — AC5 no regresión de tonos base.
  it('HU #12726 (AC5) — tonos error/warning/info/success conservan tokens (no pending)', () => {
    const { rerender } = render(<InlineAlert tone="error">E</InlineAlert>);
    expect(screen.getByRole('alert').getAttribute('style')).toContain('--badge-danger-bg');

    rerender(<InlineAlert tone="warning">W</InlineAlert>);
    expect(screen.getByRole('alert').getAttribute('style')).toContain('--badge-warning-bg');

    rerender(<InlineAlert tone="info">I</InlineAlert>);
    expect(screen.getByRole('status').getAttribute('style')).toContain('--badge-info-bg');

    rerender(<InlineAlert tone="success">S</InlineAlert>);
    expect(screen.getByRole('status').getAttribute('style')).toContain('--badge-success-bg');
  });

  it('anuncia info y éxito como status polite', () => {
    const { rerender } = render(<InlineAlert tone="info">Dato de contexto</InlineAlert>);
    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite');

    rerender(<InlineAlert tone="success">Listo</InlineAlert>);
    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite');
  });

  it('muestra el título junto al mensaje cuando se pasa', () => {
    render(
      <InlineAlert tone="warning" title="No se puede procesar todavía">
        El RUNT no reporta un SOAT vigente.
      </InlineAlert>,
    );

    const alerta = screen.getByRole('alert');
    expect(alerta).toHaveTextContent('No se puede procesar todavía');
    expect(alerta).toHaveTextContent('El RUNT no reporta un SOAT vigente.');
  });

  it('renderiza la acción opcional dentro del aviso', () => {
    render(
      <InlineAlert tone="error" action={<button type="button">Reintentar</button>}>
        No se pudo cargar
      </InlineAlert>,
    );

    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });

  it('el icono es decorativo: no aporta texto accesible', () => {
    render(<InlineAlert tone="warning">Solo el mensaje</InlineAlert>);
    expect(screen.getByRole('alert')).toHaveTextContent('Solo el mensaje');
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });
});
