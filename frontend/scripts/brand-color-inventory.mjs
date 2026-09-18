#!/usr/bin/env node
// HU #12415 — Inventario reproducible de superficies con identidad de marca y de
// colores de marca escritos a mano. Solo lectura: no modifica ningún componente
// ni estilo de producto (AC5). Vive junto a las utilidades de lint del frontend
// y solo corre bajo demanda (`pnpm brand:inventory`) o en CI acotado al diff —
// nunca como parte del build por defecto.
//
// Uso de ejemplo:
//   node scripts/brand-color-inventory.mjs
//   node scripts/brand-color-inventory.mjs --check   (no escribe, solo exit code)
//
// Salida (por defecto): frontend/docs/brand-color-inventory.json y .md
//
// Diseño (ADR-0060 §D5, mapa-hus-archivos.md #12415):
// - SURFACES enumera el alcance cerrado de la épica Marca Blanca (#12367):
//   acceso, activación de cuenta, recuperación de contraseña, cabecera y menú,
//   icono y título de la pestaña. Mantener sincronizado con
//   eslint.config.mjs (MARCA_BLANCA_SURFACE_GLOBS) — la regla de lint referencia
//   exactamente estos mismos archivos.
// - Cada aparición de hex dentro de una superficie se clasifica como
//   "marca" | "neutro" | "estado" (AC2).
// - Además se cuenta, en app/ + components/ + lib/ completos, el total de
//   apariciones de colores de marca dentro vs. fuera de las superficies del
//   alcance (AC2) — sin clasificar línea a línea el resto del repositorio.

import { readFileSync, writeFileSync, existsSync, readdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
export const FRONTEND_ROOT = path.resolve(__dirname, "..");

// Los 6 colores de marca FLIT crudos (frontend/app/globals.css:9-14). Case-insensitive:
// cualquier variante de mayúsculas cuenta como la misma aparición.
export const BRAND_HEXES = ["162744", "557eff", "00dbd5", "ff4e00", "eef5ff", "dfe5ed"];
const BRAND_HEX_SET = new Set(BRAND_HEXES.map((h) => h.toLowerCase()));

// Neutros estructurales conocidos (fondo/tarjeta modo oscuro, blanco/negro puro).
const KNOWN_NEUTRAL_HEXES = new Set(["ffffff", "000000", "05060a", "0b0f14"]);

const HEX_PATTERN = /#([0-9a-fA-F]{6})\b/g;

/**
 * Clasifica un hex de 6 dígitos (sin '#') como "marca", "neutro" o "estado".
 * - marca: exactamente uno de los 6 tokens crudos de frontend/app/globals.css:9-14.
 * - neutro: escala de grises (R=G=B) o uno de los neutros estructurales conocidos
 *   (blanco/negro puro, fondos/tarjetas oscuros del shell).
 * - estado: todo lo demás (semántico — éxito/alerta/advertencia — o acento no
 *   catalogado; #12420 decide caso a caso si requiere token propio).
 */
export function classifyHex(hexNoHash) {
  const h = hexNoHash.toLowerCase();
  if (BRAND_HEX_SET.has(h)) return "marca";
  if (KNOWN_NEUTRAL_HEXES.has(h)) return "neutro";
  const r = h.slice(0, 2);
  const g = h.slice(2, 4);
  const b = h.slice(4, 6);
  if (r === g && g === b) return "neutro";
  return "estado";
}

// Superficies del alcance cerrado (HU #12415 AC1; ADR-0060 §D5;
// .claude/state/marca-blanca/diseno/mapa-hus-archivos.md #12415/#12419).
// Cada `files` es la lista real (verificada contra el árbol) de archivos que
// componen la superficie — no solo los que hoy tienen hex literal, también los
// que la superficie usa sin colores propios (para que "deja de existir" sea
// detectable aunque el archivo nunca haya tenido un hex).
export const SURFACES = [
  {
    id: "acceso",
    label: "Pantalla de acceso",
    files: ["components/atom/Login.tsx", "app/login/page.tsx"],
  },
  {
    id: "activacion",
    label: "Activación de cuenta",
    files: ["app/invite/activate/page.tsx", "components/auth/ActivateAccountForm.tsx"],
  },
  {
    id: "recuperacion",
    label: "Recuperación de contraseña",
    files: [
      "app/auth/forgot-password/page.tsx",
      "app/auth/reset-password/page.tsx",
      "components/auth/ForgotPasswordForm.tsx",
      "components/auth/ResetPasswordForm.tsx",
      // AuthCard.tsx sirve dos variantes ("auth" para acceso/recuperación,
      // "overlay" para páginas internas como /profile/change-password, fuera
      // del alcance cerrado). Se lista completo: la regla de lint cubre el
      // archivo entero, más estricto que el mínimo exigido, sin costo — ambas
      // variantes ya comparten los mismos 6 hex de marca.
      "components/auth/AuthCard.tsx",
    ],
  },
  {
    id: "cabecera-menu",
    label: "Cabecera y menú",
    files: [
      "components/atom/Shell.tsx",
      // Menú/dock de navegación renderizado dentro de la cabecera del Shell.
      "components/atom/dock/DockDesktop.tsx",
    ],
  },
  {
    id: "pestana",
    label: "Icono y título de la pestaña",
    files: [
      "app/layout.tsx",
      // Favicon (convención app/icon.svg de Next.js) — gradiente de marca.
      "app/icon.svg",
    ],
  },
];

// Fuera del alcance cerrado a propósito (documentado para que no se reinterprete
// como omisión): /profile/change-password no es "recuperación de contraseña"
// (AC1 la limita a la recuperación no autenticada); su AuthCard variant="overlay"
// ya queda cubierto porque AuthCard.tsx se audita completo.
export const EXCLUDED_OUT_OF_SCOPE = [
  {
    file: "app/profile/change-password/page.tsx",
    reason:
      "Cambio de contraseña autenticado (ajustes de perfil), no 'recuperación de contraseña' (flujo no autenticado de AC1).",
  },
  {
    file: "components/auth/ChangePasswordForm.tsx",
    reason: "Formulario exclusivo de app/profile/change-password/page.tsx — mismo motivo.",
  },
];

// Metodología del conteo repositorio-completo (AC2 "dentro y fuera del alcance").
// Deliberadamente más amplia que el recorte de la exploración previa (F11 del
// brief: solo app/+components/ en .tsx) para no subestimar: aquí se incluyen
// también lib/ y las extensiones .ts/.jsx/.js/.css/.svg. El número resultante
// por tanto no reproduce literalmente la cifra de referencia citada en el AC2
// de la HU (2.885 apariciones / 352 archivos, de un conteo previo con otro
// alcance de extensiones) — se documenta la metodología para que el número sea
// reproducible y auditable hacia adelante, que es el objetivo del AC3.
const SCAN_DIRS = ["app", "components", "lib"];
const SCAN_EXTENSIONS = new Set([".ts", ".tsx", ".js", ".jsx", ".css", ".svg"]);
const EXCLUDED_DIR_SEGMENTS = new Set(["node_modules", ".next", "__tests__"]);

function walk(dir, out = []) {
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir)) {
    if (EXCLUDED_DIR_SEGMENTS.has(entry)) continue;
    const full = path.join(dir, entry);
    const st = statSync(full);
    if (st.isDirectory()) {
      walk(full, out);
    } else if (SCAN_EXTENSIONS.has(path.extname(entry))) {
      out.push(full);
    }
  }
  return out;
}

function toPosix(p) {
  return p.split(path.sep).join("/");
}

/** Cuenta apariciones de hex de marca (las 6 exactas) en un archivo. */
function countBrandHexOccurrences(absPath) {
  const content = readFileSync(absPath, "utf8");
  const matches = content.match(HEX_PATTERN) ?? [];
  return matches.filter((m) => BRAND_HEX_SET.has(m.slice(1).toLowerCase())).length;
}

/**
 * Escanea línea a línea los archivos de una superficie y devuelve cada
 * aparición de hex de 6 dígitos con su clasificación.
 */
function scanSurfaceFile(relPath) {
  const absPath = path.join(FRONTEND_ROOT, relPath);
  if (!existsSync(absPath)) {
    return { relPath, exists: false, occurrences: [] };
  }
  const lines = readFileSync(absPath, "utf8").split(/\r\n|\n/);
  const occurrences = [];
  lines.forEach((line, idx) => {
    let match;
    const lineRegex = new RegExp(HEX_PATTERN.source, "g");
    while ((match = lineRegex.exec(line)) !== null) {
      const hex = match[1];
      occurrences.push({
        line: idx + 1,
        hex: `#${hex}`,
        hexNormalized: `#${hex.toLowerCase()}`,
        classification: classifyHex(hex),
        snippet: line.trim().slice(0, 160),
      });
    }
  });
  return { relPath, exists: true, occurrences };
}

/** Ejecuta el inventario completo y devuelve la estructura de datos (sin escribir nada). */
export function buildInventory() {
  const surfaces = SURFACES.map((surface) => {
    const files = surface.files.map(scanSurfaceFile);
    const marca = files.flatMap((f) => f.occurrences.filter((o) => o.classification === "marca"));
    const neutro = files.flatMap((f) => f.occurrences.filter((o) => o.classification === "neutro"));
    const estado = files.flatMap((f) => f.occurrences.filter((o) => o.classification === "estado"));
    return {
      id: surface.id,
      label: surface.label,
      files,
      totals: { marca: marca.length, neutro: neutro.length, estado: estado.length },
    };
  });

  const scopeFileSet = new Set(SURFACES.flatMap((s) => s.files));
  const allScanned = SCAN_DIRS.flatMap((d) => walk(path.join(FRONTEND_ROOT, d)));

  let insideScopeOccurrences = 0;
  let insideScopeFiles = 0;
  let outsideScopeOccurrences = 0;
  let outsideScopeFiles = 0;

  for (const absPath of allScanned) {
    const rel = toPosix(path.relative(FRONTEND_ROOT, absPath));
    const count = countBrandHexOccurrences(absPath);
    if (count === 0) continue;
    if (scopeFileSet.has(rel)) {
      insideScopeOccurrences += count;
      insideScopeFiles += 1;
    } else {
      outsideScopeOccurrences += count;
      outsideScopeFiles += 1;
    }
  }

  return {
    generatedAt: new Date().toISOString(),
    hu: 12415,
    methodology: {
      scanDirs: SCAN_DIRS,
      scanExtensions: [...SCAN_EXTENSIONS],
      excludedDirSegments: [...EXCLUDED_DIR_SEGMENTS],
      brandHexes: BRAND_HEXES.map((h) => `#${h}`),
      note:
        "Conteo repositorio-completo con metodología propia (ver comentario junto a SCAN_DIRS); no reproduce literalmente la cifra de referencia citada en el AC2 de la HU.",
    },
    surfaces,
    excludedOutOfScope: EXCLUDED_OUT_OF_SCOPE,
    repoWide: {
      totalFilesScanned: allScanned.length,
      brandHexOccurrences: {
        insideScope: { files: insideScopeFiles, occurrences: insideScopeOccurrences },
        outsideScope: { files: outsideScopeFiles, occurrences: outsideScopeOccurrences },
        total: insideScopeOccurrences + outsideScopeOccurrences,
      },
    },
  };
}

function renderMarkdown(inventory) {
  const lines = [];
  lines.push("# Inventario de marca — HU #12415");
  lines.push("");
  lines.push(
    `Generado: ${inventory.generatedAt} · regenerar con \`pnpm brand:inventory\` (frontend/scripts/brand-color-inventory.mjs).`,
  );
  lines.push("");
  lines.push(
    "Inventario **cerrado**: script de solo lectura, no modifica ningún componente ni estilo de producto (AC5). El saneamiento es HU #12420.",
  );
  lines.push("");
  lines.push("## Resumen repositorio-completo (AC2)");
  lines.push("");
  lines.push("| Ámbito | Archivos con hex de marca | Apariciones de marca |");
  lines.push("|---|---|---|");
  lines.push(
    `| Dentro del alcance | ${inventory.repoWide.brandHexOccurrences.insideScope.files} | ${inventory.repoWide.brandHexOccurrences.insideScope.occurrences} |`,
  );
  lines.push(
    `| Fuera del alcance | ${inventory.repoWide.brandHexOccurrences.outsideScope.files} | ${inventory.repoWide.brandHexOccurrences.outsideScope.occurrences} |`,
  );
  lines.push(
    `| **Total** | — | **${inventory.repoWide.brandHexOccurrences.total}** |`,
  );
  lines.push("");
  lines.push(
    `Metodología: directorios \`${inventory.methodology.scanDirs.join(", ")}\`, extensiones \`${inventory.methodology.scanExtensions.join(", ")}\`, excluyendo \`${inventory.methodology.excludedDirSegments.join(", ")}\`. ${inventory.methodology.note}`,
  );
  lines.push("");

  for (const surface of inventory.surfaces) {
    lines.push(`## Superficie: ${surface.label} (\`${surface.id}\`)`);
    lines.push("");
    lines.push(`Marca: ${surface.totals.marca} · Neutro: ${surface.totals.neutro} · Estado: ${surface.totals.estado}`);
    lines.push("");
    lines.push("| Archivo | Existe |");
    lines.push("|---|---|");
    for (const f of surface.files) {
      lines.push(`| \`${f.relPath}\` | ${f.exists ? "sí" : "**NO — falta**"} |`);
    }
    lines.push("");
    const withOccurrences = surface.files.filter((f) => f.occurrences.length > 0);
    if (withOccurrences.length > 0) {
      lines.push("| Archivo | Línea | Hex | Clasificación |");
      lines.push("|---|---|---|---|");
      for (const f of withOccurrences) {
        for (const o of f.occurrences) {
          lines.push(`| \`${f.relPath}\` | ${o.line} | \`${o.hex}\` | ${o.classification} |`);
        }
      }
      lines.push("");
    }
  }

  lines.push("## Excluidas del alcance a propósito");
  lines.push("");
  lines.push("| Archivo | Motivo |");
  lines.push("|---|---|");
  for (const e of inventory.excludedOutOfScope) {
    lines.push(`| \`${e.file}\` | ${e.reason} |`);
  }
  lines.push("");

  return lines.join("\n");
}

function main() {
  const checkOnly = process.argv.includes("--check");
  const inventory = buildInventory();

  const missing = inventory.surfaces
    .flatMap((s) => s.files)
    .filter((f) => !f.exists)
    .map((f) => f.relPath);
  if (missing.length > 0) {
    console.error(
      `brand-color-inventory: ${missing.length} archivo(s) inventariado(s) ya no existen: ${missing.join(", ")}`,
    );
    process.exitCode = 1;
    if (checkOnly) return;
  }

  if (checkOnly) {
    console.log("brand-color-inventory: --check OK (todas las superficies existen).");
    return;
  }

  const outDir = path.join(FRONTEND_ROOT, "docs");
  const jsonPath = path.join(outDir, "brand-color-inventory.json");
  const mdPath = path.join(outDir, "brand-color-inventory.md");
  writeFileSync(jsonPath, JSON.stringify(inventory, null, 2) + "\n", "utf8");
  writeFileSync(mdPath, renderMarkdown(inventory) + "\n", "utf8");
  console.log(`brand-color-inventory: escrito ${toPosix(path.relative(FRONTEND_ROOT, jsonPath))} y ${toPosix(path.relative(FRONTEND_ROOT, mdPath))}`);
}

// Solo ejecutar main() cuando se invoca como CLI (no al importar desde tests).
if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  main();
}
