/**
 * Tests HU #10868 — PrevalidacionForm + null-safety de Validaciones.tsx (HU #10869).
 * Vitest + RTL.  Mock de tramitesClient.createPrevalidacion y listTenantBiometricValidations.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mocks ─────────────────────────────────────────────────────────────────────
const mocks = vi.hoisted(() => ({
  createPrevalidacion: vi.fn(),
  editPrevalidacion: vi.fn(),
  resendPrevalidacion: vi.fn(),
  listTenantBiometricPersons: vi.fn(),
  listPersonBiometricValidations: vi.fn(),
  listStuckIdentityValidations: vi.fn(),
  requeueStuckIdentityValidation: vi.fn(),
  requeueAllStuckIdentityValidations: vi.fn(),
  setActiveTramitesTenant: vi.fn(),
}));

// El módulo de Identidad resuelve el rol desde el JWT para decidir si pinta el selector de empresa.
// Aquí siempre es usuario de compañía: sin selector, sin listado de empresas.
vi.mock('@/lib/api/superadmin-client', () => ({
  superadminClient: { listCompanies: vi.fn().mockResolvedValue({ data: [] }) },
}));
vi.mock('@/lib/api/client', () => ({ getToken: () => null }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => null,
  isSuperAdmin: () => false,
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    createPrevalidacion: mocks.createPrevalidacion,
    editPrevalidacion: mocks.editPrevalidacion,
    resendPrevalidacion: mocks.resendPrevalidacion,
    listTenantBiometricPersons: mocks.listTenantBiometricPersons,
    listPersonBiometricValidations: mocks.listPersonBiometricValidations,
    listStuckIdentityValidations: mocks.listStuckIdentityValidations,
    requeueStuckIdentityValidation: mocks.requeueStuckIdentityValidation,
    requeueAllStuckIdentityValidations: mocks.requeueAllStuckIdentityValidations,
  },
  setActiveTramitesTenant: mocks.setActiveTramitesTenant,
  TramitesApiError: class TramitesApiError extends Error {
    constructor(
      public status: number,
      message: string,
      public problem: Record<string, unknown> | null,
    ) {
      super(message);
      this.name = 'TramitesApiError';
    }
  },
  getIdentitySendConflict: (err: unknown) => {
    if (!err || typeof err !== 'object') return null;
    const { status, problem } = err as { status?: unknown; problem?: unknown };
    if (status !== 409 || !problem || typeof problem !== 'object') return null;
    const p = problem as Record<string, unknown>;
    if (typeof p.motivo !== 'string' || !p.motivo) return null;
    return {
      motivo: p.motivo,
      status: typeof p.status === 'string' ? p.status : null,
      validatedAt: typeof p.validatedAt === 'string' ? p.validatedAt : null,
      validUntil: typeof p.validUntil === 'string' ? p.validUntil : null,
      validationId: typeof p.validationId === 'string' ? p.validationId : null,
      origen: typeof p.origen === 'string' ? p.origen : null,
    };
  },
}));

// ── Imports después de los mocks ────────────────────────────────────────────
import { PrevalidacionForm } from '@/components/atom/modules/PrevalidacionForm';
import { Validaciones } from '@/components/atom/modules/Validaciones';
import type {
  TenantBiometricPerson,
  TenantBiometricPersonsResponse,
  TenantBiometricValidation,
} from '@/lib/api/types/procedure-runtime';

// ── Fixtures ─────────────────────────────────────────────────────────────────

const RESULT_STANDALONE: TenantBiometricValidation = {
  id: 'pv-1',
  instanceId: null,          // HU #10869: standalone — sin trámite
  referenceNumber: null,     // HU #10869: null para standalone
  modalidad: null,           // HU #10869: null para standalone
  partyRole: null,
  name: 'Juan Prevalidado',
  documentType: 'CC',
  documentNumber: '9876543210',
  status: 'enviado',
  score: null,
  provider: 'kyverum',
  expired: false,
  rejectionReason: null,
  createdAt: '2026-07-24T10:00:00Z',
  validatedAt: null,
  validUntil: null,
  daysRemaining: null,
  captureUrl: 'https://capture.kyverum.co/abc123',
  linkExpiresAt: '2026-07-25T10:00:00Z',
  email: 'juan.prevalidado@correo.co', // CF-05 (HU #11006)
};

const RESULT_TRAMITE: TenantBiometricValidation = {
  id: 'val-1',
  instanceId: 'inst-99',
  referenceNumber: 'TRM-2026-000099',
  modalidad: 'traspaso',
  partyRole: 'comprador',
  name: 'Ana Compradora',
  documentType: 'CC',
  documentNumber: '1020304050',
  status: 'aprobado',
  score: 95,
  provider: 'kyverum',
  expired: false,
  rejectionReason: null,
  createdAt: '2026-07-20T10:00:00Z',
  validatedAt: '2026-07-20T11:00:00Z',
  validUntil: '2026-08-19T11:00:00Z',
  daysRemaining: 26,
  captureUrl: null,
  linkExpiresAt: null,
  email: null, // CF-05 (HU #11006) — BE aún no lo envía para esta fila (fixture de borde)
};

/** La grilla del módulo unificado es agrupada por persona: se proyectan los fixtures a ese DTO. */
function toPerson(v: TenantBiometricValidation): TenantBiometricPerson {
  return {
    documentType: v.documentType,
    documentNumber: v.documentNumber,
    name: v.name,
    status: v.status,
    validationCount: 1,
    worstAlertKind: null,
    latestValidationId: v.id,
    instanceId: v.instanceId,
    referenceNumber: v.referenceNumber,
    modalidad: v.modalidad,
    partyRole: v.partyRole,
    email: v.email ?? '',
    provider: v.provider as TenantBiometricPerson['provider'],
    score: v.score,
    captureUrl: v.captureUrl,
    expired: v.expired,
    createdAt: v.createdAt,
    validatedAt: v.validatedAt,
    validUntil: v.validUntil,
    daysRemaining: v.daysRemaining,
    linkExpiresAt: v.linkExpiresAt,
  };
}

function personsResponse(rows: TenantBiometricValidation[]): TenantBiometricPersonsResponse {
  return {
    persons: rows.map(toPerson),
    stats: { total: rows.length, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
    page: 1,
    pageSize: 20,
    total: rows.length,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. FORM — HU #10868
// ─────────────────────────────────────────────────────────────────────────────

describe('PrevalidacionForm (HU #10868)', () => {
  const onClose = vi.fn();
  const onSuccess = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renderiza el formulario con los campos requeridos (WCAG: labels visibles)', () => {
    render(<PrevalidacionForm onClose={onClose} onSuccess={onSuccess} />);

    expect(screen.getByLabelText(/número de documento/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/nombre completo/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/correo electrónico/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /crear prevalidación/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /cancelar/i })).toBeInTheDocument();
  });

  it('muestra errores de validación al enviar con campos vacíos', async () => {
    const user = userEvent.setup();
    render(<PrevalidacionForm onClose={onClose} onSuccess={onSuccess} />);

    // Limpiar el número de documento y el nombre para asegurar que están vacíos
    const docNumInput = screen.getByLabelText(/número de documento/i);
    await user.clear(docNumInput);

    await user.click(screen.getByRole('button', { name: /crear prevalidación/i }));

    // Debe mostrar al menos un error de validación
    await waitFor(() => {
      const alerts = screen.getAllByRole('alert');
      expect(alerts.length).toBeGreaterThan(0);
    });
    // No se llama a la API
    expect(mocks.createPrevalidacion).not.toHaveBeenCalled();
  });

  it('llama a createPrevalidacion con los datos correctos al enviar formulario válido', async () => {
    const user = userEvent.setup();
    mocks.createPrevalidacion.mockResolvedValueOnce({
      validationId: 'val-new-1',
      captureUrl: 'https://capture.kyverum.co/xyz',
      status: 'enviado',
    });

    render(<PrevalidacionForm onClose={onClose} onSuccess={onSuccess} />);

    await user.type(screen.getByLabelText(/número de documento/i), '1234567890');
    await user.type(screen.getByLabelText(/nombre completo/i), 'Carlos Prueba');
    await user.type(screen.getByLabelText(/correo electrónico/i), 'carlos@prueba.co');
    await user.click(screen.getByRole('button', { name: /crear prevalidación/i }));
    // HU #11267 AC3 — confirmación con destinatario antes de enviar.
    expect(screen.getByRole('alertdialog')).toHaveTextContent(/carlos@prueba\.co/i);
    await user.click(screen.getByRole('button', { name: /confirmar y enviar/i }));

    await waitFor(() => {
      // CF-01 (HU #11006, D1) — ya no se envía personType/legalRep*: el backend asume "natural".
      expect(mocks.createPrevalidacion).toHaveBeenCalledWith(
        expect.objectContaining({
          documentNumber: '1234567890',
          name: 'Carlos Prueba',
          email: 'carlos@prueba.co',
        }),
      );
      const sentBody = mocks.createPrevalidacion.mock.calls[0][0];
      expect(sentBody).not.toHaveProperty('personType');
      expect(sentBody).not.toHaveProperty('legalRepName');
      expect(onSuccess).toHaveBeenCalledWith(
        expect.objectContaining({ validationId: 'val-new-1' }),
      );
    });
  });

  it('409 sin cuerpo informativo: avisa que el documento ya tiene validación en el tenant', async () => {
    const user = userEvent.setup();
    const { TramitesApiError } = await import('@/lib/api/tramites-client');
    mocks.createPrevalidacion.mockRejectedValueOnce(
      new TramitesApiError(409, 'Ya existe una prevalidación activa', null),
    );

    render(<PrevalidacionForm onClose={onClose} onSuccess={onSuccess} />);

    await user.type(screen.getByLabelText(/número de documento/i), '9999999999');
    await user.type(screen.getByLabelText(/nombre completo/i), 'Pedro Activo');
    await user.type(screen.getByLabelText(/correo electrónico/i), 'pedro@activo.co');
    await user.click(screen.getByRole('button', { name: /crear prevalidación/i }));
    await user.click(screen.getByRole('button', { name: /confirmar y enviar/i }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        /ya existe una validación activa o pendiente para este documento en este tenant/i,
      );
    });
    expect(onSuccess).not.toHaveBeenCalled();
  });

  it('HU11267 AC1: 409 con cuerpo informativo muestra vigencia y Ver proceso', async () => {
    const user = userEvent.setup();
    const { TramitesApiError } = await import('@/lib/api/tramites-client');
    mocks.createPrevalidacion.mockRejectedValueOnce(
      new TramitesApiError(409, 'Conflict', {
        motivo: 'identidad_vigente',
        status: 'aprobado',
        validatedAt: '2026-08-01T00:00:00Z',
        validUntil: '2026-08-31T00:00:00Z',
        validationId: 'val-existente',
        origen: 'tramite',
      }),
    );

    render(<PrevalidacionForm onClose={onClose} onSuccess={onSuccess} />);

    await user.type(screen.getByLabelText(/número de documento/i), '111');
    await user.type(screen.getByLabelText(/nombre completo/i), 'Ana');
    await user.type(screen.getByLabelText(/correo electrónico/i), 'ana@x.co');
    await user.click(screen.getByRole('button', { name: /crear prevalidación/i }));
    await user.click(screen.getByRole('button', { name: /confirmar y enviar/i }));

    expect(await screen.findByText(/ya validada/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /ver el proceso existente/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /crear prevalidación/i })).not.toBeInTheDocument();
    expect(onSuccess).not.toHaveBeenCalled();
  });

  it('CF-01 (HU #11006, D1): no ofrece selector de tipo de persona ni campos de representante legal', () => {
    render(<PrevalidacionForm onClose={onClose} onSuccess={onSuccess} />);

    expect(screen.queryByText(/tipo de persona/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/persona jurídica/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/persona natural/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/tipo doc. rl/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/número doc. rl/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/nombre completo del rl/i)).not.toBeInTheDocument();
  });

  /**
   * Regla del módulo unificado: un documento no puede tener dos validaciones en vuelo en el mismo
   * tenant. Si ya existe, NO se crea otra fila — se reutiliza la existente y se reenvía el correo,
   * actualizándolo antes cuando el operador escribió uno distinto al registrado.
   */
  describe('documento ya existente en el tenant (no se crea, se reenvía)', () => {
    const onReused = vi.fn();

    /** 409 informativo por validación en vuelo, con el id de la validación existente. */
    async function conflictoEnVuelo(motivo = 'validacion_en_vuelo') {
      const { TramitesApiError } = await import('@/lib/api/tramites-client');
      return new TramitesApiError(409, 'Conflict', {
        motivo,
        status: 'enviado',
        validationId: 'val-existente',
        origen: 'standalone',
      });
    }

    async function enviarFormulario(email: string) {
      const user = userEvent.setup();
      render(
        <PrevalidacionForm onClose={onClose} onSuccess={onSuccess} onReused={onReused} />,
      );
      await user.type(screen.getByLabelText(/número de documento/i), '9876543210');
      await user.type(screen.getByLabelText(/nombre completo/i), 'Juan Repetido');
      await user.type(screen.getByLabelText(/correo electrónico/i), email);
      await user.click(screen.getByRole('button', { name: /crear prevalidación/i }));
      await user.click(screen.getByRole('button', { name: /confirmar y enviar/i }));
    }

    it('correo distinto al registrado: actualiza el correo y reenvía, sin crear una validación nueva', async () => {
      mocks.createPrevalidacion.mockRejectedValueOnce(await conflictoEnVuelo());
      // El backend detecta el cambio de correo y reenvía en la misma operación (resent=true).
      mocks.editPrevalidacion.mockResolvedValueOnce({
        validation: { id: 'val-existente', email: 'nuevo@correo.co' },
        captureUrl: 'https://capture.kyverum.co/nuevo',
        resent: true,
      });

      await enviarFormulario('nuevo@correo.co');

      await waitFor(() =>
        expect(mocks.editPrevalidacion).toHaveBeenCalledWith(
          'val-existente',
          expect.objectContaining({ email: 'nuevo@correo.co', name: 'Juan Repetido' }),
        ),
      );
      // Al reenviar el PATCH no hace falta el reenvío manual, y NUNCA se crea una segunda fila.
      expect(mocks.resendPrevalidacion).not.toHaveBeenCalled();
      expect(onSuccess).not.toHaveBeenCalled();
      await waitFor(() =>
        expect(onReused).toHaveBeenCalledWith(
          expect.objectContaining({
            kind: 'email_actualizado',
            validationId: 'val-existente',
            email: 'nuevo@correo.co',
          }),
        ),
      );
    });

    it('mismo correo: no actualiza nada y solo reenvía el enlace', async () => {
      mocks.createPrevalidacion.mockRejectedValueOnce(await conflictoEnVuelo());
      // Sin cambio de correo el PATCH no reenvía (resent=false) → se dispara el reenvío explícito.
      mocks.editPrevalidacion.mockResolvedValueOnce({
        validation: { id: 'val-existente', email: 'mismo@correo.co' },
        captureUrl: null,
        resent: false,
      });
      mocks.resendPrevalidacion.mockResolvedValueOnce({
        validation: { id: 'val-existente', email: 'mismo@correo.co' },
        captureUrl: 'https://capture.kyverum.co/reenvio',
        queued: false,
      });

      await enviarFormulario('mismo@correo.co');

      await waitFor(() => expect(mocks.resendPrevalidacion).toHaveBeenCalledWith('val-existente'));
      expect(mocks.createPrevalidacion).toHaveBeenCalledTimes(1);
      await waitFor(() =>
        expect(onReused).toHaveBeenCalledWith(
          expect.objectContaining({ kind: 'reenviado', email: 'mismo@correo.co' }),
        ),
      );
    });

    it('enlace vencido: también reutiliza la validación existente en vez de crear otra', async () => {
      mocks.createPrevalidacion.mockRejectedValueOnce(
        await conflictoEnVuelo('enlace_vencido_reenvio'),
      );
      mocks.editPrevalidacion.mockResolvedValueOnce({
        validation: { id: 'val-existente', email: 'otro@correo.co' },
        captureUrl: 'https://capture.kyverum.co/nuevo',
        resent: true,
      });

      await enviarFormulario('otro@correo.co');

      await waitFor(() => expect(mocks.editPrevalidacion).toHaveBeenCalled());
      expect(onReused).toHaveBeenCalled();
    });

    it('tope de reenvíos agotado: informa que ya existe y que no se pudo reenviar', async () => {
      const { TramitesApiError } = await import('@/lib/api/tramites-client');
      mocks.createPrevalidacion.mockRejectedValueOnce(await conflictoEnVuelo());
      mocks.editPrevalidacion.mockRejectedValueOnce(
        new TramitesApiError(429, 'Se agotaron los reenvíos disponibles.', null),
      );

      await enviarFormulario('nuevo@correo.co');

      await waitFor(() =>
        expect(screen.getByRole('alert')).toHaveTextContent(
          /ya existe una validación para este documento en este tenant.*no se pudo reenviar/i,
        ),
      );
      expect(onReused).not.toHaveBeenCalled();
    });

    it('identidad aprobada y vigente: no reenvía nada, solo remite al proceso existente', async () => {
      const { TramitesApiError } = await import('@/lib/api/tramites-client');
      mocks.createPrevalidacion.mockRejectedValueOnce(
        new TramitesApiError(409, 'Conflict', {
          motivo: 'identidad_vigente',
          status: 'aprobado',
          validUntil: '2026-08-31T00:00:00Z',
          validationId: 'val-vigente',
          origen: 'standalone',
        }),
      );

      await enviarFormulario('cualquiera@correo.co');

      expect(await screen.findByText(/ya validada/i)).toBeInTheDocument();
      expect(mocks.editPrevalidacion).not.toHaveBeenCalled();
      expect(mocks.resendPrevalidacion).not.toHaveBeenCalled();
      expect(onReused).not.toHaveBeenCalled();
    });

    it('validación de un trámite: no la toca; indica gestionarla desde el trámite', async () => {
      const { TramitesApiError } = await import('@/lib/api/tramites-client');
      mocks.createPrevalidacion.mockRejectedValueOnce(
        new TramitesApiError(409, 'Conflict', {
          motivo: 'validacion_en_vuelo',
          status: 'enviado',
          validationId: 'val-de-tramite',
          origen: 'tramite',
        }),
      );

      await enviarFormulario('quien.sea@correo.co');

      expect(await screen.findByText(/gestiónala desde ese trámite/i)).toBeInTheDocument();
      expect(mocks.editPrevalidacion).not.toHaveBeenCalled();
      expect(mocks.resendPrevalidacion).not.toHaveBeenCalled();
    }, 10_000);
  });

  it('invoca onClose al pulsar Cancelar', async () => {
    const user = userEvent.setup();
    render(<PrevalidacionForm onClose={onClose} onSuccess={onSuccess} />);

    await user.click(screen.getByRole('button', { name: /cancelar/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// 2. NULL-SAFETY — HU #10869 — Validaciones.tsx tolera instanceId/referenceNumber/modalidad null
// ─────────────────────────────────────────────────────────────────────────────

describe('Validaciones null-safety (HU #10869)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.listStuckIdentityValidations.mockResolvedValue({
      stuck: [],
      total: 0,
      maxDeliveryAttempts: 5,
    });
  });

  it('renderiza una prevalidación standalone (instanceId null) sin crash', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValueOnce(personsResponse([RESULT_STANDALONE]));

    render(<Validaciones />);

    await waitFor(() => {
      // La fila de la persona debe aparecer
      expect(screen.getByText('Juan Prevalidado')).toBeInTheDocument();
    });

    // El badge "Prevalidación" debe estar visible en lugar de un referenceNumber
    expect(screen.getByText('Prevalidación')).toBeInTheDocument();

    // No debe haber un enlace al trámite (sin instanceId)
    expect(screen.queryByRole('link', { name: /abrir trámite/i })).toBeNull();
  });

  it('renderiza una fila con trámite (instanceId != null) mostrando su referencia', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValueOnce(personsResponse([RESULT_TRAMITE]));

    render(<Validaciones />);

    await waitFor(() => {
      expect(screen.getByText('Ana Compradora')).toBeInTheDocument();
    });

    // La fila debe mostrar el referenceNumber real
    expect(screen.getByText('TRM-2026-000099')).toBeInTheDocument();
  });

  it('la lista mezcla prevalidaciones standalone y validaciones de trámite en una sola grilla', async () => {
    mocks.listTenantBiometricPersons.mockResolvedValueOnce(
      personsResponse([RESULT_STANDALONE, RESULT_TRAMITE]),
    );

    render(<Validaciones />);

    await waitFor(() => {
      expect(screen.getByText('Juan Prevalidado')).toBeInTheDocument();
      expect(screen.getByText('Ana Compradora')).toBeInTheDocument();
    });

    // Standalone muestra badge "Prevalidación"; trámite muestra referenceNumber
    expect(screen.getByText('Prevalidación')).toBeInTheDocument();
    expect(screen.getByText('TRM-2026-000099')).toBeInTheDocument();
  });

  it('el botón "Nueva prevalidación" vive en la pantalla principal de Identidad', async () => {
    const user = userEvent.setup();
    mocks.listTenantBiometricPersons.mockResolvedValue(personsResponse([RESULT_STANDALONE]));

    render(<Validaciones />);
    await screen.findByText('Juan Prevalidado');

    await user.click(
      screen.getByRole('button', { name: /crear nueva prevalidación de identidad/i }),
    );

    // El formulario se abre en el mismo módulo: ya no hay pantalla ni pestaña aparte.
    expect(
      await screen.findByRole('dialog', { name: /nueva prevalidación de identidad/i }),
    ).toBeInTheDocument();
  });

  it('documento repetido desde el módulo: avisa que ya existía y confirma el reenvío', async () => {
    const user = userEvent.setup();
    const { TramitesApiError } = await import('@/lib/api/tramites-client');
    mocks.listTenantBiometricPersons.mockResolvedValue(personsResponse([RESULT_STANDALONE]));
    mocks.createPrevalidacion.mockRejectedValueOnce(
      new TramitesApiError(409, 'Conflict', {
        motivo: 'validacion_en_vuelo',
        status: 'enviado',
        validationId: 'pv-1',
        origen: 'standalone',
      }),
    );
    mocks.editPrevalidacion.mockResolvedValueOnce({
      validation: { id: 'pv-1', email: 'nuevo@correo.co' },
      captureUrl: 'https://capture.kyverum.co/nuevo',
      resent: true,
    });

    render(<Validaciones />);
    await screen.findByText('Juan Prevalidado');

    await user.click(
      screen.getByRole('button', { name: /crear nueva prevalidación de identidad/i }),
    );
    await user.type(screen.getByLabelText(/número de documento/i), '9876543210');
    await user.type(screen.getByLabelText(/nombre completo/i), 'Juan Prevalidado');
    await user.type(screen.getByLabelText(/correo electrónico/i), 'nuevo@correo.co');
    await user.click(screen.getByRole('button', { name: /crear prevalidación/i }));
    await user.click(screen.getByRole('button', { name: /confirmar y enviar/i }));

    // El aviso sale visible en el panel y también anunciado a lectores de pantalla (sr-only).
    expect(
      await screen.findAllByText(/ya existía una validación para este documento en este tenant/i),
    ).toHaveLength(2);
    expect(screen.getByText(/se envió un enlace nuevo a nuevo@correo\.co/i)).toBeInTheDocument();
  });
});
