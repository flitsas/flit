// HU #12728 (D.2) — DocumentSlot: Opcional en brand-ink, OCR en cabecera, chip obligatorio por token.
import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { DocumentSlot } from '../DocumentChecklist';
import type { ChecklistItemView } from '@/lib/api/types/procedure-runtime';
import type { OcrUiResult } from '@/hooks/useProcedureDocuments';

function makeItem(over: Partial<ChecklistItemView> = {}): ChecklistItemView {
  return {
    key: 'factura',
    label: 'Factura de compra',
    obligatorio: true,
    docTipo: 'factura',
    satisfied: false,
    ...over,
  };
}

function renderSlot(
  over: Partial<ChecklistItemView> = {},
  extra: {
    ocr?: OcrUiResult;
    analyzing?: boolean;
    attachment?: Parameters<typeof DocumentSlot>[0]['attachment'];
  } = {},
) {
  const item = makeItem(over);
  const tipo = item.docTipo ?? item.key;
  return render(
    <DocumentSlot
      item={item}
      attachment={extra.attachment}
      uploading={false}
      analyzing={extra.analyzing ?? false}
      deleting={false}
      ocr={extra.ocr}
      onUpload={vi.fn()}
      onRemove={vi.fn()}
    />,
  );
}

/** Contenedor de cabecera (badges + OCR) en la esquina superior derecha. */
function headerCorner() {
  return document.querySelector('.absolute.right-3.top-3');
}

describe('HU #12728 — DocumentSlot cabecera (D.2)', () => {
  it('happy: documento opcional muestra «Opcional» en --flit-brand-ink sin opacidad', () => {
    renderSlot({ obligatorio: false, satisfied: false });

    const opcional = screen.getByText('Opcional');
    expect(opcional).toHaveStyle({ color: 'var(--flit-brand-ink)' });
    expect(opcional.className).not.toMatch(/opacity-/);
  });

  it('happy: documento obligatorio sin adjunto usa chip danger con tokens --badge-danger-*', () => {
    renderSlot({ obligatorio: true, satisfied: false });

    const chip = screen.getByRole('status', { name: 'Estado: Por cargar' });
    expect(chip).toHaveTextContent('Por cargar');
    expect(chip.getAttribute('style')).toContain('--badge-danger-bg');
    expect(chip.getAttribute('style')).toContain('--badge-danger-fg');
    expect(chip.getAttribute('style')).toContain('--badge-danger-border');
  });

  it('happy: indicador OCR vive en la cabecera derecha, no debajo del cuerpo', () => {
    const ocr: OcrUiResult = {
      status: 'verified',
      data: { tipo_documento: 'factura' },
    };
    renderSlot({ obligatorio: false }, { ocr });

    const ocrBtn = screen.getByRole('button', { name: /OCR Factura: Verificado/i });
    expect(headerCorner()?.contains(ocrBtn)).toBe(true);
    expect(document.querySelector('.mt-2 .relative.shrink-0')).toBeNull();
  });

  it('edge: durante análisis OCR no se muestra en cabecera', () => {
    const ocr: OcrUiResult = { status: 'verified', data: {} };
    renderSlot({ obligatorio: true }, { ocr, analyzing: true });

    expect(screen.queryByRole('button', { name: /OCR/i })).not.toBeInTheDocument();
  });

  it('contrato: nota informativa de instrucción de cargue se mantiene intacta (Info #557EFF + texto)', () => {
    renderSlot({
      obligatorio: false,
      instruccionCargue: 'Sube la factura original firmada por el concesionario.',
    });

    expect(
      screen.getByText('Sube la factura original firmada por el concesionario.'),
    ).toBeInTheDocument();
    const nota = screen.getByText('Sube la factura original firmada por el concesionario.')
      .parentElement;
    const infoIcon = nota?.querySelector('svg.lucide-info');
    expect(infoIcon).toBeTruthy();
    // React serializa #557EFF como rgb en el DOM inline.
    expect(infoIcon?.getAttribute('style')).toMatch(/#557EFF|rgb\(85,\s*126,\s*255\)/);
    expect(screen.getByText('Sube la factura original firmada por el concesionario.').className).toMatch(
      /opacity-70/,
    );
  });
});
