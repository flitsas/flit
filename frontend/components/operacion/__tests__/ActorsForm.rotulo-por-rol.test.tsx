import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';

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

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mocks.getActors.mockResolvedValue([]);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
});

/**
 * `TRASPASO_UNILATERAL` persiste al locatario del leasing con el rol `comprador` —no hay rol propio
 * para una única parte entrante, y cambiarlo movería el gate, la biometría y el FUR—. Lo que estaba
 * mal no era el rol sino el rótulo: «Comprador» describe un contrato que en un leasing no existe.
 * El catálogo ya nombra ese paso «Locatario»; estas pruebas fijan que ese nombre llegue a la tarjeta.
 */
describe('ActorsForm — rótulo del catálogo por rol', () => {
  it('el rótulo del catálogo manda sobre el nombre del rol', async () => {
    render(
      <ActorsForm
        instanceId="inst-1"
        modalidad="matricula_inicial"
        roles={['comprador']}
        rotuloPorRol={{ comprador: 'Locatario' }}
      />,
    );

    expect(await screen.findByText(/Datos del locatario/i)).toBeInTheDocument();
    expect(screen.queryByText(/Datos del comprador/i)).toBeNull();
  });

  it('sin rótulo del catálogo conserva el nombre del rol', async () => {
    render(
      <ActorsForm instanceId="inst-1" modalidad="matricula_inicial" roles={['comprador']} />,
    );

    expect(await screen.findByText(/Datos del comprador/i)).toBeInTheDocument();
  });

  // El unilateral con el formulario del propietario revelado: dos tarjetas, y el nombre del paso
  // («Locatario») describe SOLO a la parte entrante. El propietario conserva el suyo.
  it('con dos tarjetas solo renombra el rol al que apunta', async () => {
    render(
      <ActorsForm
        instanceId="inst-1"
        modalidad="traspaso"
        roles={['vendedor', 'comprador']}
        rotuloPorRol={{ comprador: 'Locatario' }}
      />,
    );

    expect(await screen.findByText('Vendedor')).toBeInTheDocument();
    expect(screen.getByText('Locatario')).toBeInTheDocument();
    expect(screen.queryByText('Comprador')).toBeNull();
  });
});
