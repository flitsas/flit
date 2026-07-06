import { readFileSync, readdirSync, statSync } from "node:fs";
import path from "node:path";

/**
 * HU #10492 — Modo oscuro: uniformar bordes y completar tema en pantallas incompletas.
 *
 * Uso de ejemplo:
 *   collectTsx(root) -> ["components/admin/.../X.tsx", ...]
 *   grepCount(files, /borderColor:\s*["']#DFE5ED/g) -> 0  (invariante AC1)
 *
 * Tests de contrato: verifican los invariantes de cada AC sobre TODO el árbol
 * de UI (no un solo componente), que es lo que exige un refactor transversal.
 */

const FE_ROOT = path.resolve(__dirname, "..");

function collectTsx(dir: string): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(dir)) {
    if (entry === "node_modules" || entry === ".next") continue;
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) out.push(...collectTsx(full));
    else if (entry.endsWith(".tsx")) out.push(full);
  }
  return out;
}

const uiFiles = [
  ...collectTsx(path.join(FE_ROOT, "components")),
  ...collectTsx(path.join(FE_ROOT, "app")),
];

const readAll = (files: string[]) => files.map((f) => readFileSync(f, "utf8"));

describe("HU #10492 — AC1: bordes sin override inline (token semántico theme-aware)", () => {
  const contents = readAll(uiFiles);

  it("happy path: ningún .tsx conserva borderColor inline estático #DFE5ED", () => {
    const offenders = uiFiles.filter((_, i) =>
      /borderColor:\s*["']#DFE5ED["']/.test(contents[i]),
    );
    expect(offenders).toEqual([]);
  });

  it("edge: tampoco quedan objetos style vacíos ('style={{ }}') del codemod", () => {
    const empties = uiFiles.filter((_, i) => /style=\{\{\s*\}\}/.test(contents[i]));
    expect(empties).toEqual([]);
  });

  it("contrato: los bordes condicionales de error (#FF4E00) se preservan intactos", () => {
    // El codemod solo debía tocar el borde base #DFE5ED, nunca el de error.
    const anyError = contents.some((c) => /borderColor:.*#FF4E00.*#DFE5ED/.test(c));
    expect(anyError).toBe(true);
  });
});

describe("HU #10492 — AC2: pantalla RBAC con tema oscuro completo", () => {
  const rbac = readFileSync(
    path.join(FE_ROOT, "components/atom/modules/RbacAdmin.tsx"),
    "utf8",
  );

  it("happy path: el contenedor raíz usa app-bg y texto theme-aware", () => {
    expect(rbac).toMatch(/className="app-bg[^"]*dark:text-white/);
  });

  it("edge: ya no fuerza el fondo claro inline (#EEF5FF) en el raíz", () => {
    expect(rbac).not.toMatch(/background:\s*"#EEF5FF"/);
  });

  it("contrato: los contenedores/modales blancos declaran su variante dark", () => {
    // Ningún 'bg-white' sin acompañarse de dark:bg en el mismo className.
    const whiteWithoutDark = [
      ...rbac.matchAll(/className="([^"]*\bbg-white\b[^"]*)"/g),
    ].filter((m) => !/dark:bg-/.test(m[1]));
    expect(whiteWithoutDark).toEqual([]);
  });
});

describe("HU #10492 — AC3: token de borde = gris de marca en claro, white/10 en oscuro", () => {
  const css = readFileSync(path.join(FE_ROOT, "app/globals.css"), "utf8");

  it("happy path: :root define --border como el oklch de #DFE5ED", () => {
    expect(css).toMatch(/--border:\s*oklch\(0\.9197 0\.0127 255\.51\)/);
  });

  it("contrato: .dark mantiene --border translúcido (white/10)", () => {
    const darkBlock = css.slice(css.indexOf(".dark"));
    expect(darkBlock).toMatch(/--border:\s*oklch\(1 0 0 \/ 10%\)/);
  });

  it("edge: no regresión — el token de marca --color-flit-gray sigue siendo #DFE5ED", () => {
    expect(css).toMatch(/--color-flit-gray:\s*#dfe5ed/i);
  });
});
