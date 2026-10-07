/**
 * HU-C6 (#13301) — detalle de una validación manual: 4 capturas con alt funcional, visor ampliable con
 * teclado, constancia de consentimiento, URLs de blob revocadas y estados cargando/vacío/error/lleno.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ManualReviewDetailModal } from '@/components/atom/modules/ManualReviewDetailModal';
import { createMockManualReviewClient } from '@/lib/api/manual-review-mock';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';

const NOW = new Date('2026-10-05T15:00:00Z');
const PENDIENTE = 'mock-manual-01';
const SIN_CAPTURA = 'mock-manual-02';

let creadas: string[];
let revocadas: string[];

beforeEach(() => {
  creadas = [];
  revocadas = [];
  let n = 0;
  URL.createObjectURL = vi.fn(() => {
    const u = `blob:mock/${++n}`;
    creadas.push(u);
    return u;
  });
  URL.revokeObjectURL = vi.fn((u: string) => {
    revocadas.push(u);
  });
});

afterEach(() => vi.restoreAllMocks());

const cliente = () => createMockManualReviewClient({ delayMs: 0, now: NOW });

function abrir(client: ManualReviewClient, id = PENDIENTE, onClose = vi.fn()) {
  return render(<ManualReviewDetailModal id={id} client={client} onClose={onClose} />);
}

describe('Detalle manual — lleno', () => {
  it('es un modal normal con role dialog y muestra datos, origen, fecha y constancia', async () => {
    abrir(cliente());
    const dialog = await screen.findByRole('dialog', { name: 'Detalle de validación manual' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(await within(dialog).findByText('Persona de prueba 01')).toBeInTheDocument();
    expect(within(dialog).getByText('Trámite')).toBeInTheDocument();
    expect(within(dialog).getByText('Pendiente de revisión')).toBeInTheDocument();
    expect(within(dialog).getByText(/05\/10\/2026 09:40/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Aceptado el 05\/10\/2026 09:4\d · texto consentimiento-manual-v1/)).toBeInTheDocument();
  });

  it('muestra las 4 imágenes con título y alt funcional, sin nombres de personas', async () => {
    abrir(cliente());
    for (const alt of ['fotografía de rostro', 'documento anverso', 'documento reverso', 'firma']) {
      const img = await screen.findByAltText(alt);
      expect(img.getAttribute('src')).toMatch(/^blob:/);
    }
    for (const titulo of ['Rostro', 'Documento (anverso)', 'Documento (reverso)', 'Firma']) {
      expect(screen.getByText(titulo)).toBeInTheDocument();
    }
    expect(screen.queryByAltText(/Persona de prueba/)).not.toBeInTheDocument();
  });

  it('pide las imágenes con el cliente autenticado y revoca los blob al cerrar', async () => {
    const client = cliente();
    const spy = vi.spyOn(client, 'getManualImage');
    const { unmount } = abrir(client);
    await screen.findByAltText('firma');
    expect(spy.mock.calls.map((c) => c[1]).sort()).toEqual(['anverso', 'firma', 'reverso', 'rostro']);
    expect(creadas).toHaveLength(4);
    expect(revocadas).toHaveLength(0);
    unmount();
    expect([...revocadas].sort()).toEqual([...creadas].sort());
  });
});

describe('Detalle manual — visor', () => {
  it('amplía al pulsar, navega con flechas, cierra con Escape sin cerrar el detalle y devuelve el foco', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    abrir(cliente(), PENDIENTE, onClose);
    const abrirRostro = await screen.findByRole('button', { name: 'Ampliar rostro' });
    abrirRostro.focus();
    await user.click(abrirRostro);

    const visor = await screen.findByRole('dialog', { name: 'Rostro' });
    expect(within(visor).getByAltText('fotografía de rostro')).toBeInTheDocument();
    expect(within(visor).getByText(/Rostro · 1 de 4/)).toBeInTheDocument();

    await user.keyboard('{ArrowRight}');
    expect(await screen.findByRole('dialog', { name: 'Documento (anverso)' })).toBeInTheDocument();
    await user.keyboard('{ArrowLeft}{ArrowLeft}');
    expect(await screen.findByRole('dialog', { name: 'Firma' })).toBeInTheDocument();

    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Firma' })).not.toBeInTheDocument());
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog', { name: 'Detalle de validación manual' })).toBeInTheDocument();
    expect(abrirRostro).toHaveFocus();
  });

  it('tiene botón para cerrar la imagen ampliada', async () => {
    const user = userEvent.setup();
    abrir(cliente());
    await user.click(await screen.findByRole('button', { name: 'Ampliar firma' }));
    await user.click(await screen.findByRole('button', { name: 'Cerrar imagen' }));
    expect(screen.queryByRole('dialog', { name: 'Firma' })).not.toBeInTheDocument();
  });

  it('Escape en el detalle (sin visor) lo cierra', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    abrir(cliente(), PENDIENTE, onClose);
    await screen.findByAltText('firma');
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalled();
  });
});

describe('Detalle manual — vacío y errores', () => {
  it('sin capturas: «El cliente aún no ha capturado» y la caducidad del enlace', async () => {
    abrir(cliente(), SIN_CAPTURA);
    expect(await screen.findByText('El cliente aún no ha capturado.')).toBeInTheDocument();
    expect(screen.getByText(/El enlace caduca el/)).toBeInTheDocument();
    expect(screen.getByText('Sin constancia: el cliente aún no ha aceptado.')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Capturas del cliente' })).not.toBeInTheDocument();
  });

  it('una imagen que falla muestra el marcador de error y se puede reintentar', async () => {
    const user = userEvent.setup();
    const base = cliente();
    let falla = true;
    const client: ManualReviewClient = {
      ...base,
      getManualImage: (id, kind, s) =>
        kind === 'reverso' && falla ? Promise.reject(new Error('x')) : base.getManualImage(id, kind, s),
    };
    abrir(client);
    expect(await screen.findByText('No se pudo cargar la imagen.')).toBeInTheDocument();
    expect(await screen.findByAltText('firma')).toBeInTheDocument();
    falla = false;
    await user.click(screen.getByRole('button', { name: 'Reintentar documento (reverso)' }));
    expect(await screen.findByAltText('documento reverso')).toBeInTheDocument();
  });

  it('si el detalle falla muestra el error con «Reintentar»', async () => {
    const user = userEvent.setup();
    const base = cliente();
    let falla = true;
    const client: ManualReviewClient = {
      ...base,
      getManualDetail: (id, s) => (falla ? Promise.reject(new Error('x')) : base.getManualDetail(id, s)),
    };
    abrir(client);
    expect(await screen.findByText('No se pudo cargar el detalle.')).toBeInTheDocument();
    falla = false;
    await user.click(screen.getByRole('button', { name: /Reintentar/ }));
    expect(await screen.findByText('Persona de prueba 01')).toBeInTheDocument();
  });

  it('no renderiza nada sin id', () => {
    const { container } = render(<ManualReviewDetailModal id={null} client={cliente()} onClose={() => {}} />);
    expect(container).toBeEmptyDOMElement();
  });
});
