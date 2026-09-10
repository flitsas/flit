import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CancelacionCausalCard } from '../CancelacionCausalCard';

const getInstance = vi.fn();
const patchFieldValues = vi.fn();

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: (...a: unknown[]) => getInstance(...a),
    patchFieldValues: (...a: unknown[]) => patchFieldValues(...a),
  },
}));

const INSTANCE = '11111111-1111-1111-1111-111111111111';

beforeEach(() => {
  vi.clearAllMocks();
  getInstance.mockResolvedValue({ fieldValues: [] });
  patchFieldValues.mockResolvedValue(undefined);
});

/**
 * Tarjeta de la causal de cancelación: por qué se cancela la matrícula. La casilla 13 del FUR es una
 * sola para cuatro trámites que el organismo tramita distinto, y cada causal se acredita con
 * documentos diferentes; antes no se preguntaba y el checklist pedía lo mismo para las cuatro.
 */
describe('CancelacionCausalCard', () => {
  it('ofrece las cuatro causales', async () => {
    render(<CancelacionCausalCard instanceId={INSTANCE} readOnly={false} />);

    const select = await screen.findByLabelText(/Motivo de la cancelación/);
    expect([...select.querySelectorAll('option')].map((o) => o.textContent)).toEqual([
      'Selecciona una causal…',
      'Decisión judicial',
      'Pérdida total por fuerza mayor',
      'Pérdida total por accidente',
      'Decisión voluntaria',
    ]);
  });

  it('persiste la causal elegida en field_values', async () => {
    render(<CancelacionCausalCard instanceId={INSTANCE} readOnly={false} />);

    await userEvent.selectOptions(
      await screen.findByLabelText(/Motivo de la cancelación/),
      'PERDIDA_TOTAL_ACCIDENTE',
    );

    await waitFor(() =>
      expect(patchFieldValues).toHaveBeenCalledWith(INSTANCE, [
        {
          formFieldId: null,
          fieldKey: 'cancelacion_causal',
          valueText: 'PERDIDA_TOTAL_ACCIDENTE',
          valueJson: null,
        },
      ]),
    );
  });

  it('lista los documentos obligatorios de la causal elegida', async () => {
    render(<CancelacionCausalCard instanceId={INSTANCE} readOnly={false} />);

    await userEvent.selectOptions(
      await screen.findByLabelText(/Motivo de la cancelación/),
      'PERDIDA_TOTAL_FUERZA_MAYOR',
    );

    // Los tres, no uno cualquiera de ellos.
    expect(await screen.findByText(/Certificado DIJIN o Policía/)).toBeInTheDocument();
    expect(screen.getByText(/Certificado de aseguradora o perito/)).toBeInTheDocument();
    expect(screen.getByText(/Certificado de autoridad administrativa/)).toBeInTheDocument();
  });

  it('avisa del cambio de causal para que el checklist se recargue, pero no al hidratar', async () => {
    const onCausalChange = vi.fn();
    getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'cancelacion_causal', valueText: 'DECISION_JUDICIAL' }],
    });

    render(
      <CancelacionCausalCard
        instanceId={INSTANCE}
        readOnly={false}
        onCausalChange={onCausalChange}
      />,
    );

    // El checklist que llega del servidor ya viene resuelto con la causal guardada.
    await waitFor(() =>
      expect(screen.getByLabelText(/Motivo de la cancelación/)).toHaveValue('DECISION_JUDICIAL'),
    );
    expect(onCausalChange).not.toHaveBeenCalled();

    await userEvent.selectOptions(
      screen.getByLabelText(/Motivo de la cancelación/),
      'DECISION_VOLUNTARIA',
    );
    await waitFor(() => expect(onCausalChange).toHaveBeenCalledWith('DECISION_VOLUNTARIA'));
  });

  it('el gate del paso se abre solo con causal declarada', async () => {
    const onCompletenessChange = vi.fn();
    render(
      <CancelacionCausalCard
        instanceId={INSTANCE}
        readOnly={false}
        onCompletenessChange={onCompletenessChange}
      />,
    );

    await waitFor(() => expect(onCompletenessChange).toHaveBeenCalledWith(false));

    await userEvent.selectOptions(
      await screen.findByLabelText(/Motivo de la cancelación/),
      'DECISION_JUDICIAL',
    );

    await waitFor(() => expect(onCompletenessChange).toHaveBeenLastCalledWith(true));
  });

  it('en solo lectura no deja cambiar la causal', async () => {
    render(<CancelacionCausalCard instanceId={INSTANCE} readOnly />);

    expect(await screen.findByLabelText(/Motivo de la cancelación/)).toBeDisabled();
  });
});
