/**
 * Tests HU #10944 (Feature #10864, CF-03) — Editar y reenviar prevalidación desde el módulo.
 * Vitest + RTL. Mockea tramitesClient (list/edit/resend/create) y TramitesApiError.
 *
 * Cobertura de los 6 AC de la HU (ADO #10944):
 *  AC1 — Editar el correo avisa del reenvío automático.
 *  AC2 — Acción de reenvío manual.
 *  AC3 — Campos y registros no editables (documento deshabilitado; solo lectura si hay trámite).
 *  AC4 — Estados que bloquean la acción (aprobado → editar/reenviar deshabilitados + "Nueva prevalidación").
 *  AC5 — Cooldown y tope reflejados en la interfaz.
 *  AC6 — Cuatro estados de UI + errores del backend traducidos + accesibilidad.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mocks ─────────────────────────────────────────────────────────────────────
const mocks = vi.hoisted(() => ({
  listTenantBiometricValidations: vi.fn(),
  createPrevalidacion: vi.fn(),
  editPrevalidacion: vi.fn(),
  resendPrevalidacion: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    listTenantBiometricValidations: mocks.listTenantBiometricValidations,
    createPrevalidacion: mocks.createPrevalidacion,
    editPrevalidacion: mocks.editPrevalidacion,
    resendPrevalidacion: mocks.resendPrevalidacion,
  },
  TramitesApiError: class TramitesApiError extends Error {
    constructor(
      public status: number,
      message: string,
      public problem: Record<string, unknown> | null = null,
    ) {
      super(message);
      this.name = 'TramitesApiError';
    }
  },
}));

// ── Imports después de los mocks ────────────────────────────────────────────
import { PrevalidacionesModule } from '@/components/atom/modules/PrevalidacionesModule';
import type {
  TenantBiometricValidation,
  TenantBiometricValidationsResponse,
} from '@/lib/api/types/procedure-runtime';

// ── Fixtures ─────────────────────────────────────────────────────────────────

const EDITABLE: TenantBiometricValidation = {
  id: 'pv-editable',
  instanceId: null,
  referenceNumber: null,
  modalidad: null,
  partyRole: null,
  name: 'Ana Ríos',
  documentType: 'CC',
  documentNumber: '1020304050',
  status: 'enviado',
  score: null,
  provider: 'mock',
  expired: false,
  rejectionReason: null,
  createdAt: '2026-07-24T10:00:00Z',
  validatedAt: null,
  validUntil: null,
  daysRemaining: null,
  captureUrl: '/api/v1/public/biometric/tok-1',
  linkExpiresAt: '2026-07-25T10:00:00Z',
  email: 'ana.rios@old.com', // CF-05 (HU #11006)
};

const TRAMITE_ROW: TenantBiometricValidation = {
  ...EDITABLE,
  id: 'pv-tramite',
  instanceId: 'inst-99',
  name: 'Camilo Trámite',
};

const APROBADA_ROW: TenantBiometricValidation = {
  ...EDITABLE,
  id: 'pv-aprobada',
  name: 'Luisa Aprobada',
  status: 'aprobado',
  validatedAt: '2026-06-01T10:00:00Z',
  validUntil: '2026-07-01T10:00:00Z',
  daysRemaining: 0,
  captureUrl: null,
};

function listResponse(rows: TenantBiometricValidation[]): TenantBiometricValidationsResponse {
  return {
    validations: rows,
    stats: { total: rows.length, aprobadas: 0, enProceso: 0, rechazadas: 0, expiradas: 0 },
    page: 1,
    pageSize: 20,
    total: rows.length,
  };
}

describe('PrevalidacionesModule (HU #10944)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ── AC6 (parcial): 4 estados de UI ──────────────────────────────────────────

  it('estado cargando: muestra el skeleton mientras se resuelve la carga', async () => {
    let resolveFn: (value: TenantBiometricValidationsResponse) => void = () => {};
    mocks.listTenantBiometricValidations.mockImplementationOnce(
      () => new Promise((resolve) => { resolveFn = resolve; }),
    );

    render(<PrevalidacionesModule />);

    expect(screen.getByText(/cargando prevalidaciones de identidad/i)).toBeInTheDocument();

    // El efecto difiere `load` a un microtask (para no hacer setState síncrono dentro del efecto,
    // regla react-hooks/set-state-in-effect), así que la petición NO sale durante el render: hay
    // que esperar a que el cliente se haya invocado antes de resolver su promesa.
    await waitFor(() => {
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalled();
    });

    resolveFn(listResponse([]));
    await waitFor(() => {
      expect(screen.getByText(/no hay prevalidaciones aún/i)).toBeInTheDocument();
    });
  });

  it('estado vacío: sin registros muestra el mensaje y el CTA de creación', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([]));

    render(<PrevalidacionesModule />);

    await waitFor(() => {
      expect(screen.getByText(/no hay prevalidaciones aún/i)).toBeInTheDocument();
    });
  });

  it('estado error: muestra el banner y permite reintentar', async () => {
    mocks.listTenantBiometricValidations.mockRejectedValueOnce(new Error('Fallo de red'));
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([EDITABLE]));

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/no se pudieron cargar/i);
    });

    await user.click(screen.getByRole('button', { name: /reintentar/i }));

    await waitFor(() => {
      expect(screen.getByText('Ana Ríos')).toBeInTheDocument();
    });
  });

  // ── AC3 — campos/registros no editables ─────────────────────────────────────

  it('AC3: una validación de un trámite se muestra en solo lectura, sin acciones de editar/reenviar', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([TRAMITE_ROW]));

    render(<PrevalidacionesModule />);

    await waitFor(() => {
      expect(screen.getByText('Camilo Trámite')).toBeInTheDocument();
    });

    expect(screen.getByText(/solo lectura \(pertenece a un trámite\)/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /editar prevalidación de camilo trámite/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /reenviar validación de camilo trámite/i })).toBeNull();
  });

  it('AC3: el tipo y número de documento aparecen deshabilitados con la razón al editar', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([EDITABLE]));

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Ana Ríos')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /editar prevalidación de ana ríos/i }));

    const docType = screen.getByLabelText(/tipo de documento/i) as HTMLInputElement;
    const docNum = screen.getByLabelText(/número de documento/i) as HTMLInputElement;
    expect(docType).toBeDisabled();
    expect(docNum).toBeDisabled();
    expect(docType.value).toBe('CC');
    expect(docNum.value).toBe('1020304050');
    expect(screen.getByText(/no son editables porque definen la identidad/i)).toBeInTheDocument();
  });

  // ── AC4 — estados que bloquean la acción ────────────────────────────────────

  it('AC4: una identidad aprobada bloquea editar/reenviar y ofrece "Nueva prevalidación"', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([APROBADA_ROW]));

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Luisa Aprobada')).toBeInTheDocument());

    expect(screen.getByText(/identidad aprobada: no editable ni reenviable/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /editar prevalidación de luisa aprobada/i })).toBeNull();

    await user.click(screen.getByRole('button', { name: /crear nueva prevalidación para luisa aprobada/i }));

    // Se abre el formulario de creación precargado con documento y nombre de la misma persona.
    expect((screen.getByLabelText(/número de documento/i) as HTMLInputElement).value).toBe('1020304050');
    expect((screen.getByLabelText(/nombre completo/i) as HTMLInputElement).value).toBe('Luisa Aprobada');
  });

  // ── AC1 — editar el correo avisa del reenvío automático ─────────────────────

  it('AC1: editar el correo advierte del reenvío antes de guardar y muestra el resultado al confirmar', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(listResponse([EDITABLE]));
    mocks.editPrevalidacion.mockResolvedValueOnce({
      validation: { ...baseValidationDto(), email: 'ana.rios@new.com' },
      captureUrl: 'https://capture.kyverum.co/nuevo',
      resent: true,
    });

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Ana Ríos')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /editar prevalidación de ana ríos/i }));

    const emailInput = screen.getByLabelText(/nuevo correo electrónico/i);
    await user.type(emailInput, 'ana.rios@new.com');

    // Aviso ANTES de guardar (AC1).
    expect(
      screen.getByText(/se reenviará al nuevo correo y el enlace anterior dejará de funcionar/i),
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /guardar y reenviar/i }));

    await waitFor(() => {
      expect(mocks.editPrevalidacion).toHaveBeenCalledWith(
        'pv-editable',
        expect.objectContaining({ email: 'ana.rios@new.com' }),
      );
    });

    await waitFor(() => {
      expect(screen.getByText(/se envió un enlace nuevo a ana\.rios@new\.com/i)).toBeInTheDocument();
    });
    expect(screen.getByText(/reenvíos usados: 1 de 3/i)).toBeInTheDocument();
  });

  it('AC4 (variante edición): editar solo el nombre no reenvía', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(listResponse([EDITABLE]));
    mocks.editPrevalidacion.mockResolvedValueOnce({
      validation: { ...baseValidationDto(), name: 'Ana Ríos Corregida' },
      captureUrl: null,
      resent: false,
    });

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Ana Ríos')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /editar prevalidación de ana ríos/i }));

    const nameInput = screen.getByLabelText(/^nombre$/i);
    await user.clear(nameInput);
    await user.type(nameInput, 'Ana Ríos Corregida');

    expect(
      screen.queryByText(/se reenviará al nuevo correo/i),
    ).toBeNull();

    await user.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => {
      expect(mocks.editPrevalidacion).toHaveBeenCalledWith(
        'pv-editable',
        expect.objectContaining({ name: 'Ana Ríos Corregida' }),
      );
    });
    // No debe aparecer el panel de resultado de reenvío (resent=false).
    expect(screen.queryByText(/validación reenviada/i)).toBeNull();
  });

  // ── AC2 — acción de reenvío manual ──────────────────────────────────────────

  it('AC2: el reenvío manual pide confirmación y muestra el correo destino al terminar', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(listResponse([EDITABLE]));
    mocks.resendPrevalidacion.mockResolvedValueOnce({
      validation: baseValidationDto(),
      captureUrl: 'https://capture.kyverum.co/manual',
      queued: false,
    });

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Ana Ríos')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /reenviar validación de ana ríos/i }));

    expect(screen.getByRole('alertdialog', { name: /reenviar validación/i })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /confirmar reenvío/i }));

    await waitFor(() => {
      expect(mocks.resendPrevalidacion).toHaveBeenCalledWith('pv-editable');
    });
    await waitFor(() => {
      expect(screen.getByText(/se envió un enlace nuevo a ana\.rios@old\.com/i)).toBeInTheDocument();
    });
  });

  // ── AC5 — cooldown y tope reflejados en la interfaz ─────────────────────────

  it('AC5: tras un reenvío, el botón "Reenviar" queda deshabilitado con el cooldown restante', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValue(listResponse([EDITABLE]));
    mocks.resendPrevalidacion.mockResolvedValueOnce({
      validation: baseValidationDto(),
      captureUrl: 'https://capture.kyverum.co/manual',
      queued: false,
    });

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Ana Ríos')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /reenviar validación de ana ríos/i }));
    await user.click(screen.getByRole('button', { name: /confirmar reenvío/i }));

    await waitFor(() => {
      expect(screen.getByText(/se envió un enlace nuevo/i)).toBeInTheDocument();
    });
    await user.click(screen.getByRole('button', { name: /cerrar/i }));

    await waitFor(() => {
      const resendBtn = screen.getByRole('button', { name: /reenviar validación de ana ríos/i });
      expect(resendBtn).toBeDisabled();
    });
    expect(screen.getByText(/disponible en \d+ min\./i)).toBeInTheDocument();
  });

  it('AC5: un 429 de tope agotado deshabilita "Reenviar" con el mensaje del backend', async () => {
    const { TramitesApiError } = await import('@/lib/api/tramites-client');
    mocks.listTenantBiometricValidations.mockResolvedValue(listResponse([EDITABLE]));
    mocks.resendPrevalidacion.mockRejectedValueOnce(
      new TramitesApiError(
        429,
        'Se agotaron los reenvíos disponibles. Anula el registro y crea una prevalidación nueva.',
        null,
      ),
    );

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Ana Ríos')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /reenviar validación de ana ríos/i }));
    await user.click(screen.getByRole('button', { name: /confirmar reenvío/i }));

    await waitFor(() => {
      expect(screen.getByRole('alertdialog')).toHaveTextContent(/se agotaron los reenvíos disponibles/i);
    });
    await user.click(screen.getByRole('button', { name: /cancelar/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /reenviar validación de ana ríos/i })).toBeDisabled();
    });
    expect(screen.getByText(/se agotó el tope de 3 reenvíos/i)).toBeInTheDocument();
  });

  // ── AC6 — errores del backend traducidos a mensajes accionables ────────────

  it('AC6: un 403 al editar muestra el mensaje accionable del backend', async () => {
    const { TramitesApiError } = await import('@/lib/api/tramites-client');
    mocks.listTenantBiometricValidations.mockResolvedValue(listResponse([EDITABLE]));
    mocks.editPrevalidacion.mockRejectedValueOnce(
      new TramitesApiError(403, 'Esta validación pertenece a un trámite; edítala desde el trámite.', null),
    );

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Ana Ríos')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /editar prevalidación de ana ríos/i }));
    await user.type(screen.getByLabelText(/^nombre$/i), ' corregido');
    await user.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        /esta validación pertenece a un trámite; edítala desde el trámite/i,
      );
    });
  });

  it('AC6: un 409 identidad_aprobada ofrece crear una prevalidación nueva', async () => {
    const { TramitesApiError } = await import('@/lib/api/tramites-client');
    mocks.listTenantBiometricValidations.mockResolvedValue(listResponse([EDITABLE]));
    mocks.editPrevalidacion.mockRejectedValueOnce(
      new TramitesApiError(
        409,
        'La identidad ya está aprobada. Para revalidar, crea una prevalidación nueva.',
        null,
      ),
    );

    const user = userEvent.setup();
    render(<PrevalidacionesModule />);

    await waitFor(() => expect(screen.getByText('Ana Ríos')).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: /editar prevalidación de ana ríos/i }));
    await user.type(screen.getByLabelText(/nuevo correo electrónico/i), 'nuevo@correo.com');
    await user.click(screen.getByRole('button', { name: /guardar y reenviar/i }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/la identidad ya está aprobada/i);
    });
    expect(screen.getByText(/usa la acción "nueva prevalidación"/i)).toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// HU #11006 (Feature #11004) — CF-02, CF-04, CF-05
// ─────────────────────────────────────────────────────────────────────────────

describe('PrevalidacionesModule (HU #11006 — CF-02/CF-04/CF-05)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('CF-02: consulta el listado con standalone=true directo, sin fallback client-side', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([EDITABLE]));

    render(<PrevalidacionesModule />);

    await waitFor(() => {
      expect(mocks.listTenantBiometricValidations).toHaveBeenCalledWith({ standalone: true });
    });
  });

  it('CF-04: muestra el documento completo, sin enmascarar', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([EDITABLE]));

    render(<PrevalidacionesModule />);

    await waitFor(() => {
      expect(screen.getByText('CC 1020304050')).toBeInTheDocument();
    });
    expect(screen.queryByText(/••••/)).not.toBeInTheDocument();
  });

  it('CF-05: muestra la columna Correo con el valor del backend', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([EDITABLE]));

    render(<PrevalidacionesModule />);

    await waitFor(() => {
      expect(screen.getByText('ana.rios@old.com')).toBeInTheDocument();
    });
  });

  it('CF-05: muestra "—" cuando el backend aún no envía email (BE en curso), sin romper la fila', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(
      listResponse([{ ...EDITABLE, email: null }]),
    );

    render(<PrevalidacionesModule />);

    await waitFor(() => {
      expect(screen.getByText('Ana Ríos')).toBeInTheDocument();
    });
    expect(screen.getAllByText('—').length).toBeGreaterThan(0);
  });

  it('AC3: respuesta vacía con standalone=true muestra el estado vacío, sin fallback a mostrar filas de trámite', async () => {
    mocks.listTenantBiometricValidations.mockResolvedValueOnce(listResponse([]));

    render(<PrevalidacionesModule />);

    await waitFor(() => {
      expect(screen.getByText(/no hay prevalidaciones aún/i)).toBeInTheDocument();
    });
    // No debe aparecer ninguna fila (el bug preexistente de HU #10869/#10944 caía a "mostrar todas").
    expect(screen.queryByRole('list', { name: /prevalidaciones de identidad/i })).toBeNull();
  });
});

// ── Helpers ────────────────────────────────────────────────────────────────────

function baseValidationDto() {
  return {
    id: 'pv-editable',
    partyRole: null,
    name: 'Ana Ríos',
    documentType: 'CC',
    documentNumber: '1020304050',
    email: 'ana.rios@old.com',
    status: 'enviado' as const,
    intentos: 0,
    maxIntentos: 3,
    score: null,
    expiresAt: '2026-07-28T10:00:00Z',
    validatedAt: null,
    expired: false,
    provider: 'mock',
    captureUrl: null,
    rejectionReason: null,
  };
}
