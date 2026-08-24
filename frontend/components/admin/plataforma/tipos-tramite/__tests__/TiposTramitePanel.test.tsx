import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

/**
 * ADR-0050 — configurador de tipos de trámite.
 *
 * Lo que estas pruebas fijan es lo que hacía falta y no existía: poder habilitar un tipo desde una
 * pantalla, ver por qué uno no se puede habilitar todavía, y corregir la parametrización de un tipo
 * PUBLICADO — que son los 21 del catálogo.
 */
// La clase va dentro de `vi.hoisted`: `vi.mock` se iza al principio del archivo y una declaración
// de clase en el cuerpo del módulo todavía no está inicializada cuando la fábrica corre.
const { mocks, ApiError } = vi.hoisted(() => {
  class ApiError extends Error {
    constructor(
      readonly status: number,
      readonly body: unknown,
    ) {
      // Igual que el real: el mensaje sale de `detail` de ProblemDetails.
      super(String((body as { detail?: unknown })?.detail ?? 'error'));
      this.name = 'SuperadminApiError';
    }
  }
  return {
    ApiError,
    mocks: {
      listProcedureTypes: vi.fn(),
      getConformationProfile: vi.fn(),
      getSteps: vi.fn(),
      validate: vi.fn(),
      setWizardEnabled: vi.fn(),
      updateProcedureType: vi.fn(),
      createProcedureType: vi.fn(),
      retirar: vi.fn(),
      getQuipuxMapping: vi.fn(),
      setQuipuxMapping: vi.fn(),
    },
  };
});

vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: mocks,
  SuperadminApiError: ApiError,
}));

vi.mock('@/lib/api/admin-document-types', () => ({
  fetchDocumentTypes: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 50 }),
}));

import { TiposTramitePanel } from '../TiposTramitePanel';

const BLINDAJE = {
  id: 'id-blindaje',
  code: 'BLINDAJE',
  name: 'Blindaje',
  family: 'OTROS' as const,
  publicationStatus: 'published' as const,
  isActive: true,
  wizardEnabled: false,
  publishedAt: null,
};

const MATRICULA = { ...BLINDAJE, id: 'id-mat', code: 'MATRICULA_NUEVA', name: 'Matrícula inicial', family: 'MATRICULAS' as const, wizardEnabled: true };

beforeEach(() => {
  vi.clearAllMocks();
  mocks.listProcedureTypes.mockResolvedValue([MATRICULA, BLINDAJE]);
  mocks.getConformationProfile.mockResolvedValue({
    procedureTypeId: 'id-blindaje',
    code: 'BLINDAJE',
    publicationStatus: 'published',
    version: 3,
    gateProfile: { entryMode: 'PLATE', requiresBuyer: true },
    conformationRules: [],
    sources: [],
    documentRequirements: [],
  });
  mocks.getSteps.mockResolvedValue([]);
  mocks.validate.mockResolvedValue({ isValid: true, errors: [] });
  mocks.getQuipuxMapping.mockResolvedValue(undefined);
});

describe('Configurador de tipos de trámite', () => {
  it('resume cuántos tipos hay y cuántos son operables', async () => {
    render(<TiposTramitePanel />);

    // Es el dato que responde «¿qué puede hacer hoy el gestor?», y no existía en ninguna pantalla.
    expect(await screen.findByText(/2 tipos en el catálogo/)).toBeInTheDocument();
    expect(screen.getByText(/1 operables/)).toBeInTheDocument();
  });

  it('habilita un tipo desde el interruptor', async () => {
    const user = userEvent.setup();
    mocks.setWizardEnabled.mockResolvedValue({ ...BLINDAJE, wizardEnabled: true });
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(await screen.findByRole('switch', { name: /Blindaje/ }));

    await waitFor(() =>
      expect(mocks.setWizardEnabled).toHaveBeenCalledWith('id-blindaje', true),
    );
    expect(await screen.findByText(/el gestor puede elegirlo/)).toBeInTheDocument();
  });

  it('cuando el tipo no está listo, dice TODO lo que le falta', async () => {
    // Enterarse de un impedimento por vez convierte dar de alta un tipo en una sucesión de intentos.
    const user = userEvent.setup();
    mocks.setWizardEnabled.mockRejectedValue(
      new ApiError(422, { motivos: ['El tipo no tiene pasos parametrizados.', 'El tipo está inactivo.'] }),
    );
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(await screen.findByRole('switch', { name: /Blindaje/ }));

    const aviso = await screen.findByRole('alert');
    expect(within(aviso).getByText(/no se puede habilitar/i)).toBeInTheDocument();
    expect(within(aviso).getByText(/no tiene pasos parametrizados/)).toBeInTheDocument();
    expect(within(aviso).getByText(/está inactivo/)).toBeInTheDocument();
  });

  it('avisa de que corregir un tipo publicado sube su versión', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    const nombre = await screen.findByLabelText('Nombre');
    await user.clear(nombre);
    await user.type(nombre, 'Blindaje de vehículo');

    // El gestor debe saber qué implica guardar antes de pulsar, no después.
    expect(await screen.findByText(/sube su versión/)).toBeInTheDocument();
    expect(screen.getByText(/Los trámites en curso no cambian/)).toBeInTheDocument();
  });

  it('permite reclasificar la familia, que es la corrección que negocio pide', async () => {
    const user = userEvent.setup();
    mocks.updateProcedureType.mockResolvedValue({ ...BLINDAJE, family: 'MATRICULAS' });
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.selectOptions(await screen.findByLabelText('Familia'), 'MATRICULAS');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() =>
      expect(mocks.updateProcedureType).toHaveBeenCalledWith(
        'id-blindaje',
        expect.objectContaining({ family: 'MATRICULAS' }),
      ),
    );
  });

  it('muestra los problemas de validación sin que haya que pulsar nada', async () => {
    const user = userEvent.setup();
    mocks.validate.mockResolvedValue({
      isValid: false,
      errors: [{ code: 'GATE_PROFILE_BIOMETRIC_ACTORS_MISSING', message: 'requiresBiometrics exige al menos un actor.', path: 'x' }],
    });
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));

    expect(await screen.findByText(/exige al menos un actor/)).toBeInTheDocument();
  });

  // ── Alta y retiro ─────────────────────────────────────────────────────────

  it('el alta avisa de que el código no se podrá cambiar y de que falta mapear las integraciones', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Nuevo tipo/ }));

    // Ambas cosas se descubren tarde si no se dicen aquí: el código viaja a ICT y a Quipux, y crear
    // el tipo en FLIT no lo da de alta allí.
    expect(await screen.findByText(/No se puede cambiar después/)).toBeInTheDocument();
    expect(screen.getByText(/no lo da de alta en ICT ni en Quipux/)).toBeInTheDocument();
  });

  it('normaliza el código y lo muestra antes de crear', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Nuevo tipo/ }));
    await user.type(await screen.findByLabelText('Código'), 'cambio color');

    expect(await screen.findByText(/CAMBIO_COLOR/)).toBeInTheDocument();
  });

  it('no deja crear con un código de forma inválida', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Nuevo tipo/ }));
    await user.type(await screen.findByLabelText('Código'), 'AB');
    await user.type(screen.getByLabelText('Nombre del tipo'), 'Algo');

    expect(screen.getByRole('button', { name: 'Crear tipo' })).toBeDisabled();
  });

  it('el retiro explica que archiva y no borra', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(await screen.findByRole('button', { name: /Retirar del catálogo/ }));

    // «Eliminar» sugiere una pérdida de datos que no ocurre, y eso cambia si el gestor se atreve.
    const dialogo = await screen.findByRole('dialog', { name: /Retirar tipo/ });
    expect(within(dialogo).getByText(/se archiva/)).toBeInTheDocument();
    expect(within(dialogo).getByText(/no se borra/)).toBeInTheDocument();

    await user.click(within(dialogo).getByRole('button', { name: /Sí, retirar/ }));
    await waitFor(() => expect(mocks.retirar).toHaveBeenCalledWith('id-blindaje'));
  });

  it('un tipo con trámites no se retira y se dice por qué', async () => {
    const user = userEvent.setup();
    mocks.retirar.mockRejectedValue(
      new ApiError(409, { detail: 'No se puede retirar un tipo que tiene trámites.' }),
    );
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(await screen.findByRole('button', { name: /Retirar del catálogo/ }));
    const dialogo = await screen.findByRole('dialog', { name: /Retirar tipo/ });
    await user.click(within(dialogo).getByRole('button', { name: /Sí, retirar/ }));

    // La aserción va sobre el elemento de ERROR, no sobre el texto del diálogo: este también
    // menciona los trámites, y buscarlo por texto suelto pasaría aunque el error no se pintara.
    const aviso = await screen.findByRole('alert');
    expect(aviso).toHaveTextContent('No se puede retirar un tipo que tiene trámites.');
  });

  // ── Radicación (Quipux) ───────────────────────────────────────────────────

  it('un tipo sin equivalencia dice que no se radica, sin tratarlo como error', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(screen.getByRole('button', { name: 'Radicación' }));

    // La ausencia de bloque es un estado legítimo del catálogo: ese trámite no va a la secretaría.
    expect(await screen.findByText(/no se radica en la secretaría/)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('propone el identificador y el tope desde la parametrización del tipo', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(screen.getByRole('button', { name: 'Radicación' }));
    await user.click(await screen.findByRole('button', { name: /Configurar radicación/ }));

    // El perfil del tipo dice entryMode PLATE, así que el identificador es la placa y el tope 35.
    expect(await screen.findByLabelText('Identificador del vehículo')).toHaveValue('plate');
    expect(screen.getByLabelText('Tope del nombre de empresa')).toHaveValue(35);
  });

  it('no deja guardar sin los códigos que asigna la secretaría', async () => {
    const user = userEvent.setup();
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(screen.getByRole('button', { name: 'Radicación' }));
    await user.click(await screen.findByRole('button', { name: /Configurar radicación/ }));

    // Guardar un bloque a medias dejaría al administrador creyendo que configuró algo que el worker
    // descarta en silencio.
    expect(screen.getByRole('button', { name: 'Guardar equivalencia' })).toBeDisabled();
    expect(screen.getByText(/Faltan los códigos de la secretaría/)).toBeInTheDocument();
  });

  it('guarda la equivalencia con los códigos de la secretaría', async () => {
    const user = userEvent.setup();
    mocks.setQuipuxMapping.mockResolvedValue({
      familia: 'OTROS', tipoTramite: 42, tipoRequisito: 51, prefijo: 'BL',
      campoPlaca: 'plate', campoVin: null, maxLongitudEmpresa: 35,
    });
    render(<TiposTramitePanel />);

    await user.click(await screen.findByRole('button', { name: /Blindaje/ }));
    await user.click(screen.getByRole('button', { name: 'Radicación' }));
    await user.click(await screen.findByRole('button', { name: /Configurar radicación/ }));

    await user.type(screen.getByLabelText('Código de trámite en la secretaría'), '42');
    await user.type(screen.getByLabelText('Prefijo del documento radicado'), 'BL');
    await user.click(screen.getByRole('button', { name: 'Guardar equivalencia' }));

    await waitFor(() =>
      expect(mocks.setQuipuxMapping).toHaveBeenCalledWith(
        'id-blindaje',
        expect.objectContaining({ tipoTramite: 42, prefijo: 'BL', campoPlaca: 'plate' }),
      ),
    );
  });
});
