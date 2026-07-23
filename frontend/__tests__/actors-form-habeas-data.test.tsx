import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// HU #10885 (Feature #10862, CF-04, Habeas Data) — checkbox de autorización de reúso de datos de
// persona (comprador/vendedor): marcado → PUT actores con autorizaReutilizacionDatos=true; sin
// marcar → el campo NO viaja (el backend nunca reutiliza datos de persona sin consentimiento
// explícito, fail-safe). Además, badge de origen cuando el lookup de identidad viene de caché
// (`mode: 'cache'`, AC1).
const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  getInstance: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getActors: mocks.getActors,
    saveActors: mocks.saveActors,
    runtPersonLookup: mocks.runtPersonLookup,
    ruesPersonLookup: mocks.ruesPersonLookup,
    getInstance: mocks.getInstance,
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';

const INSTANCE = 'inst-1';

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
});

async function fillRequiredComprador(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Número de documento'), '12345');
  await user.type(screen.getByLabelText(/Nombre completo/), 'Juan Perez');
  await user.type(screen.getByLabelText(/Correo electrónico/), 'juan@example.com');
}

describe('ActorsForm — consentimiento Habeas Data (HU #10885)', () => {
  it('muestra el checkbox con label asociado y copy claro', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    const checkbox = await screen.findByRole('checkbox', {
      name: /Autorizo la reutilización de estos datos en futuros trámites/i,
    });
    expect(checkbox).not.toBeChecked();
    expect(
      screen.getByText(/no se precargarán automáticamente en otros trámites/i),
    ).toBeInTheDocument();
  });

  it('marcar el checkbox y guardar envía autorizaReutilizacionDatos=true', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await fillRequiredComprador(user);
    const checkbox = screen.getByRole('checkbox', {
      name: /Autorizo la reutilización de estos datos en futuros trámites/i,
    });
    await user.click(checkbox);
    expect(checkbox).toBeChecked();

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [, actors] = mocks.saveActors.mock.calls[0];
    expect(actors[0].autorizaReutilizacionDatos).toBe(true);
  });

  it('sin marcar el checkbox, el campo no viaja en el guardado (fail-safe)', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await fillRequiredComprador(user);
    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [, actors] = mocks.saveActors.mock.calls[0];
    expect(actors[0].autorizaReutilizacionDatos).toBeUndefined();
  });

  it('traspaso: cada actor (vendedor y comprador) tiene su propio checkbox', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('group', { name: 'Vendedor' });
    const checkboxes = screen.getAllByRole('checkbox', {
      name: /Autorizo la reutilización de estos datos en futuros trámites/i,
    });
    expect(checkboxes).toHaveLength(2);
  });
});

describe('ActorsForm — AC1 origen del dato reutilizado (persona, HU #10885)', () => {
  it('mode="cache" en el lookup RUNT muestra el badge "Dato reutilizado"', async () => {
    const user = userEvent.setup();
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'JUAN CARLOS PEREZ GOMEZ',
      firstName: 'JUAN CARLOS',
      lastName: 'PEREZ GOMEZ',
      documentType: 'CC',
      documentNumber: '3216549870',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'cache',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '3216549870');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();
    expect(screen.getByText(/Dato reutilizado · RUNT/)).toBeInTheDocument();
  });

  it('mode="mock"/"real" (consulta fresca) NO muestra el badge de reúso', async () => {
    const user = userEvent.setup();
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'ANA GARCIA',
      firstName: 'ANA',
      lastName: 'GARCIA',
      documentType: 'CC',
      documentNumber: '9999999',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'mock',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '9999999');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();
    expect(screen.queryByText(/Dato reutilizado/)).not.toBeInTheDocument();
  });
});
