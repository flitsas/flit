// Mejora (Bug #14604) — el visor compartido abre el documento en una pestaña nueva a pantalla completa.
import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DocumentPreviewModal } from '../DocumentPreviewModal';

type FakeWin = {
  closed: boolean;
  opener: unknown;
  close: ReturnType<typeof vi.fn>;
  document: { open: () => void; write: () => void; close: () => void };
  location: { replace: ReturnType<typeof vi.fn> };
};

function fakeWindow(): FakeWin {
  const w: FakeWin = {
    closed: false,
    opener: null,
    close: vi.fn(() => {
      w.closed = true;
    }),
    document: { open: vi.fn(), write: vi.fn(), close: vi.fn() },
    location: { replace: vi.fn() },
  };
  return w;
}

const base = {
  title: 'impronta.pdf',
  mimetype: 'application/pdf',
  onDownload: vi.fn(),
};

describe('DocumentPreviewModal — pestaña nueva', () => {
  let win: FakeWin;

  beforeEach(() => {
    win = fakeWindow();
    vi.spyOn(window, 'open').mockReturnValue(win as unknown as Window);
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(new Blob(['%PDF-1.4'], { type: 'application/octet-stream' }))),
    );
    URL.createObjectURL = vi.fn(() => 'blob:copia');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('abre la pestaña al abrir el visor, le pasa el archivo y no muestra el modal', async () => {
    const onClose = vi.fn();
    const { rerender } = render(
      <DocumentPreviewModal {...base} open onClose={onClose} url={null} loading error={null} />,
    );
    expect(window.open).toHaveBeenCalledWith('about:blank', '_blank');
    expect(screen.queryByTestId('preview-modal-body')).not.toBeInTheDocument();

    rerender(
      <DocumentPreviewModal {...base} open onClose={onClose} url="blob:del-llamador" loading={false} error={null} />,
    );

    await waitFor(() => expect(win.location.replace).toHaveBeenCalledWith('blob:copia'));
    expect(onClose).toHaveBeenCalled();
    expect(win.close).not.toHaveBeenCalled();
    expect(screen.queryByTestId('preview-iframe')).not.toBeInTheDocument();
  });

  it('el aviso del documento («Documento final») se muestra en la pantalla actual y se puede cerrar', async () => {
    const aviso = <p>Documento final. Expediente definitivo.</p>;
    const { rerender } = render(
      <DocumentPreviewModal {...base} open onClose={vi.fn()} url={null} loading error={null} notice={aviso} />,
    );
    rerender(
      <DocumentPreviewModal
        {...base}
        open
        onClose={vi.fn()}
        url="blob:consolidado"
        loading={false}
        error={null}
        notice={aviso}
      />,
    );

    const enPantalla = await screen.findByTestId('aviso-documento-en-pantalla');
    expect(enPantalla).toHaveTextContent('Documento final. Expediente definitivo.');

    screen.getByRole('button', { name: 'Cerrar aviso' }).click();
    await waitFor(() => expect(screen.queryByTestId('aviso-documento-en-pantalla')).not.toBeInTheDocument());
  });

  it('sin aviso no pinta nada en la pantalla actual', async () => {
    const { rerender } = render(
      <DocumentPreviewModal {...base} open onClose={vi.fn()} url={null} loading error={null} />,
    );
    rerender(<DocumentPreviewModal {...base} open onClose={vi.fn()} url="blob:x" loading={false} error={null} />);
    await waitFor(() => expect(win.location.replace).toHaveBeenCalled());
    expect(screen.queryByTestId('aviso-documento-en-pantalla')).not.toBeInTheDocument();
  });

  it('con error cierra la pestaña y muestra el aviso con «Descargar» en la pantalla actual', async () => {
    const { rerender } = render(
      <DocumentPreviewModal {...base} open onClose={vi.fn()} url={null} loading error={null} />,
    );
    rerender(
      <DocumentPreviewModal {...base} open onClose={vi.fn()} url={null} loading={false} error="No se pudo abrir." />,
    );

    await waitFor(() => expect(win.close).toHaveBeenCalled());
    expect(screen.getByTestId('preview-error')).toHaveTextContent('No se pudo abrir.');
    expect(screen.getByRole('button', { name: 'Descargar documento' })).toBeInTheDocument();
  });

  it('archivo no previsualizable: cierra la pestaña y ofrece descargarlo', async () => {
    render(
      <DocumentPreviewModal
        {...base}
        mimetype="application/zip"
        open
        onClose={vi.fn()}
        url="blob:zip"
        loading={false}
        error={null}
      />,
    );
    await waitFor(() => expect(win.close).toHaveBeenCalled());
    expect(screen.getByTestId('preview-fallback')).toBeInTheDocument();
  });

  it('si el navegador bloquea la pestaña, usa el visor en modal', () => {
    vi.spyOn(window, 'open').mockReturnValue(null);
    render(
      <DocumentPreviewModal {...base} open onClose={vi.fn()} url="blob:x" loading={false} error={null} />,
    );
    expect(screen.getByTestId('preview-iframe')).toBeInTheDocument();
  });
});
