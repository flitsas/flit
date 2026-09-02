// HU #11595 — ciudad, dirección y teléfono pasan de opcionales a obligatorios en el formulario de
// actores (bloquean "Continuar" igual que nombre/documento/email). Cubre los 4 escenarios Gherkin de
// la HU: (1) actor completo permite continuar, (2) falta el teléfono bloquea y lo marca, (3) el
// layout de múltiples actores señala el error sobre el actor correcto (vendedor vs comprador), y
// (4) un trámite en curso incompleto muestra los faltantes marcados al abrir el paso, sin submit.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  actorContactLookup: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  getBiometricState: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getActors: mocks.getActors,
    saveActors: mocks.saveActors,
    runtPersonLookup: mocks.runtPersonLookup,
    ruesPersonLookup: mocks.ruesPersonLookup,
    actorContactLookup: mocks.actorContactLookup,
    lookupLegalRepresentativeByNit: mocks.lookupLegalRepresentativeByNit,
    getInstance: mocks.getInstance,
    patchFieldValues: mocks.patchFieldValues,
    getBiometricState: mocks.getBiometricState,
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';

const INSTANCE = 'inst-1';

const RUNT_FOUND = {
  found: true,
  fullName: 'Juan Perez',
  firstName: 'Juan',
  lastName: 'Perez',
  documentType: 'CC',
  documentNumber: '12345',
  source: 'RUNT',
  mode: 'mock',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.actorContactLookup.mockResolvedValue({ found: false });
  mocks.runtPersonLookup.mockResolvedValue(RUNT_FOUND);
  mocks.ruesPersonLookup.mockResolvedValue({
    found: true,
    razonSocial: 'Empresa SAS',
    documentNumber: '900123',
    source: 'RUES',
    mode: 'mock',
  });
  mocks.patchFieldValues.mockResolvedValue(undefined);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  sessionStorage.clear();
});

describe('ActorsForm — gate de campos obligatorios hacia la shell', () => {
  // El pie del asistente deshabilita "Continuar y guardar" con esta señal. Antes el botón estaba
  // siempre activo y el bloqueo llegaba tras el clic: el gestor pulsaba y no pasaba nada visible.
  it('reporta false mientras falten campos y true en cuanto se completan', async () => {
    const user = userEvent.setup();
    const onGate = vi.fn();
    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="matricula_inicial"
        onCamposRequeridosGateChange={onGate}
      />,
    );

    await waitFor(() => expect(onGate).toHaveBeenCalled());
    // Paso recién abierto: nada capturado, nada que dejar avanzar.
    expect(onGate).toHaveBeenLastCalledWith(false);

    await user.type(await screen.findByLabelText(/Número de documento/), '12345');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await screen.findByText(/Persona encontrada en RUNT/i);
    // Con la consulta hecha pero el contacto a medias sigue bloqueado.
    expect(onGate).toHaveBeenLastCalledWith(false);

    await user.type(screen.getByLabelText(/Correo electrónico/), 'juan@example.com');
    await user.type(screen.getByLabelText(/^Teléfono/), '3001234567');
    await user.type(screen.getByLabelText(/^Ciudad/), 'Bogota');
    await user.type(screen.getByLabelText(/^Dirección/), 'Calle 1 # 2-3');

    await waitFor(() => expect(onGate).toHaveBeenLastCalledWith(true));
  });

  // El gate NO puede traer consigo el marcado en rojo: `showErrors` es del formulario entero, así
  // que encenderlo mientras se teclea el documento pintaba también las tarjetas de copropietarios
  // recién añadidas, vacías y sin tocar. El motivo del bloqueo lo dice el pie del asistente.
  it('no marca ningún campo mientras el gestor está capturando', async () => {
    const user = userEvent.setup();
    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="matricula_inicial"
        onCamposRequeridosGateChange={vi.fn()}
      />,
    );

    await user.type(await screen.findByLabelText(/Número de documento/), '12345');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await screen.findByText(/Persona encontrada en RUNT/i);

    // Contacto a medias, pero sin haber pulsado guardar: ni un solo campo en rojo.
    expect(screen.queryByText('Teléfono requerido')).toBeNull();
    expect(screen.queryByText('Ciudad requerida')).toBeNull();
    expect(screen.queryByText('Dirección requerida')).toBeNull();
  });
});

describe('ActorsForm — contacto requerido (HU #11595)', () => {
  // Escenario: actor con contacto completo permite continuar.
  it('AC1: comprador con nombre, documento, email, ciudad, dirección y teléfono permite guardar', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.type(await screen.findByLabelText(/Número de documento/), '12345');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await screen.findByText(/Persona encontrada en RUNT/i);

    await user.type(screen.getByLabelText(/Correo electrónico/), 'juan@example.com');
    await user.type(screen.getByLabelText(/^Teléfono/), '3001234567');
    await user.type(screen.getByLabelText(/^Ciudad/), 'Bogota');
    await user.type(screen.getByLabelText(/^Dirección/), 'Calle 1 # 2-3');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [, actors] = mocks.saveActors.mock.calls[0];
    expect(actors[0]).toMatchObject({
      telefono: '3001234567',
      ciudad: 'Bogota',
      direccion: 'Calle 1 # 2-3',
    });
    // Sin errores pendientes en el formulario.
    expect(screen.queryByText('Teléfono requerido')).toBeNull();
    expect(screen.queryByText('Ciudad requerida')).toBeNull();
    expect(screen.queryByText('Dirección requerida')).toBeNull();
  });

  // Escenario: falta el teléfono y no se puede continuar.
  it('AC2: teléfono vacío bloquea el guardado y marca el campo como requerido', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.type(await screen.findByLabelText(/Número de documento/), '12345');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await screen.findByText(/Persona encontrada en RUNT/i);

    await user.type(screen.getByLabelText(/Correo electrónico/), 'juan@example.com');
    await user.type(screen.getByLabelText(/^Ciudad/), 'Bogota');
    await user.type(screen.getByLabelText(/^Dirección/), 'Calle 1 # 2-3');
    // Teléfono deliberadamente vacío.

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(mocks.saveActors).not.toHaveBeenCalled();
    const telefono = screen.getByLabelText(/^Teléfono/);
    expect(telefono).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('Teléfono requerido')).toBeInTheDocument();
  });

  // Escenario: layout de múltiples actores valida cada parte — el error del vendedor no se
  // confunde con el del comprador.
  it('AC3: vendedor sin ciudad bloquea y el error se marca en el vendedor, no en el comprador', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);

    const vendedorCard = (await screen.findByRole('group', { name: 'Vendedor' })) as HTMLElement;
    const compradorCard = screen.getByRole('group', { name: 'Comprador' }) as HTMLElement;

    // Vendedor: completo, EXCEPTO ciudad.
    await user.type(within(vendedorCard).getByLabelText(/Número de documento/), '111');
    await user.type(within(vendedorCard).getByLabelText(/Nombre completo/), 'Ana Vendedora');
    await user.type(within(vendedorCard).getByLabelText(/Correo electrónico/), 'ana@example.com');
    await user.type(within(vendedorCard).getByLabelText(/^Teléfono/), '3001112222');
    await user.type(within(vendedorCard).getByLabelText(/^Dirección/), 'Calle 1 # 2-3');

    // Comprador: completo.
    await user.type(within(compradorCard).getByLabelText(/Número de documento/), '222');
    await user.type(within(compradorCard).getByLabelText(/Nombre completo/), 'Beto Comprador');
    await user.type(within(compradorCard).getByLabelText(/Correo electrónico/), 'beto@example.com');
    await user.type(within(compradorCard).getByLabelText(/^Teléfono/), '3003334444');
    await user.type(within(compradorCard).getByLabelText(/^Ciudad/), 'Medellin');
    await user.type(within(compradorCard).getByLabelText(/^Dirección/), 'Calle 4 # 5-6');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(mocks.saveActors).not.toHaveBeenCalled();
    // El error de ciudad aparece en el vendedor…
    expect(within(vendedorCard).getByText('Ciudad requerida')).toBeInTheDocument();
    expect(within(vendedorCard).getByLabelText(/^Ciudad/)).toHaveAttribute('aria-invalid', 'true');
    // …y NO en el comprador (que sí tiene ciudad completa).
    expect(within(compradorCard).queryByText('Ciudad requerida')).toBeNull();
    expect(within(compradorCard).getByLabelText(/^Ciudad/)).toHaveAttribute('aria-invalid', 'false');
  });

  // Escenario: trámite en curso incompleto se muestra editable con los faltantes marcados, sin
  // esperar a que el gestor pulse "Continuar".
  it('AC4: comprador persistido sin dirección muestra el faltante marcado al abrir el paso', async () => {
    mocks.getActors.mockResolvedValue([
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '12345',
        nombreCompleto: 'Juan Perez',
        email: 'juan@example.com',
        telefono: '3001234567',
        ciudad: 'Bogota',
        // direccion ausente a propósito: el trámite quedó incompleto.
      },
    ]);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" embeddedInWizard />);

    // El paso está editable (no hay overlay de solo lectura ni bloqueo del input).
    const direccion = await screen.findByLabelText(/^Dirección/);
    expect(direccion).not.toBeDisabled();
    expect(direccion).toHaveValue('');

    // El faltante se marca SIN que el gestor haya pulsado "Continuar"/"Guardar".
    expect(direccion).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('Dirección requerida')).toBeInTheDocument();

    // Los campos que sí llegaron completos no se marcan como error.
    expect(screen.getByLabelText(/^Ciudad/)).toHaveAttribute('aria-invalid', 'false');
    expect(screen.getByLabelText(/^Teléfono/)).toHaveAttribute('aria-invalid', 'false');
  });
});
