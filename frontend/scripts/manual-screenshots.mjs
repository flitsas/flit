#!/usr/bin/env node
/**
 * HU #13013 (Épica #12755) — pipeline automatizado de capturas del manual funcional.
 *
 * Lee lib/manual/screenshots.manifest.json y genera public/manual/screenshots/{id}.png
 * recorriendo la app real con Playwright y los usuarios seed de DevelopmentAuthSeeder.
 *
 * Uso:  pnpm manual:screenshots [-- --only id1,id2] [-- --base http://localhost:3000]
 * Requiere la app corriendo en local (frontend + core-api con ASPNETCORE_ENVIRONMENT=Development).
 *
 * Contrato (AC3): el fallo de una captura no tumba las demás — se acumula, se reporta con el dato
 * que faltó y el proceso termina con exit 1 si hubo al menos un fallo.
 */
import { mkdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const MANIFEST_PATH = join(ROOT, "lib", "manual", "screenshots.manifest.json");
const OUT_DIR = join(ROOT, "public", "manual", "screenshots");

/** Usuarios seed de DevelopmentAuthSeeder.cs — solo existen en entornos de desarrollo. */
const PROFILES = {
  public: null,
  gestor: { email: "radicador@empresa.local", password: "RadicadorPass1!" },
  admin_company: { email: "admin@empresa.local", password: "AdminPass1!" },
  ot_admin: { email: "otsabaneta@flit.local", password: "OtSabaneta1!" },
  superadmin: { email: "demo@flit.local", password: "DemoPass1!" },
};

function parseArgs(argv) {
  const args = { base: "http://localhost:3000", only: null };
  for (let i = 0; i < argv.length; i += 1) {
    if (argv[i] === "--base") args.base = argv[i + 1];
    if (argv[i] === "--only") args.only = new Set(argv[i + 1].split(","));
  }
  return args;
}

async function assertServerUp(base) {
  try {
    await fetch(base, { redirect: "manual" });
  } catch {
    throw new Error(
      `No hay servidor en ${base}. Levanta la app local (pnpm dev + core-api) antes de correr el pipeline.`,
    );
  }
}

/**
 * Sesión por API, igual que la app: POST /api/v1/auth/login y el JWT queda en la cookie
 * `flit_token` (la lee el middleware) y en localStorage `flit:jwt` (lo lee el cliente).
 * Evita depender del formulario de login y sus tiempos de compilación en dev.
 */
async function loginContext(browser, base, viewport, profile) {
  const context = await browser.newContext({ viewport, locale: "es-CO" });
  const cred = PROFILES[profile];
  if (!cred) return context;

  const response = await fetch(`${base}/api/v1/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: cred.email, password: cred.password }),
  });
  if (!response.ok) {
    throw new Error(`login de ${profile} (${cred.email}) falló con HTTP ${response.status} — ¿usuarios seed sembrados?`);
  }
  const body = await response.json();
  const token = body.token ?? body.accessToken ?? body.jwt;
  if (!token) {
    throw new Error(`login de ${profile}: la respuesta no trae token (claves: ${Object.keys(body).join(", ")})`);
  }

  const { hostname } = new URL(base);
  await context.addCookies([
    { name: "flit_token", value: token, domain: hostname, path: "/", sameSite: "Lax" },
  ]);
  await context.addInitScript((jwt) => {
    window.localStorage.setItem("flit:jwt", jwt);
    window.localStorage.setItem("flit:authed", "1");
  }, token);
  return context;
}

async function capture(context, base, entry) {
  const page = await context.newPage();
  try {
    await page.goto(base + entry.route, { waitUntil: "domcontentloaded" });
    if (entry.waitFor) await page.locator(entry.waitFor).first().waitFor({ timeout: 45000 });
    // Pequeña espera de asentamiento: animaciones de entrada y fuentes.
    await page.waitForTimeout(700);
    await page.screenshot({ path: join(OUT_DIR, `${entry.id}.png`), fullPage: false });
  } finally {
    await page.close();
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const manifest = JSON.parse(readFileSync(MANIFEST_PATH, "utf8"));
  const entries = manifest.entries.filter((e) => !args.only || args.only.has(e.id));
  if (entries.length === 0) {
    console.error("manual:screenshots — el filtro --only no coincide con ninguna entrada.");
    process.exit(1);
  }

  await assertServerUp(args.base);
  mkdirSync(OUT_DIR, { recursive: true });

  const browser = await chromium.launch();
  const contexts = new Map();
  const failures = [];

  for (const entry of entries) {
    try {
      if (!contexts.has(entry.profile)) {
        contexts.set(
          entry.profile,
          await loginContext(browser, args.base, manifest.viewport, entry.profile),
        );
      }
      await capture(contexts.get(entry.profile), args.base, entry);
      console.log(`  ✓ ${entry.id}.png (${entry.profile} → ${entry.route})`);
    } catch (error) {
      // AC3: se reporta qué faltó (login del perfil, ruta o selector) y se sigue con el resto.
      failures.push({ id: entry.id, reason: error.message?.split("\n")[0] ?? String(error) });
      console.error(`  ✗ ${entry.id}: ${error.message?.split("\n")[0]}`);
    }
  }

  await browser.close();

  if (failures.length > 0) {
    console.error(
      `\nmanual:screenshots — ${failures.length} de ${entries.length} capturas fallaron:\n` +
        failures.map((f) => `  - ${f.id}: ${f.reason}`).join("\n"),
    );
    process.exit(1);
  }
  console.log(`\nmanual:screenshots — ${entries.length} capturas regeneradas en public/manual/screenshots/.`);
}

main().catch((error) => {
  console.error(`manual:screenshots — ${error.message}`);
  process.exit(1);
});
