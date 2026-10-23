import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Guarda de diseño (flit-design-guardian) de HU #13288: el componente de flujo manual de identidad vive
 * dentro de FLIT y solo puede usar tokens del guardián. Lee el código fuente, como las guardas análogas.
 */
const ROOT = join(__dirname, '..', '..', '..', '..');
const ARCHIVOS = ['components/atom/modules/IdentityManualFlowActions.tsx', 'lib/identity/manual-flow.ts'];

/** Tokens de flit_design_tokens.json que este componente puede usar (sin #0B0F14/#05060A: superficie oscura inventada). */
const HEX_PERMITIDOS = new Set(
  [
    '#162744', '#557EFF', '#00DBD5', '#DFE5ED', '#F4F8FF', '#FFFFFF', '#7C9BFF', '#FF4E00', '#E43D30',
    '#3B4FD6', '#A5B8FF', '#4F7A12', '#B8E986', '#CDEB9C', '#F3FBE8', '#B45309', '#FBBF24', '#C2410C',
    '#FCA574', '#59677D', '#8CC63F',
  ].map((h) => h.toUpperCase()),
);
/** Excepciones de rgb()/rgba(): solo el overlay de modal exigido por el guardián (rgba(22,39,68,0.45) + blur 6px). */
const RGBA_PERMITIDOS = ['rgba(22,39,68,0.45)'];
const PALETA_AJENA = /\b(?:bg|text|border|ring|outline|fill|stroke|from|to|via|divide|placeholder)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}/;

function lineas(archivo: string): string[] {
  return readFileSync(join(ROOT, archivo), 'utf8').split('\n');
}
/** Descarta comentarios de línea/bloque para no castigar la documentación. */
function codigo(archivo: string): string[] {
  return lineas(archivo).filter((l) => !/^\s*(\/\/|\/?\*)/.test(l));
}

describe.each(ARCHIVOS)('guardián de diseño · %s', (archivo) => {
  it('solo usa colores HEX de flit_design_tokens.json', () => {
    const fuera = codigo(archivo).flatMap((l) =>
      (l.match(/#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{3}\b/g) ?? []).filter((h) => !HEX_PERMITIDOS.has(h.toUpperCase())),
    );
    expect(fuera).toEqual([]);
  });

  it('no usa rgb()/rgba() sueltos (salvo el overlay del guardián)', () => {
    const fuera = codigo(archivo).flatMap((l) =>
      (l.match(/rgba?\([^)]*\)/g) ?? []).filter((c) => !RGBA_PERMITIDOS.includes(c.replace(/\s/g, ''))),
    );
    expect(fuera).toEqual([]);
  });

  it('no usa paleta ajena de Tailwind (slate, gray, etc.)', () => {
    expect(codigo(archivo).filter((l) => PALETA_AJENA.test(l))).toEqual([]);
  });

  it('no usa texto menor a 12px', () => {
    const chico = codigo(archivo).filter(
      (l) => /text-\[(?:[0-9]|1[01])(?:\.\d+)?px\]/.test(l) || /text-\[0?\.\d+r?em\]/.test(l),
    );
    expect(chico).toEqual([]);
  });

  it('no usa rounded-xl en botones (pastilla rounded-full)', () => {
    const src = codigo(archivo).join('\n');
    const botones = src.match(/(?:const \w*BTN\w* =|<button)[\s\S]*?(?:;\n|>)/g) ?? [];
    expect(botones.filter((b) => /rounded-(?:xl|2xl|lg|md)\b/.test(b))).toEqual([]);
  });
});

describe('guardián de diseño · modal de IdentityManualFlowActions', () => {
  const src = codigo(ARCHIVOS[0]).join('\n');
  it('overlay rgba(22,39,68,0.45) con blur(6px), panel de 18px y capa dark #162744', () => {
    expect(src).toContain("rgba(22,39,68,0.45)");
    expect(src).toContain('blur(6px)');
    expect(src).toContain('rounded-[18px]');
    expect(src).toContain('dark:bg-[#162744]');
    expect(src).not.toContain('backdrop-blur-md');
  });
});
