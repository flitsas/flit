/**
 * Bug #14603 — trazabilidad: el detalle de la validación manual dice quién la aprobó/rechazó y cuándo.
 * Sin datos no se pinta nada (nunca «null»/«undefined»).
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ManualReviewDetailModal } from '@/components/atom/modules/ManualReviewDetailModal';
import { createMockManualReviewClient } from '@/lib/api/manual-review-mock';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';

vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: vi.fn() }),
  ToastProvider: ({ children }: { children: React.ReactNode }) => children,
}));

const NOW = new Date('2026-10-05T15:00:00Z');
const RECHAZADA = 'mock-manual-05';
const APROBADA = 'mock-manual-03';

beforeEach(() => {
  let n = 0;
  URL.createObjectURL = vi.fn(() => `blob:mock/${++n}`);
  URL.revokeObjectURL = vi.fn();
});

function conRevision(extra: Record<string, unknown>): ManualReviewClient {
  const base = createMockManualReviewClient({ delayMs: 0, now: NOW });
  return {
    ...base,
    getManualDetail: async (id, s) => ({ ...(await base.getManualDetail(id, s)), ...extra }),
  };
}

describe('Detalle manual — quién revisó', () => {
  it('aprobada: «Aprobada por X el DD/MM/YYYY HH:mm»', async () => {
    render(
      <ManualReviewDetailModal
        id={APROBADA}
        client={conRevision({ status: 'aprobado', reviewer: null, reviewedBy: 'Ana Pérez', reviewedAt: '2026-10-05T15:00:00Z' })}
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText('Aprobada por Ana Pérez el 05/10/2026 10:00')).toBeInTheDocument();
  });

  it('rechazada: «Rechazada por X el …»', async () => {
    render(
      <ManualReviewDetailModal
        id={RECHAZADA}
        client={conRevision({ reviewer: null, reviewedBy: 'Luis Gómez', reviewedAt: '2026-10-05T15:00:00Z' })}
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText('Rechazada por Luis Gómez el 05/10/2026 10:00')).toBeInTheDocument();
  });

  it('sin revisor ni fecha no pinta nada', async () => {
    render(
      <ManualReviewDetailModal id={RECHAZADA} client={conRevision({ reviewer: null, reviewedBy: null, reviewedAt: null })} onClose={() => {}} />,
    );
    await screen.findByRole('status', { name: 'Estado: Rechazada' });
    expect(screen.queryByText(/Rechazada por|Rechazada el|null|undefined/)).not.toBeInTheDocument();
  });

  it('aprobada sin revisor: «Aprobada el …» sin «por» ni «null»', async () => {
    render(
      <ManualReviewDetailModal
        id={APROBADA}
        client={conRevision({ status: 'aprobado', reviewer: null, reviewedBy: null, reviewedAt: '2026-10-05T15:00:00Z' })}
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText('Aprobada el 05/10/2026 10:00')).toBeInTheDocument();
    expect(screen.queryByText(/por null|null/)).not.toBeInTheDocument();
  });
});

describe('Detalle manual — revisor completo (Super Admin)', () => {
  const fecha = '2026-10-05T15:00:00Z';

  it('aprobada con nombre, correo y rol', async () => {
    render(
      <ManualReviewDetailModal
        id={APROBADA}
        client={conRevision({
          status: 'aprobado',
          reviewer: { name: 'Ana Pérez', email: 'ana@flit.co', role: 'Super Admin' },
          reviewedAt: fecha,
        })}
        onClose={() => {}}
      />,
    );
    expect(
      await screen.findByText('Aprobada por Ana Pérez (ana@flit.co, Super Admin) el 05/10/2026 10:00'),
    ).toBeInTheDocument();
  });

  it('rechazada con revisor parcial (sin correo): omite el trozo y no muestra «null»', async () => {
    render(
      <ManualReviewDetailModal
        id={RECHAZADA}
        client={conRevision({ reviewer: { name: 'Luis Gómez', email: null, role: 'Super Admin' }, reviewedAt: fecha })}
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText('Rechazada por Luis Gómez (Super Admin) el 05/10/2026 10:00')).toBeInTheDocument();
    expect(screen.queryByText(/null|undefined/)).not.toBeInTheDocument();
  });

  it('reviewer vacío cae al respaldo reviewedBy', async () => {
    render(
      <ManualReviewDetailModal
        id={RECHAZADA}
        client={conRevision({ reviewer: { name: null, email: null, role: null }, reviewedBy: 'Respaldo', reviewedAt: fecha })}
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText('Rechazada por Respaldo el 05/10/2026 10:00')).toBeInTheDocument();
  });
});
