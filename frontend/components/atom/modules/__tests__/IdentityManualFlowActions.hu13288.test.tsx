/**
 * HU #13288 (Feature #13280, Épica #13202) — «Activar flujo manual» y «Regenerar enlace» para el Super Admin
 * en el detalle de una validación de identidad.
 *
 * AC1 — Super Admin con validación no aprobada/vencida ve «Activar flujo manual».
 * AC2 — Otros roles no ven el botón.
 * AC3 — Aprobada y vigente: no se muestra.
 * AC4 — Confirmar en el modal llama al endpoint y el detalle pasa a manual_activo con la caducidad + «Regenerar enlace».
 * AC5 — 409/403/404: mensaje claro sin romper el detalle.
 */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  superAdmin: true,
  activateManualIdentity: vi.fn(),
  regenerateManualLink: vi.fn(),
  listPersonBiometricValidations: vi.fn(),
  listNetworkPersonIdentityValidations: vi.fn(),
}));

vi.mock('@/lib/api/client', () => ({ getToken: () => 'token' }));
vi.mock('@/lib/auth/jwt', () => ({
  decodeJwtPayload: () => ({}),
  isSuperAdmin: () => mocks.superAdmin,
  hasPermission: () => false,
}));
vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    activateManualIdentity: mocks.activateManualIdentity,
    regenerateManualLink: mocks.regenerateManualLink,
    listPersonBiometricValidations: mocks.listPersonBiometricValidations,
    listNetworkPersonIdentityValidations: mocks.listNetworkPersonIdentityValidations,
  },
}));
vi.mock('@/components/atom/IdentityValidationTrackingPanel', () => ({
  IdentityValidationTrackingPanel: () => <div data-testid="tracking" />,
}));

import { IdentityManualFlowActions } from '@/components/atom/modules/IdentityManualFlowActions';
import { PersonIdentityDetailDrawer } from '@/components/atom/modules/PersonIdentityDetailDrawer';
import { esAprobadaVigente } from '@/lib/identity/manual-flow';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

function validation(overrides: Partial<BiometricValidation> = {}): BiometricValidation {
  return {
    id: 'val-1',
    partyRole: null,
    name: 'Ana Ríos',
    documentType: 'CC',
    documentNumber: '1020304050',
    email: 'ana@example.com',
    status: 'expirado',
    intentos: 0,
    maxIntentos: 3,
    score: null,
    expiresAt: '2026-10-06T15:30:00Z',
    validatedAt: null,
    expired: true,
    provider: 'kyverum',
    captureUrl: null,
    createdAt: '2026-10-01T10:00:00Z',
    ...overrides,
  };
}

function problem(status: number, title: string) {
  return Object.assign(new Error(`${status} ${title}`), { status, problem: { title } });
}

const okActivation = {
  validationId: 'val-1',
  tenantId: 't',
  procedureInstanceId: null,
  provider: 'manual',
  status: 'manual_activo',
  expiresAt: '2026-10-06T15:30:00Z',
  activatedAt: '2026-10-05T15:30:00Z',
  kyverumCancelado: true,
  origin: 'tramite',
  emailEnviado: true,
};

beforeEach(() => {
  vi.clearAllMocks();
  mocks.superAdmin = true;
});

describe('visibilidad del botón (AC1-AC3)', () => {
  it('Super Admin con validación vencida ve «Activar flujo manual»', () => {
    render(<IdentityManualFlowActions validation={validation()} onChanged={vi.fn()} />);
    expect(screen.getByRole('button', { name: 'Activar flujo manual' })).toBeInTheDocument();
  });

  it('un usuario que no es Super Admin no ve nada', () => {
    mocks.superAdmin = false;
    const { container } = render(<IdentityManualFlowActions validation={validation()} onChanged={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('con la identidad aprobada y vigente no se muestra', () => {
    const reciente = new Date(Date.now() - 2 * 86_400_000).toISOString();
    const { container } = render(
      <IdentityManualFlowActions
        validation={validation({ status: 'aprobado', expired: false, validatedAt: reciente })}
        onChanged={vi.fn()}
      />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('aprobada pero con la vigencia vencida (más de 30 días) sí se ofrece', () => {
    const vieja = new Date(Date.now() - 45 * 86_400_000).toISOString();
    expect(esAprobadaVigente({ status: 'aprobado', validatedAt: vieja })).toBe(false);
    render(
      <IdentityManualFlowActions validation={validation({ status: 'aprobado', validatedAt: vieja })} onChanged={vi.fn()} />,
    );
    expect(screen.getByRole('button', { name: 'Activar flujo manual' })).toBeInTheDocument();
  });

  it('en pendiente de revisión manual no se ofrece activar de nuevo', () => {
    const { container } = render(
      <IdentityManualFlowActions
        validation={validation({ status: 'pendiente_revision_manual', provider: 'manual', expired: false })}
        onChanged={vi.fn()}
      />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});

describe('modal de activación (AC4)', () => {
  it('sin Kyverum en curso muestra el texto corto y no el aviso de cancelación', async () => {
    const user = userEvent.setup();
    render(<IdentityManualFlowActions validation={validation()} onChanged={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Activar flujo manual' }));
    const dialog = screen.getByRole('dialog', { name: 'Activar flujo manual' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(within(dialog).getByText('Se enviará al cliente un enlace de captura por correo. El enlace caduca en 24 horas.')).toBeInTheDocument();
    expect(within(dialog).queryByText(/Se cancelará la validación de Kyverum/)).not.toBeInTheDocument();
  });

  it.each(['enviado', 'en_proceso', 'pendiente_envio'] as const)(
    'con Kyverum en curso (%s) avisa que se cancelará',
    async (status) => {
      const user = userEvent.setup();
      render(
        <IdentityManualFlowActions validation={validation({ status, expired: false })} onChanged={vi.fn()} />,
      );
      await user.click(screen.getByRole('button', { name: 'Activar flujo manual' }));
      expect(screen.getByText('Se cancelará la validación de Kyverum en curso')).toBeInTheDocument();
    },
  );

  it('confirmar llama al endpoint, cierra el modal y avisa para recargar el detalle', async () => {
    const user = userEvent.setup();
    const onChanged = vi.fn();
    mocks.activateManualIdentity.mockResolvedValue(okActivation);
    render(<IdentityManualFlowActions validation={validation()} onChanged={onChanged} />);
    await user.click(screen.getByRole('button', { name: 'Activar flujo manual' }));
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
    expect(mocks.activateManualIdentity).toHaveBeenCalledWith('val-1');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByText(/No se pudo enviar el correo/)).not.toBeInTheDocument();
  });

  it('con emailEnviado=false avisa que el correo no salió (la activación quedó hecha)', async () => {
    const user = userEvent.setup();
    const onChanged = vi.fn();
    mocks.activateManualIdentity.mockResolvedValue({ ...okActivation, emailEnviado: false });
    render(<IdentityManualFlowActions validation={validation()} onChanged={onChanged} />);
    await user.click(screen.getByRole('button', { name: 'Activar flujo manual' }));
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    expect(
      await screen.findByText('No se pudo enviar el correo al titular. Regenera el enlace cuando tenga un correo válido.'),
    ).toBeInTheDocument();
    expect(onChanged).toHaveBeenCalled();
  });

  it('mientras envía deshabilita los botones y marca aria-busy', async () => {
    const user = userEvent.setup();
    let resolver: (v: unknown) => void = () => {};
    mocks.activateManualIdentity.mockReturnValue(new Promise((r) => (resolver = r)));
    render(<IdentityManualFlowActions validation={validation()} onChanged={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Activar flujo manual' }));
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    expect(screen.getByRole('button', { name: 'Enviando…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    resolver(okActivation);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });
});

describe('errores (AC5)', () => {
  it.each([
    [409, 'identidad_aprobada_vigente', 'Esta validación ya está aprobada y vigente.'],
    [409, 'identidad_manual', 'Esta validación ya está en flujo manual.'],
    [409, 'tramite_inactivo', 'El trámite está anulado o revocado.'],
    [403, 'Forbidden', 'No tienes permiso para esta acción. Solo el Super Admin puede hacerla.'],
    [404, 'Not Found', 'No se encontró la validación. Actualiza el detalle.'],
    [500, 'boom', 'No se pudo activar el flujo manual. Inténtalo de nuevo.'],
  ])('%i %s muestra «%s» sin cerrar el modal', async (status, title, mensaje) => {
    const user = userEvent.setup();
    const onChanged = vi.fn();
    mocks.activateManualIdentity.mockRejectedValue(problem(status, title));
    render(<IdentityManualFlowActions validation={validation()} onChanged={onChanged} />);
    await user.click(screen.getByRole('button', { name: 'Activar flujo manual' }));
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(mensaje);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(onChanged).not.toHaveBeenCalled();
    // Se puede reintentar: el botón vuelve a estar disponible.
    expect(screen.getByRole('button', { name: 'Confirmar' })).toBeEnabled();
  });
});

describe('flujo manual activo: chip y regenerar', () => {
  const activa = () =>
    validation({ status: 'manual_activo', provider: 'manual', expired: false, expiresAt: '2026-10-06T15:30:00Z' });

  it('muestra el chip «Flujo manual activo», el vencimiento DD/MM/YYYY HH:mm y «Regenerar enlace»', () => {
    render(<IdentityManualFlowActions validation={activa()} onChanged={vi.fn()} />);
    expect(screen.getByRole('status', { name: 'Flujo manual activo' })).toBeInTheDocument();
    expect(screen.getByText(/^06\/10\/2026 10:30$/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Regenerar enlace' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Activar flujo manual' })).not.toBeInTheDocument();
  });

  it('regenerar pide confirmación, llama al endpoint y recarga', async () => {
    const user = userEvent.setup();
    const onChanged = vi.fn();
    mocks.regenerateManualLink.mockResolvedValue({ ...okActivation, emailEnviado: true });
    render(<IdentityManualFlowActions validation={activa()} onChanged={onChanged} />);
    await user.click(screen.getByRole('button', { name: 'Regenerar enlace' }));
    expect(mocks.regenerateManualLink).not.toHaveBeenCalled();
    const dialog = screen.getByRole('dialog', { name: 'Regenerar enlace' });
    expect(within(dialog).getByText('El enlace anterior dejará de funcionar y se enviará uno nuevo por correo.')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Confirmar' }));
    await waitFor(() => expect(onChanged).toHaveBeenCalledTimes(1));
    expect(mocks.regenerateManualLink).toHaveBeenCalledWith('val-1');
  });

  it('regenerar con emailEnviado=false muestra el aviso', async () => {
    const user = userEvent.setup();
    mocks.regenerateManualLink.mockResolvedValue({ ...okActivation, emailEnviado: false });
    render(<IdentityManualFlowActions validation={activa()} onChanged={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Regenerar enlace' }));
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    expect(await screen.findByText(/No se pudo enviar el correo al titular/)).toBeInTheDocument();
  });

  it('regenerar: 409 flujo_manual_no_activo y 404 muestran mensaje claro', async () => {
    const user = userEvent.setup();
    mocks.regenerateManualLink.mockRejectedValueOnce(problem(409, 'flujo_manual_no_activo'));
    render(<IdentityManualFlowActions validation={activa()} onChanged={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Regenerar enlace' }));
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('El flujo manual ya no está activo');
    mocks.regenerateManualLink.mockRejectedValueOnce(problem(404, 'Not Found'));
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('No se encontró la validación'));
  });
});

describe('validación manual rechazada: esperando nueva captura', () => {
  const futuro = () => new Date(Date.now() + 20 * 3_600_000).toISOString();
  const pasado = () => new Date(Date.now() - 3_600_000).toISOString();
  const rechazada = (expiresAt: string) =>
    validation({ status: 'rechazado', provider: 'manual', expired: false, expiresAt });

  it('con enlace vigente muestra el chip «Rechazada · esperando nueva captura», el vencimiento y «Regenerar enlace», sin «Activar»', () => {
    render(<IdentityManualFlowActions validation={rechazada(futuro())} onChanged={vi.fn()} />);
    expect(screen.getByRole('status', { name: 'Rechazada · esperando nueva captura' })).toBeInTheDocument();
    expect(screen.queryByText('Flujo manual activo')).not.toBeInTheDocument();
    expect(screen.getByText(/El enlace vence:/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Regenerar enlace' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Activar flujo manual' })).not.toBeInTheDocument();
  });

  it('con enlace vencido ofrece «Regenerar enlace» y también «Activar flujo manual»', async () => {
    const user = userEvent.setup();
    render(<IdentityManualFlowActions validation={rechazada(pasado())} onChanged={vi.fn()} />);
    expect(screen.getByRole('status', { name: 'Rechazada · esperando nueva captura' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Acciones del flujo manual de identidad' }));
    expect(screen.getByRole('menuitem', { name: 'Regenerar enlace' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Activar flujo manual' })).toBeInTheDocument();
  });

  it('rechazada con Kyverum (no manual) sigue sin chip ni «Regenerar enlace»', () => {
    render(
      <IdentityManualFlowActions
        validation={validation({ status: 'rechazado', provider: 'kyverum', expired: false })}
        onChanged={vi.fn()}
      />,
    );
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Regenerar enlace' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Activar flujo manual' })).toBeInTheDocument();
  });
});

describe('accesibilidad del modal', () => {
  it('foco inicial dentro, trampa de Tab, Escape cierra y el foco vuelve al disparador', async () => {
    const user = userEvent.setup();
    render(<IdentityManualFlowActions validation={validation()} onChanged={vi.fn()} />);
    const trigger = screen.getByRole('button', { name: 'Activar flujo manual' });
    await user.click(trigger);
    const dialog = screen.getByRole('dialog');
    const cancelar = within(dialog).getByRole('button', { name: 'Cancelar' });
    const confirmar = within(dialog).getByRole('button', { name: 'Confirmar' });
    expect(cancelar).toHaveFocus();
    await user.tab();
    expect(confirmar).toHaveFocus();
    await user.tab();
    expect(cancelar).toHaveFocus();
    await user.tab({ shift: true });
    expect(confirmar).toHaveFocus();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });
});

describe('integración con el detalle de la persona', () => {
  it('activar actualiza el detalle en sitio: chip, vencimiento y «Regenerar enlace»; Escape del modal no cierra el detalle', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    const antes = validation();
    const despues = validation({
      status: 'manual_activo',
      provider: 'manual',
      expired: false,
      expiresAt: '2026-10-06T15:30:00Z',
    });
    mocks.listPersonBiometricValidations
      .mockResolvedValueOnce({ name: 'Ana Ríos', validations: [antes], allTerminal: true })
      .mockResolvedValue({ name: 'Ana Ríos', validations: [despues], allTerminal: false });
    mocks.activateManualIdentity.mockResolvedValue(okActivation);

    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1020304050" onClose={onClose} />);
    const activar = await screen.findByRole('button', { name: 'Activar flujo manual' });

    // Escape sobre el modal cierra solo el modal, no el panel de detalle.
    await user.click(activar);
    await user.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Activar flujo manual' }));
    await user.click(screen.getByRole('button', { name: 'Confirmar' }));
    expect(await screen.findByRole('button', { name: 'Regenerar enlace' })).toBeInTheDocument();
    expect(screen.getByText('Flujo manual activo')).toBeInTheDocument();
    expect(screen.getAllByText('Esperando captura del cliente').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Activar flujo manual' })).not.toBeInTheDocument();
  });

  it('un usuario sin rol Super Admin no ve el botón en el detalle', async () => {
    mocks.superAdmin = false;
    mocks.listPersonBiometricValidations.mockResolvedValue({
      name: 'Ana Ríos',
      validations: [validation()],
      allTerminal: true,
    });
    render(<PersonIdentityDetailDrawer documentType="CC" documentNumber="1020304050" onClose={vi.fn()} />);
    await screen.findByText('Sesión más reciente');
    expect(screen.queryByRole('button', { name: 'Activar flujo manual' })).not.toBeInTheDocument();
  });

  it('en solo consulta (compañía hija de la red) no se ofrece', async () => {
    mocks.listNetworkPersonIdentityValidations.mockResolvedValue({
      name: 'Ana Ríos',
      validations: [validation()],
      allTerminal: true,
    });
    render(
      <PersonIdentityDetailDrawer
        documentType="CC"
        documentNumber="1020304050"
        networkTenantId="hija-1"
        onClose={vi.fn()}
      />,
    );
    await screen.findByText('Sesión más reciente');
    expect(screen.queryByRole('button', { name: 'Activar flujo manual' })).not.toBeInTheDocument();
  });
});
