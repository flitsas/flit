import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type { WizardState } from '@/lib/api/types/procedure-runtime';

/**
 * HU #10874 — Interfaz de subsanación con checklist y re-radicar. Archivo DEDICADO (no comparte
 * `tramite-wizard.test.tsx`, ya extenso) con el mismo harness de mocks que el resto de tests del
 * wizard, mínimo necesario para montar una instancia existente en estado `subsanacion`.
 *
 * AC1 — el operador ve el motivo + checklist de ítems a subsanar y puede editar el trámite
 * (no es de solo lectura, a diferencia de otros estados post-entrega).
 * AC2 — con las observaciones "resueltas" (checklist marcado), Re-radicar dispara el submit.
 */
const mocks = vi.hoisted(() => ({
  createInstance: vi.fn(),
  getInstance: vi.fn(),
  getWizardState: vi.fn(),
  startSubsanacion: vi.fn(),
  cancelSubsanacion: vi.fn(),
  patchFieldValues: vi.fn(),
  setCurrentStep: vi.fn(),
  runPreflight: vi.fn(),
  getPreflight: vi.fn(),
  getConsultationConfig: vi.fn(),
  getCommercial: vi.fn(),
  putCommercial: vi.fn(),
  submitInstance: vi.fn(),
  transitionInstance: vi.fn(),
  finalizeDraft: vi.fn(),
  getActors: vi.fn(),
  saveActors: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  listTransitOffices: vi.fn(),
  getBiometricState: vi.fn(),
  iniciarBiometric: vi.fn(),
  simulateBiometric: vi.fn(),
  ensureIdentity: vi.fn(),
  // HU #10875 — panel consolidado de identidad (IdentityStatusPanel), montado por el wizard siempre
  // que hay instanceId.
  getInstanceIdentityValidationAlerts: vi.fn(),
  listBiometric: vi.fn(),
  listFirmas: vi.fn(),
  listParticipantes: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
  DEV_TENANT_ID: 'tenant-dev',
  DEV_USER_ID: 'user-dev',
  getDuplicateActiveProcedureId: () => null,
  getVehicleStateBlock: () => null,
}));

const toastShow = vi.hoisted(() => vi.fn());
vi.mock('@/components/admin/Toast', () => ({
  useToast: () => ({ show: toastShow }),
}));

const routerPush = vi.hoisted(() => vi.fn());
vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: routerPush, replace: vi.fn(), prefetch: vi.fn() }),
}));

import { TramiteWizard } from '@/components/operacion/TramiteWizard';

// Matrícula con TODOS los pasos completos salvo FUR (diferido): la frontera cae directo en el
// paso de decisión, que es donde vive el panel de subsanación / el botón Re-radicar.
const SUBSANACION_WIZARD: WizardState = {
  modalidad: 'matricula_inicial',
  tipologiaCodigo: 'matricula_inicial',
  totalSteps: 5,
  canSubmit: true,
  blockers: [],
  status: 'subsanacion',
  allowedTransitions: ['entregado'],
  steps: [
    { index: 0, key: 'consulta_vin', label: 'Consulta VIN', status: 'complete', reasons: [] },
    { index: 1, key: 'documentos', label: 'Documentos', status: 'complete', reasons: [] },
    { index: 2, key: 'comprador', label: 'Comprador', status: 'complete', reasons: [] },
    { index: 3, key: 'identidad', label: 'Identidad', status: 'complete', reasons: [] },
    { index: 4, key: 'fur', label: 'FUR', status: 'incomplete', reasons: ['fur_pendiente'] },
  ],
};

const SUBSANACION_METADATA = JSON.stringify({
  motivo: 'Corrige el documento del comprador y el valor comercial declarado.',
  items: [
    { campo: 'Documento del comprador', detalle: 'La cédula cargada está borrosa; vuelve a subirla.' },
    { campo: 'Valor comercial', detalle: 'El valor declarado no coincide con el FUR.' },
  ],
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.createInstance.mockResolvedValue({ id: 'inst-1' });
  mocks.getWizardState.mockResolvedValue(SUBSANACION_WIZARD);
  mocks.getInstance.mockResolvedValue({
    id: 'inst-sub',
    status: 'subsanacion',
    fieldValues: [],
    statusHistory: [
      { fromStatus: 'borrador', toStatus: 'preparado', changedAt: '2026-07-01T10:00:00Z', reason: null },
      { fromStatus: 'preparado', toStatus: 'entregado', changedAt: '2026-07-01T10:05:00Z', reason: null },
      {
        fromStatus: 'entregado',
        toStatus: 'subsanacion',
        changedAt: '2026-07-02T09:00:00Z',
        reason: 'Corrige el documento del comprador y el valor comercial declarado.',
        metadata: SUBSANACION_METADATA,
      },
    ],
  });
  mocks.patchFieldValues.mockResolvedValue({ id: 'inst-sub', fieldValues: [] });
  mocks.setCurrentStep.mockResolvedValue({ id: 'inst-sub', currentStep: null });
  mocks.runPreflight.mockResolvedValue({ overall: 'green', checks: [], createdAt: '2026-06-18T00:00:00Z' });
  mocks.getPreflight.mockResolvedValue(null);
  mocks.getConsultationConfig.mockResolvedValue({
    vehicleVin: 'kyverum_runt',
    vehiclePlate: 'kyverum_runt',
    conductor: 'kyverum_runt_conductor',
  });
  mocks.getCommercial.mockResolvedValue({
    valorVenta: null,
    causal: null,
    tasaImpuesto: null,
    derechos: null,
    metodoPago: null,
  });
  mocks.putCommercial.mockResolvedValue(null);
  mocks.submitInstance.mockResolvedValue({ id: 'inst-sub', status: 'entregado' });
  mocks.transitionInstance.mockResolvedValue({ id: 'inst-sub', status: 'entregado' });
  mocks.finalizeDraft.mockResolvedValue({ id: 'inst-sub', status: 'borrador' });
  mocks.getActors.mockResolvedValue([]);
  mocks.saveActors.mockResolvedValue(undefined);
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([]);
  mocks.getBiometricState.mockResolvedValue({ validations: [], provider: 'mock' });
  mocks.getInstanceIdentityValidationAlerts.mockResolvedValue({ alerts: [], total: 0 });
  mocks.simulateBiometric.mockResolvedValue({ id: 'bio-1', status: 'aprobado' });
  mocks.iniciarBiometric.mockResolvedValue({ validation: { id: 'bio-1', status: 'en_proceso' } });
  mocks.listBiometric.mockResolvedValue([]);
  mocks.listFirmas.mockResolvedValue([]);
  mocks.listParticipantes.mockResolvedValue([]);
  mocks.ensureIdentity.mockResolvedValue({ outcome: 'ya_vigente' });
});

describe('TramiteWizard — subsanación (HU #10874, AC1)', () => {
  it('muestra el motivo y el checklist de ítems, y el trámite sigue editable (sin banner de solo lectura)', async () => {
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    expect(await screen.findByText('Trámite en subsanación')).toBeInTheDocument();
    expect(
      screen.getByText('Corrige el documento del comprador y el valor comercial declarado.'),
    ).toBeInTheDocument();
    expect(screen.getByText(/Documento del comprador/)).toBeInTheDocument();
    expect(screen.getByText(/Valor comercial/)).toBeInTheDocument();
    const checklist = screen.getByRole('list', { name: 'Checklist de ítems a subsanar' });
    expect(within(checklist).getAllByRole('checkbox')).toHaveLength(2);

    // No es el modo de solo lectura (Track C): el banner "solo visualización" NO aparece.
    expect(screen.queryByText(/solo visualización/i)).not.toBeInTheDocument();
    // El botón de salida sigue siendo el de edición, no "Volver al listado" (editLocked=false).
    expect(screen.getByRole('button', { name: /Cancelar y volver al selector/ })).toBeInTheDocument();
  });

  it('en el paso de decisión no ofrece Preparar/Finalizar (ese flujo es de borrador)', async () => {
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    await screen.findByText('Trámite en subsanación');
    expect(
      screen.queryByRole('button', { name: 'Finalizar y enviar trámite' }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Finalizar' })).not.toBeInTheDocument();
  });
});

describe('TramiteWizard — subsanación (HU #10874, AC2)', () => {
  it('Re-radicar queda deshabilitado hasta Guardar y continuar + checklist; luego dispara submit', async () => {
    const user = userEvent.setup();
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    await screen.findByText('Trámite en subsanación');

    // Re-radicar vive en el PIE, y el pie solo ofrece la acción terminal en el paso de decisión —
    // igual que "Finalizar y enviar trámite" en el flujo normal. Antes de llegar ahí no existe.
    expect(screen.queryByRole('button', { name: /re-radicar/i })).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: /^Paso 5: Resumen/ }));

    const boton = await screen.findByRole('button', { name: /re-radicar/i });
    expect(boton).toBeDisabled();

    // Sin edición guardada: aunque marques el checklist, Re-radicar sigue off.
    const checklist = screen.getByRole('list', { name: 'Checklist de ítems a subsanar' });
    for (const checkbox of within(checklist).getAllByRole('checkbox')) {
      await user.click(checkbox);
    }
    expect(boton).toBeDisabled();

    await user.click(screen.getByRole('button', { name: /guardar y continuar/i }));
    await waitFor(() => expect(boton).toBeEnabled());

    await user.click(boton);

    await waitFor(() => expect(mocks.submitInstance).toHaveBeenCalledWith('inst-sub'));
    await waitFor(() =>
      expect(toastShow).toHaveBeenCalledWith(
        'Trámite re-radicado a tránsito correctamente.',
        'success',
      ),
    );
  });
});

/**
 * Entrada al flujo: un trámite `rechazado` sin subsanación activa es de SOLO LECTURA, y activarla
 * (POST /subsanar) es lo único que lo reabre. El control vive dentro del propio aviso de solo
 * visualización — que es donde se explica el bloqueo — y sin él el estado no tiene salida.
 */
describe('TramiteWizard — activar la subsanación desde el aviso de rechazado', () => {
  const RECHAZADO_WIZARD: WizardState = {
    ...SUBSANACION_WIZARD,
    status: 'rechazado',
    subsanacionActiva: false,
  };

  const RECHAZADO_HISTORY = [
    { fromStatus: 'borrador', toStatus: 'preparado', changedAt: '2026-07-01T10:00:00Z', reason: null },
    { fromStatus: 'preparado', toStatus: 'entregado', changedAt: '2026-07-01T10:05:00Z', reason: null },
    {
      fromStatus: 'entregado',
      toStatus: 'rechazado',
      changedAt: '2026-07-02T09:00:00Z',
      reason: 'Corrige el documento del comprador y el valor comercial declarado.',
      metadata: SUBSANACION_METADATA,
    },
  ];

  beforeEach(() => {
    mocks.getInstance.mockResolvedValue({
      id: 'inst-sub',
      status: 'rechazado',
      fieldValues: [],
      statusHistory: RECHAZADO_HISTORY,
    });
  });

  it('ofrece "Subsanar trámite" y, al activarlo, reabre la edición sin recargar', async () => {
    const user = userEvent.setup();
    // El flag vive en GET /wizard: el POST lo enciende y el `refresh()` del wizard lo relee.
    let wizardActual: WizardState = RECHAZADO_WIZARD;
    mocks.getWizardState.mockImplementation(() => Promise.resolve(wizardActual));
    mocks.startSubsanacion.mockImplementation(() => {
      wizardActual = { ...RECHAZADO_WIZARD, subsanacionActiva: true };
      return Promise.resolve({ id: 'inst-sub', status: 'rechazado', subsanacionActiva: true });
    });

    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    expect(await screen.findByText(/Trámite rechazado/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Subsanar trámite/i }));

    await waitFor(() => expect(mocks.startSubsanacion).toHaveBeenCalledWith('inst-sub'));
    expect(await screen.findByText('Trámite en subsanación')).toBeInTheDocument();
    // El aviso de solo lectura —y con él su propio botón— desaparece: ya no es el estado vigente.
    await waitFor(() => expect(screen.queryByText(/solo visualización/i)).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Subsanar trámite/i })).not.toBeInTheDocument();
    expect(toastShow).toHaveBeenCalledWith(
      'Subsanación activada: ya puedes corregir el trámite.',
      'success',
    );
  });

  it('con la subsanación ya activa no vuelve a ofrecer el botón (el panel toma el relevo)', async () => {
    mocks.getWizardState.mockResolvedValue({ ...RECHAZADO_WIZARD, subsanacionActiva: true });

    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    expect(await screen.findByText('Trámite en subsanación')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Subsanar trámite/i })).not.toBeInTheDocument();
    expect(mocks.startSubsanacion).not.toHaveBeenCalled();
  });

  it('si el POST falla lo dice en el aviso y el trámite sigue en solo lectura', async () => {
    const user = userEvent.setup();
    mocks.getWizardState.mockResolvedValue(RECHAZADO_WIZARD);
    mocks.startSubsanacion.mockRejectedValue(
      new Error('Solo un trámite en estado rechazado puede iniciar subsanación.'),
    );

    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    await screen.findByText(/Trámite rechazado/);
    await user.click(screen.getByRole('button', { name: /Subsanar trámite/i }));

    expect(
      await screen.findByText('Solo un trámite en estado rechazado puede iniciar subsanación.'),
    ).toBeInTheDocument();
    // Sigue en solo lectura y el botón vuelve a estar disponible para reintentar.
    expect(screen.getByText(/solo visualización/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Subsanar trámite/i })).toBeEnabled();
  });
});

/**
 * Salida por renuncia. Vive en el enlace de la cabecera —el mismo sitio que en cualquier otro
 * estado devuelve al listado— y no en el panel: las dos salidas de la subsanación quedan así en los
 * dos extremos del asistente, cierre abajo y renuncia arriba.
 */
describe('TramiteWizard — cancelar la subsanación desde el enlace de salida', () => {
  const RECHAZADO_EN_SUBSANACION: WizardState = {
    ...SUBSANACION_WIZARD,
    status: 'rechazado',
    subsanacionActiva: true,
  };

  beforeEach(() => {
    mocks.getWizardState.mockResolvedValue(RECHAZADO_EN_SUBSANACION);
    mocks.getInstance.mockResolvedValue({
      id: 'inst-sub',
      status: 'rechazado',
      fieldValues: [],
      statusHistory: [
        {
          fromStatus: 'entregado',
          toStatus: 'rechazado',
          changedAt: '2026-07-02T09:00:00Z',
          reason: 'Corrige el documento del comprador.',
          metadata: SUBSANACION_METADATA,
        },
      ],
    });
    mocks.cancelSubsanacion.mockResolvedValue({ id: 'inst-sub', status: 'rechazado' });
  });

  it('el enlace deja de ser una salida neutra y apaga el flag', async () => {
    const user = userEvent.setup();
    const onExit = vi.fn();
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={onExit} />);

    await screen.findByText('Trámite en subsanación');
    // El rótulo nombra la consecuencia: «Cancelar» a secas ocultaría que el trámite deja de editarse.
    const enlace = screen.getByRole('button', {
      name: 'Cancelar la subsanación y volver al listado',
    });
    expect(enlace).toHaveTextContent('← Cancelar subsanación');

    await user.click(enlace);

    await waitFor(() => expect(mocks.cancelSubsanacion).toHaveBeenCalledWith('inst-sub'));
    await waitFor(() =>
      expect(toastShow).toHaveBeenCalledWith(
        'Subsanación cancelada. El trámite sigue rechazado.',
        'success',
      ),
    );
    await waitFor(() => expect(onExit).toHaveBeenCalled());
  });

  it('el panel ya no trae su propio botón de cancelar', async () => {
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={() => {}} />);

    await screen.findByText('Trámite en subsanación');
    expect(
      screen.queryByRole('button', { name: /^Cancelar la subsanación$/ }),
    ).not.toBeInTheDocument();
  });

  it('estado legado `subsanacion` (sin flag que apagar): el enlace sigue siendo la salida de siempre', async () => {
    const user = userEvent.setup();
    const onExit = vi.fn();
    mocks.getWizardState.mockResolvedValue(SUBSANACION_WIZARD);
    mocks.getInstance.mockResolvedValue({
      id: 'inst-sub',
      status: 'subsanacion',
      fieldValues: [],
      statusHistory: [],
    });
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={onExit} />);

    await screen.findByText('Trámite en subsanación');
    await user.click(screen.getByRole('button', { name: 'Cancelar y volver al selector' }));

    expect(mocks.cancelSubsanacion).not.toHaveBeenCalled();
    expect(onExit).toHaveBeenCalled();
  });

  it('si el POST falla lo dice y no sale del asistente', async () => {
    const user = userEvent.setup();
    const onExit = vi.fn();
    mocks.cancelSubsanacion.mockRejectedValue(new Error('El trámite fue modificado por otro proceso.'));
    render(<TramiteWizard existingInstanceId="inst-sub" onExit={onExit} />);

    await screen.findByText('Trámite en subsanación');
    await user.click(
      screen.getByRole('button', { name: 'Cancelar la subsanación y volver al listado' }),
    );

    expect(
      await screen.findByText('El trámite fue modificado por otro proceso.'),
    ).toBeInTheDocument();
    expect(onExit).not.toHaveBeenCalled();
  });
});
