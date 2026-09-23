import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
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
  getCamaraComercioRequirements: vi.fn(),
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
    getCamaraComercioRequirements: mocks.getCamaraComercioRequirements,
  },
}));

import { ActorsForm } from '@/components/operacion/ActorsForm';

const INSTANCE = 'inst-descarte';

/** Buzón de Cámara de Comercio del paso (hay uno por parte jurídica; los tests usan una sola). */
const buzon = () => screen.findByLabelText('Certificado de Cámara de Comercio');

/** Adjunto de Cámara de Comercio de un rol, tal como lo devuelve el expediente. */
function adjunto(id: string, tipo: string) {
  return {
    id,
    tipo,
    filename: 'certificado.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 1024,
    uploadedAt: '2026-09-22T10:00:00Z',
  };
}

function requisito(rol: string) {
  return {
    rol,
    tipo: `camara_comercio_${rol}`,
    esObligatorio: true,
    exencion: 'ninguna',
    vigencia: 'indeterminada',
    diasDesdeExpedicion: null,
  };
}

/** Actor persona jurídica ya persistido (NIT ⇒ jurídica). */
function actorJuridico(rol: string, nit: string) {
  return {
    rol,
    tipoDocumento: 'NIT',
    numeroDocumento: nit,
    nombreCompleto: `SOCIEDAD ${rol.toUpperCase()} SAS`,
    personType: 'juridical',
    email: 'contacto@sociedad.co',
    telefono: '3001234567',
    representanteLegal: {
      tipoDocumento: 'CC',
      numeroDocumento: '79123456',
      nombreCompleto: 'Representante Legal',
      email: 'rl@sociedad.co',
    },
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({ status: 'borrador', fieldValues: [] });
  mocks.getChecklist.mockResolvedValue([]);
  mocks.getAttachments.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.runtPersonLookup.mockResolvedValue({ found: false });
  mocks.ruesPersonLookup.mockResolvedValue({ found: false });
  mocks.lookupLegalRepresentativeByNit.mockResolvedValue(null);
  mocks.deleteAttachment.mockResolvedValue(true);
  mocks.getCamaraComercioRequirements.mockResolvedValue([]);
});

describe('ActorsForm — descarte del certificado al dejar de ser persona jurídica (HU #12779)', () => {
  /**
   * AC1 — el buzón se desmonta en el mismo render en que la parte deja de ser jurídica, así que el
   * descarte tiene que vivir en el formulario: el componente hijo ya no está para hacerlo.
   */
  it('AC1 — al cambiar el actor a persona natural se descarta su certificado', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([actorJuridico('comprador', '900111222')]);
    mocks.getCamaraComercioRequirements.mockResolvedValue([requisito('comprador')]);
    mocks.getAttachments.mockResolvedValue([adjunto('att-c', 'camara_comercio_comprador')]);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await waitFor(() => expect(mocks.getCamaraComercioRequirements).toHaveBeenCalled());

    // El selector de tipo de documento es el único control de la naturaleza de la persona. El
    // primero es el del actor; el siguiente es el del representante legal, que no la gobierna.
    const selects = await screen.findAllByLabelText('Tipo de documento');
    await user.selectOptions(selects[0], 'CC');

    await waitFor(() => expect(mocks.deleteAttachment).toHaveBeenCalledWith(INSTANCE, 'att-c'));
  });

  /**
   * AC1 — lo que ve el gestor, no solo la llamada de borrado: el buzón se oculta aunque el actor
   * siga guardado como jurídico. Antes el buzón dependía de lo guardado y se quedaba en pantalla
   * mostrando el certificado.
   */
  it('AC1 — al cambiar a persona natural el buzón se oculta sin guardar el paso', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([actorJuridico('comprador', '900111222')]);
    mocks.getCamaraComercioRequirements.mockImplementation(
      async (_id: string, _t: string | undefined, actors?: Array<{ tipoDocumento: string }>) =>
        actors?.[0]?.tipoDocumento === 'NIT' ? [requisito('comprador')] : [],
    );
    mocks.getAttachments.mockResolvedValue([adjunto('att-c', 'camara_comercio_comprador')]);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    expect(await within(await buzon()).findByText('Cargado')).toBeInTheDocument();

    const selects = await screen.findAllByLabelText('Tipo de documento');
    await user.selectOptions(selects[0], 'CC');

    await waitFor(() =>
      expect(screen.queryByLabelText('Certificado de Cámara de Comercio')).not.toBeInTheDocument(),
    );
    expect(mocks.saveActors).not.toHaveBeenCalled();
  });

  /**
   * AC2 — al volver a persona jurídica el buzón reaparece VACÍO: se vuelve a montar y relee el
   * expediente, que ya no tiene el certificado descartado.
   */
  it('AC2 — volver a persona jurídica reabre el buzón vacío', async () => {
    const user = userEvent.setup();
    let descartado = false;
    mocks.getActors.mockResolvedValue([actorJuridico('comprador', '900111222')]);
    mocks.getCamaraComercioRequirements.mockImplementation(
      async (_id: string, _t: string | undefined, actors?: Array<{ tipoDocumento: string }>) =>
        actors?.[0]?.tipoDocumento === 'NIT' ? [requisito('comprador')] : [],
    );
    mocks.getAttachments.mockImplementation(async () =>
      descartado ? [] : [adjunto('att-c', 'camara_comercio_comprador')],
    );
    mocks.deleteAttachment.mockImplementation(async () => {
      descartado = true;
      return true;
    });

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);
    expect(await within(await buzon()).findByText('Cargado')).toBeInTheDocument();

    const selects = await screen.findAllByLabelText('Tipo de documento');
    await user.selectOptions(selects[0], 'CC');
    await waitFor(() => expect(mocks.deleteAttachment).toHaveBeenCalledWith(INSTANCE, 'att-c'));

    const tipo = (await screen.findAllByLabelText('Tipo de documento'))[0];
    await user.selectOptions(tipo, 'NIT');

    expect(await within(await buzon()).findByText('Por cargar')).toBeInTheDocument();
    expect(within(await buzon()).queryByText('Cargado')).not.toBeInTheDocument();
  });

  /**
   * AC3 — el descarte es por rol. Si las dos partes eran jurídicas y solo una cambió, la otra
   * conserva su certificado: son documentos de sociedades distintas.
   */
  it('AC3 — el certificado de la otra parte jurídica NO se descarta', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([
      actorJuridico('vendedor', '900111222'),
      actorJuridico('comprador', '900333444'),
    ]);
    mocks.getCamaraComercioRequirements.mockResolvedValue([
      requisito('vendedor'),
      requisito('comprador'),
    ]);
    mocks.getAttachments.mockResolvedValue([
      adjunto('att-v', 'camara_comercio_vendedor'),
      adjunto('att-c', 'camara_comercio_comprador'),
    ]);

    render(<ActorsForm instanceId={INSTANCE} modalidad="traspaso" />);

    await waitFor(() => expect(mocks.getCamaraComercioRequirements).toHaveBeenCalled());

    const selects = await screen.findAllByLabelText('Tipo de documento');
    await user.selectOptions(selects[0], 'CC');

    await waitFor(() => expect(mocks.deleteAttachment).toHaveBeenCalledWith(INSTANCE, 'att-v'));
    expect(mocks.deleteAttachment).not.toHaveBeenCalledWith(INSTANCE, 'att-c');
  });

  /**
   * Abrir un trámite cuyo actor YA era jurídico no puede borrar nada: en el primer render nadie
   * «dejó de ser» jurídico. Sin esta guarda, reabrir el trámite tiraría el certificado cargado.
   */
  it('abrir el trámite con el actor ya jurídico no descarta nada', async () => {
    mocks.getActors.mockResolvedValue([actorJuridico('comprador', '900111222')]);
    mocks.getCamaraComercioRequirements.mockResolvedValue([requisito('comprador')]);
    mocks.getAttachments.mockResolvedValue([adjunto('att-c', 'camara_comercio_comprador')]);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await waitFor(() => expect(mocks.getCamaraComercioRequirements).toHaveBeenCalled());
    // Margen para que cualquier efecto pendiente corra antes de afirmar la ausencia de borrado.
    await new Promise((r) => setTimeout(r, 50));

    expect(mocks.deleteAttachment).not.toHaveBeenCalled();
  });

  it('un actor persona natural desde el principio no consulta ni descarta', async () => {
    mocks.getActors.mockResolvedValue([
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '1020304050',
        nombreCompleto: 'Persona Natural',
        personType: 'natural',
        email: 'persona@correo.co',
        telefono: '3001234567',
      },
    ]);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await waitFor(() => expect(mocks.getActors).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 50));

    expect(mocks.deleteAttachment).not.toHaveBeenCalled();
  });
});

describe('ActorsForm — el buzón sigue al formulario, no a lo guardado (HU #12777 AC1)', () => {
  it('marcar NIT muestra el buzón sin guardar el paso y consulta con los actores en pantalla', async () => {
    const user = userEvent.setup();
    mocks.getActors.mockResolvedValue([
      {
        rol: 'comprador',
        tipoDocumento: 'CC',
        numeroDocumento: '1020304050',
        nombreCompleto: 'Persona Natural',
        personType: 'natural',
        email: 'persona@correo.co',
        telefono: '3001234567',
      },
    ]);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    const selects = await screen.findAllByLabelText('Tipo de documento');
    expect(screen.queryByLabelText('Certificado de Cámara de Comercio')).not.toBeInTheDocument();

    await user.selectOptions(selects[0], 'NIT');

    // Aparece aunque el backend todavía no conozca a la parte como jurídica (lo guardado es CC).
    expect(await screen.findByLabelText('Certificado de Cámara de Comercio')).toBeInTheDocument();
    expect(mocks.saveActors).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(mocks.getCamaraComercioRequirements).toHaveBeenLastCalledWith(
        INSTANCE,
        undefined,
        expect.arrayContaining([expect.objectContaining({ rol: 'comprador', tipoDocumento: 'NIT' })]),
      ),
    );
  });

  it('si la consulta falla, el paso se comporta como antes: sin buzón', async () => {
    mocks.getActors.mockResolvedValue([actorJuridico('comprador', '900111222')]);
    mocks.getCamaraComercioRequirements.mockResolvedValue(null);

    render(<ActorsForm instanceId={INSTANCE} modalidad="matricula_inicial" />);

    await waitFor(() => expect(mocks.getCamaraComercioRequirements).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 50));
    expect(screen.queryByLabelText('Certificado de Cámara de Comercio')).not.toBeInTheDocument();
  });
});
