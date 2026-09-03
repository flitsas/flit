import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import type {
  ChecklistView,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

// ── Mock del cliente HTTP (sin red real) ───────────────────────────
const mocks = vi.hoisted(() => ({
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  getInstance: vi.fn(),
  analyzeDocument: vi.fn(),
  persistOcrFields: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  setImprontaDiferida: vi.fn(),
  generarImpronta: vi.fn(),
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
    setImprontaDiferida: mocks.setImprontaDiferida,
    generarImpronta: mocks.generarImpronta,
  },
}));

import {
  DocumentChecklist,
  validateFile,
  MAX_SIZE_BYTES,
} from '@/components/operacion/DocumentChecklist';

const INSTANCE = 'inst-1';

const CHECKLIST: ChecklistView = {
  items: [
    {
      key: 'cedula',
      label: 'Cédula del comprador',
      obligatorio: true,
      docTipo: 'CEDULA',
      satisfied: false,
    },
    {
      key: 'soat',
      label: 'SOAT vigente',
      obligatorio: false,
      docTipo: 'SOAT',
      satisfied: false,
    },
  ],
  faltanObligatorios: 1,
  completo: false,
};

function pngFile(name = 'doc.png', size = 1000): File {
  const file = new File(['x'], name, { type: 'image/png' });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getChecklist.mockResolvedValue(CHECKLIST);
  mocks.getAttachments.mockResolvedValue([]);
  mocks.getInstance.mockResolvedValue({ fieldValues: [] });
  mocks.analyzeDocument.mockResolvedValue({ ok: true, tipo: 'soat', data: { es_valido: true } });
  mocks.persistOcrFields.mockResolvedValue(undefined);
  mocks.uploadAttachment.mockResolvedValue({ id: 'att-1' });
  mocks.deleteAttachment.mockResolvedValue(undefined);
});

describe('DocumentChecklist — render guiado por checklist', () => {
  it('renderiza un slot por ítem con badges obligatorio/opcional', async () => {
    render(<DocumentChecklist instanceId={INSTANCE} />);

    expect(await screen.findByText(/Cédula del comprador/)).toBeInTheDocument();
    expect(screen.getByText('(CEDULA)')).toBeInTheDocument();
    expect(screen.getByText('(SOAT)')).toBeInTheDocument();
    expect(screen.getByText('Por cargar')).toBeInTheDocument();
    expect(screen.getByText('Opcional')).toBeInTheDocument();
  });

  it('muestra el resumen "faltan N obligatorios" cuando no está completo', async () => {
    render(<DocumentChecklist instanceId={INSTANCE} />);
    expect(await screen.findByText(/Faltan 1 obligatorio/)).toBeInTheDocument();
  });

  it('con hideHeader (wizard) sigue mostrando cuántos obligatorios faltan', async () => {
    render(<DocumentChecklist instanceId={INSTANCE} hideHeader />);
    expect(await screen.findByText(/Faltan 1 obligatorio/)).toBeInTheDocument();
    expect(screen.queryByText('Gestión de documentos')).toBeNull();
  });

  it('muestra "Documentos completos" cuando el checklist está completo', async () => {
    mocks.getChecklist.mockResolvedValue({
      ...CHECKLIST,
      faltanObligatorios: 0,
      completo: true,
      items: CHECKLIST.items.map((i) => ({ ...i, satisfied: true })),
    });
    render(<DocumentChecklist instanceId={INSTANCE} />);
    expect(
      await screen.findByText('Documentos completos'),
    ).toBeInTheDocument();
  });

  it('marca ✓ los ítems satisfied y ofrece borrar el adjunto', async () => {
    const attachment: ProcedureAttachment = {
      id: 'att-1',
      tipo: 'CEDULA',
      filename: 'cedula.png',
      mimetype: 'image/png',
      sizeBytes: 2048,
      sha256: 'abc',
      source: 'upload',
      uploadedAt: '2026-06-18T00:00:00Z',
    };
    mocks.getChecklist.mockResolvedValue({
      ...CHECKLIST,
      items: [
        { ...CHECKLIST.items[0], satisfied: true },
        CHECKLIST.items[1],
      ],
    });
    mocks.getAttachments.mockResolvedValue([attachment]);

    render(<DocumentChecklist instanceId={INSTANCE} />);

    expect(await screen.findByText(/cedula\.png/)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Borrar Cédula del comprador/ }),
    ).toBeInTheDocument();
  });

  it('empareja el adjunto con su casilla aunque el código tenga mayúsculas', async () => {
    // Los dos extremos NO guardan el código igual: `docTipo` conserva el casing con el que se creó el
    // tipo en el módulo Documental, y el backend persiste `tipo` en minúsculas al subir. Con
    // comparación exacta, el documento se subía y la casilla seguía vacía y obligatoria: para el
    // gestor, «no carga».
    const attachment: ProcedureAttachment = {
      id: 'att-2',
      tipo: 'cedula', // minúsculas: lo que devuelve el backend
      filename: 'documento.png',
      mimetype: 'image/png',
      sizeBytes: 2048,
      sha256: 'abc',
      source: 'upload',
      uploadedAt: '2026-06-18T00:00:00Z',
    };
    mocks.getAttachments.mockResolvedValue([attachment]);

    render(<DocumentChecklist instanceId={INSTANCE} />);

    // docTipo del checklist: 'CEDULA'.
    expect(await screen.findByText(/documento\.png/)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Borrar Cédula del comprador/ }),
    ).toBeInTheDocument();
  });

  it('no inventa emparejamientos: un adjunto de otro tipo deja la casilla vacía', async () => {
    const attachment: ProcedureAttachment = {
      id: 'att-3',
      tipo: 'paz_salvo',
      filename: 'otro.png',
      mimetype: 'image/png',
      sizeBytes: 2048,
      sha256: 'abc',
      source: 'upload',
      uploadedAt: '2026-06-18T00:00:00Z',
    };
    mocks.getAttachments.mockResolvedValue([attachment]);

    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText(/Cédula del comprador/);
    expect(screen.queryByText(/otro\.png/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Borrar Cédula del comprador/ }),
    ).not.toBeInTheDocument();
  });
});

describe('DocumentChecklist — upload', () => {
  it('sube un archivo válido y refresca el checklist', async () => {
    const user = userEvent.setup();
    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText(/Cédula del comprador/);
    const input = screen.getByLabelText(/Subir Cédula del comprador/);
    await user.upload(input, pngFile());

    await waitFor(() =>
      expect(mocks.uploadAttachment).toHaveBeenCalledTimes(1),
    );
    const [instanceId, tipo, file] = mocks.uploadAttachment.mock.calls[0];
    expect(instanceId).toBe(INSTANCE);
    expect(tipo).toBe('CEDULA');
    expect(file).toBeInstanceOf(File);
    // refresca: getChecklist se llama de nuevo (1 inicial + 1 tras subir).
    await waitFor(() =>
      expect(mocks.getChecklist).toHaveBeenCalledTimes(2),
    );
  });

  it('rechaza por tamaño (>20MB) sin llamar al cliente', async () => {
    const user = userEvent.setup();
    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText(/Cédula del comprador/);
    const input = screen.getByLabelText(/Subir Cédula del comprador/);
    await user.upload(input, pngFile('big.png', MAX_SIZE_BYTES + 1));

    expect(mocks.uploadAttachment).not.toHaveBeenCalled();
    expect(await screen.findByText(/supera el máximo de 20 MB/)).toBeInTheDocument();
  });

  it('rechaza por tamaño usando el límite por-tipo (inline y dinámico, no 20 MB)', async () => {
    // El tipo tiene un maxSizeBytes propio (1 MB): un archivo de 2 MB debe rechazarse en el
    // cliente (inline) con el límite real formateado, sin llegar al backend ni al mensaje global.
    mocks.getChecklist.mockResolvedValue({
      ...CHECKLIST,
      items: [{ ...CHECKLIST.items[0], maxSizeBytes: 1_000_000 }, CHECKLIST.items[1]],
    });
    const user = userEvent.setup();
    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText(/Cédula del comprador/);
    const input = screen.getByLabelText(/Subir Cédula del comprador/);
    await user.upload(input, pngFile('grande.png', 2_000_000));

    expect(mocks.uploadAttachment).not.toHaveBeenCalled();
    const msg = await screen.findByText(/supera el máximo/);
    expect(msg.textContent).toMatch(/977 KB/);
    expect(msg.textContent).not.toMatch(/20 MB/);
  });

  it('rechaza por mime no permitido sin llamar al cliente', async () => {
    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText(/Cédula del comprador/);
    const input = screen.getByLabelText(
      /Subir Cédula del comprador/,
    ) as HTMLInputElement;
    const exe = new File(['x'], 'virus.exe', {
      type: 'application/octet-stream',
    });
    // fireEvent en vez de userEvent.upload: este último respeta el atributo
    // `accept` del input y descarta el archivo antes del onChange, lo que
    // impediría ejercitar la guarda client-side del componente.
    fireEvent.change(input, { target: { files: [exe] } });

    expect(mocks.uploadAttachment).not.toHaveBeenCalled();
    expect(
      await screen.findByText(/Tipo de archivo no permitido/),
    ).toBeInTheDocument();
  });
});

describe('validateFile — unidad', () => {
  it('acepta un png dentro del límite', () => {
    expect(validateFile(pngFile('ok.png', 1000))).toBeNull();
  });

  it('rechaza tipo no permitido', () => {
    const txt = new File(['x'], 'a.txt', { type: 'text/plain' });
    expect(validateFile(txt)).toMatch(/no permitido/);
  });

  it('rechaza tamaño excesivo', () => {
    expect(validateFile(pngFile('big.png', MAX_SIZE_BYTES + 1))).toMatch(
      /20 MB/,
    );
  });

  // HU #10524 (RF08/09/10) — validación por tipo con respaldo a los límites globales.
  it('límite por tipo más estricto de MIME rechaza un formato antes permitido', () => {
    // Un tipo restringido a solo PDF rechaza un PNG (que globalmente sí se acepta).
    expect(validateFile(pngFile('foto.png', 1000), { allowedMimes: ['application/pdf'] }))
      .toMatch(/no permitido/);
  });

  it('límite por tipo más estricto de tamaño rechaza por encima del máximo del tipo', () => {
    const msg = validateFile(pngFile('grande.png', 2_000_000), { maxSizeBytes: 1_000_000 });
    expect(msg).toMatch(/supera el máximo/);
  });

  it('límites por tipo vacíos ⇒ respaldo a los globales', () => {
    expect(validateFile(pngFile('ok.png', 1000), {})).toBeNull();
    expect(validateFile(pngFile('ok.png', 1000), { allowedMimes: [] })).toBeNull();
  });

  it('acepta dentro de un límite por tipo permisivo', () => {
    expect(
      validateFile(pngFile('ok.png', 1500), {
        allowedMimes: ['image/png', 'application/pdf'],
        maxSizeBytes: 5_000_000,
      }),
    ).toBeNull();
  });
});

const SOAT_ATT: ProcedureAttachment = {
  id: 'att-soat',
  tipo: 'soat',
  filename: 'soat.png',
  mimetype: 'image/png',
  sizeBytes: 1000,
  sha256: 'abc',
  source: 'upload',
  uploadedAt: '2026-06-18T00:00:00Z',
};

describe('DocumentChecklist — OCR en el buzón', () => {
  it('un documento que no es del buzón no dice Cargado', async () => {
    const user = userEvent.setup();
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'soat',
      data: { es_valido: false, observaciones: 'Es una factura.' },
    });
    mocks.uploadAttachment.mockImplementation(async () => {
      mocks.getAttachments.mockResolvedValue([SOAT_ATT]);
      return { id: SOAT_ATT.id };
    });

    render(<DocumentChecklist instanceId={INSTANCE} />);
    await screen.findByText(/SOAT vigente/);
    await user.upload(screen.getByLabelText(/Subir SOAT vigente/), pngFile('factura.png'));

    expect(await screen.findByText('No coincide')).toBeInTheDocument();
    expect(await screen.findByLabelText(/OCR SOAT: Rechazado/)).toBeInTheDocument();
    expect(screen.queryByText('Cargado')).toBeNull();
  });

  it('reemplazar con el documento correcto cambia la marca, no deja la anterior', async () => {
    const user = userEvent.setup();
    mocks.analyzeDocument
      .mockResolvedValueOnce({
        ok: true,
        tipo: 'soat',
        data: { es_valido: false, observaciones: 'Es una factura.' },
      })
      .mockResolvedValueOnce({ ok: true, tipo: 'soat', data: { es_valido: true } });
    mocks.uploadAttachment.mockImplementation(async () => {
      mocks.getAttachments.mockResolvedValue([SOAT_ATT]);
      return { id: SOAT_ATT.id };
    });

    render(<DocumentChecklist instanceId={INSTANCE} />);
    await screen.findByText(/SOAT vigente/);
    await user.upload(screen.getByLabelText(/Subir SOAT vigente/), pngFile('mal.png'));
    expect(await screen.findByText('No coincide')).toBeInTheDocument();

    await user.upload(screen.getByLabelText(/Subir SOAT vigente/), pngFile('soat.png'));
    expect(await screen.findByText('Cargado')).toBeInTheDocument();
    expect(await screen.findByLabelText(/OCR SOAT: Verificado/)).toBeInTheDocument();
    expect(screen.queryByText('No coincide')).toBeNull();
    expect(screen.queryByLabelText(/OCR SOAT: Rechazado/)).toBeNull();
  });

  it('borrar el adjunto quita la marca OCR', async () => {
    const user = userEvent.setup();
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'soat',
      data: { es_valido: false, observaciones: 'Es una factura.' },
    });
    mocks.uploadAttachment.mockImplementation(async () => {
      mocks.getAttachments.mockResolvedValue([SOAT_ATT]);
      return { id: SOAT_ATT.id };
    });
    mocks.deleteAttachment.mockImplementation(async () => {
      mocks.getAttachments.mockResolvedValue([]);
    });

    render(<DocumentChecklist instanceId={INSTANCE} />);
    await screen.findByText(/SOAT vigente/);
    await user.upload(screen.getByLabelText(/Subir SOAT vigente/), pngFile('mal.png'));
    expect(await screen.findByText('No coincide')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /Borrar SOAT vigente/ }));
    await waitFor(() => {
      expect(screen.queryByText('No coincide')).toBeNull();
      expect(screen.queryByLabelText(/OCR SOAT: Rechazado/)).toBeNull();
    });
    expect(screen.getByText('Opcional')).toBeInTheDocument();
  });
});

describe('DocumentChecklist — generar impronta en el slot', () => {
  const IMPRONTA_OPCIONAL: ChecklistView = {
    items: [
      {
        key: 'impronta',
        label: 'Improntas',
        obligatorio: false,
        docTipo: 'impronta',
        satisfied: false,
      },
    ],
    faltanObligatorios: 0,
    completo: true,
  };

  const IMPRONTA_PDF: ProcedureAttachment = {
    id: 'imp-1',
    tipo: 'impronta',
    filename: 'impronta.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 4096,
    sha256: 'abc',
    source: 'system',
    uploadedAt: '2026-08-26T00:00:00Z',
  };

  it('ofrece generar aunque el documento sea opcional, y no muestra el check diferido', async () => {
    mocks.getChecklist.mockResolvedValue(IMPRONTA_OPCIONAL);
    render(<DocumentChecklist instanceId={INSTANCE} />);

    expect(await screen.findByRole('button', { name: 'Generar impronta' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Adjuntar archivo' })).toBeInTheDocument();
    expect(
      screen.queryByText(/La impronta se generará automáticamente en el paso de firma/),
    ).toBeNull();
  });

  it('no ofrece generar si el tipo está en MANUAL', async () => {
    mocks.getChecklist.mockResolvedValue(IMPRONTA_OPCIONAL);
    render(
      <DocumentChecklist instanceId={INSTANCE} permiteGenerarImprontaAutomatica={false} />,
    );

    await screen.findByText(/Improntas/);
    expect(screen.queryByRole('button', { name: 'Generar impronta' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Adjuntar archivo' })).toBeInTheDocument();
  });

  it('al generar adjunta el PDF y permite verlo o reemplazarlo', async () => {
    const user = userEvent.setup();
    mocks.getChecklist.mockResolvedValue(IMPRONTA_OPCIONAL);
    mocks.generarImpronta.mockImplementation(async () => {
      mocks.getAttachments.mockResolvedValue([IMPRONTA_PDF]);
      mocks.getChecklist.mockResolvedValue({
        ...IMPRONTA_OPCIONAL,
        items: [{ ...IMPRONTA_OPCIONAL.items[0], satisfied: true }],
      });
      return {
        attachmentId: 'imp-1',
        filename: 'impronta.pdf',
        sha256: 'abc',
        radicado: 'R-1',
        hash: 'h-1',
      };
    });

    render(<DocumentChecklist instanceId={INSTANCE} />);
    await user.click(await screen.findByRole('button', { name: 'Generar impronta' }));

    await waitFor(() => expect(mocks.generarImpronta).toHaveBeenCalledWith(INSTANCE));
    expect(await screen.findByText(/impronta\.pdf/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Previsualizar/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reemplazar archivo' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Generar impronta' })).toBeNull();
  });

  it('muestra el error del proveedor si la generación falla', async () => {
    const user = userEvent.setup();
    mocks.getChecklist.mockResolvedValue(IMPRONTA_OPCIONAL);
    mocks.generarImpronta.mockRejectedValue(
      new Error('Debe seleccionar el organismo de tránsito antes de generar la impronta.'),
    );

    render(<DocumentChecklist instanceId={INSTANCE} />);
    await user.click(await screen.findByRole('button', { name: 'Generar impronta' }));

    expect(
      await screen.findByText(/organismo de tránsito antes de generar la impronta/i),
    ).toBeInTheDocument();
  });
});
