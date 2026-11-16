/**
 * HU #14311 — el detalle de revisión manual marca «No verificada» y el motivo junto a cada foto que no pasó la
 * validación en el dispositivo; sin aviso si todas pasaron o la captura es anterior; y no bloquea aprobar/rechazar.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { ManualReviewDetailModal } from '@/components/atom/modules/ManualReviewDetailModal';
import { createMockManualReviewClient } from '@/lib/api/manual-review-mock';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import type { ManualPhotoChecks } from '@/lib/api/types/manual-review';

const NOW = new Date('2026-10-05T15:00:00Z');
const PENDIENTE = 'mock-manual-01';

const OK = { estado: 'verificada', motivo: null } as const;

beforeEach(() => {
  let n = 0;
  URL.createObjectURL = vi.fn(() => `blob:mock/${++n}`);
  URL.revokeObjectURL = vi.fn();
});

function clienteCon(validacionFotos: ManualPhotoChecks | null | undefined): ManualReviewClient {
  const base = createMockManualReviewClient({ delayMs: 0, now: NOW });
  return {
    ...base,
    getManualDetail: async (id, signal) => {
      const detail = await base.getManualDetail(id, signal);
      return validacionFotos === undefined ? detail : { ...detail, validacionFotos };
    },
  };
}

async function abrir(client: ManualReviewClient) {
  render(<ManualReviewDetailModal id={PENDIENTE} client={client} onClose={() => {}} />);
  const dialog = await screen.findByRole('dialog', { name: 'Detalle de validación manual' });
  await screen.findByAltText('documento anverso');
  return dialog;
}

describe('Detalle manual — fotos no verificadas (HU #14311)', () => {
  it('AC1: «No verificada» y el motivo junto a cada foto que no pasó; las verificadas no llevan aviso', async () => {
    const dialog = await abrir(
      clienteCon({
        rostro: OK,
        anverso: { estado: 'no_verificada', motivo: 'lado_incorrecto' },
        reverso: { estado: 'no_verificada', motivo: 'sin_soporte' },
      }),
    );
    const avisos = within(dialog).getAllByText('No verificada.');
    expect(avisos).toHaveLength(2);
    const anverso = within(screen.getByText('Documento (anverso)').closest('li') as HTMLElement);
    expect(anverso.getByText('No verificada.')).toBeInTheDocument();
    expect(anverso.getByText(/Puede ser el lado equivocado del documento/)).toBeInTheDocument();
    const reverso = within(screen.getByText('Documento (reverso)').closest('li') as HTMLElement);
    expect(reverso.getByText(/El dispositivo no pudo validarla/)).toBeInTheDocument();
    const rostro = within(screen.getByText('Rostro').closest('li') as HTMLElement);
    expect(rostro.queryByText('No verificada.')).not.toBeInTheDocument();
    const firma = within(screen.getByText('Firma').closest('li') as HTMLElement);
    expect(firma.queryByText('No verificada.')).not.toBeInTheDocument();
  });

  it('cada motivo del contrato tiene un texto claro y uno desconocido no rompe la pantalla', async () => {
    const dialog = await abrir(
      clienteCon({
        rostro: { estado: 'no_verificada', motivo: 'varios_rostros' },
        anverso: { estado: 'no_verificada', motivo: 'sin_documento' },
        reverso: { estado: 'no_verificada', motivo: 'motivo_nuevo' },
      }),
    );
    expect(within(dialog).getByText(/Se detectó más de una persona/)).toBeInTheDocument();
    expect(within(dialog).getByText(/No se detectó un documento/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Sin detalle del motivo/)).toBeInTheDocument();
  });

  it('AC2: con todas las fotos verificadas no hay ningún aviso', async () => {
    const dialog = await abrir(clienteCon({ rostro: OK, anverso: OK, reverso: OK }));
    expect(within(dialog).queryByText(/No verificada/)).not.toBeInTheDocument();
  });

  it.each([[null], [undefined]])('capturas anteriores (validacionFotos = %s): sin aviso', async (valor) => {
    const dialog = await abrir(clienteCon(valor));
    expect(within(dialog).queryByText(/No verificada/)).not.toBeInTheDocument();
  });

  it('AC3: con fotos no verificadas se puede aprobar y rechazar como en cualquier otro caso', async () => {
    const dialog = await abrir(
      clienteCon({ rostro: { estado: 'no_verificada', motivo: 'sin_rostro' }, anverso: OK, reverso: OK }),
    );
    expect(within(dialog).getByText(/No se detectó un rostro/)).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /aprobar/i })).toBeEnabled();
    expect(within(dialog).getByRole('button', { name: /rechazar/i })).toBeEnabled();
  });
});
