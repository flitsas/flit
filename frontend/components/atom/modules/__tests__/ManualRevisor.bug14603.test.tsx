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
        client={conRevision({ status: 'aprobado', reviewedBy: 'Ana Pérez', reviewedAt: '2026-10-05T15:00:00Z' })}
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText('Aprobada por Ana Pérez el 05/10/2026 10:00')).toBeInTheDocument();
  });

  it('rechazada: «Rechazada por X el …»', async () => {
    render(
      <ManualReviewDetailModal
        id={RECHAZADA}
        client={conRevision({ reviewedBy: 'Luis Gómez', reviewedAt: '2026-10-05T15:00:00Z' })}
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText('Rechazada por Luis Gómez el 05/10/2026 10:00')).toBeInTheDocument();
  });

  it('sin revisor ni fecha no pinta nada', async () => {
    render(
      <ManualReviewDetailModal id={RECHAZADA} client={conRevision({ reviewedBy: null, reviewedAt: null })} onClose={() => {}} />,
    );
    await screen.findByRole('status', { name: 'Estado: Rechazada' });
    expect(screen.queryByText(/Rechazada por|Rechazada el|null|undefined/)).not.toBeInTheDocument();
  });

  it('aprobada sin revisor: «Aprobada el …» sin «por» ni «null»', async () => {
    render(
      <ManualReviewDetailModal
        id={APROBADA}
        client={conRevision({ status: 'aprobado', reviewedBy: null, reviewedAt: '2026-10-05T15:00:00Z' })}
        onClose={() => {}}
      />,
    );
    expect(await screen.findByText('Aprobada el 05/10/2026 10:00')).toBeInTheDocument();
    expect(screen.queryByText(/por null|null/)).not.toBeInTheDocument();
  });
});
