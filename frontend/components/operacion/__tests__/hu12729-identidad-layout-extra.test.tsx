// Uso de ejemplo: read('BiometricStep.tsx') + wizard-field-styles → aserciones de layout HU #12729.
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const root = join(__dirname, '..');
const biometric = readFileSync(join(root, 'BiometricStep.tsx'), 'utf8');
const styles = readFileSync(join(root, 'wizard-field-styles.ts'), 'utf8');

describe('HU #12729 — AC1 dos columnas con firma/QR (una parte)', () => {
  it('happy: grilla md:grid-cols-2 cuando hay captura (firma/QR)', () => {
    expect(biometric).toMatch(/showCaptureSide \? 'md:grid-cols-2'/);
    expect(biometric).toMatch(/grid grid-cols-1 gap-4 md:grid-cols-2/);
  });

  it('contrato: QR size 120 se conserva en KyverumPendingView', () => {
    expect(biometric).toContain('size={120}');
  });
});

describe('HU #12729 — AC2 varias partes desde md', () => {
  it('happy: partes.length > 1 usa md:grid-cols-2', () => {
    expect(biometric).toMatch(/partes\.length > 1 \? 'md:grid-cols-2'/);
  });

  it('edge: por debajo de md la grilla base es grid-cols-1', () => {
    expect(biometric).toMatch(/grid grid-cols-1 gap-4/);
  });
});

describe('HU #12729 — AC3 trazabilidad a ancho completo', () => {
  it('happy: ParteTrazabilidadSection / TrazabilidadDisclosure con aria-expanded', () => {
    expect(biometric).toMatch(/function ParteTrazabilidadSection/);
    expect(biometric).toMatch(/aria-expanded=\{open\}/);
    expect(biometric).toMatch(/Ver trazabilidad de validación/);
  });

  it('edge: disclosure inicia plegado (useState false)', () => {
    expect(biometric).toMatch(/const \[open, setOpen\] = useState\(false\)/);
  });
});

describe('HU #12729 — AC4 botones azules corporativos', () => {
  it('happy: WIZARD_BTN_BRAND sólido sin degradado en tarjetas', () => {
    expect(styles).toMatch(/export const WIZARD_BTN_BRAND/);
    expect(styles).toMatch(/text-white/);
    expect(biometric).toMatch(/WIZARD_BTN_BRAND/);
    expect(biometric).toMatch(/background: WIZARD_BTN_SOLID/);
  });

  it('contrato: outline para cancelar/reintentar; CTA gradient no en acciones de tarjeta', () => {
    expect(styles).toMatch(/export const WIZARD_BTN_BRAND_OUTLINE/);
    expect(biometric).toMatch(/WIZARD_BTN_BRAND_OUTLINE/);
    // Las acciones de tarjeta usan WIZARD_BTN_SOLID / BRAND, no WIZARD_CTA_GRADIENT.
    const cardBtnBlock = biometric.match(
      /className=\{`\$\{WIZARD_BTN_BRAND\}[\s\S]{0,200}WIZARD_BTN_SOLID/,
    );
    expect(cardBtnBlock).toBeTruthy();
  });
});

describe('HU #12729 — AC5 foco teclado / contraste documentado', () => {
  it('contrato: focus-visible ring en disclosure y botones brand', () => {
    expect(biometric).toMatch(/focus-visible:ring-2 focus-visible:ring-\[#557EFF\]/);
    expect(styles).toMatch(/focus-visible:outline/);
  });
});

describe('HU #12729 — AC6 no regresión (marca de specs existentes)', () => {
  it('contrato: BiometricStep sigue exportando el componente principal', () => {
    expect(biometric).toMatch(/export function BiometricStep/);
  });
});
