import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// HU #10956 — revierte parcialmente el consentimiento Habeas Data de HU #10885: el check de
// "autorizo la reutilización de mis datos" desaparece del formulario (la identidad de un actor se
// consulta SIEMPRE en vivo, sin gate de consentimiento). A cambio, tras resolver la identidad se
// precargan sus datos de CONTACTO (ciudad/correo/dirección/teléfono) cuando la persona ya es
// conocida por la compañía — nombre y documento siguen viniendo únicamente del RUNT/RUES.
// Además, badge de origen "Dato reutilizado" cuando el lookup de IDENTIDAD viene de caché
// (`mode: 'cache'`, AC1 de HU #10885 — sigue vigente para RUES; no se toca por esta HU).
const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  actorContactLookup: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  getInstance: vi.fn(),
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
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';
import type { ActorContactLookupResult } from '@/lib/api/types/procedure-runtime';

const INSTANCE = 'inst-1';

// Anotado con el tipo del contrato, NO inferido: sin la anotación TypeScript deduce los campos
// como el literal `null`, y `typeof EMPTY_CONTACT` rechaza cualquier string en los tests que
// resuelven el lookup con datos reales.
const EMPTY_CONTACT: ActorContactLookupResult = {
  ciudad: null,
  email: null,
  direccion: null,
  telefono: null,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.actorContactLookup.mockResolvedValue(EMPTY_CONTACT);
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
});

describe('ActorsForm — AC1 (HU #10956) el check de Habeas Data ya no se ofrece', () => {
  it('matrícula (SPLIT, persona natural): no muestra el checkbox de reutilización', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByLabelText('Número de documento');
    expect(
      screen.queryByRole('checkbox', { name: /Autorizo la reutilización/i }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/Habeas Data/i)).not.toBeInTheDocument();
  });

  it('matrícula (SPLIT, persona jurídica): tampoco muestra el checkbox', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.click(await screen.findByRole('button', { name: 'Persona jurídica' }));
    expect(
      screen.queryByRole('checkbox', { name: /Autorizo la reutilización/i }),
    ).not.toBeInTheDocument();
  });

  it('traspaso (MULTI): ninguno de los dos actores (vendedor/comprador) ofrece el checkbox', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);
    await screen.findByRole('group', { name: 'Vendedor' });
    expect(
      screen.queryAllByRole('checkbox', { name: /Autorizo la reutilización/i }),
    ).toHaveLength(0);
  });

  it('guardar NUNCA envía autorizaReutilizacionDatos, ni siquiera si el actor persistido ya lo traía en true', async () => {
    // Actor rehidratado desde el backend con un consentimiento previo (dato legado de HU #10885,
    // anterior a esta HU). El guardado debe descartarlo por completo (AC1).
    mocks.getActors.mockResolvedValue([
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '12345',
        nombreCompleto: 'Juan Perez',
        email: 'juan@example.com',
        autorizaReutilizacionDatos: true,
        // HU #11595 — ciudad, dirección y teléfono ahora son obligatorios.
        telefono: '3001234567',
        ciudad: 'Bogota',
        direccion: 'Calle 1 # 2-3',
      },
    ]);
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByDisplayValue('Juan Perez');

    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'Juan Perez',
      documentType: 'CC',
      documentNumber: '12345',
      source: 'RUNT',
      mode: 'mock',
    });
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await screen.findByText(/Persona encontrada en RUNT/i);

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [, actors] = mocks.saveActors.mock.calls[0];
    expect(actors[0]).not.toHaveProperty('autorizaReutilizacionDatos');
  });
});

describe('ActorsForm — AC2/AC3/AC4 (HU #10956) precarga de datos de contacto', () => {
  it('AC2: al resolver la identidad por RUNT, precarga ciudad/correo/dirección/teléfono conocidos (nombre y documento siguen viniendo del RUNT)', async () => {
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
      mode: 'real',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
    });
    mocks.actorContactLookup.mockResolvedValue({
      ciudad: 'Bogotá',
      email: 'juan.contacto@empresa.com',
      direccion: 'Cra 1 # 2-3',
      telefono: '3001112233',
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '3216549870');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();

    await waitFor(() =>
      expect(mocks.actorContactLookup).toHaveBeenCalledWith({
        tipoDocumento: 'CC',
        numeroDocumento: '3216549870',
      }),
    );

    await waitFor(() => {
      expect(screen.getByLabelText(/^Ciudad/)).toHaveValue('Bogotá');
      expect(screen.getByLabelText(/^Dirección/)).toHaveValue('Cra 1 # 2-3');
      expect(screen.getByLabelText(/Teléfono/)).toHaveValue('3001112233');
      expect(screen.getByLabelText(/Correo electrónico/)).toHaveValue('juan.contacto@empresa.com');
    });

    // El nombre viene del RUNT, nunca del lookup de contacto (que no lo expone).
    expect(screen.getByLabelText(/Nombre completo/)).toHaveValue('JUAN CARLOS PEREZ GOMEZ');
  });

  it('AC3: no pisa un campo que el operador ya había editado antes de que resuelva la precarga', async () => {
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
      mode: 'real',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
    });
    mocks.actorContactLookup.mockResolvedValue({
      ciudad: 'Medellín',
      email: 'otro-correo@empresa.com',
      direccion: 'Calle falsa 123',
      telefono: '3009998877',
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '9999999');
    // El operador ya escribió su propio correo ANTES de resolver la identidad.
    await user.type(screen.getByLabelText(/Correo electrónico/), 'operador@example.com');

    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();

    await waitFor(() => expect(screen.getByLabelText(/^Ciudad/)).toHaveValue('Medellín'));

    // La ciudad (no tocada) SÍ se precarga; el correo (editado a mano) se conserva intacto.
    expect(screen.getByLabelText(/Correo electrónico/)).toHaveValue('operador@example.com');
    expect(screen.getByLabelText(/^Dirección/)).toHaveValue('Calle falsa 123');
  });

  it('AC4: persona sin antecedentes deja los 4 campos vacíos y editables, sin error', async () => {
    const user = userEvent.setup();
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'NUEVA PERSONA',
      firstName: 'NUEVA',
      lastName: 'PERSONA',
      documentType: 'CC',
      documentNumber: '111222',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'real',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
    });
    mocks.actorContactLookup.mockResolvedValue(EMPTY_CONTACT);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '111222');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();

    await waitFor(() => expect(mocks.actorContactLookup).toHaveBeenCalledTimes(1));

    expect(screen.getByLabelText(/^Ciudad/)).toHaveValue('');
    expect(screen.getByLabelText(/^Dirección/)).toHaveValue('');
    expect(screen.getByLabelText(/Teléfono/)).toHaveValue('');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();

    // Siguen editables: el operador puede completarlos a mano sin bloqueo.
    await user.type(screen.getByLabelText(/^Ciudad/), 'Cali');
    expect(screen.getByLabelText(/^Ciudad/)).toHaveValue('Cali');
  });
});

describe('ActorsForm — AC5 (HU #10956) estados de UI del lookup de contacto', () => {
  it('muestra un aviso mientras carga y lo retira al resolver (found)', async () => {
    const user = userEvent.setup();
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'CARLOS RUIZ',
      firstName: 'CARLOS',
      lastName: 'RUIZ',
      documentType: 'CC',
      documentNumber: '555666',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'real',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
    });
    let resolveContact!: (v: ActorContactLookupResult) => void;
    mocks.actorContactLookup.mockReturnValue(
      new Promise((resolve) => {
        resolveContact = resolve;
      }),
    );

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '555666');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();

    expect(
      await screen.findByText(/Buscando datos de contacto conocidos/i),
    ).toBeInTheDocument();

    resolveContact({
      ciudad: 'Barranquilla',
      email: null,
      direccion: null,
      telefono: null,
    });

    await waitFor(() => expect(screen.getByLabelText(/^Ciudad/)).toHaveValue('Barranquilla'));
    expect(screen.queryByText(/Buscando datos de contacto conocidos/i)).not.toBeInTheDocument();
    expect(
      screen.getByText(/Contacto precargado desde un trámite anterior/i),
    ).toBeInTheDocument();
  });

  it('si el lookup de contacto falla, avisa sin bloquear la captura manual (degradación silenciosa)', async () => {
    const user = userEvent.setup();
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'LUIS TORRES',
      firstName: 'LUIS',
      lastName: 'TORRES',
      documentType: 'CC',
      documentNumber: '777888',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'real',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
    });
    mocks.actorContactLookup.mockRejectedValue(new Error('Servicio no disponible'));

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '777888');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();

    expect(
      await screen.findByText(/No se pudo precargar el contacto conocido/i),
    ).toBeInTheDocument();

    // Nunca bloquea: el operador completa el contacto a mano sin problema.
    const ciudadInput = screen.getByLabelText(/^Ciudad/);
    expect(ciudadInput).not.toBeDisabled();
    await user.type(ciudadInput, 'Manizales');
    expect(ciudadInput).toHaveValue('Manizales');
  });
});

describe('ActorsForm — AC1 origen del dato reutilizado (identidad, HU #10885, sin cambios)', () => {
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
