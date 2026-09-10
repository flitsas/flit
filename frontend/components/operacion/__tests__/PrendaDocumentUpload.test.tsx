import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  getInstance: vi.fn(),
  analyzeDocument: vi.fn(),
  persistOcrFields: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  listOcrTipos: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getChecklist: mocks.getChecklist,
    getAttachments: mocks.getAttachments,
    getInstance: mocks.getInstance,
    analyzeDocument: mocks.analyzeDocument,
    persistOcrFields: mocks.persistOcrFields,
    uploadAttachment: mocks.uploadAttachment,
    deleteAttachment: mocks.deleteAttachment,
    listOcrTipos: mocks.listOcrTipos,
  },
}));

import { PrendaDocumentUpload } from '../PrendaDocumentUpload';
import { tipoLabel } from '../DocumentChecklist';
import { resetTiposOcrCache } from '@/hooks/useProcedureDocuments';

const INSTANCE = 'inst-prenda';

function pdfFile(name = 'prenda.pdf'): File {
  const file = new File(['x'], name, { type: 'application/pdf' });
  Object.defineProperty(file, 'size', { value: 2048 });
  return file;
}

beforeEach(() => {
  vi.clearAllMocks();
  resetTiposOcrCache();
  mocks.getChecklist.mockResolvedValue({ items: [], faltanObligatorios: 0, completo: true });
  mocks.getAttachments.mockResolvedValue([]);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.persistOcrFields.mockResolvedValue(undefined);
  mocks.uploadAttachment.mockResolvedValue({ id: 'att-1' });
  // Lo que devuelve GET /ocr/tipos: `prenda_registro` entra en la lista desde la HU #12045.
  mocks.listOcrTipos.mockResolvedValue([
    'inscripcion_prenda',
    'prenda_registro',
    'soat',
  ]);
});

// ── HU #12047 — el gestor de prenda también ve el veredicto ─────────────────
// El OCR ya corría aquí (la carga pasa por el mismo `useProcedureDocuments` que el checklist), pero
// el panel se dibujaba con el CÓDIGO CRUDO por título y la rejilla VACÍA: `prenda_registro` no
// estaba en los mapas de etiqueta ni de resumen. Se pagaba el análisis y no se enseñaba el acreedor.

describe('PrendaDocumentUpload — veredicto del OCR (HU #12047)', () => {
  it('analiza el documento al subirlo bajo el DocTipo prenda_registro', async () => {
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'prenda_registro',
      data: { es_valido: true, acreedor_nombre: 'BANCO DE BOGOTA S.A.' },
    });
    const user = userEvent.setup();

    render(
      <PrendaDocumentUpload instanceId={INSTANCE} decision="registrar" docTipo="prenda_registro" />,
    );

    const input = await screen.findByLabelText(/Subir/i);
    await user.upload(input, pdfFile());

    await waitFor(() =>
      expect(mocks.analyzeDocument).toHaveBeenCalledWith(
        'prenda_registro',
        expect.any(File),
        undefined,
      ),
    );
  });

  it('muestra el acreedor y su NIT, que son el dato por el que se pide el documento', async () => {
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'prenda_registro',
      data: {
        es_valido: true,
        acreedor_nombre: 'BANCO DE BOGOTA S.A.',
        acreedor_documento: '860002964',
        vehiculo_chasis: '9F8HJD49RM640413',
      },
    });
    const user = userEvent.setup();

    render(
      <PrendaDocumentUpload instanceId={INSTANCE} decision="registrar" docTipo="prenda_registro" />,
    );

    await user.upload(await screen.findByLabelText(/Subir/i), pdfFile());

    // El veredicto se anuncia con el NOMBRE del documento. Antes decía «OCR prenda_registro»: el
    // código interno, porque el tipo no estaba en el mapa de etiquetas.
    const verdicto = await screen.findByRole('button', {
      name: /OCR Inscripción de Prenda: Verificado/i,
    });

    // Y los campos viven detrás del detalle. Antes esta rejilla salía VACÍA —sin entrada en el mapa
    // de resumen— así que el análisis se pagaba y el gestor no veía nada de lo extraído.
    await user.click(verdicto);

    expect(await screen.findByText('BANCO DE BOGOTA S.A.')).toBeInTheDocument();
    expect(screen.getByText('860002964')).toBeInTheDocument();
    expect(screen.getByText('9F8HJD49RM640413')).toBeInTheDocument();
  });

  it('el rechazo del OCR no impide que el documento se cargue', async () => {
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'prenda_registro',
      data: { es_valido: false, observaciones: 'Es un paz y salvo de prenda.' },
    });
    const user = userEvent.setup();

    render(
      <PrendaDocumentUpload instanceId={INSTANCE} decision="registrar" docTipo="prenda_registro" />,
    );

    await user.upload(await screen.findByLabelText(/Subir/i), pdfFile());

    await waitFor(() => expect(mocks.uploadAttachment).toHaveBeenCalled());
  });
});

describe('tipoLabel — alias de código (HU #12047)', () => {
  it('prenda_registro se rotula como el documento que es', () => {
    expect(tipoLabel('prenda_registro')).toBe('Inscripción de Prenda');
    expect(tipoLabel('inscripcion_prenda')).toBe('Inscripción de Prenda');
  });

  it('un código sin etiqueta se muestra tal cual, sin romperse', () => {
    expect(tipoLabel('no_existe')).toBe('no_existe');
  });
});
