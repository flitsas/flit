import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// Novedad 28 — el botón "Consultar RUNT" del representante legal nace deshabilitado cuando el
// actor jurídico viene precargado desde el directorio (HU #10906), y su consulta es obligatoria
// si cambia el documento del representante. Ver
// `.claude/state/pending-work-items/novedad-28-runt-rl-precargado.md` (AC1-AC5).

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

// Uso de ejemplo: mocks.lookupLegalRepresentativeByNit.mockResolvedValue(MATCH) precarga la
// compañía + representante legal (HU #10906), lo que deja `isPreloaded === true` en el bloque de RL.
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

const RUNT_FOUND = {
  found: true,
  fullName: 'Carlos Ramirez Actualizado',
  firstName: 'Carlos',
  lastName: 'Ramirez Actualizado',
  documentType: 'CC',
  documentNumber: '79999999',
  source: 'RUNT',
  mode: 'mock',
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.patchFieldValues.mockResolvedValue(undefined);
});

/** Completa los campos obligatorios del actor (comprador) para llegar al gate de guardado. */
async function fillRequiredActorFields(user: ReturnType<typeof userEvent.setup>) {
  await user.type(document.getElementById('comprador-email') as HTMLInputElement, 'contacto@valle.co');
  await user.type(document.getElementById('comprador-telefono') as HTMLInputElement, '3001234567');
  await user.type(screen.getByLabelText(/^Ciudad/), 'Bogota');
  await user.type(screen.getByLabelText(/^Dirección/), 'Calle 1 # 2-3');
}

/** Precarga un comprador jurídico con match del directorio (HU #10906) — deja `isPreloaded=true`. */
async function renderPreloadedJuridicalBuyer() {
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(MATCH);
  const user = userEvent.setup();
  render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
  await user.click(await screen.findByRole('button', { name: 'Persona jurídica' }));
  await user.type(screen.getByPlaceholderText(/Número de documento del comprador/), '900555666');
  await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));
  await screen.findByText('Precargado desde el directorio de la compañía');
  return user;
}

describe('ActorsForm — novedad 28: gate de RUNT del representante legal precargado', () => {
  it(
    'AC1: el botón nace deshabilitado cuando el actor viene precargado del directorio',
    async () => {
      await renderPreloadedJuridicalBuyer();

      const boton = screen.getByRole('button', { name: 'Actualizar RUNT' });
      expect(boton).toBeDisabled();
      // AC5 — el motivo no depende solo de la opacidad: hay un texto accesible asociado.
      expect(boton).toHaveAttribute('aria-describedby', '0-rl-runt-preloaded-hint');
      expect(
        screen.getByText(/Cambia el tipo o número de documento del/i),
      ).toBeInTheDocument();
    },
    10000,
  );

  it(
    'AC2: al cambiar el número de documento del RL, el botón queda habilitado',
    async () => {
      const user = await renderPreloadedJuridicalBuyer();

      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '79999999');

      const boton = await screen.findByRole('button', { name: 'Consultar RUNT' });
      expect(boton).not.toBeDisabled();
    },
    10000,
  );

  it(
    'AC3: con el documento del RL modificado y sin consulta RUNT exitosa, el guardado queda bloqueado',
    async () => {
      const user = await renderPreloadedJuridicalBuyer();
      await fillRequiredActorFields(user);

      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '79999999');

      await user.click(screen.getByRole('button', { name: /Guardar actores/ }));
      expect(mocks.saveActors).not.toHaveBeenCalled();

      // Al ejecutar la consulta RUNT del representante y resolver "found", el gate se levanta.
      mocks.runtPersonLookup.mockResolvedValue(RUNT_FOUND);
      await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
      await screen.findByText('Representante encontrado en RUNT.');

      await user.click(screen.getByRole('button', { name: /Guardar actores/ }));
      await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    },
    10000,
  );

  it(
    'AC4: actor jurídico de captura manual (sin match del directorio) no exige RUNT del RL',
    async () => {
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

      const user = userEvent.setup();
      render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
      await user.click(await screen.findByRole('button', { name: 'Persona jurídica' }));
      await user.type(screen.getByPlaceholderText(/Número de documento del comprador/), '900999888');
      await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));
      await screen.findByText(/Empresa encontrada en RUES/i);

      // Sin actor precargado del directorio (`isPreloaded=false`): el botón de RUNT del RL sigue
      // habilitado desde el primer render, como hoy (una vez tiene número de documento que consultar).
      await user.type(document.getElementById('0-rl-numeroDoc') as HTMLInputElement, '1020304050');
      expect(screen.getByRole('button', { name: 'Consultar RUNT' })).not.toBeDisabled();

      await user.type(document.getElementById('0-rl-nombre') as HTMLInputElement, 'Rep Legal');
      await user.type(document.getElementById('0-rl-email') as HTMLInputElement, 'rl@example.com');
      await fillRequiredActorFields(user);

      // Sin consultar el RUNT del RL, el guardado NO se bloquea (comportamiento vigente, AC4).
      await user.click(screen.getByRole('button', { name: /Guardar actores/ }));
      await waitFor(() => expect(mocks.saveActors).toHaveBeenCalledTimes(1));
    },
    10000,
  );
});
