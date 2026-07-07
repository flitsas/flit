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

// C8 (ADR-0026): el único tipo válido es PDF; las imágenes ahora se rechazan.
function pdfFile(name = 'doc.pdf', size = 1000): File {
  const file = new File(['x'], name, { type: 'application/pdf' });
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
    expect(screen.getByText('Obligatorio')).toBeInTheDocument();
    expect(screen.getByText('(opcional)')).toBeInTheDocument();
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
});

describe('DocumentChecklist — upload', () => {
  it('sube un archivo válido y refresca el checklist', async () => {
    const user = userEvent.setup();
    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText('Cédula del comprador');
    const input = screen.getByLabelText('Subir Cédula del comprador');
    await user.upload(input, pdfFile());

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
    await user.upload(input, pdfFile('big.pdf', MAX_SIZE_BYTES + 1));

    expect(mocks.uploadAttachment).not.toHaveBeenCalled();
    expect(await screen.findByText(/supera el máximo de 20 MB/)).toBeInTheDocument();
  });

  it('rechaza una imagen (C8: solo PDF) sin llamar al cliente', async () => {
    render(<DocumentChecklist instanceId={INSTANCE} />);

    await screen.findByText('Cédula del comprador');
    const input = screen.getByLabelText(
      'Subir Cédula del comprador',
    ) as HTMLInputElement;
    // fireEvent en vez de userEvent.upload: este último respeta el atributo
    // `accept` del input y descarta el archivo antes del onChange, lo que
    // impediría ejercitar la guarda client-side del componente.
    fireEvent.change(input, { target: { files: [pngFile('cedula.png')] } });

    expect(mocks.uploadAttachment).not.toHaveBeenCalled();
    expect(
      await screen.findByText(/Tipo de archivo no permitido/),
    ).toBeInTheDocument();
  });
});

describe('validateFile — unidad', () => {
  it('acepta un pdf dentro del límite', () => {
    expect(validateFile(pdfFile('ok.pdf', 1000))).toBeNull();
  });

  it('rechaza una imagen (C8: solo PDF)', () => {
    expect(validateFile(pngFile('foto.png', 1000))).toMatch(/no permitido/);
  });

  it('rechaza tipo no permitido', () => {
    const txt = new File(['x'], 'a.txt', { type: 'text/plain' });
    expect(validateFile(txt)).toMatch(/no permitido/);
  });

  it('rechaza tamaño excesivo', () => {
    expect(validateFile(pdfFile('big.pdf', MAX_SIZE_BYTES + 1))).toMatch(
      /20 MB/,
    );
  });
});
