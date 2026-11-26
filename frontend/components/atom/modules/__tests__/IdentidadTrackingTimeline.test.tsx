import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { IdentidadHitosTimeline, intentosPorHito } from '../IdentidadTrackingTimeline';
import type { HitoIdentidad } from '@/lib/tramites/identidad-lectura';

function hito(n: number, extra: Partial<HitoIdentidad> = {}): HitoIdentidad {
  return {
    id: `h-${n}`,
    cuando: `2026-11-${String(10 + n).padStart(2, '0')}T10:00:00Z`,
    titulo: `Hito ${n}`,
    detalle: null,
    tono: 'neutro',
    ...extra,
  };
}

// Más reciente primero, como los entrega hitosDeIdentidad.
// Intento 2: hitos 6, 5 y el envío 4. Intento 1: hitos 3, 2 y el envío 1.
const dos_intentos = [hito(6), hito(5), hito(4, { inicioCiclo: true }), hito(3), hito(2), hito(1, { inicioCiclo: true })];

describe('IdentidadTrackingTimeline', () => {
  it('numera los intentos: cada primer envío abre uno nuevo; sin envío todo es el intento 1', () => {
    expect(intentosPorHito(dos_intentos)).toEqual([2, 2, 2, 1, 1, 1]);
    expect(intentosPorHito([hito(2), hito(1)])).toEqual([1, 1]);
    // El hito de apertura más antiguo no suma un intento por sí solo.
    expect(intentosPorHito([hito(2), hito(1, { inicioCiclo: true })])).toEqual([1, 1]);
  });

  it('con pocos hitos los muestra todos y no ofrece colapsar', () => {
    render(<IdentidadHitosTimeline hitos={[hito(2), hito(1)]} />);
    expect(screen.getByText('Hito 1')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('con muchos hitos deja los 5 recientes y despliega el resto agrupado por intento', async () => {
    const user = userEvent.setup();
    render(<IdentidadHitosTimeline hitos={dos_intentos} />);

    expect(screen.getByText('Últimos 5 de 6 hitos')).toBeInTheDocument();
    expect(screen.queryByText('Hito 1')).not.toBeInTheDocument();
    expect(screen.queryByText(/^Intento/)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Ver los 1 anteriores' }));
    expect(screen.getByText('Hito 1')).toBeInTheDocument();
    expect(screen.getByText('Intento 2')).toBeInTheDocument();
    expect(screen.getByText('Intento 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ver solo los recientes' })).toHaveAttribute('aria-expanded', 'true');
  });
});
