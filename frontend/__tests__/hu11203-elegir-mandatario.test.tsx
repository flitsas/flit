import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

/**
 * HU #11203 — el gestor elige quién firma el mandato al registrar el trámite.
 *
 * Antes el firmante se resolvía al aprobar: el gestor no sabía quién iba a firmar y, con varios
 * mandatarios, era el organismo el que tenía que decidirlo con el trámite ya radicado.
 */
const mocks = vi.hoisted(() => ({
  listMandateSigners: vi.fn(),
  setMandateSigner: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
}));

import { MandatarioSection } from '@/components/operacion/MandatarioSection';

const ANA = {
  id: 'ms-ana',
  nombre: 'Ana Restrepo',
  tipoDocumento: 'CC',
  documento: '1020304050',
  identidadVigente: true,
  identidadHasta: '2026-12-31',
};

const CARLOS = {
  id: 'ms-carlos',
  nombre: 'Carlos Pérez',
  tipoDocumento: 'CC',
  documento: '9080706050',
  identidadVigente: false,
  identidadHasta: null,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.setMandateSigner.mockResolvedValue(undefined);
});

function renderSection() {
  return render(<MandatarioSection instanceId="inst-1" />);
}

describe('HU #11203 — elegir el mandatario que firma', () => {
  it('AC1: se muestran los mandatarios habilitados para el organismo del trámite', async () => {
    mocks.listMandateSigners.mockResolvedValue({
      opciones: [ANA, CARLOS],
      elegidoId: null,
      editable: true,
    });
    renderSection();

    expect(await screen.findByText('Ana Restrepo')).toBeInTheDocument();
    expect(screen.getByText('Carlos Pérez')).toBeInTheDocument();
    expect(screen.getByText(/Opcional/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Mandatario/i })).toBeInTheDocument();
    expect(mocks.listMandateSigners).toHaveBeenCalledWith('inst-1', undefined);
  });

  it('AC2: se ven nombres, documento y vigencia de la validación de identidad', async () => {
    mocks.listMandateSigners.mockResolvedValue({
      opciones: [ANA, CARLOS],
      elegidoId: null,
      editable: true,
    });
    renderSection();

    expect(await screen.findByText('CC 1020304050')).toBeInTheDocument();
    expect(screen.getByText(/Identidad vigente hasta el 2026\/12\/31/)).toBeInTheDocument();
    // Sin ninguna de las dos vías el mandato no puede firmarse hoy: se avisa al elegir, no al final,
    // y se apunta a la salida (firmar más adelante) en vez de dejar al gestor bloqueado.
    expect(screen.getByText(/Sin firma del baúl ni identidad vigentes/)).toBeInTheDocument();
  });

  it('un mandatario con firma del baúl vigente NO se anuncia como si le faltara algo', async () => {
    // Son dos vías alternativas. El gate de aprobación bloqueaba —y la UI avisaba— mirando solo la
    // identidad, así que quien podía firmar con su firma del baúl aparecía como incompleto.
    mocks.listMandateSigners.mockResolvedValue({
      opciones: [{ ...CARLOS, firmaBaulVigente: true }],
      elegidoId: null,
      editable: true,
    });
    renderSection();

    expect(await screen.findByText(/Firma del baúl vigente/)).toBeInTheDocument();
    expect(screen.queryByText(/Sin firma del baúl ni identidad vigentes/)).not.toBeInTheDocument();
  });

  it('AC3: con un único mandatario habilitado queda seleccionado por defecto', async () => {
    mocks.listMandateSigners.mockResolvedValue({
      opciones: [ANA],
      elegidoId: ANA.id,
      editable: true,
    });
    renderSection();

    const radios = await screen.findAllByRole('radio');
    expect(radios).toHaveLength(1);
    expect(radios[0]).toBeChecked();
  });

  it('AC4: en borrador se cambia el mandatario y queda guardado', async () => {
    const user = userEvent.setup();
    mocks.listMandateSigners.mockResolvedValue({
      opciones: [ANA, CARLOS],
      elegidoId: ANA.id,
      editable: true,
    });
    renderSection();

    await screen.findByText('Carlos Pérez');
    const radios = screen.getAllByRole('radio');
    await user.click(radios[1]);

    await waitFor(() =>
      expect(mocks.setMandateSigner).toHaveBeenCalledWith('inst-1', CARLOS.id, undefined),
    );
    // Se recarga para reflejar lo que quedó guardado, no lo que se supone que quedó.
    expect(mocks.listMandateSigners).toHaveBeenCalledTimes(2);
  });

  it('AC5: fuera de borrador se muestra pero no se puede cambiar', async () => {
    const user = userEvent.setup();
    mocks.listMandateSigners.mockResolvedValue({
      opciones: [ANA, CARLOS],
      elegidoId: ANA.id,
      editable: false,
    });
    renderSection();

    expect(await screen.findByText(/ya salió de borrador/)).toBeInTheDocument();
    const radios = screen.getAllByRole('radio');
    radios.forEach((r) => expect(r).toBeDisabled());

    await user.click(radios[1]);
    expect(mocks.setMandateSigner).not.toHaveBeenCalled();
  });

  it('sin mandatarios no se pinta una sección vacía', async () => {
    mocks.listMandateSigners.mockResolvedValue({ opciones: [], elegidoId: null, editable: true });
    const { container } = renderSection();

    await waitFor(() => expect(mocks.listMandateSigners).toHaveBeenCalled());
    // El mandato lo firma el mandatario institucional del organismo, si lo tiene: no hay nada que elegir.
    expect(container).toBeEmptyDOMElement();
  });
});
