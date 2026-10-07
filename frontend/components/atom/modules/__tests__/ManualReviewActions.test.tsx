/**
 * HU-C7 (#13302) — aprobar y rechazar desde el detalle: visibilidad por estado, confirmación,
 * motivo obligatorio y homologado, actualización sin recargar y errores 409/400.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ManualReviewDetailModal } from '@/components/atom/modules/ManualReviewDetailModal';
import { createMockManualReviewClient } from '@/lib/api/manual-review-mock';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import { ApiError } from '@/lib/api/types';
import { MOTIVOS_RECHAZO_MANUAL, etiquetaMotivoRechazoManual } from '@/lib/identidad/motivos-rechazo-manual';

const NOW = new Date('2026-10-05T15:00:00Z');
// Orden del simulado: 01 pendiente · 02 manual_activo · 04 aprobado · 05 rechazado · 06 vencido.
const PENDIENTE = 'mock-manual-01';

beforeEach(() => {
  let n = 0;
  URL.createObjectURL = vi.fn(() => `blob:mock/${++n}`);
  URL.revokeObjectURL = vi.fn();
});

const cliente = () => createMockManualReviewClient({ delayMs: 0, now: NOW });

function abrir(client: ManualReviewClient, id = PENDIENTE, onChanged = vi.fn()) {
  render(<ManualReviewDetailModal id={id} client={client} onClose={() => {}} onChanged={onChanged} />);
  return onChanged;
}

describe('Visibilidad de las acciones', () => {
  it('con el registro pendiente de revisión se ofrecen Aprobar y Rechazar', async () => {
    abrir(cliente());
    expect(await screen.findByRole('button', { name: 'Aprobar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Rechazar' })).toBeInTheDocument();
  });

  it.each([
    ['manual_activo', 'mock-manual-02'],
    ['aprobado', 'mock-manual-04'],
    ['rechazado', 'mock-manual-05'],
    ['expirado', 'mock-manual-06'],
  ])('con el registro en %s no aparecen', async (_estado, id) => {
    abrir(cliente(), id);
    await screen.findByText(/Persona de prueba/);
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument();
  });

  it('un registro rechazado muestra el motivo en español', async () => {
    abrir(cliente(), 'mock-manual-05');
    const etiqueta = etiquetaMotivoRechazoManual(MOTIVOS_RECHAZO_MANUAL[4].code)!;
    expect(await screen.findByText(etiqueta)).toBeInTheDocument();
  });
});

describe('Aprobar', () => {
  it('confirma «vigente 30 días», aprueba, actualiza el detalle sin recargar y avisa a la tabla', async () => {
    const user = userEvent.setup();
    const client = cliente();
    const spy = vi.spyOn(client, 'approveManual');
    const onChanged = abrir(client);

    await user.click(await screen.findByRole('button', { name: 'Aprobar' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Aprobar validación' });
    expect(within(dialogo).getByText(/quedará vigente 30 días/)).toBeInTheDocument();
    await user.click(within(dialogo).getByRole('button', { name: 'Aprobar' }));

    expect(await screen.findByText('Aprobada por 30 días.')).toBeInTheDocument();
    expect(spy).toHaveBeenCalledWith(PENDIENTE);
    await waitFor(() => expect(screen.getByText('Aprobada manual')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument();
    expect(onChanged).toHaveBeenCalled();
  });

  it('cancelar no llama al backend', async () => {
    const user = userEvent.setup();
    const client = cliente();
    const spy = vi.spyOn(client, 'approveManual');
    abrir(client);
    await user.click(await screen.findByRole('button', { name: 'Aprobar' }));
    await user.click(await screen.findByRole('button', { name: 'Cancelar' }));
    expect(spy).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog', { name: 'Aprobar validación' })).not.toBeInTheDocument();
  });

  it('Escape cierra solo la confirmación', async () => {
    const user = userEvent.setup();
    abrir(cliente());
    await user.click(await screen.findByRole('button', { name: 'Aprobar' }));
    await screen.findByRole('dialog', { name: 'Aprobar validación' });
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog', { name: 'Aprobar validación' })).not.toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Detalle de validación manual' })).toBeInTheDocument();
  });

  it('409: avisa que ya no está pendiente y refresca el detalle', async () => {
    const user = userEvent.setup();
    const base = cliente();
    const client: ManualReviewClient = {
      ...base,
      approveManual: async (id) => {
        await base.approveManual(id); // otra persona ya la resolvió
        throw new ApiError(409, 'estado_invalido', { code: 'estado_invalido' });
      },
    };
    abrir(client);
    await user.click(await screen.findByRole('button', { name: 'Aprobar' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Aprobar validación' });
    await user.click(within(dialogo).getByRole('button', { name: 'Aprobar' }));
    await waitFor(() => expect(screen.getByText('Aprobada manual')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument();
  });

  it('error genérico: mensaje claro, el registro no cambia y se puede reintentar', async () => {
    const user = userEvent.setup();
    const base = cliente();
    const client: ManualReviewClient = { ...base, approveManual: () => Promise.reject(new ApiError(500, 'x')) };
    abrir(client);
    await user.click(await screen.findByRole('button', { name: 'Aprobar' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Aprobar validación' });
    await user.click(within(dialogo).getByRole('button', { name: 'Aprobar' }));
    expect(await within(dialogo).findByRole('alert')).toHaveTextContent('No se pudo completar la acción. Inténtalo de nuevo.');
    expect(screen.getByText('Pendiente de revisión')).toBeInTheDocument();
    expect(within(dialogo).getByRole('button', { name: 'Aprobar' })).toBeEnabled();
  });
});

describe('Rechazar', () => {
  it('sin motivo el botón Rechazar está deshabilitado y ofrece los 6 motivos homologados', async () => {
    const user = userEvent.setup();
    abrir(cliente());
    await user.click(await screen.findByRole('button', { name: 'Rechazar' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Rechazar validación' });
    expect(within(dialogo).getByRole('button', { name: 'Rechazar' })).toBeDisabled();
    expect(within(dialogo).getByText('El cliente recibirá este motivo por correo y podrá repetir la captura.')).toBeInTheDocument();
    const select = within(dialogo).getByLabelText('Motivo del rechazo');
    const opciones = within(select).getAllByRole('option').map((o) => o.textContent);
    expect(opciones).toEqual(['Elige un motivo', ...MOTIVOS_RECHAZO_MANUAL.map((m) => m.label)]);
    expect(MOTIVOS_RECHAZO_MANUAL).toHaveLength(6);
  });

  it('con motivo rechaza con su código, informa del correo y actualiza el detalle', async () => {
    const user = userEvent.setup();
    const client = cliente();
    const spy = vi.spyOn(client, 'rejectManual');
    const onChanged = abrir(client);
    await user.click(await screen.findByRole('button', { name: 'Rechazar' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Rechazar validación' });
    await user.selectOptions(within(dialogo).getByLabelText('Motivo del rechazo'), 'imagen_borrosa');
    await user.click(within(dialogo).getByRole('button', { name: 'Rechazar' }));

    expect(spy).toHaveBeenCalledWith(PENDIENTE, 'imagen_borrosa');
    expect(
      await screen.findByText('Rechazada. El cliente recibirá el motivo por correo con un enlace nuevo.'),
    ).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText('Motivo del rechazo')).toBeInTheDocument());
    expect(screen.getByText('Imagen borrosa')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument();
    expect(onChanged).toHaveBeenCalled();
  });

  it('400 motivo_invalido: mensaje claro y el registro no cambia', async () => {
    const user = userEvent.setup();
    const base = cliente();
    const client: ManualReviewClient = {
      ...base,
      rejectManual: () => Promise.reject(new ApiError(400, 'x', { code: 'motivo_invalido' })),
    };
    abrir(client);
    await user.click(await screen.findByRole('button', { name: 'Rechazar' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Rechazar validación' });
    await user.selectOptions(within(dialogo).getByLabelText('Motivo del rechazo'), 'imagen_borrosa');
    await user.click(within(dialogo).getByRole('button', { name: 'Rechazar' }));
    expect(await within(dialogo).findByRole('alert')).toHaveTextContent('El motivo elegido no es válido');
    expect(screen.getByText('Pendiente de revisión')).toBeInTheDocument();
  });
});
