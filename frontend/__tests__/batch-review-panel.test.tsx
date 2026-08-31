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
    progreso: null,
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

  it('reutiliza el resumen OCR del cargue campo a campo', async () => {
    const user = userEvent.setup();
    renderPanel(estado({ items: buildReviewItems([pieza()], [], null) }));

    await user.click(screen.getByRole('button', { name: /OCR SOAT: Verificado/ }));

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

  // El backend manda los NÚMEROS de página y la pantalla solo mostraba cuántas eran: con eso el
  // gestor tenía que abrir el archivo entero para dar con ellas. Ahora se listan, comprimidas en
  // rangos (8 y 9 van seguidas, así que se enumeran; un tramo de tres o más se abrevia).
  it('nombra las páginas que quedan fuera, no solo cuántas son', () => {
    renderPanel(
      estado({
        items: buildReviewItems([pieza()], [], null),
        noReconocidos: [{ sourceFilename: 'expediente.pdf', paginas: [1, 8, 9], totalPaginas: 16 }],
      }),
    );

    expect(screen.getByText(/páginas 1 y 8, 9 \(3 de 16\)/)).toBeInTheDocument();
    expect(screen.getByText(/Carga cada documento en su casilla/)).toBeInTheDocument();
  });

  it('comprime en rangos los tramos largos', () => {
    renderPanel(
      estado({
        items: buildReviewItems([pieza()], [], null),
        noReconocidos: [
          { sourceFilename: 'expediente.pdf', paginas: [4, 7, 12, 13, 14, 15, 20], totalPaginas: 30 },
        ],
      }),
    );

    expect(screen.getByText(/páginas 4, 7, 12–15 y 20 \(7 de 30\)/)).toBeInTheDocument();
  });

  // Que sobren 3 de 16 y que sobren 16 de 16 no son la misma situación: en la segunda el archivo
  // entero queda fuera y no hay nada que revisar.
  it('cuando el archivo entero queda fuera lo dice como tal', () => {
    renderPanel(
      estado({
        noReconocidos: [
          { sourceFilename: 'Fur_Borrador.pdf', paginas: [1, 2, 3], totalPaginas: 3 },
        ],
      }),
    );

    expect(
      screen.getByText(/ninguna de sus 3 páginas corresponde a un requisito de este trámite/),
    ).toBeInTheDocument();
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

    expect(screen.getByText('No reconocimos ningún documento')).toBeInTheDocument();
    expect(
      screen.getByText(/Ninguna de las páginas que cargaste corresponde a un requisito/),
    ).toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });

  // «Adjuntar 0 documentos» apagado se lee como una acción que el gestor debería poder completar.
  // Sin nada reconocido no hay nada que adjuntar: queda una sola salida.
  it('sin nada reconocido no se ofrece adjuntar', () => {
    renderPanel(estado());

    expect(screen.queryByRole('button', { name: /Adjuntar/ })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Descartar' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Entendido' })).toBeInTheDocument();
  });

  it('durante la subida se bloquean las acciones', () => {
    renderPanel(estado({ phase: 'uploading', items: buildReviewItems([pieza()], [], null) }));

    expect(screen.getByRole('checkbox')).toBeDisabled();
    expect(screen.getByRole('button', { name: /Adjuntando/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Descartar' })).toBeDisabled();
  });
});
