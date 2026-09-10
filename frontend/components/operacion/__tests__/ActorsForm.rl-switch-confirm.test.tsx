import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

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

const INSTANCE = 'inst-rl-switch';

const MATCH_TWO: LegalRepresentativeLookupResult = {
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
      documento: '1038409485',
      nombres: 'Juan',
      primerApellido: 'Motoya',
      segundoApellido: '',
      email: 'juan@valle.co',
      telefono: '3105556677',
      firmaVigente: true,
      identidadVigente: true,
    },
  ],
};

const RUNT_FOUND = {
  found: true,
  fullName: 'Ana Runt Consultada',
  firstName: 'Ana',
  lastName: 'Runt Consultada',
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
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(MATCH_TWO);
  mocks.ruesPersonLookup.mockResolvedValue({
    found: true,
    razonSocial: 'Razón social RUES SAS',
    estado: 'ACTIVA',
    documentNumber: '900555666',
    matriculaMercantil: null,
    camaraComercio: null,
    documentType: 'NIT',
    source: 'RUES',
    mode: 'live',
  });
});

async function renderPreloaded() {
  const user = userEvent.setup({ delay: null });
  render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
  await user.selectOptions(await screen.findByLabelText('Tipo de documento'), 'NIT');
  await user.type(screen.getByPlaceholderText(/Número de documento del comprador/), '900555666');
  await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));
  await screen.findByText('Empresa encontrada en RUES');
  // La tarjeta del directorio ya no se anuncia con un título propio: se reconoce por sus datos.
  await screen.findByText('Representante:');
  return user;
}

describe('ActorsForm — confirmación al cambiar el documento del RL', () => {
  it(
    'cancelar el modal no consulta RUNT y conserva los datos del RL precargado',
    async () => {
      const user = await renderPreloaded();
      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '79999999');

      await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
      await screen.findByRole('heading', { name: 'Cambiar representante legal' });
      await user.click(screen.getByRole('button', { name: 'Cancelar' }));

      expect(mocks.runtPersonLookup).not.toHaveBeenCalled();
      expect(screen.queryByRole('heading', { name: 'Cambiar representante legal' })).toBeNull();
      expect((document.getElementById('0-rl-email') as HTMLInputElement).value).toBe(
        'carlos@valle.co',
      );
    },
    25000,
  );

  it(
    'cédula que no está en el directorio confirma consulta RUNT, limpia contacto y cambia el banner',
    async () => {
      const user = await renderPreloaded();
      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '79999999');

      mocks.runtPersonLookup.mockResolvedValue(RUNT_FOUND);
      await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
      await screen.findByText(/Vas a consultar otro documento en/i);
      await user.click(screen.getByRole('button', { name: 'Continuar' }));

      await screen.findByText('Representante encontrado en RUNT.');
      expect(mocks.runtPersonLookup).toHaveBeenCalledWith(INSTANCE, {
        documentType: 'CC',
        documentNumber: '79999999',
      });
      // El aviso de abandono ya no lleva título: lo dice el propio párrafo de la tarjeta.
      expect(
        screen.getByText(/Consultaste otro representante no registrado/),
      ).toBeInTheDocument();
      expect((document.getElementById('0-rl-email') as HTMLInputElement).value).toBe('');
      expect((document.getElementById('0-rl-telefono') as HTMLInputElement).value).toBe('');
      expect((document.getElementById('0-rl-nombre') as HTMLInputElement).value).toBe(
        'Ana Runt Consultada',
      );
    },
    25000,
  );

  it(
    'al escribir la cédula de otro RL del directorio apalanca nombre, correo y teléfono sin consultar RUNT',
    async () => {
      const user = await renderPreloaded();
      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '1038409485');

      expect(mocks.runtPersonLookup).not.toHaveBeenCalled();
      expect((document.getElementById('0-rl-nombre') as HTMLInputElement).value).toMatch(/Juan/);
      expect((document.getElementById('0-rl-email') as HTMLInputElement).value).toBe('juan@valle.co');
      expect((document.getElementById('0-rl-telefono') as HTMLInputElement).value).toBe('3105556677');
      expect(screen.getByLabelText('Representante legal que firma')).toHaveValue('1');
    },
    25000,
  );

  it(
    'cédula de otro RL activo precarga el directorio y no llama RUNT',
    async () => {
      const user = await renderPreloaded();
      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '1038409485');

      expect(mocks.runtPersonLookup).not.toHaveBeenCalled();
      expect(mocks.lookupLegalRepresentativeByNit).toHaveBeenCalledWith('900555666');
      expect(screen.getByText('Representante:')).toBeInTheDocument();
      expect((document.getElementById('0-rl-email') as HTMLInputElement).value).toBe('juan@valle.co');
      expect((document.getElementById('0-rl-nombre') as HTMLInputElement).value).toMatch(/Juan/);
      expect(screen.getByRole('button', { name: 'Actualizar RUNT' })).toBeDisabled();
    },
    25000,
  );

  it(
    'cédula del directorio con ceros a la izquierda precarga nombre y correo del RL coincidente',
    async () => {
      mocks.lookupLegalRepresentativeByNit.mockResolvedValue({
        ...MATCH_TWO,
        representantes: [
          MATCH_TWO.representantes[0],
          {
            ...MATCH_TWO.representantes[1],
            documento: '003265891',
          },
        ],
      });
      const user = await renderPreloaded();
      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '3265891');

      expect(mocks.runtPersonLookup).not.toHaveBeenCalled();
      expect((document.getElementById('0-rl-email') as HTMLInputElement).value).toBe('juan@valle.co');
      expect((document.getElementById('0-rl-nombre') as HTMLInputElement).value).toMatch(/Juan/);
    },
    25000,
  );
});
