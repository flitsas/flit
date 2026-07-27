import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
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
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';
import type { LegalRepresentativeLookupResult } from '@/lib/api/types/procedure-runtime';

const INSTANCE = 'inst-1';

const MATCH: LegalRepresentativeLookupResult = {
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
  ],
};

// HU #10937 — la misma compañía con DOS representantes: el gestor elige cuál firma. Cada uno trae sus
// propias banderas de firma/identidad (por su documento).
const MATCH_MULTI: LegalRepresentativeLookupResult = {
  company: {
    nit: '900555666',
    razonSocial: 'Comercializadora del Valle SAS',
    email: 'contacto@valle.co',
    address: null,
    city: null,
    phone: null,
  },
  representante: MATCH.representante,
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

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.patchFieldValues.mockResolvedValue(undefined);
});

/** Prepara un comprador jurídico (persona jurídica) con el NIT escrito, listo para consultar. */
async function renderJuridicalBuyerWithNit(nit: string) {
  const user = userEvent.setup();
  render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
  await user.click(await screen.findByRole('button', { name: 'Persona jurídica' }));
  // El input principal de identificación (no el del representante legal) se distingue por placeholder.
  await user.type(screen.getByPlaceholderText(/Número de documento del comprador/), nit);
  return user;
}

describe('ActorsForm — precarga por NIT desde el directorio del tenant (HU #10906)', () => {
  it('con match del tenant: precarga y NO consulta RUES/RUNT', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue(MATCH);

    const user = await renderJuridicalBuyerWithNit('900555666');
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));

    // Se consultó el directorio del tenant por NIT…
    await waitFor(() =>
      expect(mocks.lookupLegalRepresentativeByNit).toHaveBeenCalledWith('900555666'),
    );
    // …y NO se disparó RUES ni RUNT (cortocircuito R3).
    expect(mocks.ruesPersonLookup).not.toHaveBeenCalled();
    expect(mocks.runtPersonLookup).not.toHaveBeenCalled();

    // Precarga visible: card del directorio + razón social autopoblada + representante.
    expect(
      await screen.findByText('Precargado desde el directorio de la compañía'),
    ).toBeInTheDocument();
    expect(screen.getByDisplayValue('Comercializadora del Valle SAS')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Carlos Ramírez Núñez')).toBeInTheDocument();
    expect(screen.getByDisplayValue('carlos@valle.co')).toBeInTheDocument();
    // Badges de firma/identidad vigentes.
    expect(screen.getByText('Firma vigente')).toBeInTheDocument();
    expect(screen.getByText('Sin identidad vigente')).toBeInTheDocument();
  });

  it('con un solo representante: NO muestra selector (auto-seleccionado)', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue(MATCH);

    const user = await renderJuridicalBuyerWithNit('900555666');
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));

    expect(
      await screen.findByText('Precargado desde el directorio de la compañía'),
    ).toBeInTheDocument();
    // Con un único representante no se ofrece selección.
    expect(screen.queryByLabelText('Representante legal que firma')).toBeNull();
    // El representante único quedó precargado en la sección de representante legal.
    expect(screen.getByDisplayValue('Carlos Ramírez Núñez')).toBeInTheDocument();
  });

  it('con varios representantes: muestra selector y precarga el primero; al elegir otro cambia datos y badges', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue(MATCH_MULTI);

    const user = await renderJuridicalBuyerWithNit('900555666');
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));

    // Selector presente (dos representantes) y primero precargado (Carlos) con SUS banderas.
    const select = await screen.findByLabelText('Representante legal que firma');
    expect(select).toBeInTheDocument();
    expect(screen.getByDisplayValue('Carlos Ramírez Núñez')).toBeInTheDocument();
    expect(screen.getByText('Firma vigente')).toBeInTheDocument();
    expect(screen.getByText('Sin identidad vigente')).toBeInTheDocument();

    // Elegir el segundo representante (Ana): precarga sus datos y sus banderas (firma no, identidad sí).
    await user.selectOptions(select, '1');
    expect(screen.getByDisplayValue('Ana Gómez')).toBeInTheDocument();
    expect(screen.getByDisplayValue('ana@valle.co')).toBeInTheDocument();
    expect(screen.getByText('Sin firma vigente')).toBeInTheDocument();
    expect(screen.getByText('Identidad vigente')).toBeInTheDocument();
  });

  it('con varios representantes: al guardar, el actor persiste el representante ELEGIDO (su documento)', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue(MATCH_MULTI);
    mocks.saveActors.mockResolvedValue([]);

    const user = await renderJuridicalBuyerWithNit('900555666');
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));
    const select = await screen.findByLabelText('Representante legal que firma');
    await user.selectOptions(select, '1'); // elige a Ana

    // Completa el correo del actor (obligatorio) — el input del actor se distingue por su id de los
    // demás campos "Correo electrónico" (el del representante legal ya viene precargado) — y guarda.
    const actorEmail = document.getElementById('comprador-email') as HTMLInputElement;
    await user.type(actorEmail, 'contacto@valle.co');
    await user.click(screen.getByRole('button', { name: /Guardar actores/ }));

    await waitFor(() => expect(mocks.saveActors).toHaveBeenCalled());
    // saveActors(instanceId, actors, tenantId) → los actores son el segundo argumento.
    const saved = mocks.saveActors.mock.calls.at(-1)?.[1];
    expect(saved[0].representanteLegal.numeroDocumento).toBe('52988777');
    expect(saved[0].representanteLegal.nombreCompleto).toBe('Ana Gómez');
  });

  it('sin match (404 → null): cae al flujo RUES normal', async () => {
    mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
    mocks.ruesPersonLookup.mockResolvedValue({
      found: true,
      razonSocial: 'Empresa Externa SAS',
      estado: 'ACTIVA',
      documentNumber: '900999888',
      matriculaMercantil: null,
      camaraComercio: null,
      documentType: 'NIT',
      source: 'RUES',
      mode: 'mock',
    });

    const user = await renderJuridicalBuyerWithNit('900999888');
    await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));

    await waitFor(() =>
      expect(mocks.lookupLegalRepresentativeByNit).toHaveBeenCalledWith('900999888'),
    );
    // Sin match ⇒ SÍ consulta RUES (fallback) y no muestra la card de precarga.
    await waitFor(() =>
      expect(mocks.ruesPersonLookup).toHaveBeenCalledWith(INSTANCE, {
        documentNumber: '900999888',
      }),
    );
    expect(await screen.findByText('Empresa encontrada en RUES')).toBeInTheDocument();
    expect(
      screen.queryByText('Precargado desde el directorio de la compañía'),
    ).toBeNull();
  });
});
