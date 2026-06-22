import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getActors: mocks.getActors,
    saveActors: mocks.saveActors,
  },
}));

import { ActorsForm, validateActors } from '@/components/operacion/ActorsForm';
import type { ProcedureActor } from '@/lib/api/types/procedure-runtime';

const INSTANCE = 'inst-1';

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
});

describe('ActorsForm — render por modalidad', () => {
  it('matrícula inicial muestra solo el comprador', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    expect(
      await screen.findByRole('group', { name: 'Comprador' }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: 'Vendedor' })).toBeNull();
  });

  it('traspaso muestra vendedor y comprador', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);
    expect(
      await screen.findByRole('group', { name: 'Vendedor' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Comprador' })).toBeInTheDocument();
  });
});

describe('ActorsForm — validación cliente', () => {
  it('bloquea submit y marca aria-invalid en requeridos vacíos', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.click(
      await screen.findByRole('button', { name: /Guardar actores/ }),
    );

    expect(mocks.saveActors).not.toHaveBeenCalled();
    const numero = screen.getByLabelText(/Número de documento/);
    expect(numero).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getAllByText('Número requerido').length).toBeGreaterThan(0);
  });

  it('marca email inválido', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.type(screen.getByLabelText(/Número de documento/), '123');
    await user.type(screen.getByLabelText(/Nombre completo/), 'Juan Perez');
    await user.type(screen.getByLabelText(/Correo electrónico/), 'no-es-email');
    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(mocks.saveActors).not.toHaveBeenCalled();
    const email = screen.getByLabelText(/Correo electrónico/);
    expect(email).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('Correo no válido')).toBeInTheDocument();
  });

  it('regla vendedor≠comprador: rechaza documento idéntico', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);

    await screen.findByRole('group', { name: 'Vendedor' });
    const numeros = screen.getAllByLabelText(/Número de documento/);
    const nombres = screen.getAllByLabelText(/Nombre completo/);
    const emails = screen.getAllByLabelText(/Correo electrónico/);

    // vendedor (índice 0)
    await user.type(numeros[0], '999');
    await user.type(nombres[0], 'Ana Vendedora');
    await user.type(emails[0], 'ana@example.com');
    // comprador (índice 1) — mismo documento, distinto email
    await user.type(numeros[1], '999');
    await user.type(nombres[1], 'Beto Comprador');
    await user.type(emails[1], 'beto@example.com');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(mocks.saveActors).not.toHaveBeenCalled();
    expect(
      screen.getByText(/no pueden ser la misma persona/),
    ).toBeInTheDocument();
  });
});

describe('ActorsForm — submit', () => {
  it('llama saveActors con los actores válidos (teléfono opcional omitido)', async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="matricula_inicial"
        onSaved={onSaved}
      />,
    );

    await user.type(
      await screen.findByLabelText(/Número de documento/),
      '12345',
    );
    await user.type(screen.getByLabelText(/Nombre completo/), 'Juan Perez');
    await user.type(
      screen.getByLabelText(/Correo electrónico/),
      'juan@example.com',
    );
    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [instanceId, actors] = mocks.saveActors.mock.calls[0];
    expect(instanceId).toBe(INSTANCE);
    expect(actors).toEqual([
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '12345',
        nombreCompleto: 'Juan Perez',
        email: 'juan@example.com',
        telefono: undefined,
      },
    ]);
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(/Actores guardados/)).toBeInTheDocument();
  });
});

describe('validateActors — unidad', () => {
  const base: ProcedureActor = {
    rol: 'comprador',
    tipoDocumento: 'CC',
    numeroDocumento: '1',
    nombreCompleto: 'X',
    email: 'x@y.com',
    telefono: undefined,
  };

  it('acepta un comprador válido en matrícula', () => {
    expect(validateActors([base], 'matricula_inicial').valid).toBe(true);
  });

  it('detecta email coincidente vendedor/comprador en traspaso', () => {
    const v = validateActors(
      [
        { ...base, rol: 'vendedor', numeroDocumento: '2' },
        { ...base, rol: 'comprador', numeroDocumento: '3' },
      ],
      'traspaso',
    );
    expect(v.valid).toBe(false);
  });
});
