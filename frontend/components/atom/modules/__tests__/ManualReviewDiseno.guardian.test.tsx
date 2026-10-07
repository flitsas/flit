// Guarda de diseño FLIT de las pantallas de ADMINISTRACIÓN de la validación de identidad manual (Épica #13202).
// Lee el código fuente (no renderiza) y falla si vuelve a entrar un color fuera de los tokens de la línea base
// (`flit-design-guardian/references/flit_design_tokens.json`), una clase de paleta ajena de Tailwind o texto bajo 12px.
// La página pública del cliente (`app/verificacion/**`) queda FUERA a propósito: es la única que imita a Kyverum.
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const frontend = join(__dirname, '..', '..', '..', '..');

/** Archivos de administración cubiertos (rutas relativas a `frontend/`). */
const ARCHIVOS = [
  'components/atom/modules/ValidacionesManuales.tsx',
  'components/atom/modules/ManualStatusBadge.tsx',
  'components/atom/modules/ManualReviewDetailModal.tsx',
  'components/atom/modules/ManualReviewActions.tsx',
  'components/atom/modules/ManualImageGallery.tsx',
  'components/atom/modules/manual-field-styles.ts',
  'components/atom/modules/ApprovalOriginChip.tsx',
  'components/atom/modules/IdentityCaptureLinkBlock.tsx',
  'components/atom/SectionTabs.tsx',
  'lib/identidad/manual-review-meta.ts',
  'lib/identidad/motivos-rechazo-manual.ts',
] as const;

/**
 * HEX permitidos: cada uno sale de `flit_design_tokens.json` (marca, texto, borde, fondo de app/hover de tabla, `badge.*`,
 * `dark.badge.*`, `gradient.danger` y foco oscuro). Agregar uno aquí exige un token que lo respalde, no un gusto.
 */
const HEX_PERMITIDOS = new Set(
  [
    '#557EFF', '#00DBD5', '#8CC63F', '#FF4E00', '#162744', '#DFE5ED', // color.brand.*
    '#FFFFFF', '#EEF5FF', '#F4F8FF', // color.background.card / app / table.rowHover
    '#59677D', '#7D8798', // color.text.secondary / muted
    '#3B4FD6', '#4F7A12', '#B45309', '#C2410C', '#445569', '#F3FBE8', '#CDEB9C', '#F9AC00', // color.badge.*
    '#A5B8FF', '#B8E986', '#FBBF24', '#FCA574', '#CBD5E1', // dark.badge.*.fg
    '#E43D30', // gradient.danger (segundo stop)
    '#7C9BFF', // dark.border.focus / focusRing.colorDark
  ].map((h) => h.toUpperCase()),
);

/**
 * Excepciones por archivo (regla → motivo). Vacía a propósito: hoy ningún archivo del alcance necesita una.
 * Si hace falta una, se documenta aquí con su justificación y se acota al archivo, nunca global.
 */
const EXCEPCIONES: Record<string, { regla: 'hex' | 'paleta' | 'tamano'; motivo: string }[]> = {};

const PALETA_AJENA =
  /\b(?:text|bg|border|ring|outline|from|to|via|fill|stroke|divide|shadow|accent|decoration|placeholder)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}\b/g;
const TEXTO_MENOR_12 = /\btext-\[(?:\d|1[01])px\]/g;
// Texto blanco al 69 % o menos (piso de opacidad del guardián: 0.7).
const TEXTO_BAJA_OPACIDAD = /\btext-white\/(?:\[0?\.[0-6]\d*\]|[0-6]\d?)(?![\d.])/g;
const COLOR_FUNCIONAL = /\brgba?\(/g;
const HEX = /#[0-9A-Fa-f]{8}\b|#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{3}\b/g;

/** Código sin comentarios: los números de HU (`#13202`) y las notas no son colores. */
function codigo(rel: string): string {
  const src = readFileSync(join(frontend, rel), 'utf8');
  return src.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
}

function excepcion(rel: string, regla: 'hex' | 'paleta' | 'tamano'): boolean {
  return (EXCEPCIONES[rel] ?? []).some((e) => e.regla === regla);
}

describe('Guardián de diseño FLIT · administración de la identidad manual', () => {
  it.each(ARCHIVOS)('%s: solo HEX de los tokens FLIT y sin rgb()/rgba() sueltos', (rel) => {
    if (excepcion(rel, 'hex')) return;
    const src = codigo(rel);
    const ajenos = (src.match(HEX) ?? []).filter((h) => !HEX_PERMITIDOS.has(h.toUpperCase()));
    expect(ajenos, `HEX fuera de token en ${rel}`).toEqual([]);
    expect(src.match(COLOR_FUNCIONAL) ?? [], `rgb()/rgba() ad hoc en ${rel}`).toEqual([]);
  });

  it.each(ARCHIVOS)('%s: sin clases de paleta ajena de Tailwind (slate, gray, red, amber…)', (rel) => {
    if (excepcion(rel, 'paleta')) return;
    expect(codigo(rel).match(PALETA_AJENA) ?? [], `paleta ajena en ${rel}`).toEqual([]);
  });

  it.each(ARCHIVOS)('%s: ningún texto bajo 12px ni texto blanco bajo 0.7 de opacidad', (rel) => {
    if (excepcion(rel, 'tamano')) return;
    const src = codigo(rel);
    expect(src.match(TEXTO_MENOR_12) ?? [], `texto <12px en ${rel}`).toEqual([]);
    expect(src.match(TEXTO_BAJA_OPACIDAD) ?? [], `texto con opacidad <0.7 en ${rel}`).toEqual([]);
  });

  it('«Rechazar» usa el degradado de peligro de la línea base, en pastilla, no un rojo plano', () => {
    const acciones = codigo('components/atom/modules/ManualReviewActions.tsx');
    expect(acciones).toContain('WIZARD_CTA_GRADIENT_DANGER');
    expect(acciones).toContain('rounded-full');
    const estilos = codigo('components/operacion/wizard-field-styles.ts');
    expect(estilos).toContain("'linear-gradient(135deg, #FF4E00 0%, #E43D30 100%)'");
  });

  it('el detalle de Identidad (porción de la épica) mantiene el piso de 12px en «Origen de la aprobación»', () => {
    const drawer = codigo('components/atom/modules/PersonIdentityDetailDrawer.tsx');
    expect(drawer).toMatch(/text-xs[^"]*"[^>]*>Origen de la aprobación/);
  });

  it('cada estado de la pestaña usa uno de los 5 tonos de badge', () => {
    const meta = codigo('lib/identidad/manual-review-meta.ts');
    const tonos = [...meta.matchAll(/tone: '(\w+)'/g)].map((m) => m[1]);
    expect(tonos.length).toBeGreaterThanOrEqual(5);
    for (const t of tonos) expect(['success', 'warning', 'danger', 'info', 'neutral']).toContain(t);
    expect(meta).toMatch(/aprobado: \{[^}]*tone: 'success'/);
    expect(meta).toMatch(/rechazado: \{[^}]*tone: 'danger'/);
    expect(meta).toMatch(/expirado: \{[^}]*tone: 'neutral'/);
  });
});
