/**
 * Estado «Rechazada» visible (HU #13299/#13300/#13301, ajuste del PO): tras un rechazo la fila queda en `rechazado` con su motivo y
 * un enlace nuevo vigente. El listado la muestra y se puede filtrar; el detalle trae el motivo en español y el aviso del enlace
 * nuevo; Aprobar/Rechazar solo existen en `pendiente_revision_manual`. Más: errores reales del backend en las acciones.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ManualReviewDetailModal } from '@/components/atom/modules/ManualReviewDetailModal';
import { ValidacionesManuales } from '@/components/atom/modules/ValidacionesManuales';
import { createMockManualReviewClient } from '@/lib/api/manual-review-mock';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import { ApiError } from '@/lib/api/types';
import { MOTIVOS_RECHAZO_MANUAL, etiquetaMotivoRechazoManual } from '@/lib/identidad/motivos-rechazo-manual';

const NOW = new Date('2026-10-05T15:00:00Z');
const PENDIENTE = 'mock-manual-01';
const RECHAZADA = 'mock-manual-05';

beforeEach(() => {
  let n = 0;
  URL.createObjectURL = vi.fn(() => `blob:mock/${++n}`);
  URL.revokeObjectURL = vi.fn();
});

const cliente = () => createMockManualReviewClient({ delayMs: 0, now: NOW });

describe('Detalle de una validación rechazada', () => {
  it('muestra el estado Rechazada, el motivo en español y el aviso del enlace nuevo con DD/MM/YYYY HH:mm', async () => {
    render(<ManualReviewDetailModal id={RECHAZADA} client={cliente()} onClose={() => {}} />);
    const dialog = await screen.findByRole('dialog', { name: 'Detalle de validación manual' });

    expect(await within(dialog).findByRole('status', { name: 'Estado: Rechazada' })).toBeInTheDocument();
    const etiqueta = etiquetaMotivoRechazoManual(MOTIVOS_RECHAZO_MANUAL[4].code)!;
    expect(within(dialog).getByText(etiqueta)).toBeInTheDocument();
    // linkExpiresAt simulado = ahora + 20 h (05/10/2026 10:00 Bogotá + 20 h).
    expect(
      within(dialog).getByText('Se envió un enlace nuevo al cliente. Vence el 06/10/2026 06:00'),
    ).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument();
  });

  it('una rechazada sin vencimiento del enlace no muestra el aviso', async () => {
    const base = cliente();
    const client: ManualReviewClient = {
      ...base,
      getManualDetail: async (id, s) => ({ ...(await base.getManualDetail(id, s)), linkExpiresAt: null }),
    };
    render(<ManualReviewDetailModal id={RECHAZADA} client={client} onClose={() => {}} />);
    await screen.findByRole('status', { name: 'Estado: Rechazada' });
    expect(screen.queryByText(/Se envió un enlace nuevo/)).not.toBeInTheDocument();
  });

  it('el aviso solo aplica a «rechazado» (una pendiente no lo muestra)', async () => {
    render(<ManualReviewDetailModal id={PENDIENTE} client={cliente()} onClose={() => {}} />);
    await screen.findByRole('button', { name: 'Aprobar' });
    expect(screen.queryByText(/Se envió un enlace nuevo/)).not.toBeInTheDocument();
  });
});

describe('Listado y filtro con «Rechazada»', () => {
  it('el filtro de estado ofrece «Rechazada» y consulta con status=rechazado; las filas traen el chip danger', async () => {
    const user = userEvent.setup();
    const client = cliente();
    const spy = vi.spyOn(client, 'listManual');
    render(<ValidacionesManuales client={client} />);
    await screen.findByText('Persona de prueba 01');

    const select = screen.getByLabelText('Estado');
    expect(within(select).getByRole('option', { name: 'Rechazada' })).toBeInTheDocument();
    await user.selectOptions(select, 'rechazado');

    await waitFor(() => expect(spy.mock.calls.at(-1)![0]).toMatchObject({ status: 'rechazado', page: 1 }));
    const tabla = await screen.findByRole('table', { name: 'Validaciones manuales' });
    await waitFor(() => {
      const chips = within(tabla).getAllByRole('status', { name: 'Estado: Rechazada' });
      expect(chips.length).toBeGreaterThan(0);
      expect(chips[0].getAttribute('style') ?? '').toContain('--badge-danger-bg');
    });
  });
});

describe('Acciones: respuestas reales del backend', () => {
  async function rechazarCon(client: ManualReviewClient) {
    const user = userEvent.setup();
    render(<ManualReviewDetailModal id={PENDIENTE} client={client} onClose={() => {}} />);
    await user.click(await screen.findByRole('button', { name: 'Rechazar' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Rechazar validación' });
    await user.selectOptions(within(dialogo).getByLabelText('Motivo del rechazo'), 'imagen_borrosa');
    await user.click(within(dialogo).getByRole('button', { name: 'Rechazar' }));
    return dialogo;
  }

  it('emailEnviado=false: el rechazo queda y se avisa que el correo no salió', async () => {
    const base = cliente();
    const client: ManualReviewClient = {
      ...base,
      rejectManual: async (id, code) => ({ ...(await base.rejectManual(id, code)), emailEnviado: false }),
    };
    await rechazarCon(client);
    expect(await screen.findByText(/el correo al cliente no pudo enviarse/)).toBeInTheDocument();
    expect(await screen.findByRole('status', { name: 'Estado: Rechazada' })).toBeInTheDocument();
  });

  it('409 tramite_inactivo: mensaje propio dentro del diálogo, sin tratarlo como «ya no está pendiente»', async () => {
    const base = cliente();
    const client: ManualReviewClient = {
      ...base,
      rejectManual: () => Promise.reject(new ApiError(409, 'x', { code: 'tramite_inactivo' })),
    };
    const dialogo = await rechazarCon(client);
    expect(await within(dialogo).findByRole('alert')).toHaveTextContent('El trámite está anulado o revocado');
    expect(screen.getByText('Pendiente de revisión')).toBeInTheDocument();
  });

  it('409 estado_invalido: refresca el detalle (ya no está pendiente)', async () => {
    const base = cliente();
    const client: ManualReviewClient = {
      ...base,
      approveManual: () => Promise.reject(new ApiError(409, 'x', { code: 'estado_invalido' })),
    };
    const user = userEvent.setup();
    render(<ManualReviewDetailModal id={PENDIENTE} client={client} onClose={() => {}} />);
    await user.click(await screen.findByRole('button', { name: 'Aprobar' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Aprobar validación' });
    await user.click(within(dialogo).getByRole('button', { name: 'Aprobar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Aprobar validación' })).not.toBeInTheDocument());
  });
});
