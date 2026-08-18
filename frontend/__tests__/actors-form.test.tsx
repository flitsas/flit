import { createRef } from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  actorContactLookup: vi.fn(),
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
    lookupLegalRepresentativeByNit: mocks.lookupLegalRepresentativeByNit,
    actorContactLookup: mocks.actorContactLookup,
    getInstance: mocks.getInstance,
    patchFieldValues: mocks.patchFieldValues,
    getBiometricState: mocks.getBiometricState,
  },
}));

import {
  ActorsForm,
  isIdentityConsultationReady,
  validateActors,
  type ActorsFormHandle,
} from '@/components/operacion/ActorsForm';
import type { ProcedureActor } from '@/lib/api/types/procedure-runtime';

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

/** Completa consulta RUNT exitosa para el actor visible (layout split / un documento). */
async function consultRuntOk(
  user: ReturnType<typeof userEvent.setup>,
  doc = '12345',
  fullName = 'Juan Perez',
) {
  mocks.runtPersonLookup.mockResolvedValue({ ...RUNT_FOUND, documentNumber: doc, fullName });
  const buttons = screen.getAllByRole('button', { name: 'Consultar RUNT' });
  await user.click(buttons[0]);
  await screen.findByText(/Persona encontrada en RUNT|encontrada en RUNT/i);
}

describe('ActorsForm — layout split (un comprador)', () => {
  it('matrícula inicial muestra las 2 secciones (Identificación + Datos de contacto)', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    expect(
      await screen.findByText(/Datos del comprador/),
    ).toBeInTheDocument();
    expect(screen.getByText('Datos de contacto')).toBeInTheDocument();
    // Sección de identificación: documento + Consultar RUNT.
    expect(screen.getByLabelText('Número de documento')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Consultar RUNT' }),
    ).toBeInTheDocument();
    // Sección de contacto: ciudad y dirección (nuevos, obligatorias desde HU #11595).
    expect(screen.getByLabelText(/^Ciudad/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Dirección/)).toBeInTheDocument();
    // No es el layout de fieldsets ni renderiza vendedor.
    expect(screen.queryByRole('group', { name: 'Vendedor' })).toBeNull();
  });

  it('ciudad autocomplete: filtra y selecciona (≥2 chars)', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const ciudad = await screen.findByLabelText(/^Ciudad/);
    await user.type(ciudad, 'med');
    // Sugerencia filtrada del catálogo.
    const opcion = await screen.findByRole('button', { name: 'Medellin' });
    await user.click(opcion);

    expect(ciudad).toHaveValue('Medellin');
  });
});

describe('ActorsForm — fecha de expedición (RNMC, FEATURE 05)', () => {
  it('oculta la fecha de expedición cuando el RNMC no aplica (rnmcEnabled ausente/false)', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    // Espera a que monte el formulario.
    await screen.findByLabelText('Número de documento');
    expect(screen.queryByLabelText(/Fecha de expedición del documento/i)).toBeNull();
  });

  it('muestra la fecha de expedición cuando el RNMC aplica (rnmcEnabled=true)', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" rnmcEnabled />);
    expect(
      await screen.findByLabelText(/Fecha de expedición del documento/i),
    ).toBeInTheDocument();
  });
});

describe('ActorsForm — render por modalidad', () => {
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

  // HU #11019 — el correo compartido deja de bloquear (el documento sigue haciéndolo).
  it('regla vendedor≠comprador: ACEPTA el mismo correo', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);

    await screen.findByRole('group', { name: 'Vendedor' });
    const numeros = screen.getAllByLabelText(/Número de documento/);
    const nombres = screen.getAllByLabelText(/Nombre completo/);
    const emails = screen.getAllByLabelText(/Correo electrónico/);
    // HU #11595 — ciudad, dirección y teléfono son obligatorios para ambos actores.
    const telefonos = screen.getAllByLabelText(/Teléfono/);
    const ciudades = screen.getAllByLabelText(/Ciudad/);
    const direcciones = screen.getAllByLabelText(/Dirección/);

    await user.type(numeros[0], '111');
    await user.type(nombres[0], 'Ana Vendedora');
    await user.type(emails[0], 'compartido@example.com');
    await user.type(telefonos[0], '3001112222');
    await user.type(ciudades[0], 'Bogota');
    await user.type(direcciones[0], 'Calle 1 # 2-3');
    await user.type(numeros[1], '222');
    await user.type(nombres[1], 'Beto Comprador');
    await user.type(emails[1], 'compartido@example.com');
    await user.type(telefonos[1], '3003334444');
    await user.type(ciudades[1], 'Medellin');
    await user.type(direcciones[1], 'Calle 4 # 5-6');

    const consultButtons = screen.getAllByRole('button', { name: 'Consultar RUNT' });
    mocks.runtPersonLookup
      .mockResolvedValueOnce({ ...RUNT_FOUND, documentNumber: '111', fullName: 'Ana Vendedora' })
      .mockResolvedValueOnce({ ...RUNT_FOUND, documentNumber: '222', fullName: 'Beto Comprador' });
    await user.click(consultButtons[0]);
    await screen.findAllByText(/Persona encontrada en RUNT/i);
    await user.click(consultButtons[1]);
    await waitFor(() =>
      expect(screen.getAllByText(/Persona encontrada en RUNT/i).length).toBeGreaterThanOrEqual(2),
    );

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    expect(screen.queryByText(/no pueden ser la misma persona/)).toBeNull();
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
      screen.getByText(/no pueden tener el mismo número de documento/),
    ).toBeInTheDocument();
  });
});

describe('ActorsForm — submit', () => {
  // HU #11595 — ciudad, dirección y teléfono pasaron de opcionales a obligatorios.
  it('llama saveActors con los actores válidos (ciudad, dirección y teléfono obligatorios)', async () => {
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
    await consultRuntOk(user, '12345', 'Juan Perez');
    await user.type(
      screen.getByLabelText(/Correo electrónico/),
      'juan@example.com',
    );
    await user.type(screen.getByLabelText(/Teléfono/), '3001234567');
    await user.type(screen.getByLabelText(/Ciudad/), 'Bogota');
    await user.type(screen.getByLabelText(/Dirección/), 'Calle 1 # 2-3');
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
        telefono: '3001234567',
        ciudad: 'Bogota',
        direccion: 'Calle 1 # 2-3',
        // HU #10543: por defecto persona natural.
        personType: 'natural',
      },
    ]);
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(/Actores guardados/)).toBeInTheDocument();
  });

  it('sin consulta RUNT exitosa no guarda y muestra aviso', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.type(await screen.findByLabelText(/Número de documento/), '12345');
    await user.type(screen.getByLabelText(/Nombre completo/), 'Juan Perez');
    await user.type(screen.getByLabelText(/Correo electrónico/), 'juan@example.com');
    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(mocks.saveActors).not.toHaveBeenCalled();
    expect(
      screen.getByText(/Consulta RUNT para traer la información/i),
    ).toBeInTheDocument();
  });

  it('consulta fallida informa y sigue bloqueando el guardado', async () => {
    const user = userEvent.setup();
    mocks.runtPersonLookup.mockRejectedValue(new Error('timeout RUNT'));
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.type(await screen.findByLabelText(/Número de documento/), '12345');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    expect(await screen.findByText(/No se pudo consultar RUNT/i)).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Nombre completo/), 'Juan Perez');
    await user.type(screen.getByLabelText(/Correo electrónico/), 'juan@example.com');
    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(mocks.saveActors).not.toHaveBeenCalled();
    expect(isIdentityConsultationReady('error')).toBe(false);
    expect(isIdentityConsultationReady('found')).toBe(true);
  });
});

describe('ActorsForm — tipo de persona (HU #10543)', () => {
  it('por defecto persona natural: selector activo y nota de cédula automática', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    expect(
      await screen.findByRole('button', { name: 'Persona natural' }),
    ).toHaveAttribute('aria-pressed', 'true');
    expect(
      screen.getByRole('button', { name: 'Persona jurídica' }),
    ).toHaveAttribute('aria-pressed', 'false');
    // Persona natural: la cédula se incorpora desde la validación de identidad.
    expect(screen.getByText(/no se carga manualmente/)).toBeInTheDocument();
  });

  it('al elegir persona jurídica: oculta la nota y guarda personType=juridical', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.click(
      await screen.findByRole('button', { name: 'Persona jurídica' }),
    );
    expect(screen.queryByText(/no se carga manualmente/)).toBeNull();

    // El bloque de representante legal añade su propio «Número de documento»: se apunta al del actor
    // por su placeholder para que la query no sea ambigua.
    await user.type(screen.getByPlaceholderText(/Número de documento del comprador/), '900123');
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));
    expect(await screen.findByText(/Empresa encontrada en RUES/i)).toBeInTheDocument();
    // Idem con el correo: el representante legal aporta otro campo con la misma etiqueta.
    await user.type(
      document.querySelector('#comprador-email') as HTMLInputElement,
      'empresa@example.com',
    );
    // Persona jurídica exige el representante legal (sujeto de identidad, HU #10688).
    await user.type(
      document.getElementById('0-rl-numeroDoc') as HTMLInputElement,
      '1020304050',
    );
    await user.type(
      document.getElementById('0-rl-nombre') as HTMLInputElement,
      'Rep Legal',
    );
    await user.type(
      document.getElementById('0-rl-email') as HTMLInputElement,
      'rl@example.com',
    );
    // HU #11595 — ciudad, dirección y teléfono del actor (comprador) son obligatorios. El teléfono
    // se distingue por id: el representante legal también tiene un input "Teléfono".
    await user.type(
      document.getElementById('comprador-telefono') as HTMLInputElement,
      '3001234567',
    );
    await user.type(screen.getByLabelText(/^Ciudad/), 'Bogota');
    await user.type(screen.getByLabelText(/^Dirección/), 'Calle 1 # 2-3');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    const [, actors] = mocks.saveActors.mock.calls[0];
    expect(actors[0].personType).toBe('juridical');
  });

  // HU #11014 — el documento manda: un actor con NIT es persona jurídica sin tocar el selector.
  it('un actor rehidratado con NIT queda como persona jurídica', async () => {
    mocks.getActors.mockResolvedValue([
      {
        rol: 'comprador',
        tipoDocumento: 'NIT',
        numeroDocumento: '900123456',
        nombreCompleto: 'Empresa SAS',
        email: 'empresa@example.com',
        personType: 'natural',
      },
    ]);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    // El bloque de representante legal solo se pinta cuando la parte es jurídica.
    await waitFor(() =>
      expect(document.getElementById('0-rl-numeroDoc')).toBeInTheDocument(),
    );
  });
});

describe('ActorsForm — save() vía ref (embebido en wizard)', () => {
  it('expone save() que valida y persiste; oculta el botón propio', async () => {
    mocks.getActors.mockResolvedValue([
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '123',
        nombreCompleto: 'Juan Perez',
        email: 'juan@example.com',
        // HU #11595 — ciudad, dirección y teléfono ya obligatorios: sin ellos save() fallaría.
        telefono: '3001234567',
        ciudad: 'Bogota',
        direccion: 'Calle 1 # 2-3',
      },
    ]);
    const ref = createRef<ActorsFormHandle>();
    render(
      <ActorsForm
        ref={ref}
        instanceId={INSTANCE}
        modalidad="matricula_inicial"
        embeddedInWizard
      />,
    );

    // Hidrata desde el backend (espera a que pinte el nombre cargado).
    expect(await screen.findByDisplayValue('Juan Perez')).toBeInTheDocument();
    // Embebido → no hay botón "Guardar actores" propio.
    expect(screen.queryByRole('button', { name: /Guardar actores/ })).toBeNull();

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    await screen.findByText(/Persona encontrada en RUNT/i);

    let ok: boolean | undefined;
    await act(async () => {
      ok = await ref.current!.save();
    });

    expect(ok).toBe(true);
    expect(mocks.saveActors).toHaveBeenCalledTimes(1);
    const [instanceId, actors] = mocks.saveActors.mock.calls[0];
    expect(instanceId).toBe(INSTANCE);
    expect(actors[0]).toMatchObject({
      rol: 'comprador',
      numeroDocumento: '12345',
      nombreCompleto: 'Juan Perez',
      email: 'juan@example.com',
    });
  });

  it('save() devuelve false y no persiste si hay campos inválidos', async () => {
    mocks.getActors.mockResolvedValue([]);
    const ref = createRef<ActorsFormHandle>();
    render(
      <ActorsForm
        ref={ref}
        instanceId={INSTANCE}
        modalidad="matricula_inicial"
        embeddedInWizard
      />,
    );
    await screen.findByText(/Datos del comprador/);

    let ok: boolean | undefined;
    await act(async () => {
      ok = await ref.current!.save();
    });

    expect(ok).toBe(false);
    expect(mocks.saveActors).not.toHaveBeenCalled();
  });
});

describe('ActorsForm — cards RUNT enriquecidas', () => {
  it('muestra Card A con datos del conductor cuando found=true', async () => {
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
      mode: 'mock',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      nroPazYSalvo: '840377030067',
      hasActiveLicense: true,
      licenseCategories: 'B1',
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '3216549870');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();
    expect(screen.getByText('JUAN CARLOS')).toBeInTheDocument();
    expect(screen.getByText('PEREZ GOMEZ')).toBeInTheDocument();
    // Conductor status
    expect(screen.getByText('ACTIVO')).toBeInTheDocument();
    // Card B multas negativa
    expect(screen.getByText(/Sin multas ni comparendos pendientes/)).toBeInTheDocument();
    // Nombre autopoblado en sección de contacto
    expect(screen.getByDisplayValue('JUAN CARLOS PEREZ GOMEZ')).toBeInTheDocument();
  });

  // El backend entrega el nombre desglosado: firstName es solo el PRIMER nombre. La tarjeta
  // muestra los de pila juntos; si se pintara solo firstName, "JOSE GABRIEL JAIME" saldría "JOSE".
  it('junta primer y segundo nombre en la fila Nombres', async () => {
    const user = userEvent.setup();
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'JOSE GABRIEL JAIME ACOSTA MADRID',
      firstName: 'JOSE',
      secondName: 'GABRIEL JAIME',
      lastName: 'ACOSTA MADRID',
      firstLastName: 'ACOSTA',
      secondLastName: 'MADRID',
      documentType: 'CC',
      documentNumber: '71600391',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'real',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
      licenseCategories: 'C1,B1',
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '71600391');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();
    expect(screen.getByText('JOSE GABRIEL JAIME')).toBeInTheDocument();
    expect(screen.getByText('ACOSTA MADRID')).toBeInTheDocument();
    expect(screen.getByDisplayValue('JOSE GABRIEL JAIME ACOSTA MADRID')).toBeInTheDocument();
  });

  it('muestra alerta roja cuando hasPendingFines=true', async () => {
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
      hasPendingFines: true,
      hasActiveLicense: true,
      licenseCategories: 'B1',
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '9999999');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText(/ALERTA: Comparendos\/Multas pendientes/)).toBeInTheDocument();
  });

  it('lista el detalle de los comparendos bajo la alerta cuando el SIMIT lo trae', async () => {
    const user = userEvent.setup();
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'DANIEL AMADO GARCIA',
      firstName: 'DANIEL',
      lastName: 'AMADO GARCIA',
      documentType: 'CC',
      documentNumber: '1193552679',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'real',
      citizenStatus: 'ACTIVA',
      hasPendingFines: true,
      hasActiveLicense: true,
      licenseCategories: 'A2,C1,B1',
      fines: [
        {
          numero: '25612001000012662173',
          fecha: '2024-05-01',
          valor: 344730,
          organismo: 'STRIA TTOyTTE MCPAL SABANETA',
          estado: 'Pendiente',
          infraccion: 'Semáforo en rojo',
        },
      ],
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await user.type(await screen.findByLabelText('Número de documento'), '1193552679');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    expect(await screen.findByText(/ALERTA: Comparendos\/Multas pendientes/)).toBeInTheDocument();
    const detalle = screen.getByRole('list', { name: 'Detalle de comparendos' });
    expect(detalle).toHaveTextContent('Comparendo 25612001000012662173');
    expect(detalle).toHaveTextContent('Semáforo en rojo');
    expect(detalle).toHaveTextContent('$344.730 COP');
    expect(detalle).toHaveTextContent('STRIA TTOyTTE MCPAL SABANETA');
  });

  it('al volver al paso restaura la consulta sin volver a llamar RUNT', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '1193552679',
        nombreCompleto: 'DANIEL AMADO',
        email: 'd@x.com',
      },
    ]);
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'DANIEL AMADO',
      firstName: 'DANIEL',
      lastName: 'AMADO',
      documentType: 'CC',
      documentNumber: '1193552679',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'mock',
      citizenStatus: 'ACTIVA',
      hasPendingFines: true,
      hasActiveLicense: true,
      licenseCategories: 'A2,C1,B1',
    });

    const { unmount } = render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="matricula_inicial"
        roles={['comprador']}
        layout="split"
        embeddedInWizard
      />,
    );

    await screen.findByLabelText('Número de documento');
    expect(screen.getByLabelText('Número de documento')).toHaveValue('1193552679');
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();
    expect(mocks.runtPersonLookup).toHaveBeenCalledTimes(1);

    unmount();
    mocks.runtPersonLookup.mockClear();

    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="matricula_inicial"
        roles={['comprador']}
        layout="split"
        embeddedInWizard
      />,
    );

    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();
    expect(screen.getByText(/ALERTA: Comparendos\/Multas pendientes/i)).toBeInTheDocument();
    expect(mocks.runtPersonLookup).not.toHaveBeenCalled();
  });
});

describe('ActorsForm — prefill documento del propietario (paso vendedor)', () => {
  beforeEach(() => {
    mocks.runtPersonLookup.mockResolvedValue({ found: false });
  });

  const renderVendedor = () =>
    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="traspaso"
        roles={['vendedor']}
        layout="split"
        embeddedInWizard
        seedDocumentoFromOwner
        autoConsultRunt
      />,
    );

  it('siembra el documento del vendedor desde owner_document_* (solo lectura)', async () => {
    mocks.getInstance.mockResolvedValue({
      fieldValues: [
        { fieldKey: 'plate', valueText: 'ABC123' },
        { fieldKey: 'owner_document_type', valueText: 'CC' },
        { fieldKey: 'owner_document_number', valueText: '1090123456' },
      ],
    });

    renderVendedor();

    const numero = await screen.findByLabelText('Número de documento');
    await waitFor(() => expect(numero).toHaveValue('1090123456'));
    expect(numero).toHaveAttribute('readonly');
    expect(
      screen.queryByRole('button', { name: 'Consultar RUNT' }),
    ).not.toBeInTheDocument();
  });

  it('consulta RUNT automáticamente cuando el documento está sembrado', async () => {
    mocks.getInstance.mockResolvedValue({
      fieldValues: [
        { fieldKey: 'owner_document_type', valueText: 'CC' },
        { fieldKey: 'owner_document_number', valueText: '1090123456' },
      ],
    });
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'ANA VENDEDORA',
      firstName: 'ANA',
      lastName: 'VENDEDORA',
      documentType: 'CC',
      documentNumber: '1090123456',
      licenseStatus: 'ACTIVO',
      source: 'RUNT',
      mode: 'mock',
      citizenStatus: 'ACTIVA',
      hasPendingFines: false,
      hasActiveLicense: true,
    });

    renderVendedor();

    await waitFor(() => expect(mocks.runtPersonLookup).toHaveBeenCalledWith(
      INSTANCE,
      { documentType: 'CC', documentNumber: '1090123456' },
    ));
    expect(await screen.findByText('Persona encontrada en RUNT')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Consultar RUNT' })).not.toBeInTheDocument();
  });

  it('no pisa el documento del vendedor ya persistido y auto-consulta RUNT', async () => {
    mocks.getActors.mockResolvedValue([
      {
        rol: 'vendedor',
        tipoDocumento: 'CC',
        numeroDocumento: '555',
        nombreCompleto: 'Ana Vendedora',
        email: 'ana@example.com',
      },
    ]);
    mocks.getInstance.mockResolvedValue({
      fieldValues: [
        { fieldKey: 'owner_document_type', valueText: 'CC' },
        { fieldKey: 'owner_document_number', valueText: '1090123456' },
      ],
    });

    renderVendedor();

    const numero = await screen.findByLabelText('Número de documento');
    await screen.findByDisplayValue('Ana Vendedora');
    // El documento persistido manda: el seed no lo sobreescribe.
    expect(numero).toHaveValue('555');
    await waitFor(() => expect(mocks.runtPersonLookup).toHaveBeenCalledWith(
      INSTANCE,
      { documentType: 'CC', documentNumber: '555' },
    ));
  });

  it('sin owner_document_number no siembra nada y deja el documento editable', async () => {
    mocks.getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'plate', valueText: 'ABC123' }],
    });

    renderVendedor();

    const numero = await screen.findByLabelText('Número de documento');
    // Da tiempo a que resuelva el fetch del seed; debe quedar vacío.
    await waitFor(() => expect(mocks.getInstance).toHaveBeenCalled());
    expect(numero).toHaveValue('');
    expect(numero).not.toHaveAttribute('readonly');
    expect(mocks.runtPersonLookup).not.toHaveBeenCalled();
    expect(
      screen.queryByRole('button', { name: 'Consultar RUNT' }),
    ).not.toBeInTheDocument();
  });

  // El layout split del vendedor no expone un selector de tipo visible: el tipo sembrado se
  // verifica a través del payload de guardado (save() vía ref).
  it('el seed también fija el TIPO de documento del propietario (visible en el guardado)', async () => {
    const user = userEvent.setup();
    mocks.getInstance.mockResolvedValue({
      fieldValues: [
        { fieldKey: 'owner_document_type', valueText: 'CE' },
        { fieldKey: 'owner_document_number', valueText: '1090123456' },
      ],
    });
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'Ana Vendedora',
      documentType: 'CE',
      documentNumber: '1090123456',
      source: 'RUNT',
      mode: 'mock',
    });
    const ref = createRef<ActorsFormHandle>();
    render(
      <ActorsForm
        ref={ref}
        instanceId={INSTANCE}
        modalidad="traspaso"
        roles={['vendedor']}
        layout="split"
        embeddedInWizard
        seedDocumentoFromOwner
        autoConsultRunt
      />,
    );

    await waitFor(() =>
      expect(screen.getByLabelText('Número de documento')).toHaveValue('1090123456'),
    );
    // Auto-consulta RUNT al montar (autoConsultRunt).
    await screen.findByText(/Persona encontrada en RUNT/i);
    // Completa los requeridos del vendedor para que el guardado sea válido (HU #11595: ciudad,
    // dirección y teléfono también son obligatorios).
    await user.type(screen.getByLabelText(/Correo electrónico/), 'ana@example.com');
    await user.type(screen.getByLabelText(/Teléfono/), '3001234567');
    await user.type(screen.getByLabelText(/Ciudad/), 'Bogota');
    await user.type(screen.getByLabelText(/Dirección/), 'Calle 1 # 2-3');

    let ok: boolean | undefined;
    await act(async () => {
      ok = await ref.current!.save();
    });

    expect(ok).toBe(true);
    const [, actors] = mocks.saveActors.mock.calls[0];
    expect(actors[0]).toMatchObject({ tipoDocumento: 'CE', numeroDocumento: '1090123456' });
  });

  it('un tipo de documento inválido del propietario cae a CC', async () => {
    const user = userEvent.setup();
    mocks.getInstance.mockResolvedValue({
      fieldValues: [
        { fieldKey: 'owner_document_type', valueText: 'ZZ' }, // no está en el catálogo
        { fieldKey: 'owner_document_number', valueText: '1090123456' },
      ],
    });
    mocks.runtPersonLookup.mockResolvedValue({
      found: true,
      fullName: 'Ana Vendedora',
      documentType: 'CC',
      documentNumber: '1090123456',
      source: 'RUNT',
      mode: 'mock',
    });
    const ref = createRef<ActorsFormHandle>();
    render(
      <ActorsForm
        ref={ref}
        instanceId={INSTANCE}
        modalidad="traspaso"
        roles={['vendedor']}
        layout="split"
        embeddedInWizard
        seedDocumentoFromOwner
        autoConsultRunt
      />,
    );

    await waitFor(() =>
      expect(screen.getByLabelText('Número de documento')).toHaveValue('1090123456'),
    );
    await screen.findByText(/Persona encontrada en RUNT/i);
    await user.type(screen.getByLabelText(/Correo electrónico/), 'ana@example.com');
    // HU #11595 — ciudad, dirección y teléfono también son obligatorios.
    await user.type(screen.getByLabelText(/Teléfono/), '3001234567');
    await user.type(screen.getByLabelText(/Ciudad/), 'Bogota');
    await user.type(screen.getByLabelText(/Dirección/), 'Calle 1 # 2-3');

    let ok: boolean | undefined;
    await act(async () => {
      ok = await ref.current!.save();
    });

    expect(ok).toBe(true);
    const [, actors] = mocks.saveActors.mock.calls[0];
    expect(actors[0].tipoDocumento).toBe('CC');
  });

  it('en formulario unificado solo siembra el vendedor, no el comprador', async () => {
    mocks.getActors.mockResolvedValue([]);
    mocks.getInstance.mockResolvedValue({
      fieldValues: [
        { fieldKey: 'owner_document_type', valueText: 'CC' },
        { fieldKey: 'owner_document_number', valueText: '1090123456' },
      ],
    });

    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="traspaso"
        roles={['vendedor', 'comprador']}
        embeddedInWizard
        seedDocumentoFromOwner
      />,
    );

    const vendedorDoc = await screen.findByDisplayValue('1090123456');
    expect(vendedorDoc).toHaveAttribute('id', 'actor-vendedor-numeroDoc');

    const compradorDoc = document.getElementById('actor-comprador-numeroDoc');
    expect(compradorDoc).toBeTruthy();
    expect(compradorDoc).toHaveValue('');
  });
});

describe('validateActors — unidad', () => {
  // HU #11595 — ciudad, dirección y teléfono ahora son obligatorios: el "base" válido debe traerlos.
  const base: ProcedureActor = {
    rol: 'comprador',
    tipoDocumento: 'CC',
    numeroDocumento: '1',
    nombreCompleto: 'X',
    email: 'x@y.com',
    telefono: '3001234567',
    ciudad: 'Bogota',
    direccion: 'Calle 1 # 2-3',
  };

  it('acepta un comprador válido en matrícula', () => {
    expect(validateActors([base], 'matricula_inicial').valid).toBe(true);
  });

  // HU #11019 — el email coincidente ya no invalida: ambas partes pueden compartir buzón.
  it('acepta email coincidente vendedor/comprador en traspaso', () => {
    const v = validateActors(
      [
        { ...base, rol: 'vendedor', numeroDocumento: '2' },
        { ...base, rol: 'comprador', numeroDocumento: '3' },
      ],
      'traspaso',
    );
    expect(v.valid).toBe(true);
  });

  it('rechaza número de documento con letras cuando el tipo no es pasaporte', () => {
    const v = validateActors([{ ...base, tipoDocumento: 'CC', numeroDocumento: '12A4' }], 'matricula_inicial');
    expect(v.valid).toBe(false);
    expect(v.byActor[0].numeroDocumento).toContain('dígitos');
  });

  it('acepta pasaporte alfanumérico', () => {
    const v = validateActors([{ ...base, tipoDocumento: 'PAS', numeroDocumento: 'AB123CD' }], 'matricula_inicial');
    expect(v.valid).toBe(true);
  });

  it('rechaza nombre con caracteres especiales', () => {
    const v = validateActors([{ ...base, nombreCompleto: '<script>' }], 'matricula_inicial');
    expect(v.valid).toBe(false);
  });
});

/**
 * REGRESIÓN — el velo de espera al consultar un actor.
 *
 * Dos defectos distintos lo hacían invisible y conviene fijar los dos, porque fallan por motivos
 * que no se parecen en nada:
 *
 * 1. Se pintaba con `fixed inset-0` dentro del árbol del formulario. Basta un ancestro con
 *    `transform`, `filter` o `backdrop-filter` para que ese `fixed` se resuelva contra él y el velo
 *    quede recortado al recuadro del formulario. Por eso ahora va portalizado a `<body>`, igual que
 *    `Modal` — y por eso este test lo busca en `document.body`, no en el contenedor del render.
 * 2. Colgaba de «hay una consulta en curso», y en traspaso el vendedor se consulta solo al montar:
 *    el velo aparecía al entrar al paso sin que nadie lo hubiera pedido.
 */
describe('ActorsForm — velo de espera de la consulta', () => {
  it('no aparece al montar el formulario', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    await screen.findByText(/Datos del comprador/);
    expect(screen.queryByText(/Consultando información en el RUNT/)).toBeNull();
  });

  it('cubre la pantalla mientras dura la consulta que dispara el gestor', async () => {
    // Consulta colgada a propósito: el velo tiene que seguir en pantalla al asertar.
    let resolver: (v: unknown) => void = () => {};
    mocks.runtPersonLookup.mockImplementation(
      () => new Promise((r) => { resolver = r; }),
    );

    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.type(
      await screen.findByPlaceholderText(/Número de documento del comprador/),
      '12345',
    );
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    // Se busca en el documento entero: el velo vive en <body>, fuera del árbol del formulario.
    const velo = await screen.findByText(/Consultando información en el RUNT/);
    expect(document.body.contains(velo)).toBe(true);

    resolver(RUNT_FOUND);
    await waitFor(() =>
      expect(screen.queryByText(/Consultando información en el RUNT/)).toBeNull(),
    );
  });
});
