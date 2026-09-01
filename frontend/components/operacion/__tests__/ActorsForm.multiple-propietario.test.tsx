// Múltiple Propietario (ADR-0053) — UI de pestañas + reparto porcentual en `ActorsForm.tsx`.
// La lógica pura (solidario, redistribución, reindexado de mapas posicionales) ya está cubierta
// exhaustivamente en `frontend/lib/tramites/__tests__/ownership-share.test.ts`, sin RTL. Este
// archivo verifica que `ActorsForm.tsx` la conecta correctamente al DOM: el caso de un solo actor
// no sufre regresión, las pestañas/porcentaje aparecen y se comportan como pide el encargo, los dos
// mensajes de bloqueo son exactos, el máximo de 4 se respeta, y el estado de identidad por actor
// (consulta RUNT) no se mezcla al agregar/quitar copropietarios.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  actorContactLookup: vi.fn(),
  getBiometricState: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getActors: mocks.getActors,
    saveActors: mocks.saveActors,
    runtPersonLookup: mocks.runtPersonLookup,
    ruesPersonLookup: mocks.ruesPersonLookup,
    getInstance: mocks.getInstance,
    patchFieldValues: mocks.patchFieldValues,
    lookupLegalRepresentativeByNit: mocks.lookupLegalRepresentativeByNit,
    actorContactLookup: mocks.actorContactLookup,
    getBiometricState: mocks.getBiometricState,
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';

const INSTANCE = 'inst-mp-1';

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.patchFieldValues.mockResolvedValue(undefined);
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.actorContactLookup.mockResolvedValue({ found: false });
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.runtPersonLookup.mockResolvedValue({
    found: true,
    fullName: 'Persona Encontrada',
    firstName: 'Persona',
    lastName: 'Encontrada',
    documentType: 'CC',
    documentNumber: '111',
    source: 'RUNT',
    mode: 'mock',
  });
});

function addButton(sideLabel: string) {
  return screen.getByRole('button', { name: `Agregar copropietario de ${sideLabel}` });
}

describe('ActorsForm — Múltiple Propietario, un solo actor (regresión, caso mayoritario)', () => {
  it('matrícula inicial con un comprador: sin pestañas, sin bloque de porcentaje', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText(/Número de documento/);

    expect(screen.queryByRole('tablist')).toBeNull();
    expect(screen.queryByText(/Porcentaje de propiedad/)).toBeNull();
    // El disparador para agregar SÍ está disponible (punto de entrada — decisión de UI, ver handoff).
    expect(addButton('comprador')).toBeInTheDocument();
  });

  it('traspaso con un vendedor y un comprador: ningún lado muestra pestañas', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('group', { name: 'Vendedor' });

    expect(screen.queryByRole('tablist')).toBeNull();
    expect(addButton('vendedor')).toBeInTheDocument();
    expect(addButton('comprador')).toBeInTheDocument();
  });
});

describe('ActorsForm — Múltiple Propietario, agregar/quitar copropietarios (matrícula inicial)', () => {
  async function addSecondComprador(user: ReturnType<typeof userEvent.setup>) {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText(/Número de documento/);
    await user.click(addButton('comprador'));
    await screen.findByRole('tablist');
  }

  it('al agregar el segundo aparecen las pestañas con el rótulo del rol + ordinal, y el bloque de %', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    expect(screen.getByRole('tab', { name: /Comprador 1/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Comprador 2/ })).toBeInTheDocument();
    expect(screen.getByText(/Porcentaje de propiedad/)).toBeInTheDocument();
    // Reparto por defecto 50/50 — el solidario (Comprador 1) absorbe el residuo.
    expect(screen.getByRole('tab', { name: /Comprador 1 50%/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Comprador 2 50%/ })).toBeInTheDocument();
  });

  it('el solidario (ordinal=1) absorbe el residuo mientras no se edite a mano', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    // La pestaña 2 queda activa tras agregar (foco natural en lo recién creado).
    const input2 = screen.getByLabelText(/Porcentaje exacto de Comprador 2/) as HTMLInputElement;
    await user.clear(input2);
    await user.type(input2, '30');

    await waitFor(() =>
      expect(screen.getByRole('tab', { name: /Comprador 1 70%/ })).toBeInTheDocument(),
    );
  });

  it('al editar el solidario a mano, deja de absorber el residuo', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    await user.click(screen.getByRole('tab', { name: /Comprador 1/ }));
    const input1 = screen.getByLabelText(/Porcentaje exacto de Comprador 1/) as HTMLInputElement;
    await user.clear(input1);
    await user.type(input1, '40');
    await waitFor(() => expect(input1).toHaveValue(40));

    // Cambia el agregado: el solidario YA NO se recalcula — queda fijo en 40, aunque la suma se rompa.
    await user.click(screen.getByRole('tab', { name: /Comprador 2/ }));
    const input2 = screen.getByLabelText(/Porcentaje exacto de Comprador 2/) as HTMLInputElement;
    await user.clear(input2);
    await user.type(input2, '10');

    await user.click(screen.getByRole('tab', { name: /Comprador 1/ }));
    const input1Again = screen.getByLabelText(/Porcentaje exacto de Comprador 1/) as HTMLInputElement;
    expect(input1Again).toHaveValue(40);
  });

  it('al eliminar la segunda pestaña, la primera queda con 100% escrito (no se oculta el bloque)', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    await user.click(screen.getByRole('button', { name: 'Quitar Comprador 2' }));

    await waitFor(() => expect(screen.queryByRole('tab', { name: /Comprador 2/ })).toBeNull());
    // El bloque NO se oculta (encargo cerrado): sigue mostrando "Comprador 1" con 100%.
    expect(screen.getByRole('tab', { name: /Comprador 1 100%/ })).toBeInTheDocument();
    expect(screen.getByText(/Porcentaje de propiedad/)).toBeInTheDocument();
  });

  it('máximo 4 propietarios por lado: el botón "+" se deshabilita al llegar al límite', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText(/Número de documento/);

    await user.click(addButton('comprador'));
    await screen.findByRole('tablist');
    await user.click(addButton('comprador'));
    await user.click(addButton('comprador'));

    await waitFor(() => expect(screen.getByRole('tab', { name: /Comprador 4/ })).toBeInTheDocument());
    expect(addButton('comprador')).toBeDisabled();
  });

  it('los dos mensajes de bloqueo son distintos y textuales (no paráfrasis)', async () => {
    const user = userEvent.setup();
    await addSecondComprador(user);

    // Suma != 100: el solidario en 40, el agregado se queda en 50 (sin tocarlo) → suma 90.
    await user.click(screen.getByRole('tab', { name: /Comprador 1/ }));
    const input1 = screen.getByLabelText(/Porcentaje exacto de Comprador 1/) as HTMLInputElement;
    await user.clear(input1);
    await user.type(input1, '40');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(
      await screen.findByText('La suma de los porcentajes debe ser exactamente 100%.'),
    ).toBeInTheDocument();
    expect(
      screen.queryByText('Todos los propietarios deben tener un porcentaje mayor a 0%.'),
    ).toBeNull();

    // Ahora el agregado a 0%: el solidario vuelve a absorber (100), pero el agregado queda en 0.
    await user.click(screen.getByRole('tab', { name: /Comprador 2/ }));
    const input2 = screen.getByLabelText(/Porcentaje exacto de Comprador 2/) as HTMLInputElement;
    await user.clear(input2);
    await user.type(input2, '0');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(
      await screen.findByText('Todos los propietarios deben tener un porcentaje mayor a 0%.'),
    ).toBeInTheDocument();
    expect(mocks.saveActors).not.toHaveBeenCalled();
  });
});

describe('ActorsForm — Múltiple Propietario, sin estado fantasma al desplazar índices', () => {
  it('la consulta RUNT del vendedor#1 sobrevive a insertar un vendedor#2 antes del comprador', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);

    const vendedorCard = (await screen.findByRole('group', { name: 'Vendedor' })) as HTMLElement;
    const compradorCard = screen.getByRole('group', { name: 'Comprador' }) as HTMLElement;

    // Consulta RUNT del vendedor único (ordinal=1, índice 0 en el array `actors`).
    await user.type(within(vendedorCard).getByLabelText(/Número de documento/), '111');
    await user.click(within(vendedorCard).getByRole('button', { name: /Consultar RUNT/ }));
    await within(vendedorCard).findByText(/Persona encontrada en RUNT/i);

    // El comprador (índice 1, ANTES de agregar) sigue vacío y sin consultar.
    expect((within(compradorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');

    // Agrega un 2do vendedor: se inserta EN el índice 1 (justo tras el vendedor#1), desplazando al
    // comprador de índice 1 → 2. Sin reindexar los mapas posicionales, la consulta RUNT del
    // vendedor#1 podría "saltar" al actor equivocado tras este desplazamiento.
    await user.click(within(vendedorCard).getByRole('button', { name: 'Agregar copropietario de vendedor' }));
    await within(vendedorCard).findByRole('tablist');

    // Vuelve a la pestaña del vendedor#1: su consulta sigue siendo LA SUYA, no se perdió ni se
    // reasoció al vendedor#2 recién creado (que debe seguir sin consultar).
    await user.click(within(vendedorCard).getByRole('tab', { name: /Vendedor 1/ }));
    expect(within(vendedorCard).getByText(/Persona encontrada en RUNT/i)).toBeInTheDocument();

    await user.click(within(vendedorCard).getByRole('tab', { name: /Vendedor 2/ }));
    expect(within(vendedorCard).queryByText(/Persona encontrada en RUNT/i)).toBeNull();
    expect((within(vendedorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');

    // El comprador (ahora en índice 2) tampoco heredó ni perdió nada: sigue vacío, tal cual estaba.
    expect((within(compradorCard).getByLabelText(/Número de documento/) as HTMLInputElement).value).toBe('');
    expect(mocks.runtPersonLookup).toHaveBeenCalledTimes(1);
  });
});
