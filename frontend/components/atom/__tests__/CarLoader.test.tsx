import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

import { CarLoader, CarLoaderModal } from '@/components/atom/CarLoader';

/**
 * Escena de espera larga del asistente. Lo que se prueba no es la animación —eso es CSS— sino las
 * dos cosas de las que depende que la espera se entienda: que el mensaje nombre al tercero por el
 * que se espera, y que siga comunicando aunque los SVG no carguen.
 */
describe('CarLoader', () => {
  it('anuncia la espera como estado en vivo, no como texto suelto', () => {
    render(<CarLoader mode="runt" />);
    const region = screen.getByRole('status');
    expect(region).toHaveAttribute('aria-busy', 'true');
    expect(region).toHaveAttribute('aria-live', 'polite');
  });

  it.each([
    ['runt' as const, /RUNT/],
    ['ocr' as const, /documentos con IA/],
    ['radicacion' as const, /organismo de tránsito/],
  ])('el modo %s nombra ante quién se espera', (mode, esperado) => {
    render(<CarLoader mode={mode} />);
    expect(screen.getByText(esperado)).toBeInTheDocument();
  });

  it('un rótulo explícito manda sobre el mensaje del modo', () => {
    render(<CarLoader mode="ocr" label="Generando el FUR…" />);
    expect(screen.getByText('Generando el FUR…')).toBeInTheDocument();
    expect(screen.queryByText(/documentos con IA/)).toBeNull();
  });

  it('si los SVG no cargan, cae a la órbita dibujada y conserva el mensaje', () => {
    const { container } = render(<CarLoader mode="radicacion" />);
    // Las tres capas son decorativas: se localizan por su etiqueta, no por rol.
    const capas = container.querySelectorAll('img');
    expect(capas.length).toBe(3);

    fireEvent.error(capas[0]);

    expect(container.querySelectorAll('img')).toHaveLength(0);
    expect(container.querySelector('.flit-loader')).not.toBeNull();
    expect(screen.getByText(/organismo de tránsito/)).toBeInTheDocument();
  });

  it('el velo cubre la pantalla sin declararse diálogo (es un aviso, no pide decisión)', () => {
    render(<CarLoaderModal mode="runt" />);
    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});
