import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BlindajeDeclaracionCard } from '../BlindajeDeclaracionCard';
import type { ProcedureAttachment } from '@/lib/api/types/procedure-runtime';

const getInstance = vi.fn();
const getAttachments = vi.fn();
const patchFieldValues = vi.fn();
const uploadAttachment = vi.fn();
const deleteAttachment = vi.fn();

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: (...a: unknown[]) => getInstance(...a),
    getAttachments: (...a: unknown[]) => getAttachments(...a),
    patchFieldValues: (...a: unknown[]) => patchFieldValues(...a),
    uploadAttachment: (...a: unknown[]) => uploadAttachment(...a),
    deleteAttachment: (...a: unknown[]) => deleteAttachment(...a),
  },
}));

const INSTANCE = '11111111-1111-1111-1111-111111111111';

function certificado(): ProcedureAttachment {
  return {
    id: 'att-1',
    tipo: 'certificado_blindaje',
    filename: 'certificado.pdf',
  } as ProcedureAttachment;
}

beforeEach(() => {
  vi.clearAllMocks();
  getInstance.mockResolvedValue({ fieldValues: [] });
  getAttachments.mockResolvedValue([]);
  patchFieldValues.mockResolvedValue(undefined);
  uploadAttachment.mockResolvedValue(undefined);
  deleteAttachment.mockResolvedValue(undefined);
});

/**
 * Tarjeta de blindaje: qué declara el trámite (nivel 1/2/3 o desmonte) y el certificado que lo
 * acredita. Antes era un párrafo informativo que afirmaba `blindaje = true` por su cuenta, así que
 * las cuatro opciones producían el mismo FUR.
 */
describe('BlindajeDeclaracionCard', () => {
  it('ofrece las cuatro opciones del trámite', async () => {
    render(<BlindajeDeclaracionCard instanceId={INSTANCE} readOnly={false} />);

    const select = await screen.findByLabelText(/Opción del trámite/);
    expect(
      [...select.querySelectorAll('option')].map((o) => o.textContent),
    ).toEqual([
      'Selecciona una opción…',
      'Blindaje nivel 1',
      'Blindaje nivel 2',
      'Blindaje nivel 3',
      'Desmontar blindaje',
    ]);
  });

  it('un nivel persiste la opción y deja la bandera en true', async () => {
    const user = userEvent.setup();
    render(<BlindajeDeclaracionCard instanceId={INSTANCE} readOnly={false} />);

    await user.selectOptions(
      await screen.findByLabelText(/Opción del trámite/),
      'Blindaje nivel 2',
    );

    await waitFor(() => expect(patchFieldValues).toHaveBeenCalled());
    expect(patchFieldValues).toHaveBeenCalledWith(INSTANCE, [
      expect.objectContaining({ fieldKey: 'blindaje_nivel', valueText: 'NIVEL_2' }),
      expect.objectContaining({ fieldKey: 'blindaje', valueText: 'true' }),
    ]);
  });

  it('el desmonte deja la bandera en false: el vehículo queda SIN blindaje', async () => {
    const user = userEvent.setup();
    render(<BlindajeDeclaracionCard instanceId={INSTANCE} readOnly={false} />);

    await user.selectOptions(
      await screen.findByLabelText(/Opción del trámite/),
      'Desmontar blindaje',
    );

    await waitFor(() => expect(patchFieldValues).toHaveBeenCalled());
    expect(patchFieldValues).toHaveBeenCalledWith(INSTANCE, [
      expect.objectContaining({ fieldKey: 'blindaje_nivel', valueText: 'DESMONTE' }),
      expect.objectContaining({ fieldKey: 'blindaje', valueText: 'false' }),
    ]);
  });

  it('previsualiza lo que el FUR imprimirá, con la casilla que marca', async () => {
    getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'blindaje_nivel', valueText: 'DESMONTE' }],
    });
    render(<BlindajeDeclaracionCard instanceId={INSTANCE} readOnly={false} />);

    expect(
      await screen.findByText(/DESMONTE DE BLINDAJE\. · BLINDADO: NO/),
    ).toBeInTheDocument();
  });

  // ── Gate de Continuar ────────────────────────────────────────────────────

  it('sin opción y sin certificado el paso no está completo', async () => {
    const onCompletenessChange = vi.fn();
    render(
      <BlindajeDeclaracionCard
        instanceId={INSTANCE}
        readOnly={false}
        onCompletenessChange={onCompletenessChange}
      />,
    );

    await waitFor(() => expect(onCompletenessChange).toHaveBeenCalledWith(false));
    expect(onCompletenessChange).not.toHaveBeenCalledWith(true);
  });

  it('con opción pero sin certificado sigue incompleto', async () => {
    getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'blindaje_nivel', valueText: 'NIVEL_1' }],
    });
    const onCompletenessChange = vi.fn();
    render(
      <BlindajeDeclaracionCard
        instanceId={INSTANCE}
        readOnly={false}
        onCompletenessChange={onCompletenessChange}
      />,
    );

    await screen.findByText(/Adjunta el certificado obligatorio/);
    expect(onCompletenessChange).not.toHaveBeenCalledWith(true);
  });

  it('el certificado es obligatorio también en el desmonte', async () => {
    // Retirar un blindaje también hay que acreditarlo: es la razón de que el gate no dependa de la
    // opción escogida.
    getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'blindaje_nivel', valueText: 'DESMONTE' }],
    });
    getAttachments.mockResolvedValue([]);
    const onCompletenessChange = vi.fn();
    render(
      <BlindajeDeclaracionCard
        instanceId={INSTANCE}
        readOnly={false}
        onCompletenessChange={onCompletenessChange}
      />,
    );

    await screen.findByText(/Adjunta el certificado obligatorio/);
    expect(onCompletenessChange).not.toHaveBeenCalledWith(true);
  });

  it('con opción y certificado el paso está completo', async () => {
    getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'blindaje_nivel', valueText: 'NIVEL_3' }],
    });
    getAttachments.mockResolvedValue([certificado()]);
    const onCompletenessChange = vi.fn();
    render(
      <BlindajeDeclaracionCard
        instanceId={INSTANCE}
        readOnly={false}
        onCompletenessChange={onCompletenessChange}
      />,
    );

    await waitFor(() => expect(onCompletenessChange).toHaveBeenCalledWith(true));
    expect(await screen.findByText('Validado')).toBeInTheDocument();
  });

  // ── Adjunto ──────────────────────────────────────────────────────────────

  it('sube el certificado con su DocTipo propio, no como «otro»', async () => {
    const user = userEvent.setup();
    const { container } = render(
      <BlindajeDeclaracionCard instanceId={INSTANCE} readOnly={false} />,
    );

    await screen.findByLabelText(/Opción del trámite/);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(input, new File(['x'], 'cert.pdf', { type: 'application/pdf' }));

    await waitFor(() => expect(uploadAttachment).toHaveBeenCalled());
    expect(uploadAttachment).toHaveBeenCalledWith(
      INSTANCE,
      'certificado_blindaje',
      expect.any(File),
    );
  });

  it('en solo lectura no ofrece adjuntar ni cambiar la opción', async () => {
    getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'blindaje_nivel', valueText: 'NIVEL_1' }],
    });
    render(<BlindajeDeclaracionCard instanceId={INSTANCE} readOnly />);

    expect(await screen.findByLabelText(/Opción del trámite/)).toBeDisabled();
    expect(screen.queryByRole('button', { name: /Adjuntar archivo/ })).not.toBeInTheDocument();
  });
});
