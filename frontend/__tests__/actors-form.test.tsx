import { createRef } from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  getInstance: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getActors: mocks.getActors,
    saveActors: mocks.saveActors,
    runtPersonLookup: mocks.runtPersonLookup,
    getInstance: mocks.getInstance,
  },
}));

import {
  ActorsForm,
  validateActors,
  type ActorsFormHandle,
} from '@/components/operacion/ActorsForm';
import type { ProcedureActor } from '@/lib/api/types/procedure-runtime';

const INSTANCE = 'inst-1';

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
});

describe('ActorsForm — layout split (un comprador)', () => {
  it('matrícula inicial muestra las 2 secciones (Identificación + Datos de contacto)', async () => {
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    expect(
      await screen.findByText(/Identificación · Comprador/),
    ).toBeInTheDocument();
    expect(screen.getByText('Datos de contacto')).toBeInTheDocument();
    // Sección de identificación: documento + Consultar RUNT.
    expect(screen.getByLabelText('Número de documento')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Consultar RUNT' }),
    ).toBeInTheDocument();
    // Sección de contacto: ciudad y dirección (nuevos).
    expect(screen.getByLabelText('Ciudad')).toBeInTheDocument();
    expect(screen.getByLabelText('Dirección')).toBeInTheDocument();
    // No es el layout de fieldsets ni renderiza vendedor.
    expect(screen.queryByRole('group', { name: 'Vendedor' })).toBeNull();
  });

  it('ciudad autocomplete: filtra y selecciona (≥2 chars)', async () => {
    const user = userEvent.setup();
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const ciudad = await screen.findByLabelText('Ciudad');
    await user.type(ciudad, 'med');
    // Sugerencia filtrada del catálogo.
    const opcion = await screen.findByRole('button', { name: 'Medellin' });
    await user.click(opcion);

    expect(ciudad).toHaveValue('Medellin');
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

describe('ActorsForm — save() vía ref (embebido en wizard)', () => {
  it('expone save() que valida y persiste; oculta el botón propio', async () => {
    mocks.getActors.mockResolvedValue([
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '123',
        nombreCompleto: 'Juan Perez',
        email: 'juan@example.com',
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
      numeroDocumento: '123',
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
    await screen.findByText(/Identificación · Comprador/);

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
});

describe('ActorsForm — prefill documento del propietario (paso vendedor)', () => {
  const renderVendedor = () =>
    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="traspaso"
        roles={['vendedor']}
        layout="split"
        embeddedInWizard
        seedDocumentoFromOwner
      />,
    );

  it('siembra el documento del vendedor desde owner_document_* (editable)', async () => {
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
    // Editable: no deshabilitado ni readonly.
    expect(numero).not.toBeDisabled();
    expect(numero).not.toHaveAttribute('readonly');
  });

  it('no pisa el documento del vendedor ya persistido', async () => {
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
  });

  it('sin owner_document_number no siembra nada', async () => {
    mocks.getInstance.mockResolvedValue({
      fieldValues: [{ fieldKey: 'plate', valueText: 'ABC123' }],
    });

    renderVendedor();

    const numero = await screen.findByLabelText('Número de documento');
    // Da tiempo a que resuelva el fetch del seed; debe quedar vacío.
    await waitFor(() => expect(mocks.getInstance).toHaveBeenCalled());
    expect(numero).toHaveValue('');
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
      />,
    );

    await waitFor(() =>
      expect(screen.getByLabelText('Número de documento')).toHaveValue('1090123456'),
    );
    // Completa los requeridos del vendedor para que el guardado sea válido.
    await user.type(screen.getByLabelText(/Nombre completo/), 'Ana Vendedora');
    await user.type(screen.getByLabelText(/Correo electrónico/), 'ana@example.com');

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
      />,
    );

    await waitFor(() =>
      expect(screen.getByLabelText('Número de documento')).toHaveValue('1090123456'),
    );
    await user.type(screen.getByLabelText(/Nombre completo/), 'Ana Vendedora');
    await user.type(screen.getByLabelText(/Correo electrónico/), 'ana@example.com');

    let ok: boolean | undefined;
    await act(async () => {
      ok = await ref.current!.save();
    });

    expect(ok).toBe(true);
    const [, actors] = mocks.saveActors.mock.calls[0];
    expect(actors[0].tipoDocumento).toBe('CC');
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
