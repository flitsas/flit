import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

/**
 * INVENTARIO DEL PASO DE ACTORES (`ActorsForm`).
 *
 * Red de seguridad del rediseño visual: fija QUÉ datos tiene que seguir pidiendo el paso, no cómo
 * se ven. El rediseño mueve secciones, cambia tarjetas por acordeones y reordena rejillas; nada de
 * eso puede hacer desaparecer un dato del comprador, del vendedor ni de su representante legal.
 * Un campo que se pierde aquí es un trámite que el organismo devuelve.
 *
 * Se consulta por rótulo accesible y por región/rol a propósito: son los dos contratos que
 * sobreviven a un cambio de maquetación. No se mira ni una clase, ni una etiqueta HTML, ni el
 * texto decorativo de las tarjetas. Si un campo deja de tener rótulo el test cae — que es
 * exactamente lo que se busca, porque un campo sin rótulo ya está roto para el lector de pantalla.
 *
 * Dos rótulos están duplicados a propósito en persona jurídica (el actor y su representante legal
 * piden ambos «Número de documento», «Correo electrónico» y «Teléfono»). En vez de desempatarlos
 * por orden del DOM —que es justo lo que el rediseño puede cambiar— se fija el NÚMERO de campos
 * con ese rótulo: si uno desaparece, la cuenta cae.
 */

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

import { ActorsForm } from '@/components/operacion/ActorsForm';
import type { LegalRepresentativeLookupResult } from '@/lib/api/types/procedure-runtime';

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

/** Compañía del directorio del tenant con DOS representantes (HU #10937): habilita el selector. */
const PRECARGA_DOS_REPRESENTANTES: LegalRepresentativeLookupResult = {
  company: {
    nit: '900555666',
    razonSocial: 'Comercializadora del Valle SAS',
    email: 'contacto@valle.co',
    address: null,
    city: null,
    phone: null,
  },
  representante: {
    tipoDoc: 'CC',
    documento: '79123456',
    nombres: 'Carlos',
    primerApellido: 'Ramírez',
    segundoApellido: 'Núñez',
    email: 'carlos@valle.co',
    telefono: '3001234567',
  },
  firmaVigente: true,
  identidadVigente: false,
  representantes: [
    {
      tipoDoc: 'CC',
      documento: '79123456',
      nombres: 'Carlos',
      primerApellido: 'Ramírez',
      segundoApellido: 'Núñez',
      email: 'carlos@valle.co',
      telefono: '3001234567',
      firmaVigente: true,
      identidadVigente: false,
    },
    {
      tipoDoc: 'CC',
      documento: '52988777',
      nombres: 'Ana',
      primerApellido: 'Gómez',
      segundoApellido: null,
      email: 'ana@valle.co',
      telefono: '3009998888',
      firmaVigente: false,
      identidadVigente: true,
    },
  ],
};

/** Representante único con firma Y identidad vigentes: habilita la elección del mecanismo (HU #11061). */
const PRECARGA_DOBLE_VIGENCIA: LegalRepresentativeLookupResult = {
  ...PRECARGA_DOS_REPRESENTANTES,
  firmaVigente: true,
  identidadVigente: true,
  representantes: [
    {
      tipoDoc: 'CC',
      documento: '79123456',
      nombres: 'Carlos',
      primerApellido: 'Ramírez',
      segundoApellido: 'Núñez',
      email: 'carlos@valle.co',
      telefono: '3001234567',
      firmaVigente: true,
      identidadVigente: true,
    },
  ],
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
    estado: 'ACTIVA',
    documentNumber: '900555666',
    documentType: 'NIT',
    source: 'RUES',
    mode: 'mock',
  });
  mocks.patchFieldValues.mockResolvedValue(undefined);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  sessionStorage.clear();
});

/** Monta el paso tal como lo usa la matrícula inicial: un solo actor (comprador), layout partido. */
async function renderMatricula(props: { rnmcEnabled?: boolean } = {}) {
  const user = userEvent.setup();
  render(
    <ActorsForm
      instanceId={INSTANCE}
      modalidad="matricula_inicial"
      layout="split"
      {...props}
    />,
  );
  await screen.findByRole('form', { name: 'Captura de actores del trámite' });
  return user;
}

/** Monta el paso tal como lo usa el traspaso: vendedor + comprador. */
async function renderTraspaso(props: { rnmcEnabled?: boolean } = {}) {
  const user = userEvent.setup();
  render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" {...props} />);
  await screen.findByRole('group', { name: 'Vendedor' });
  return user;
}

describe('ActorsForm — inventario: matrícula inicial (un comprador, layout partido)', () => {
  it('conserva las dos secciones y todos los datos del comprador', async () => {
    await renderMatricula();

    // Las dos secciones del paso.
    expect(screen.getByRole('heading', { name: 'Datos del comprador' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Datos de contacto' })).toBeInTheDocument();

    // 1 · Tipo de persona: grupo con las dos naturalezas posibles del actor.
    const tipoPersona = screen.getByRole('group', { name: 'Tipo de persona' });
    expect(within(tipoPersona).getByRole('button', { name: 'Persona natural' })).toBeInTheDocument();
    expect(
      within(tipoPersona).getByRole('button', { name: 'Persona jurídica' }),
    ).toBeInTheDocument();

    // 2 · Identificación: número de documento + su consulta a la fuente oficial.
    expect(screen.getByLabelText('Número de documento')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Consultar RUNT' })).toBeInTheDocument();

    // 3 · Contacto: nombre, correo, teléfono, ciudad y dirección.
    expect(screen.getByLabelText(/^Nombre completo/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Correo electrónico/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Teléfono/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Ciudad/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Dirección/)).toBeInTheDocument();

    // 4 · Espejo de solo lectura del documento dentro del bloque de contacto: el gestor ve a quién
    // le está escribiendo los datos sin volver a la sección de arriba.
    expect(screen.getByLabelText('Documento')).toHaveAttribute('readonly');
  });

  it('la ciudad conserva el autocompletado del catálogo', async () => {
    const user = await renderMatricula();

    const ciudad = screen.getByLabelText(/^Ciudad/);
    await user.type(ciudad, 'med');

    const sugerencias = await screen.findByRole('list', { name: 'Sugerencias de ciudad' });
    await user.click(within(sugerencias).getByRole('button', { name: 'Medellin' }));

    expect(ciudad).toHaveValue('Medellin');
  });

  it('persona jurídica: el nombre pasa a razón social y la consulta al RUES', async () => {
    const user = await renderMatricula();

    await user.click(screen.getByRole('button', { name: 'Persona jurídica' }));

    expect(screen.getByLabelText(/^Razón social/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Consultar RUES' })).toBeInTheDocument();
  });

  // La fecha de expedición solo existe cuando el RNMC aplica al trámite (FEATURE 05) y solo tiene
  // sentido para una persona natural: una empresa no tiene cédula que expedir.
  it('la fecha de expedición se pide solo con RNMC y solo en persona natural', async () => {
    const user = await renderMatricula({ rnmcEnabled: true });

    expect(screen.getByLabelText(/^Fecha de expedición del documento/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Persona jurídica' }));
    expect(screen.queryByLabelText(/^Fecha de expedición del documento/)).toBeNull();
  });

  it('sin RNMC no se pide la fecha de expedición', async () => {
    await renderMatricula();

    expect(screen.queryByLabelText(/^Fecha de expedición del documento/)).toBeNull();
  });
});

describe('ActorsForm — inventario: representante legal (persona jurídica)', () => {
  it('conserva todos los datos del representante legal', async () => {
    const user = await renderMatricula();
    await user.click(screen.getByRole('button', { name: 'Persona jurídica' }));

    // Tipo de documento del representante: persona natural, por eso NO ofrece NIT.
    const tipoDoc = screen.getByLabelText('Tipo de documento');
    const opciones = within(tipoDoc).getAllByRole('option');
    expect(opciones.map((o) => o.textContent)).toEqual([
      'Cédula de ciudadanía (CC)',
      'Cédula de extranjería (CE)',
      'Pasaporte (PAS)',
      'Tarjeta de identidad (TI)',
    ]);
    expect(within(tipoDoc).queryByRole('option', { name: 'NIT' })).toBeNull();

    // Documento del representante + su propia consulta al RUNT (la de la empresa va al RUES).
    expect(screen.getAllByLabelText('Número de documento')).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Consultar RUNT' })).toBeInTheDocument();

    // Nombre propio del representante y su contacto: el correo es obligatorio (HU #10688) porque
    // es quien valida la identidad de la persona jurídica; el teléfono sigue siendo opcional.
    expect(screen.getByLabelText('Nombre completo del representante')).toBeInTheDocument();
    expect(screen.getAllByLabelText(/^Correo electrónico/)).toHaveLength(2);
    expect(screen.getAllByLabelText(/^Teléfono/)).toHaveLength(2);
  });

  it('con varios representantes conserva el selector de quién firma', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue(PRECARGA_DOS_REPRESENTANTES);

    const user = await renderMatricula();
    await user.type(screen.getByLabelText('Número de documento'), '900555666');
    await user.click(screen.getByRole('button', { name: 'Persona jurídica' }));
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));

    const selector = await screen.findByLabelText('Representante legal que firma');
    expect(within(selector).getAllByRole('option')).toHaveLength(2);
  });

  it('con firma e identidad vigentes conserva la elección del mecanismo de firma', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue(PRECARGA_DOBLE_VIGENCIA);

    const user = await renderMatricula();
    await user.type(screen.getByLabelText('Número de documento'), '900555666');
    await user.click(screen.getByRole('button', { name: 'Persona jurídica' }));
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));

    const mecanismo = await screen.findByLabelText('Firma con la que se registra el trámite');
    expect(within(mecanismo).getByRole('option', { name: 'Firma del baúl' })).toBeInTheDocument();
    expect(
      within(mecanismo).getByRole('option', { name: 'Sello de validación de identidad' }),
    ).toBeInTheDocument();
  });
});

describe('ActorsForm — inventario: traspaso (vendedor y comprador)', () => {
  it('conserva las dos tarjetas de actores', async () => {
    await renderTraspaso();

    expect(screen.getByRole('group', { name: 'Vendedor' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Comprador' })).toBeInTheDocument();
  });

  it.each(['Vendedor', 'Comprador'])(
    'la tarjeta de %s conserva todos sus campos',
    async (rol) => {
      await renderTraspaso();
      const tarjeta = screen.getByRole('group', { name: rol });

      const tipoPersona = within(tarjeta).getByRole('group', { name: 'Tipo de persona' });
      expect(
        within(tipoPersona).getByRole('button', { name: 'Persona natural' }),
      ).toBeInTheDocument();
      expect(
        within(tipoPersona).getByRole('button', { name: 'Persona jurídica' }),
      ).toBeInTheDocument();

      expect(within(tarjeta).getByLabelText('Tipo de documento')).toBeInTheDocument();
      expect(within(tarjeta).getByLabelText(/^Número de documento/)).toBeInTheDocument();
      expect(within(tarjeta).getByRole('button', { name: 'Consultar RUNT' })).toBeInTheDocument();
      expect(within(tarjeta).getByLabelText(/^Nombre completo/)).toBeInTheDocument();
      expect(within(tarjeta).getByLabelText(/^Correo electrónico/)).toBeInTheDocument();
      expect(within(tarjeta).getByLabelText(/^Teléfono/)).toBeInTheDocument();
      expect(within(tarjeta).getByLabelText(/^Ciudad/)).toBeInTheDocument();
      expect(within(tarjeta).getByLabelText(/^Dirección/)).toBeInTheDocument();
    },
  );

  it('el tipo de documento ofrece el catálogo completo (CC, CE, NIT, PAS, TI)', async () => {
    await renderTraspaso();
    const tipoDoc = within(screen.getByRole('group', { name: 'Vendedor' })).getByLabelText(
      'Tipo de documento',
    );

    expect(within(tipoDoc).getAllByRole('option').map((o) => o.textContent)).toEqual([
      'Cédula de ciudadanía (CC)',
      'Cédula de extranjería (CE)',
      'NIT',
      'Pasaporte (PAS)',
      'Tarjeta de identidad (TI)',
    ]);
  });

  it('con RNMC cada actor conserva su fecha de expedición', async () => {
    await renderTraspaso({ rnmcEnabled: true });

    for (const rol of ['Vendedor', 'Comprador']) {
      const tarjeta = screen.getByRole('group', { name: rol });
      expect(
        within(tarjeta).getByLabelText(/^Fecha de expedición del documento/),
      ).toBeInTheDocument();
    }
  });
});

describe('ActorsForm — inventario: reglas que el rediseño no puede aflojar', () => {
  // El vendedor y el comprador no pueden ser la misma persona: el traspaso quedaría sin cambio de
  // propietario. La regla mira el DOCUMENTO (el correo compartido sí está permitido, HU #11019).
  it('traspaso: vendedor y comprador no pueden compartir el número de documento', async () => {
    const user = await renderTraspaso();

    const vendedor = screen.getByRole('group', { name: 'Vendedor' });
    const comprador = screen.getByRole('group', { name: 'Comprador' });

    await user.type(within(vendedor).getByLabelText(/^Número de documento/), '999');
    await user.type(within(vendedor).getByLabelText(/^Nombre completo/), 'Ana Vendedora');
    await user.type(within(vendedor).getByLabelText(/^Correo electrónico/), 'ana@example.com');
    await user.type(within(comprador).getByLabelText(/^Número de documento/), '999');
    await user.type(within(comprador).getByLabelText(/^Nombre completo/), 'Beto Comprador');
    await user.type(within(comprador).getByLabelText(/^Correo electrónico/), 'beto@example.com');

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    expect(mocks.saveActors).not.toHaveBeenCalled();
    expect(
      screen.getByText(/no pueden tener el mismo número de documento/),
    ).toBeInTheDocument();
  });

  // HU #10688 — en persona jurídica el sujeto de identidad es el representante legal: sin su correo
  // no hay a quién enviarle la validación, así que el paso no puede guardarse.
  it('persona jurídica: el correo del representante legal es obligatorio', async () => {
    const user = await renderMatricula();

    // Se escribe el NIT ANTES de cambiar a jurídica: mientras el actor es natural el rótulo
    // «Número de documento» todavía es único (el representante legal aún no está en pantalla).
    await user.type(screen.getByLabelText('Número de documento'), '900555666');
    await user.click(screen.getByRole('button', { name: 'Persona jurídica' }));
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));
    await screen.findByText(/Empresa encontrada en RUES/i);

    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() =>
      expect(screen.getByText('Correo del representante legal requerido')).toBeInTheDocument(),
    );
    expect(mocks.saveActors).not.toHaveBeenCalled();
  });
});

/**
 * PARIDAD ENTRE MODALIDADES — tipo de documento del actor.
 *
 * El layout partido (matrícula, un solo comprador) no ofrecía este selector: el actor nacía con
 * `tipoDocumento: 'CC'` y la consulta salía al RUNT con ese valor. Un comprador con cédula de
 * extranjería o pasaporte se consultaba como si tuviera cédula de ciudadanía, y el proveedor no
 * tenía forma de encontrarlo.
 *
 * No era una diferencia de diseño entre asistentes: era un dato que en matrícula no se podía
 * capturar. El mismo trámite debe pedir los mismos datos con independencia de la modalidad.
 */
describe('Paridad de datos entre modalidades — tipo de documento', () => {
  it('matrícula ofrece el catálogo de tipos, sin NIT (ese lo fija persona jurídica)', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const selector = await screen.findByLabelText('Tipo de documento');
    expect(selector).toBeInTheDocument();

    const opciones = Array.from((selector as HTMLSelectElement).options).map((o) => o.textContent);
    expect(opciones).toEqual([
      'Cédula de ciudadanía (CC)',
      'Cédula de extranjería (CE)',
      'Pasaporte (PAS)',
      'Tarjeta de identidad (TI)',
    ]);
  });

  it('lo elegido es lo que viaja a la consulta, no un CC por defecto', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.selectOptions(await screen.findByLabelText('Tipo de documento'), 'CE');
    await user.type(
      screen.getByPlaceholderText(/Número de documento del comprador/),
      '987654',
    );
    await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));

    await waitFor(() =>
      expect(mocks.runtPersonLookup).toHaveBeenCalledWith(
        INSTANCE,
        expect.objectContaining({ documentType: 'CE', documentNumber: '987654' }),
      ),
    );
  });

  it('en persona jurídica el selector desaparece: el documento es siempre NIT', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await user.click(await screen.findByRole('button', { name: 'Persona jurídica' }));
    // Se busca por id y no por rótulo: en jurídica aparece el bloque del representante legal, que
    // trae su propio "Tipo de documento". El que debe desaparecer es el del actor.
    expect(document.getElementById('comprador-tipoDoc')).toBeNull();
  });
});
