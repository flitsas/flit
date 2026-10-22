import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

/**
 * HU #13822 (Feature #13819) — Modal de reenvío de validación por persona, con selección múltiple
 * y «Copiar enlace». AC6 (menú deshabilitado) vive en hu12163-admin-tramite-acciones.test.tsx.
 */

const mocks = vi.hoisted(() => ({
  adminListValidacionesReenviables: vi.fn(),
  adminReenviarValidacionesLote: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: mocks,
}));

import { ReenviarValidacionIdentidadModal } from '@/components/operacion/ReenviarValidacionIdentidadModal';

function persona(over: Record<string, unknown> = {}) {
  return {
    validationId: 'v-1',
    partyRole: 'vendedor',
    ordinal: 1,
    name: 'Ana',
    email: 'ana@correo.co',
    status: 'enviado',
    captureUrl: null,
    captureExpiresAt: null,
    esFlujoManual: false,
    ...over,
  };
}

function item(over: Record<string, unknown> = {}) {
  return {
    validationId: 'v-1',
    ok: true,
    queued: false,
    captureUrl: 'https://kyverum.local/nuevo',
    emailActualizado: false,
    email: 'ana@correo.co',
    error: null,
    mensaje: null,
    ...over,
  };
}

const onSuccess = vi.fn();
const onError = vi.fn();
const onClose = vi.fn();

function renderModal(initialValidationId?: string) {
  return render(
    <ReenviarValidacionIdentidadModal
      open
      onClose={onClose}
      instanceId="inst-1"
      referenceNumber="TR-0001"
      initialValidationId={initialValidationId}
      onSuccess={onSuccess}
      onError={onError}
    />,
  );
}

let writeText: ReturnType<typeof vi.spyOn>;

/** user-event instala su propio portapapeles en setup(): se espía después de crearlo. */
function setupUser() {
  const user = userEvent.setup();
  writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
  return user;
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('HU #13822 — reenvío por persona', () => {
  it('AC1: solo ofrece a quien no firmó (vendedor), con su correo registrado y ya marcado', async () => {
    mocks.adminListValidacionesReenviables.mockResolvedValue({ personas: [persona()] });
    renderModal();

    const casilla = await screen.findByRole('checkbox', { name: 'Vendedor · Ana' });
    expect(casilla).toBeChecked();
    expect(screen.getByText('ana@correo.co')).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /Comprador/ })).not.toBeInTheDocument();
    expect(mocks.adminListValidacionesReenviables).toHaveBeenCalledWith('inst-1', undefined);
  });

  it('AC2: una casilla por persona; se marcan varias y se reenvía en un solo clic', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables.mockResolvedValue({
      personas: [
        persona(),
        persona({ validationId: 'v-2', ordinal: 2, name: 'Luis', email: 'luis@correo.co' }),
        persona({ validationId: 'v-3', partyRole: 'comprador', name: 'Pedro', email: 'p@correo.co' }),
      ],
    });
    mocks.adminReenviarValidacionesLote.mockResolvedValue({
      items: [item(), item({ validationId: 'v-3', email: 'p@correo.co' })],
    });
    renderModal();

    const ana = await screen.findByRole('checkbox', { name: 'Vendedor · Ana' });
    const luis = screen.getByRole('checkbox', { name: 'Vendedor · Luis' });
    const pedro = screen.getByRole('checkbox', { name: 'Comprador · Pedro' });
    expect(ana).not.toBeChecked();
    expect(luis).not.toBeChecked();
    expect(screen.getByRole('button', { name: 'Reenviar' })).toBeDisabled();

    await user.click(ana);
    await user.click(pedro);
    await user.click(screen.getByRole('button', { name: 'Reenviar' }));

    expect(mocks.adminReenviarValidacionesLote).toHaveBeenCalledTimes(1);
    expect(mocks.adminReenviarValidacionesLote).toHaveBeenCalledWith(
      'inst-1',
      [
        { validationId: 'v-1', email: null },
        { validationId: 'v-3', email: null },
      ],
      undefined,
    );
  });

  it('AC3: correo nuevo por persona marcada; formato inválido bloquea con «Correo inválido.»', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables.mockResolvedValue({
      personas: [persona(), persona({ validationId: 'v-2', ordinal: 2, name: 'Luis' })],
    });
    mocks.adminReenviarValidacionesLote.mockResolvedValue({
      items: [item({ emailActualizado: true, email: 'nuevo@correo.com' })],
    });
    renderModal();

    await user.click(await screen.findByRole('checkbox', { name: 'Vendedor · Ana' }));
    // Luis no está marcado: no tiene campo de correo.
    const grupoLuis = screen.getByRole('group', { name: 'Vendedor · Luis' });
    expect(within(grupoLuis).queryByLabelText(/nuevo correo/i)).not.toBeInTheDocument();

    const grupoAna = screen.getByRole('group', { name: 'Vendedor · Ana' });
    const campo = within(grupoAna).getByLabelText(/nuevo correo/i);
    const reenviar = screen.getByRole('button', { name: 'Reenviar' });

    await user.type(campo, 'sin-arroba');
    const aviso = await within(grupoAna).findByText('Correo inválido.');
    expect(aviso).toHaveStyle({ color: '#FF4E00' });
    expect(reenviar).toBeDisabled();

    await user.clear(campo);
    await user.type(campo, 'nuevo@correo.com');
    expect(reenviar).not.toBeDisabled();
    await user.click(reenviar);

    expect(mocks.adminReenviarValidacionesLote).toHaveBeenCalledWith(
      'inst-1',
      [{ validationId: 'v-1', email: 'nuevo@correo.com' }],
      undefined,
    );
  });

  it('AC4: «Copiar enlace» solo con enlace vigente; copia y avisa «Enlace copiado.»', async () => {
    const user = setupUser();
    const futuro = new Date(Date.now() + 3_600_000).toISOString();
    const pasado = new Date(Date.now() - 3_600_000).toISOString();
    mocks.adminListValidacionesReenviables.mockResolvedValue({
      personas: [
        persona({ captureUrl: 'https://kyverum.local/vigente', captureExpiresAt: futuro }),
        persona({
          validationId: 'v-2',
          name: 'Luis',
          captureUrl: 'https://kyverum.local/vencido',
          captureExpiresAt: pasado,
        }),
        persona({ validationId: 'v-3', name: 'Sin', captureUrl: null }),
      ],
    });
    renderModal();

    const grupoAna = await screen.findByRole('group', { name: 'Vendedor · Ana' });
    expect(
      within(screen.getByRole('group', { name: 'Vendedor · Luis' })).queryByRole('button', {
        name: 'Copiar enlace',
      }),
    ).not.toBeInTheDocument();
    expect(
      within(screen.getByRole('group', { name: 'Vendedor · Sin' })).queryByRole('button', {
        name: 'Copiar enlace',
      }),
    ).not.toBeInTheDocument();

    await user.click(within(grupoAna).getByRole('button', { name: 'Copiar enlace' }));
    expect(writeText).toHaveBeenCalledWith('https://kyverum.local/vigente');
    const ok = await within(grupoAna).findByText('Enlace copiado.');
    expect(ok).toHaveStyle({ color: '#8CC63F' });
  });

  it('AC4: tras reenviar el modal NO se cierra y muestra el enlace nuevo con «Copiar enlace»', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables.mockResolvedValue({ personas: [persona()] });
    mocks.adminReenviarValidacionesLote.mockResolvedValue({ items: [item()] });
    renderModal();

    await user.click(await screen.findByRole('button', { name: 'Reenviar' }));

    expect(await screen.findByText('Validación de identidad reenviada.')).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
    // El padre recarga el listado en onSuccess y esa recarga desmonta la fila con el modal:
    // el aviso solo puede salir al cerrar (validación en navegador, épica #13817).
    expect(onSuccess).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Copiar enlace' }));
    expect(writeText).toHaveBeenCalledWith('https://kyverum.local/nuevo');
    expect(await screen.findByText('Enlace copiado.')).toBeInTheDocument();

    const botonesCerrar = screen.getAllByRole('button', { name: 'Cerrar' });
    await user.click(botonesCerrar[botonesCerrar.length - 1]!);
    expect(onSuccess).toHaveBeenCalledWith('Validación de identidad reenviada.');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('AC4: el enlace se ve completo y se copia con dominio aunque el proveedor lo devuelva relativo', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables.mockResolvedValue({ personas: [persona()] });
    mocks.adminReenviarValidacionesLote.mockResolvedValue({
      items: [item({ captureUrl: '/api/v1/public/biometric/tok-123' })],
    });
    renderModal();

    await user.click(await screen.findByRole('button', { name: 'Reenviar' }));

    const esperado = `${window.location.origin}/api/v1/public/biometric/tok-123`;
    const enlace = await screen.findByRole('link', { name: 'Abrir enlace de captura' });
    expect(enlace).toHaveAttribute('href', esperado);
    expect(enlace).toHaveTextContent(esperado);

    await user.click(screen.getByRole('button', { name: 'Copiar enlace' }));
    expect(writeText).toHaveBeenCalledWith(esperado);
  });

  it('AC4: cerrar sin haber reenviado no avisa éxito ni recarga el listado', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables.mockResolvedValue({ personas: [persona()] });
    renderModal();

    await user.click(await screen.findByRole('button', { name: 'Cancelar' }));

    expect(onSuccess).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('AC5: resultado parcial dice para quién salió y para quién no, con el mensaje', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables.mockResolvedValue({
      personas: [persona(), persona({ validationId: 'v-2', ordinal: 2, name: 'Luis' })],
    });
    mocks.adminReenviarValidacionesLote.mockResolvedValue({
      items: [
        item(),
        item({
          validationId: 'v-2',
          ok: false,
          captureUrl: null,
          error: 'identidad_aprobada',
          mensaje: 'Luis ya firmó: no hay nada que reenviar.',
        }),
      ],
    });
    renderModal();

    await user.click(await screen.findByRole('checkbox', { name: 'Vendedor · Ana' }));
    await user.click(screen.getByRole('checkbox', { name: 'Vendedor · Luis' }));
    await user.click(screen.getByRole('button', { name: 'Reenviar' }));

    expect(
      await screen.findByText('Salió para: Vendedor · Ana. No salió para: Vendedor · Luis.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Luis ya firmó: no hay nada que reenviar.')).toBeInTheDocument();
    // Solo Ana tiene enlace que copiar.
    expect(screen.getAllByRole('button', { name: 'Copiar enlace' })).toHaveLength(1);
  });

  it('AC5: reenvío en cola muestra el aviso y no ofrece enlace para copiar', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables.mockResolvedValue({ personas: [persona()] });
    mocks.adminReenviarValidacionesLote.mockResolvedValue({
      items: [item({ queued: true, captureUrl: null })],
    });
    renderModal();

    await user.click(await screen.findByRole('button', { name: 'Reenviar' }));

    const cola =
      'El reenvío quedó en cola: el proveedor tuvo una falla transitoria y se reintentará automáticamente.';
    expect((await screen.findAllByText(cola)).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Copiar enlace' })).not.toBeInTheDocument();
  });

  it('AC6: sin personas pendientes muestra «Comprador y vendedor ya firmaron. No hay a quién reenviar.»', async () => {
    mocks.adminListValidacionesReenviables.mockResolvedValue({ personas: [] });
    renderModal();

    expect(
      await screen.findByText('Comprador y vendedor ya firmaron. No hay a quién reenviar.'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reenviar' })).not.toBeInTheDocument();
  });

  it('persona en flujo manual: deshabilitada y con el aviso, sin permitir marcarla', async () => {
    mocks.adminListValidacionesReenviables.mockResolvedValue({
      personas: [persona({ esFlujoManual: true })],
    });
    renderModal();

    const casilla = await screen.findByRole('checkbox', { name: 'Vendedor · Ana' });
    expect(casilla).toBeDisabled();
    expect(casilla).not.toBeChecked();
    expect(
      screen.getByText('En validación manual: el enlace se regenera desde la revisión manual.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reenviar' })).toBeDisabled();
  });

  it('rol locatario se etiqueta «Locatario»', async () => {
    mocks.adminListValidacionesReenviables.mockResolvedValue({
      personas: [persona({ partyRole: 'locatario', name: 'Mara' })],
    });
    renderModal();
    expect(await screen.findByRole('checkbox', { name: 'Locatario · Mara' })).toBeInTheDocument();
  });

  it('initialValidationId: queda marcada si está; si no está, avisa con un mensaje claro', async () => {
    mocks.adminListValidacionesReenviables.mockResolvedValue({
      personas: [persona(), persona({ validationId: 'v-2', ordinal: 2, name: 'Luis' })],
    });
    const { unmount } = renderModal('v-2');
    expect(await screen.findByRole('checkbox', { name: 'Vendedor · Luis' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Vendedor · Ana' })).not.toBeChecked();
    unmount();

    renderModal('no-existe');
    expect(
      await screen.findByText(/ya no está pendiente de firma/),
    ).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Vendedor · Ana' })).not.toBeChecked();
  });

  it('estado de error al cargar: ofrece reintentar y se recupera', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables
      .mockRejectedValueOnce(new Error('Falla de red'))
      .mockResolvedValueOnce({ personas: [persona()] });
    renderModal();

    expect(await screen.findByText(/Falla de red/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /reintentar/i }));
    await waitFor(() =>
      expect(screen.getByRole('checkbox', { name: 'Vendedor · Ana' })).toBeInTheDocument(),
    );
  });

  it('error del servidor al reenviar (422): se muestra el detalle y el modal sigue abierto', async () => {
    const user = setupUser();
    mocks.adminListValidacionesReenviables.mockResolvedValue({ personas: [persona()] });
    mocks.adminReenviarValidacionesLote.mockRejectedValue(new Error('El trámite está anulado.'));
    renderModal();

    await user.click(await screen.findByRole('button', { name: 'Reenviar' }));

    expect(await screen.findByText('El trámite está anulado.')).toBeInTheDocument();
    expect(onError).toHaveBeenCalledWith('El trámite está anulado.');
    expect(onClose).not.toHaveBeenCalled();
  });
});
