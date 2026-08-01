// HU #10886 (AC1) — al editar el correo de un actor con una validación de identidad vigente, la UI
// debe advertir que se reenviará el correo de validación y se invalidará el enlace anterior, y pedir
// confirmación explícita ANTES de persistir (PUT /instances/{id}/actors). Sin confirmación, no hay PUT.
// Correo sin cambios (o sin validación vigente) → sin modal, guarda directo.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  getInstance: vi.fn(),
  getBiometricState: vi.fn(),
  runtPersonLookup: vi.fn(),
  actorContactLookup: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getActors: mocks.getActors,
    saveActors: mocks.saveActors,
    getInstance: mocks.getInstance,
    getBiometricState: mocks.getBiometricState,
    runtPersonLookup: mocks.runtPersonLookup,
    actorContactLookup: mocks.actorContactLookup,
    lookupLegalRepresentativeByNit: mocks.lookupLegalRepresentativeByNit,
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';

const INSTANCE = 'inst-1';

const PERSISTED_COMPRADOR = {
  rol: 'comprador' as const,
  tipoDocumento: 'CC' as const,
  numeroDocumento: '123',
  nombreCompleto: 'Juan Perez',
  email: 'juan@old.com',
  personType: 'natural' as const,
};

function activeValidation(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'v1',
    partyRole: 'comprador',
    name: 'Juan Perez',
    documentType: 'CC',
    documentNumber: '123',
    email: 'juan@old.com',
    status: 'en_proceso',
    intentos: 0,
    maxIntentos: 3,
    score: null,
    expiresAt: '2026-08-01T00:00:00Z',
    validatedAt: null,
    expired: false,
    provider: 'kyverum',
    captureUrl: 'https://kyverum.example/capture/abc',
    ...overrides,
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'kyverum' });
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.actorContactLookup.mockResolvedValue({ found: false });
  mocks.runtPersonLookup.mockResolvedValue({
    found: true,
    fullName: 'Juan Perez',
    documentType: 'CC',
    documentNumber: '123',
    source: 'RUNT',
    mode: 'mock',
  });
});

/** Consulta RUNT del comprador hidratado (gate de guardado). */
async function consultPersistedComprador(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Consultar RUNT' }));
  await screen.findByText(/Persona encontrada en RUNT/i);
}

async function changeEmail(newEmail: string) {
  const user = userEvent.setup();
  await consultPersistedComprador(user);
  const email = await screen.findByLabelText(/Correo electrónico/);
  await user.clear(email);
  await user.type(email, newEmail);
  await user.click(screen.getByRole('button', { name: /Guardar actores/ }));
  return user;
}

describe('ActorsForm — aviso de reenvío al editar el correo (HU #10886 AC1)', () => {
  it('actor con validación en curso: cambiar el correo muestra el modal de confirmación y NO persiste sin confirmar', async () => {
    mocks.getActors.mockResolvedValue([PERSISTED_COMPRADOR]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [activeValidation()],
      provider: 'kyverum',
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByDisplayValue('Juan Perez');

    await changeEmail('juan@new.com');

    const dialog = await screen.findByRole('dialog', { name: 'Reenviar validación de identidad' });
    expect(within(dialog).getByText(/juan@new.com/)).toBeInTheDocument();
    expect(within(dialog).getByText(/invalidará el enlace anterior/)).toBeInTheDocument();
    expect(mocks.saveActors).not.toHaveBeenCalled();
  });

  it('cancelar el aviso NO envía el PUT de actores', async () => {
    mocks.getActors.mockResolvedValue([PERSISTED_COMPRADOR]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [activeValidation()],
      provider: 'kyverum',
    });

    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByDisplayValue('Juan Perez');
    await changeEmail('juan@new.com');

    const dialog = await screen.findByRole('dialog', { name: 'Reenviar validación de identidad' });
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Reenviar validación de identidad' })).not.toBeInTheDocument(),
    );
    expect(mocks.saveActors).not.toHaveBeenCalled();
  });

  it('confirmar el aviso persiste los actores con el correo nuevo', async () => {
    mocks.getActors.mockResolvedValue([PERSISTED_COMPRADOR]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [activeValidation()],
      provider: 'kyverum',
    });

    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByDisplayValue('Juan Perez');
    await changeEmail('juan@new.com');

    const dialog = await screen.findByRole('dialog', { name: 'Reenviar validación de identidad' });
    await user.click(within(dialog).getByRole('button', { name: 'Continuar' }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [instanceId, actors] = mocks.saveActors.mock.calls[0];
    expect(instanceId).toBe(INSTANCE);
    expect(actors[0]).toMatchObject({ email: 'juan@new.com' });
    expect(await screen.findByText(/Actores guardados/)).toBeInTheDocument();
  });

  it('correo sin cambios: no muestra el modal y guarda directo', async () => {
    mocks.getActors.mockResolvedValue([PERSISTED_COMPRADOR]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [activeValidation()],
      provider: 'kyverum',
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByDisplayValue('Juan Perez');

    const user = userEvent.setup();
    await consultPersistedComprador(user);
    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    // Correo sin cambios: no hace falta consultar el estado biométrico.
    expect(mocks.getBiometricState).not.toHaveBeenCalled();
  });

  it('correo cambiado SIN validación vigente (rechazada/expirada): no muestra el modal', async () => {
    mocks.getActors.mockResolvedValue([PERSISTED_COMPRADOR]);
    mocks.getBiometricState.mockResolvedValue({
      validations: [activeValidation({ status: 'rechazado' })],
      provider: 'kyverum',
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByDisplayValue('Juan Perez');
    await changeEmail('juan@new.com');

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
