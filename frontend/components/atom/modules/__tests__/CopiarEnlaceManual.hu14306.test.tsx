/**
 * HU #14306 (Feature #14304, Épica #14271) — «Copiar enlace» en la fila de validaciones manuales.
 *
 * AC1 — fila que espera captura con enlace vigente: copia al portapapeles y avisa «Enlace copiado».
 * AC2 — enlace vencido o no recuperable: muestra el motivo y ofrece «Regenerar enlace».
 * AC3 — fila aprobada o con captura enviada: la acción no aparece.
 * AC4 — el aviso de correo fallido (IdentityManualFlowActions.hu13288) sugiere copiar el enlace.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const mocks = vi.hoisted(() => ({
  copyManualLink: vi.fn(),
  regenerateManualLink: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: { copyManualLink: mocks.copyManualLink, regenerateManualLink: mocks.regenerateManualLink },
}));

import { ValidacionesManuales } from '@/components/atom/modules/ValidacionesManuales';
import { ToastProvider } from '@/components/admin/Toast';
import { createMockManualReviewClient } from '@/lib/api/manual-review-mock';
import { puedeCopiarEnlaceManual, motivoFalloCopiaEnlace } from '@/lib/identity/manual-flow';

const NOW = new Date('2026-10-05T15:00:00Z');
const cliente = () => createMockManualReviewClient({ count: 6, delayMs: 0, now: NOW });
const URL_SECRETA = 'https://captura.example.test/m/token-secreto-123';

function problem(status: number, code: string) {
  return Object.assign(new Error(`${status} ${code}`), { status, problem: { code } });
}

async function filaDe(nombre: string) {
  const tabla = await screen.findByRole('table', { name: 'Validaciones manuales' });
  return within(tabla).getByText(nombre).closest('tr') as HTMLElement;
}

let writeText: ReturnType<typeof vi.fn>;

beforeEach(() => {
  vi.clearAllMocks();
  writeText = vi.fn().mockResolvedValue(undefined);
});

/** userEvent.setup() instala su propio portapapeles: el espía va DESPUÉS. */
function instalarPortapapeles() {
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
}

/** Abre el menú «Acciones» de la fila; sus ítems se portalizan a document.body. */
async function abrirAcciones(user: ReturnType<typeof userEvent.setup>, fila: HTMLElement) {
  await user.click(within(fila).getByRole('button', { name: /^Acciones de/ }));
}

describe('HU #14306 — Copiar enlace', () => {
  it('AC1: copia el enlace y avisa «Enlace copiado» en una notificación flotante', async () => {
    const user = userEvent.setup();
    instalarPortapapeles();
    mocks.copyManualLink.mockResolvedValue({ url: URL_SECRETA, expiresAt: '2026-10-06T15:00:00Z' });
    render(<ToastProvider><ValidacionesManuales client={cliente()} /></ToastProvider>);
    const fila = await filaDe('Persona de prueba 02'); // manual_activo
    await abrirAcciones(user, fila);
    await user.click(await screen.findByRole('menuitem', { name: /Copiar enlace de Persona de prueba 02/ }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(URL_SECRETA));
    expect(mocks.copyManualLink).toHaveBeenCalledWith(expect.any(String));
    // Notificación flotante en su región aria-live: no se pinta entre los filtros y la tabla.
    const aviso = await screen.findByText('Enlace copiado');
    const region = aviso.closest('[role="status"]');
    expect(region).toHaveAttribute('aria-live', 'polite');
    expect(screen.getByRole('table').parentElement?.contains(aviso)).toBe(false);
    // El enlace nunca se pinta en pantalla.
    expect(document.body.textContent).not.toContain('token-secreto-123');
  });

  it('también se ofrece en una fila rechazada (enlace nuevo para repetir la captura)', async () => {
    const user = userEvent.setup();
    render(<ToastProvider><ValidacionesManuales client={cliente()} /></ToastProvider>);
    const fila = await filaDe('Persona de prueba 05');
    await abrirAcciones(user, fila);
    expect(await screen.findByRole('menuitem', { name: /Copiar enlace de/ })).toBeInTheDocument();
  });

  it('AC2: enlace vencido muestra el motivo y «Regenerar enlace» reutiliza el flujo existente', async () => {
    const user = userEvent.setup();
    instalarPortapapeles();
    mocks.copyManualLink.mockRejectedValue(problem(409, 'enlace_vencido'));
    mocks.regenerateManualLink.mockResolvedValue({ emailEnviado: true });
    render(<ToastProvider><ValidacionesManuales client={cliente()} /></ToastProvider>);
    const fila = await filaDe('Persona de prueba 02');
    await abrirAcciones(user, fila);
    await user.click(await screen.findByRole('menuitem', { name: /Copiar enlace de/ }));
    const dialogo = await screen.findByRole('dialog');
    expect(within(dialogo).getByText(/El enlace venció\./)).toBeInTheDocument();
    expect(writeText).not.toHaveBeenCalled();
    await user.click(within(dialogo).getByRole('button', { name: 'Regenerar enlace' }));
    await waitFor(() => expect(mocks.regenerateManualLink).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('AC2: enlace no recuperable también ofrece regenerar; Cancelar cierra sin regenerar', async () => {
    const user = userEvent.setup();
    instalarPortapapeles();
    mocks.copyManualLink.mockRejectedValue(problem(409, 'enlace_no_disponible'));
    render(<ToastProvider><ValidacionesManuales client={cliente()} /></ToastProvider>);
    const fila = await filaDe('Persona de prueba 02');
    await abrirAcciones(user, fila);
    await user.click(await screen.findByRole('menuitem', { name: /Copiar enlace de/ }));
    const dialogo = await screen.findByRole('dialog');
    expect(within(dialogo).getByText(/No se puede recuperar este enlace\./)).toBeInTheDocument();
    await user.click(within(dialogo).getByRole('button', { name: 'Cancelar' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(mocks.regenerateManualLink).not.toHaveBeenCalled();
  });

  it('flujo_manual_no_activo avisa sin ofrecer regenerar', async () => {
    const user = userEvent.setup();
    instalarPortapapeles();
    mocks.copyManualLink.mockRejectedValue(problem(409, 'flujo_manual_no_activo'));
    render(<ToastProvider><ValidacionesManuales client={cliente()} /></ToastProvider>);
    const fila = await filaDe('Persona de prueba 02');
    await abrirAcciones(user, fila);
    await user.click(await screen.findByRole('menuitem', { name: /Copiar enlace de/ }));
    expect(await screen.findByText('El flujo manual ya no está activo. Actualiza la lista.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('si el portapapeles falla, muestra un mensaje claro sin error técnico', async () => {
    const user = userEvent.setup();
    instalarPortapapeles();
    writeText.mockRejectedValue(new Error('NotAllowedError: secreto'));
    mocks.copyManualLink.mockResolvedValue({ url: URL_SECRETA, expiresAt: '2026-10-06T15:00:00Z' });
    render(<ToastProvider><ValidacionesManuales client={cliente()} /></ToastProvider>);
    const fila = await filaDe('Persona de prueba 02');
    await abrirAcciones(user, fila);
    await user.click(await screen.findByRole('menuitem', { name: /Copiar enlace de/ }));
    expect(await screen.findByText('No se pudo copiar el enlace. Inténtalo de nuevo.')).toBeInTheDocument();
    expect(document.body.textContent).not.toContain('NotAllowedError');
    expect(document.body.textContent).not.toContain('token-secreto-123');
  });

  it('AC3: no aparece en filas aprobada, pendiente de revisión ni expirada', async () => {
    const user = userEvent.setup();
    render(<ToastProvider><ValidacionesManuales client={cliente()} /></ToastProvider>);
    for (const n of ['01', '03', '04', '06']) {
      const fila = await filaDe(`Persona de prueba ${n}`);
      await abrirAcciones(user, fila);
      expect(await screen.findByRole('menuitem', { name: /Ver detalle/ })).toBeInTheDocument();
      expect(screen.queryByRole('menuitem', { name: /Copiar enlace/ })).not.toBeInTheDocument();
      await user.keyboard('{Escape}');
    }
  });

  it('reglas puras: estados y motivos', () => {
    expect(puedeCopiarEnlaceManual('manual_activo')).toBe(true);
    expect(puedeCopiarEnlaceManual('rechazado')).toBe(true);
    for (const s of ['aprobado', 'pendiente_revision_manual', 'expirado']) {
      expect(puedeCopiarEnlaceManual(s)).toBe(false);
    }
    expect(motivoFalloCopiaEnlace(problem(403, 'x')).mensaje).toMatch(/Super Admin/);
    expect(motivoFalloCopiaEnlace(problem(404, 'x')).mensaje).toMatch(/No se encontró/);
    expect(motivoFalloCopiaEnlace(new Error('red')).motivo).toBe('otro');
  });
});
