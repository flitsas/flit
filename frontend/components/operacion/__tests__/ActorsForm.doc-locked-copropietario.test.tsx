// HU #14655 — documento editable en vendedores copropietarios (traspaso + autoConsultRunt).
// Antes: `docLocked` aplicaba a todo actor con `rol === rolDelPropietario` y documento no vacío;
// al digitar el primer dígito del vendedor 2 el input pasaba a solo lectura.
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  getCamaraComercioRequirements: vi.fn(() => Promise.resolve([])),
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
    getCamaraComercioRequirements: mocks.getCamaraComercioRequirements,
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

const INSTANCE = 'inst-hu-14655';

const COMPLETE_VENDEDOR = {
  rol: 'vendedor' as const,
  tipoDocumento: 'CC' as const,
  numeroDocumento: '1090123456',
  nombreCompleto: 'Propietario Inscrito',
  email: 'vendedor1@example.com',
  ciudad: 'Bogota',
  direccion: 'Calle 1 # 2-3',
  telefono: '3001234567',
  personType: 'natural' as const,
  ordinal: 1,
};

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue({
    fieldValues: [
      { fieldKey: 'owner_document_type', valueText: 'CC' },
      { fieldKey: 'owner_document_number', valueText: '1090123456' },
    ],
  });
  mocks.patchFieldValues.mockResolvedValue(undefined);
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.actorContactLookup.mockResolvedValue({ found: false });
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.runtPersonLookup.mockResolvedValue({
    found: true,
    fullName: 'Propietario Inscrito',
    firstName: 'Propietario',
    lastName: 'Inscrito',
    documentType: 'CC',
    documentNumber: '1090123456',
    source: 'RUNT',
    mode: 'mock',
  });
});

describe('ActorsForm — HU #14655 documento en copropietarios del traspaso', () => {
  it('AC1 — el vendedor ordinal 2 permanece editable al digitar el documento (autoConsultRunt)', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([COMPLETE_VENDEDOR]);
    // Sin auto-consulta exitosa al montar: dejamos el input visible para ejercitar docLocked.
    mocks.runtPersonLookup.mockResolvedValue({ found: false });

    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="traspaso"
        autoConsultRunt
        seedDocumentoFromOwner
        embeddedInWizard
      />,
    );

    const vendedorCard = await screen.findByRole('group', { name: /Vendedor/i });
    await waitFor(() =>
      expect(within(vendedorCard).getByLabelText(/Número de documento/)).toHaveValue('1090123456'),
    );

    // Completar contacto del vendedor 1 para habilitar Agregar (secuencia HU #14192).
    await user.clear(within(vendedorCard).getByLabelText(/Correo electr/i));
    await user.type(within(vendedorCard).getByLabelText(/Correo electr/i), 'vendedor1@example.com');
    await user.clear(within(vendedorCard).getByLabelText(/^Ciudad/i));
    await user.type(within(vendedorCard).getByLabelText(/^Ciudad/i), 'Bogota');
    await user.clear(within(vendedorCard).getByLabelText(/Direcci/i));
    await user.type(within(vendedorCard).getByLabelText(/Direcci/i), 'Calle 1 # 2-3');
    await user.clear(within(vendedorCard).getByLabelText(/Tel[eé]fono/i));
    await user.type(within(vendedorCard).getByLabelText(/Tel[eé]fono/i), '3001234567');

    const addBtn = within(vendedorCard).getByRole('button', {
      name: 'Agregar propietario de vendedor',
    });
    await waitFor(() => expect(addBtn).toBeEnabled());
    await user.click(addBtn);
    await within(vendedorCard).findByRole('tab', { name: /Vendedor 2/ });

    const doc2 = within(vendedorCard).getByLabelText(/Número de documento/) as HTMLInputElement;
    expect(doc2).toHaveValue('');
    expect(doc2).not.toHaveAttribute('readonly');

    await user.type(doc2, '9876543210');
    expect(doc2).toHaveValue('9876543210');
    expect(doc2).not.toHaveAttribute('readonly');
    expect(
      within(vendedorCard).getByRole('button', { name: /Consultar RUNT/i }),
    ).toBeInTheDocument();
  });

  it('AC2 — el vendedor ordinal 1 mantiene el documento en solo lectura con autoConsultRunt', async () => {
    mocks.getActors.mockResolvedValue([COMPLETE_VENDEDOR]);
    mocks.runtPersonLookup.mockResolvedValue({ found: false });

    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="traspaso"
        autoConsultRunt
        seedDocumentoFromOwner
        embeddedInWizard
      />,
    );

    const vendedorCard = await screen.findByRole('group', { name: /Vendedor/i });
    const doc1 = await waitFor(() => {
      const el = within(vendedorCard).getByLabelText(/Número de documento/) as HTMLInputElement;
      expect(el).toHaveValue('1090123456');
      return el;
    });
    expect(doc1).toHaveAttribute('readonly');
    expect(
      within(vendedorCard).queryByRole('button', { name: /Consultar RUNT/i }),
    ).not.toBeInTheDocument();
  });

  it('AC4 — el comprador sigue pudiendo digitar documento con autoConsultRunt en el vendedor', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([COMPLETE_VENDEDOR]);
    mocks.runtPersonLookup.mockResolvedValue({ found: false });

    render(
      <ActorsForm
        instanceId={INSTANCE}
        modalidad="traspaso"
        autoConsultRunt
        seedDocumentoFromOwner
        embeddedInWizard
      />,
    );

    const compradorCard = await screen.findByRole('group', { name: /Comprador/i });
    const docComprador = within(compradorCard).getByLabelText(/Número de documento/) as HTMLInputElement;
    expect(docComprador).not.toHaveAttribute('readonly');
    await user.type(docComprador, '55555555');
    expect(docComprador).toHaveValue('55555555');
    expect(docComprador).not.toHaveAttribute('readonly');
    expect(
      within(compradorCard).getByRole('button', { name: /Consultar RUNT/i }),
    ).toBeInTheDocument();
  });
});
