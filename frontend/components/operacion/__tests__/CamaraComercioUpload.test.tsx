import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';

const mocks = vi.hoisted(() => ({
  getInstance: vi.fn(),
  getChecklist: vi.fn(),
  getAttachments: vi.fn(),
  uploadAttachment: vi.fn(),
  deleteAttachment: vi.fn(),
  analyzeDocument: vi.fn(),
  persistOcrFields: vi.fn(),
  fetchAttachmentPreviewUrl: vi.fn(),
  downloadAttachment: vi.fn(),
}));

vi.mock('@/lib/api/tramites-client', () => ({
  tramitesClient: {
    getInstance: mocks.getInstance,
    getChecklist: mocks.getChecklist,
    getAttachments: mocks.getAttachments,
    uploadAttachment: mocks.uploadAttachment,
    deleteAttachment: mocks.deleteAttachment,
    analyzeDocument: mocks.analyzeDocument,
    persistOcrFields: mocks.persistOcrFields,
    fetchAttachmentPreviewUrl: mocks.fetchAttachmentPreviewUrl,
    downloadAttachment: mocks.downloadAttachment,
  },
}));

import {
  CamaraComercioUpload,
  camaraComercioTipo,
  textoExencion,
} from '@/components/operacion/CamaraComercioUpload';
import type {
  CamaraComercioRequirement,
  ProcedureAttachment,
} from '@/lib/api/types/procedure-runtime';

const INSTANCE = 'inst-camara';

function requirement(over: Partial<CamaraComercioRequirement> = {}): CamaraComercioRequirement {
  return {
    rol: 'vendedor',
    tipo: 'camara_comercio_vendedor',
    esObligatorio: true,
    exencion: 'ninguna',
    vigencia: 'indeterminada',
    diasDesdeExpedicion: null,
    ...over,
  };
}

function adjunto(tipo = 'camara_comercio_vendedor'): ProcedureAttachment {
  return {
    id: 'att-1',
    tipo,
    filename: 'certificado.pdf',
    mimetype: 'application/pdf',
    sizeBytes: 1024,
    uploadedAt: '2026-09-22T10:00:00Z',
  } as ProcedureAttachment;
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getInstance.mockResolvedValue({ status: 'borrador', fieldValues: [] });
  mocks.getChecklist.mockResolvedValue([]);
  mocks.getAttachments.mockResolvedValue([]);
});

describe('CamaraComercioUpload', () => {
  // ── AC1 — el buzón aparece ─────────────────────────────────────────────────

  it('AC1 — se renderiza el contenedor de la parte jurídica', async () => {
    render(<CamaraComercioUpload instanceId={INSTANCE} requirement={requirement()} />);

    await waitFor(() =>
      expect(screen.getByLabelText('Certificado de Cámara de Comercio')).toBeInTheDocument(),
    );
  });

  it('AC4 — el selector de archivos solo ofrece PDF', async () => {
    render(<CamaraComercioUpload instanceId={INSTANCE} requirement={requirement()} />);

    const input = await screen.findByLabelText(/^Subir Certificado de Cámara de Comercio/);
    expect(input).toHaveAttribute('accept', 'application/pdf');
  });

  it('el texto del buzón obligatorio no repite que el actor es persona jurídica', async () => {
    render(<CamaraComercioUpload instanceId={INSTANCE} requirement={requirement()} />);

    expect(await screen.findByText(/^Adjunta el certificado de existencia y representación legal/)).toBeInTheDocument();
    expect(screen.queryByText(/Este actor es persona jurídica/)).not.toBeInTheDocument();
  });

  // ── AC2 — obligatorio bloquea ──────────────────────────────────────────────

  it('AC2 — sin adjunto y obligatorio, el gate del paso queda cerrado', async () => {
    const onSatisfiedChange = vi.fn();

    render(
      <CamaraComercioUpload
        instanceId={INSTANCE}
        requirement={requirement()}
        onSatisfiedChange={onSatisfiedChange}
      />,
    );

    await waitFor(() => expect(onSatisfiedChange).toHaveBeenCalledWith(false));
  });

  it('AC2 — el estado se comunica con texto, no solo con color', async () => {
    render(<CamaraComercioUpload instanceId={INSTANCE} requirement={requirement()} />);

    // El estado lo pinta `DocumentSlot`, que es su dueño canónico. El contenedor no lo repite.
    expect(await screen.findByText('Por cargar')).toBeInTheDocument();
  });

  // ── AC3 — con adjunto se abre el gate ──────────────────────────────────────

  it('AC3 — con el certificado cargado, el gate se abre', async () => {
    mocks.getAttachments.mockResolvedValue([adjunto()]);
    const onSatisfiedChange = vi.fn();

    render(
      <CamaraComercioUpload
        instanceId={INSTANCE}
        requirement={requirement()}
        onSatisfiedChange={onSatisfiedChange}
      />,
    );

    await waitFor(() => expect(onSatisfiedChange).toHaveBeenCalledWith(true));
  });

  // ── El buzón opcional nunca se oculta ──────────────────────────────────────

  it('opcional: el buzón sigue visible y NO bloquea el avance', async () => {
    const onSatisfiedChange = vi.fn();

    render(
      <CamaraComercioUpload
        instanceId={INSTANCE}
        requirement={requirement({ esObligatorio: false, exencion: 'firma_y_escritura' })}
        onSatisfiedChange={onSatisfiedChange}
      />,
    );

    expect(await screen.findByLabelText('Certificado de Cámara de Comercio')).toBeInTheDocument();
    expect(await screen.findByText('Opcional')).toBeInTheDocument();
    expect(screen.queryByText('Por cargar')).not.toBeInTheDocument();
    await waitFor(() => expect(onSatisfiedChange).toHaveBeenCalledWith(true));
  });

  it('opcional: el texto nombra firma y escritura y dice que no hace falta cargarlo', async () => {
    render(
      <CamaraComercioUpload
        instanceId={INSTANCE}
        requirement={requirement({ esObligatorio: false, exencion: 'firma_y_escritura' })}
      />,
    );

    const texto = await screen.findByText(/firma precargada y escritura vigentes/i);
    expect(texto).toHaveTextContent(/no necesitas cargarlo para continuar/i);
  });

  // ── Alerta de vigencia ─────────────────────────────────────────────────────

  it('con el certificado vencido muestra la alerta y no bloquea', async () => {
    mocks.getAttachments.mockResolvedValue([adjunto()]);
    const onSatisfiedChange = vi.fn();

    render(
      <CamaraComercioUpload
        instanceId={INSTANCE}
        requirement={requirement({ vigencia: 'excedida', diasDesdeExpedicion: 45 })}
        onSatisfiedChange={onSatisfiedChange}
      />,
    );

    expect(await screen.findByText(/45 días/)).toBeInTheDocument();
    await waitFor(() => expect(onSatisfiedChange).toHaveBeenCalledWith(true));
  });

  it('vigencia indeterminada NO pinta alerta: una fecha ilegible no es un documento vencido', async () => {
    mocks.getAttachments.mockResolvedValue([adjunto()]);

    render(
      <CamaraComercioUpload
        instanceId={INSTANCE}
        requirement={requirement({ vigencia: 'indeterminada' })}
      />,
    );

    await waitFor(() => expect(screen.getByText('Cargado')).toBeInTheDocument());
    expect(screen.queryByText(/de expedición/i)).not.toBeInTheDocument();
  });

  it('un certificado vigente tampoco pinta alerta', async () => {
    mocks.getAttachments.mockResolvedValue([adjunto()]);

    render(
      <CamaraComercioUpload
        instanceId={INSTANCE}
        requirement={requirement({ vigencia: 'vigente', diasDesdeExpedicion: 9 })}
      />,
    );

    await waitFor(() => expect(screen.getByText('Cargado')).toBeInTheDocument());
    expect(screen.queryByText(/recomendamos actualizarlo/i)).not.toBeInTheDocument();
  });

  it('al cargar el certificado persiste la fecha de expedición que leyó el OCR (HU #12776)', async () => {
    const ocr = { es_valido: true, legibilidad: 'buena', fecha_expedicion: '2026-08-01' };
    mocks.analyzeDocument.mockResolvedValue({ ok: true, tipo: 'camara_comercio_vendedor', data: ocr });
    mocks.persistOcrFields.mockResolvedValue({ persistidos: 1 });
    mocks.uploadAttachment.mockResolvedValue({ id: 'att-1' });

    render(<CamaraComercioUpload instanceId={INSTANCE} requirement={requirement()} />);

    const input = await screen.findByLabelText(/^Subir Certificado de Cámara de Comercio/);
    fireEvent.change(input, {
      target: { files: [new File(['%PDF-1.4'], 'certificado.pdf', { type: 'application/pdf' })] },
    });

    await waitFor(() =>
      expect(mocks.persistOcrFields).toHaveBeenCalledWith(
        INSTANCE,
        'camara_comercio_vendedor',
        ocr,
        undefined,
      ),
    );
  });

  it('certificado rechazado por el OCR: avisa sin campos para que no quede la fecha del anterior', async () => {
    mocks.analyzeDocument.mockResolvedValue({
      ok: true,
      tipo: 'camara_comercio_vendedor',
      data: { es_valido: false, tipo_documento: 'rut', fecha_expedicion: '2026-08-01' },
    });
    mocks.persistOcrFields.mockResolvedValue({ persistidos: 0 });
    mocks.uploadAttachment.mockResolvedValue({ id: 'att-1' });

    render(<CamaraComercioUpload instanceId={INSTANCE} requirement={requirement()} />);

    const input = await screen.findByLabelText(/^Subir Certificado de Cámara de Comercio/);
    fireEvent.change(input, {
      target: { files: [new File(['%PDF-1.4'], 'otro.pdf', { type: 'application/pdf' })] },
    });

    await waitFor(() =>
      expect(mocks.persistOcrFields).toHaveBeenCalledWith(INSTANCE, 'camara_comercio_vendedor', {}, undefined),
    );
    // Del documento rechazado no se toma ningún dato.
    expect(mocks.persistOcrFields).not.toHaveBeenCalledWith(
      INSTANCE,
      'camara_comercio_vendedor',
      expect.objectContaining({ fecha_expedicion: '2026-08-01' }),
      undefined,
    );
  });

  // ── El adjunto de una parte no satisface a la otra ─────────────────────────

  it('el certificado del vendedor no satisface al comprador', async () => {
    mocks.getAttachments.mockResolvedValue([adjunto('camara_comercio_vendedor')]);
    const onSatisfiedChange = vi.fn();

    render(
      <CamaraComercioUpload
        instanceId={INSTANCE}
        requirement={requirement({ rol: 'comprador', tipo: 'camara_comercio_comprador' })}
        onSatisfiedChange={onSatisfiedChange}
      />,
    );

    await waitFor(() => expect(onSatisfiedChange).toHaveBeenCalledWith(false));
  });
});

describe('camaraComercioTipo', () => {
  it.each([
    ['vendedor', 'camara_comercio_vendedor'],
    ['comprador', 'camara_comercio_comprador'],
    ['locatario', 'camara_comercio_locatario'],
  ])('%s → %s', (rol, esperado) => {
    expect(camaraComercioTipo(rol)).toBe(esperado);
  });

  it('normaliza casing y espacios', () => {
    expect(camaraComercioTipo('  VENDEDOR ')).toBe('camara_comercio_vendedor');
  });
});

describe('textoExencion', () => {
  it('sin exención no hay texto: el buzón es obligatorio y no hay nada que explicar', () => {
    expect(textoExencion('ninguna')).toBeNull();
  });

  it('la única exención nombra las dos condiciones juntas', () => {
    expect(textoExencion('firma_y_escritura')).toMatch(/firma precargada y escritura vigentes/i);
  });
});
