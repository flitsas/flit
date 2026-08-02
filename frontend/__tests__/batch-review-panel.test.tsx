import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BatchReviewPanel } from '@/components/operacion/BatchReviewPanel';
import { buildReviewItems, type BatchReviewState } from '@/hooks/useProcedureBatchUpload';
import type { BatchOcrPiece, ProcedureAttachment } from '@/lib/api/types/procedure-runtime';

function pieza(over: Partial<BatchOcrPiece> = {}): BatchOcrPiece {
  return {
    tipo: 'soat',
    sourceFilename: 'expediente.pdf',
    filename: 'soat_expediente.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 2048,
    paginas: [5, 6, 7],
    totalPaginasOrigen: 16,
    confianza: 0.93,
    motivo: 'Poliza SOAT de aseguradora colombiana',
    data: { es_valido: true, numero_poliza: 'SOAT-123', aseguradora: 'SEGUROS DEL ESTADO' },
    analisisError: null,
    contentBase64: 'JVBERi0=',
    ...over,
  };
}

function estado(over: Partial<BatchReviewState> = {}): BatchReviewState {
  return {
    phase: 'reviewing',
    items: [],
    noReconocidos: [],
    errores: [],
    archivos: [],
    error: null,
    ...over,
  };
}

function renderPanel(state: BatchReviewState, onToggle = vi.fn(), onConfirm = vi.fn()) {
  const aceptadas = state.items.filter((i) => i.decision === 'accept');
  render(
    <BatchReviewPanel
      state={state}
      aceptadas={aceptadas}
      onToggle={onToggle}
      onConfirm={onConfirm}
      onCancel={vi.fn()}
    />,
  );
  return { onToggle, onConfirm };
}

describe('BatchReviewPanel', () => {
  it('muestra la pieza con su tipo, certeza y rango de páginas recortado', () => {
    renderPanel(estado({ items: buildReviewItems([pieza()], [], null) }));

    expect(screen.getByText('SOAT')).toBeInTheDocument();
    expect(screen.getByText(/93% de certeza/)).toBeInTheDocument();
    expect(screen.getByText(/págs\. 5–7 de 16/)).toBeInTheDocument();
    expect(screen.getByText(/De expediente\.pdf/)).toBeInTheDocument();
  });

  it('reutiliza el resumen OCR del cargue campo a campo', () => {
    renderPanel(estado({ items: buildReviewItems([pieza()], [], null) }));

    // Mismos campos y mismas etiquetas que ve el operador al cargar el SOAT en su casilla.
    expect(screen.getByText('SOAT-123')).toBeInTheDocument();
    expect(screen.getByText('SEGUROS DEL ESTADO')).toBeInTheDocument();
  });

  it('una pieza válida llega marcada', () => {
    renderPanel(estado({ items: buildReviewItems([pieza()], [], null) }));

    expect(screen.getByRole('checkbox')).toBeChecked();
    expect(screen.getByRole('button', { name: /Adjuntar 1 documento/ })).toBeEnabled();
  });

  it('avisa del conflicto y llega desmarcada cuando la casilla ya tiene documento', () => {
    const adjunto = { id: 'a1', tipo: 'soat', filename: 'soat-viejo.pdf' } as ProcedureAttachment;

    renderPanel(estado({ items: buildReviewItems([pieza()], [adjunto], null) }));

    expect(screen.getByRole('checkbox')).not.toBeChecked();
    expect(screen.getByText(/Ya hay un documento en esta casilla \(soat-viejo\.pdf\)/)).toBeInTheDocument();
  });

  it('avisa cuando dos piezas compiten por la misma casilla', () => {
    const items = buildReviewItems(
      [pieza({ confianza: 0.5, paginas: [2] }), pieza({ confianza: 0.95, paginas: [9] })],
      [],
      null,
    );

    renderPanel(estado({ items }));

    expect(screen.getByText(/otro documento para esta misma casilla/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Adjuntar 1 documento/ })).toBeInTheDocument();
  });

  it('marcar una pieza avisa al contenedor', async () => {
    const adjunto = { id: 'a1', tipo: 'soat', filename: 'viejo.pdf' } as ProcedureAttachment;
    const { onToggle } = renderPanel(estado({ items: buildReviewItems([pieza()], [adjunto], null) }));

    await userEvent.click(screen.getByRole('checkbox'));

    expect(onToggle).toHaveBeenCalledWith(expect.stringContaining('soat'), 'accept');
  });

  it('sin nada marcado no se puede adjuntar', () => {
    const adjunto = { id: 'a1', tipo: 'soat', filename: 'viejo.pdf' } as ProcedureAttachment;

    renderPanel(estado({ items: buildReviewItems([pieza()], [adjunto], null) }));

    expect(screen.getByRole('button', { name: /Adjuntar 0 documentos/ })).toBeDisabled();
  });

  it('lista las páginas sin clasificar con la salida concreta', () => {
    renderPanel(
      estado({
        items: buildReviewItems([pieza()], [], null),
        noReconocidos: [{ sourceFilename: 'expediente.pdf', paginas: [1, 8, 9], totalPaginas: 16 }],
      }),
    );

    expect(screen.getByText(/3 de 16 páginas sin clasificar/)).toBeInTheDocument();
    expect(screen.getByText(/cárgalo directamente en su casilla/)).toBeInTheDocument();
  });

  it('lista los archivos que no se pudieron procesar con su motivo', () => {
    renderPanel(
      estado({
        errores: [{ filename: 'roto.zip', motivo: 'No se pudo abrir el comprimido.' }],
      }),
    );

    expect(screen.getByRole('alert')).toHaveTextContent('roto.zip');
    expect(screen.getByRole('alert')).toHaveTextContent('No se pudo abrir el comprimido.');
  });

  it('cuando no se identificó nada lo dice sin pintar una lista vacía', () => {
    renderPanel(estado());

    expect(screen.getByText(/No pudimos identificar documentos/)).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('durante la subida se bloquean las acciones', () => {
    renderPanel(estado({ phase: 'uploading', items: buildReviewItems([pieza()], [], null) }));

    expect(screen.getByRole('checkbox')).toBeDisabled();
    expect(screen.getByRole('button', { name: /Adjuntando/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Descartar' })).toBeDisabled();
  });
});
