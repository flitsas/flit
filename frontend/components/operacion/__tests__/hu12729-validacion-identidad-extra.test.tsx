// Uso de ejemplo:
//   read('BiometricStep.tsx') → md:grid-cols-2 con captura, QR size={120}, WIZARD_BTN_BRAND
//   WIZARD_BTN_BRAND / WIZARD_BTN_BRAND_OUTLINE exportados desde wizard-field-styles
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  WIZARD_BTN_BRAND,
  WIZARD_BTN_BRAND_OUTLINE,
  WIZARD_BTN_SOLID,
  WIZARD_CTA_GRADIENT,
} from '../wizard-field-styles';

const FE_ROOT = path.resolve(__dirname, '../..');
const read = (rel: string) => readFileSync(path.join(FE_ROOT, rel), 'utf8');

describe('HU #12729 — AC1/AC2 rejilla dos columnas (firma/QR y partes)', () => {
  it('happy: ParteBlock usa md:grid-cols-2 cuando hay captura lateral', () => {
    const src = read('operacion/BiometricStep.tsx');
    expect(src).toMatch(/showCaptureSide \? 'md:grid-cols-2'/);
    expect(src).toMatch(/grid grid-cols-1 gap-4 md:grid-cols-2/);
  });

  it('happy: esqueleto multi-parte usa md:grid-cols-2 (no solo lg)', () => {
    const src = read('operacion/BiometricStep.tsx');
    expect(src).toMatch(/partes\.length > 1 \? 'md:grid-cols-2'/);
  });

  it('contrato: QR de captura conserva size={120}', () => {
    const src = read('operacion/BiometricStep.tsx');
    expect(src).toMatch(/<QRCodeSVG value=\{captureUrl\} size=\{120\}/);
  });

  it('edge: sin captura lateral no fuerza segunda columna en ParteBlock', () => {
    const src = read('operacion/BiometricStep.tsx');
    expect(src).toMatch(
      /grid grid-cols-1 gap-4 \$\{showCaptureSide \? 'md:grid-cols-2' : ''\}/,
    );
  });
});

describe('HU #12729 — AC3 trazabilidad a ancho completo', () => {
  it('happy: disclosure Ver trazabilidad expone aria-expanded', () => {
    const src = read('operacion/BiometricStep.tsx');
    expect(src).toMatch(/Ver trazabilidad de validación/);
    expect(src).toMatch(/aria-expanded=\{open\}/);
  });

  it('contrato: trazabilidad vive fuera de la rejilla de tarjetas (ancho completo)', () => {
    const src = read('operacion/BiometricStep.tsx');
    expect(src).toMatch(/function ParteTrazabilidadSection/);
    expect(src).toMatch(/function TrazabilidadDisclosure/);
    // Hermano del grid (después de `</div>` del grid), no anidado en la tarjeta.
    expect(src).toMatch(
      /\$\{showCaptureSide \? 'md:grid-cols-2' : ''\}`\}>[\s\S]*?<\/div>\s*<ParteTrazabilidadSection/,
    );
  });
});

describe('HU #12729 — AC4 botones azules corporativos (sin degradado en tarjetas)', () => {
  it('happy: tokens WIZARD_BTN_BRAND usan azul sólido y font-semibold', () => {
    expect(WIZARD_BTN_SOLID).toBe('#557EFF');
    expect(WIZARD_BTN_BRAND).toMatch(/font-semibold/);
    expect(WIZARD_BTN_BRAND).toMatch(/text-white/);
    expect(WIZARD_BTN_BRAND).not.toMatch(/gradient/i);
    expect(WIZARD_BTN_BRAND_OUTLINE).toMatch(/border/);
    expect(WIZARD_BTN_BRAND_OUTLINE).toMatch(/font-semibold/);
  });

  it('contrato: BiometricStep usa WIZARD_BTN_BRAND y no WIZARD_CTA_GRADIENT en acciones de tarjeta', () => {
    const src = read('operacion/BiometricStep.tsx');
    expect(src).toMatch(/WIZARD_BTN_BRAND/);
    expect(src).toMatch(/WIZARD_BTN_BRAND_OUTLINE/);
    expect(src).not.toMatch(/WIZARD_CTA_GRADIENT/);
  });

  it('edge: CTA del wizard (FirmaFurStep) conserva el degradado', () => {
    const firma = read('operacion/FirmaFurStep.tsx');
    expect(firma).toMatch(/WIZARD_CTA_GRADIENT/);
    expect(WIZARD_CTA_GRADIENT).toMatch(/linear-gradient/);
  });
});

describe('HU #12729 — AC5 contraste / foco teclado (contrato de clases)', () => {
  it('contrato: botones brand exponen focus-visible:outline', () => {
    expect(WIZARD_BTN_BRAND).toMatch(/focus-visible:outline/);
    expect(WIZARD_BTN_BRAND_OUTLINE).toMatch(/focus-visible:outline/);
  });

  it('happy: outline usa borde/texto de marca vía style o WIZARD_BTN_SOLID en BiometricStep', () => {
    const src = read('operacion/BiometricStep.tsx');
    expect(src).toMatch(/WIZARD_BTN_SOLID|#557EFF|borderColor.*557EFF/);
  });
});
