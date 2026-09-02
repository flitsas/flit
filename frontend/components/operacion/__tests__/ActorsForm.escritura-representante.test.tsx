import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getActors: vi.fn(),
  saveActors: vi.fn(),
  runtPersonLookup: vi.fn(),
  ruesPersonLookup: vi.fn(),
  getInstance: vi.fn(),
  patchFieldValues: vi.fn(),
  lookupLegalRepresentativeByNit: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  analyzeDocument: vi.fn(),
  persistOcrFields: vi.fn(),
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
    getChecklist: mocks.getChecklist,
    getAttachments: mocks.getAttachments,
    uploadAttachment: mocks.uploadAttachment,
    deleteAttachment: mocks.deleteAttachment,
    analyzeDocument: mocks.analyzeDocument,
    persistOcrFields: mocks.persistOcrFields,
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';
import { escrituraRepresentanteTipo } from '@/components/operacion/EscrituraRepresentanteUpload';
import type { LegalRepresentativeLookupResult } from '@/lib/api/types/procedure-runtime';

const INSTANCE = 'inst-escritura-rl';
const NIT = '900555666';
/** Cédula que NO figura entre los representantes del directorio. */
const CEDULA_FUERA = '79999999';

const DIRECTORIO: LegalRepresentativeLookupResult = {
  company: {
    nit: NIT,
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
  documentNumber: CEDULA_FUERA,
  source: 'RUNT',
  mode: 'mock',
};

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.patchFieldValues.mockResolvedValue(undefined);
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(DIRECTORIO);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: [], completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.uploadAttachment.mockResolvedValue(undefined);
  mocks.ruesPersonLookup.mockResolvedValue({
    found: true,
    razonSocial: 'Razón social RUES SAS',
    estado: 'ACTIVA',
    documentNumber: NIT,
    matriculaMercantil: null,
    camaraComercio: null,
    documentType: 'NIT',
    source: 'RUES',
    mode: 'live',
  });
});

const AVISO = 'Este representante no está registrado en la compañía';

async function renderConDirectorio(onGate?: (ok: boolean) => void) {
  const user = userEvent.setup({ delay: null });
  render(
    <ActorsForm
      instanceId={INSTANCE}
      modalidad="matricula_inicial"
      onEscrituraRepresentanteGateChange={onGate}
    />,
  );
  await user.selectOptions(await screen.findByLabelText('Tipo de documento'), 'NIT');
  await user.type(screen.getByPlaceholderText(/Número de documento del comprador/), NIT);
  await user.click(screen.getByRole('button', { name: 'Consultar RUES' }));
  // La tarjeta del directorio ya no se anuncia con un título propio: se reconoce por sus datos.
  await screen.findByText('Representante:');
  return user;
}

async function cambiarAlRepresentanteDeFuera(user: ReturnType<typeof userEvent.setup>) {
  const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
  await user.clear(numeroDocRl);
  await user.type(numeroDocRl, CEDULA_FUERA);
  mocks.runtPersonLookup.mockResolvedValue(RUNT_FOUND);
  await user.click(screen.getByRole('button', { name: 'Consultar RUNT' }));
  await screen.findByText(/Vas a consultar otro documento en/i);
  await user.click(screen.getByRole('button', { name: 'Continuar' }));
  await screen.findByText('Representante encontrado en RUNT.');
}

describe('ActorsForm — escritura del representante fuera del directorio', () => {
  it(
    'con el representante precargado del directorio no se pide escritura y el gate queda abierto',
    async () => {
      const onGate = vi.fn();
      await renderConDirectorio(onGate);

      await waitFor(() => expect(mocks.lookupLegalRepresentativeByNit).toHaveBeenCalled());
      expect(screen.queryByText(AVISO)).toBeNull();
      expect(onGate).not.toHaveBeenCalledWith(false);
    },
    25000,
  );

  it(
    'cambiar a otro representante DEL directorio tampoco pide escritura',
    async () => {
      const onGate = vi.fn();
      const user = await renderConDirectorio(onGate);

      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '1038409485');

      await waitFor(() =>
        expect((document.getElementById('0-rl-email') as HTMLInputElement).value).toBe(
          'juan@valle.co',
        ),
      );
      expect(screen.queryByText(AVISO)).toBeNull();
      expect(onGate).not.toHaveBeenCalledWith(false);
    },
    25000,
  );

  it(
    'cambiar a un representante que NO está en el directorio pide la escritura y cierra el gate',
    async () => {
      const onGate = vi.fn();
      const user = await renderConDirectorio(onGate);
      await cambiarAlRepresentanteDeFuera(user);

      await screen.findByText(AVISO);
      await waitFor(() => expect(onGate).toHaveBeenCalledWith(false));
    },
    25000,
  );

  it(
    'volver al representante original reabre el gate sin cargar nada',
    async () => {
      const onGate = vi.fn();
      const user = await renderConDirectorio(onGate);
      await cambiarAlRepresentanteDeFuera(user);
      await waitFor(() => expect(onGate).toHaveBeenCalledWith(false));

      const numeroDocRl = document.getElementById('0-rl-numeroDoc') as HTMLInputElement;
      await user.clear(numeroDocRl);
      await user.type(numeroDocRl, '79123456');

      await waitFor(() => expect(screen.queryByText(AVISO)).toBeNull());
      await waitFor(() => expect(onGate).toHaveBeenLastCalledWith(true));
    },
    25000,
  );

  it(
    'al adjuntar la escritura del representante el gate se vuelve a abrir',
    async () => {
      const onGate = vi.fn();
      const user = await renderConDirectorio(onGate);
      await cambiarAlRepresentanteDeFuera(user);
      await screen.findByText(AVISO);
      await waitFor(() => expect(onGate).toHaveBeenCalledWith(false));

      // Tras subirlo, el hijo relee adjuntos: esto es lo que devuelve ese refresh.
      const tipo = escrituraRepresentanteTipo('comprador');
      mocks.getAttachments.mockResolvedValue([
        {
          id: 'att-1',
          tipo,
          filename: 'escritura.pdf',
          mimetype: 'application/pdf',
          sizeBytes: 1024,
          source: 'user',
          createdAt: new Date().toISOString(),
        },
      ]);

      const zona = document.querySelector(
        '[aria-label="Carga de la escritura del representante legal"]',
      );
      const input = zona?.querySelector('input[type="file"]') as HTMLInputElement;
      expect(input).toBeTruthy();
      await user.upload(input, new File(['x'], 'escritura.pdf', { type: 'application/pdf' }));

      await waitFor(() => expect(mocks.uploadAttachment).toHaveBeenCalled());
      expect(mocks.uploadAttachment.mock.calls[0][1]).toBe(tipo);
      await waitFor(() => expect(onGate).toHaveBeenLastCalledWith(true));
    },
    25000,
  );
});

describe('escrituraRepresentanteTipo', () => {
  it('usa un código por rol para que dos partes jurídicas no se pisen el adjunto', () => {
    expect(escrituraRepresentanteTipo('comprador')).toBe('escritura_representante');
    expect(escrituraRepresentanteTipo('vendedor')).toBe('escritura_representante_vendedor');
    expect(escrituraRepresentanteTipo('locatario')).toBe('escritura_representante_locatario');
  });
});
