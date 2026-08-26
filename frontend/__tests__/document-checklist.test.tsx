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
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getChecklist: mocks.getChecklist,
    getAttachments: mocks.getAttachments,
    getInstance: mocks.getInstance,
    analyzeDocument: mocks.analyzeDocument,
    uploadAttachment: mocks.uploadAttachment,
    deleteAttachment: mocks.deleteAttachment,
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
  mocks.uploadAttachment.mockResolvedValue({ id: 'att-1' });
  mocks.deleteAttachment.mockResolvedValue(undefined);
});

describe('DocumentChecklist — render guiado por checklist', () => {
  it('renderiza un slot por ítem con badges obligatorio/opcional', async () => {
    render(<DocumentChecklist instanceId={INSTANCE} />);

    expect(await screen.findByText('Cédula del comprador')).toBeInTheDocument();
    expect(screen.getByText('SOAT vigente')).toBeInTheDocument();
    expect(screen.getByText('* Obligatorio')).toBeInTheDocument();
    expect(screen.getByText('Opcional')).toBeInTheDocument();
  });

  it('muestra el resumen "faltan N obligatorios" cuando no está completo', async () => {
    render(<DocumentChecklist instanceId={INSTANCE} />);
    expect(await screen.findByText(/Faltan 1 obligatorio/)).toBeInTheDocument();
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
      screen.getByRole('button', { name: 'Borrar Cédula del comprador' }),
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
      screen.getByRole('button', { name: 'Borrar Cédula del comprador' }),
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

    await screen.findByText('Cédula del comprador');
    expect(screen.queryByText(/otro\.png/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Borrar Cédula del comprador' }),
    ).not.toBeInTheDocument();
  });
});

describe('DocumentChecklist — upload', () => {
  it('sube un archivo válido y refresca el checklist', async () => {
    const user = userEvent.setup();
    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText('Cédula del comprador');
    const input = screen.getByLabelText('Subir Cédula del comprador');
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

    await screen.findByText('Cédula del comprador');
    const input = screen.getByLabelText('Subir Cédula del comprador');
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

    await screen.findByText('Cédula del comprador');
    const input = screen.getByLabelText('Subir Cédula del comprador');
    await user.upload(input, pngFile('grande.png', 2_000_000));

    expect(mocks.uploadAttachment).not.toHaveBeenCalled();
    const msg = await screen.findByText(/supera el máximo/);
    expect(msg.textContent).toMatch(/977 KB/);
    expect(msg.textContent).not.toMatch(/20 MB/);
  });

  it('rechaza por mime no permitido sin llamar al cliente', async () => {
    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText('Cédula del comprador');
    const input = screen.getByLabelText(
      'Subir Cédula del comprador',
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
