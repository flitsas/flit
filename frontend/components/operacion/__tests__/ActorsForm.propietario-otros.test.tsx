import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';

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

const INSTANCE = 'inst-otros-1';

/** Documento del propietario capturado en el paso 1 de la consulta por placa. */
const OWNER_FIELD_VALUES = {
  fieldValues: [
    { fieldKey: 'plate', valueText: 'ABC123', valueJson: null, formFieldId: '', source: 'user' },
    { fieldKey: 'owner_document_type', valueText: 'CC', valueJson: null, formFieldId: '', source: 'user' },
    { fieldKey: 'owner_document_number', valueText: '79123456', valueJson: null, formFieldId: '', source: 'user' },
  ],
};

const RUNT_FOUND = {
  found: true,
  fullName: 'Marta Lucía Peñaloza',
  firstName: 'Marta Lucía',
  lastName: 'Peñaloza',
  documentType: 'CC',
  documentNumber: '79123456',
  source: 'RUNT',
  mode: 'mock',
};

beforeEach(() => {
  vi.clearAllMocks();
  // El formulario cachea la consulta resuelta en sessionStorage por instancia, y jsdom la conserva
  // entre tests del mismo archivo: sin limpiarla, el segundo test restaura el snapshot del primero
  // y la consulta automática no se dispara (aserción falsamente roja).
  sessionStorage.clear();
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getInstance.mockResolvedValue(OWNER_FIELD_VALUES);
  mocks.patchFieldValues.mockResolvedValue(undefined);
  mocks.runtPersonLookup.mockResolvedValue(RUNT_FOUND);
});

/**
 * Familia OTROS: el paso captura UN solo titular y ese titular ES el propietario inscrito en el
 * RUNT. Se persiste con el rol `comprador` porque el modelo no tiene rol 'propietario', y ahí estaba
 * el defecto: la siembra del documento del paso 1 y la consulta automática estaban clavadas en
 * 'vendedor', así que en OTROS nunca caían. El gestor volvía a teclear un documento que la consulta
 * ya había traído — y podía teclear OTRO, que convierte la novedad en un traspaso encubierto.
 */
function renderPropietarioDeOtros() {
  return render(
    <ActorsForm
      instanceId={INSTANCE}
      modalidad="matricula_inicial"
      roles={['comprador']}
      layout="split"
      seedDocumentoFromOwner
      rolDelPropietario="comprador"
      autoConsultRunt
    />,
  );
}

describe('ActorsForm — titular de la familia OTROS', () => {
  it('siembra el documento del propietario capturado en el paso 1', async () => {
    renderPropietarioDeOtros();

    const doc = await screen.findByLabelText('Número de documento');
    await waitFor(() => expect((doc as HTMLInputElement).value).toBe('79123456'));
  });

  it('consulta el RUNT sola, sin pedir un clic en «Consultar RUNT»', async () => {
    renderPropietarioDeOtros();

    await waitFor(() => expect(mocks.runtPersonLookup).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: 'Consultar RUNT' })).not.toBeInTheDocument();
  });

  it('bloquea la identidad: el documento y el nombre son los del registro', async () => {
    renderPropietarioDeOtros();

    const doc = (await screen.findByLabelText('Número de documento')) as HTMLInputElement;
    await waitFor(() => expect(doc.value).toBe('79123456'));
    expect(doc.readOnly).toBe(true);

    // Cambiar el nombre sería cambiar de titular, y cambiar de titular es un traspaso.
    await waitFor(() => {
      const nombre = document.getElementById('comprador-nombre') as HTMLInputElement;
      expect(nombre.readOnly).toBe(true);
    });
  });

  it('deja editable el contacto de notificación (art. 5.1.10)', async () => {
    renderPropietarioDeOtros();
    await screen.findByLabelText('Número de documento');

    // El RUNT puede traer correo o dirección desactualizados, y ahí llegan los avisos del trámite.
    for (const id of ['comprador-email', 'comprador-telefono', 'comprador-direccion']) {
      const campo = document.getElementById(id) as HTMLInputElement | null;
      if (!campo) continue;
      expect(campo.readOnly).toBe(false);
      expect(campo.disabled).toBe(false);
    }
  });

  it('el copy habla del propietario actual, no de un comprador', async () => {
    renderPropietarioDeOtros();

    expect(await screen.findByText('Datos del propietario actual')).toBeInTheDocument();
    expect(screen.queryByText(/figurará como propietario/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/información del comprador/i)).not.toBeInTheDocument();
  });
});

describe('ActorsForm — regresión de matrícula inicial', () => {
  it('sin propietario previo no se siembra nada y el documento sigue editable', async () => {
    // Matrícula inicial: el vehículo no tiene propietario inscrito de quien sembrar.
    mocks.getInstance.mockResolvedValue({ fieldValues: [] });
    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const doc = (await screen.findByLabelText('Número de documento')) as HTMLInputElement;
    expect(doc.value).toBe('');
    expect(doc.readOnly).toBe(false);
    expect(screen.getByRole('button', { name: 'Consultar RUNT' })).toBeInTheDocument();
    expect(mocks.runtPersonLookup).not.toHaveBeenCalled();
  });
});
