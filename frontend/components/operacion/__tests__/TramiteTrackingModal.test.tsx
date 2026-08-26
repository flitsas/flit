'use client';

import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TramiteTrackingModal } from '@/components/operacion/TramiteTrackingModal';

const getInstance = vi.fn();

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: (...args: unknown[]) => getInstance(...args),
  },
}));

describe('TramiteTrackingModal', () => {
  beforeEach(() => {
    getInstance.mockReset();
    getInstance.mockResolvedValue({
      id: 'inst-1',
      statusHistory: [
        { fromStatus: null, toStatus: 'borrador', changedAt: '2026-07-01T09:00:00Z', reason: null },
        {
          fromStatus: 'borrador',
          toStatus: 'preparado',
          changedAt: '2026-07-02T09:00:00Z',
          reason: null,
        },
      ],
    });
  });

  it('carga statusHistory y pinta la línea de tiempo', async () => {
    render(
      <TramiteTrackingModal
        open
        instanceId="inst-1"
        titleHint="TR-1"
        onClose={() => undefined}
      />,
    );

    expect(await screen.findByText(/Preparado desde Borrador/)).toBeInTheDocument();
    expect(getInstance).toHaveBeenCalledWith('inst-1', undefined);
  });

  it('cerrado no llama getInstance', async () => {
    render(
      <TramiteTrackingModal open={false} instanceId="inst-1" onClose={() => undefined} />,
    );
    await waitFor(() => expect(getInstance).not.toHaveBeenCalled());
  });
});
