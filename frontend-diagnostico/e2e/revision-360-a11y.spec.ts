import fs from "node:fs";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";
import { lanzarExpress } from "./express";

/**
 * Revisión local del rediseño UI Kit 2026 (HU #14562): 360x800 (sin scroll horizontal, controles alcanzables, texto sin
 * cortar), axe-core (WCAG 2.x A/AA) y teclado (foco visible, orden de tab, Escape cierra modales).
 *
 * NO es un spec de regresión: registra hallazgos (JSON + capturas) en vez de fallar en el primero, para que QA los revise.
 * Solo corre en el project `movil-360` del config local (e2e/playwright.config.ts). axe-core se inyecta desde la copia que
 * ya trae el workspace (dependencia de eslint-plugin-jsx-a11y); no se añadió @axe-core/playwright.
 *
 * Variables: E2E_USUARIO/E2E_CLAVE (usuario_diagnostico), E2E_BACKOFFICE/E2E_CLAVE_BACKOFFICE (SuperAdmin FLIT),
 * E2E_EMPRESA (uuid de la compañía cliente para el backoffice), E2E_PLACA_EXPRESS (por defecto TST101).
 */
const APP = process.env.DIAGNOSTICO_URL ?? "http://127.0.0.1:4024";
const PLACA = process.env.E2E_PLACA_EXPRESS ?? "TST101";
const SALIDA = path.join(
  process.env.E2E_SALIDA ?? path.resolve(__dirname, "../../.claude/state/rediseno-diagnostico/evidencias/e2e-local"),
  "revision",
);
fs.mkdirSync(SALIDA, { recursive: true });

function rutaAxe(): string {
  const pnpm = path.resolve(__dirname, "../../node_modules/.pnpm");
  for (const dir of fs.readdirSync(pnpm)) {
    const candidato = path.join(pnpm, dir, "node_modules/axe-core/axe.min.js");
    if (fs.existsSync(candidato)) return candidato;
  }
  throw new Error("axe-core no está en el workspace");
}
const AXE = rutaAxe();

async function entrar(page: Page, usuario: string, clave: string, destino = "/") {
  await page.goto(`${APP}${destino}`);
  await page.waitForURL((url) => !url.href.startsWith(APP));
  await page.getByLabel(/correo|usuario/i).fill(usuario);
  await page.getByLabel("Contraseña", { exact: true }).fill(clave);
  await page.getByRole("button", { name: /ingresar|iniciar sesión/i }).click();
  await page.waitForURL((url) => url.href.startsWith(APP) && !url.pathname.startsWith("/api/"));
}

type Revision = Record<string, unknown>;
function guardar(nombre: string, datos: Revision) {
  fs.writeFileSync(path.join(SALIDA, `${nombre}.json`), JSON.stringify(datos, null, 2), "utf8");
}

/** Métricas 360: desborde de página, controles fuera del viewport, textos cortados. */
async function medir360(page: Page) {
  return page.evaluate(() => {
    const vw = document.documentElement.clientWidth;
    const desborde = document.documentElement.scrollWidth - vw;
    const nombre = (el: Element) =>
      (el.getAttribute("aria-label") || (el as HTMLElement).innerText || el.getAttribute("name") || el.tagName).trim().slice(0, 60);
    const visible = (el: Element) => {
      const r = el.getBoundingClientRect();
      const s = getComputedStyle(el);
      return r.width > 0 && r.height > 0 && s.visibility !== "hidden" && s.display !== "none";
    };
    // ¿Algún ancestro con scroll horizontal propio (tabla con overflow-x) contiene el control? Entonces es alcanzable.
    const dentroDeScroll = (el: Element) => {
      for (let p = el.parentElement; p; p = p.parentElement) {
        const s = getComputedStyle(p);
        if ((s.overflowX === "auto" || s.overflowX === "scroll") && p.scrollWidth > p.clientWidth) return true;
      }
      return false;
    };
    const controles = [...document.querySelectorAll("a[href], button, input, select, textarea, [role=button], [role=tab], [role=radio], [role=switch]")]
      .filter(visible);
    const fuera = controles
      .map((el) => ({ el, r: el.getBoundingClientRect() }))
      .filter(({ r }) => r.right > vw + 1 || r.left < -1)
      .map(({ el, r }) => ({ control: nombre(el), left: Math.round(r.left), right: Math.round(r.right), enScrollPropio: dentroDeScroll(el) }));
    const pequenos = controles
      .map((el) => ({ el, r: el.getBoundingClientRect() }))
      .filter(({ el, r }) => (r.width < 24 || r.height < 24) && !(el instanceof HTMLInputElement && (el.type === "checkbox" || el.type === "radio")))
      .map(({ el, r }) => ({ control: nombre(el), w: Math.round(r.width), h: Math.round(r.height) }));
    const cortados = [...document.querySelectorAll("body *")]
      .filter((el) => el.children.length === 0 && (el.textContent ?? "").trim().length > 0 && visible(el))
      .filter((el) => {
        const s = getComputedStyle(el);
        const corta = s.overflowX === "hidden" || s.overflow === "hidden" || s.textOverflow === "ellipsis" || s.whiteSpace === "nowrap";
        return corta && el.scrollWidth > el.clientWidth + 1 && !dentroDeScroll(el);
      })
      .map((el) => ({ texto: (el.textContent ?? "").trim().slice(0, 80), title: el.getAttribute("title") }));
    return { viewport: vw, desbordeHorizontalPx: desborde, controlesFueraDelViewport: fuera, controlesMenoresDe24px: pequenos, textosCortados: cortados.slice(0, 30) };
  });
}

async function axe(page: Page) {
  await page.addScriptTag({ path: AXE });
  const r = await page.evaluate(async () => {
    // @ts-expect-error axe se inyecta en la página
    const res = await window.axe.run(document, { runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] } });
    return res.violations.map((v: { id: string; impact: string; help: string; nodes: { target: string[]; failureSummary: string }[] }) => ({
      id: v.id,
      impacto: v.impact,
      ayuda: v.help,
      nodos: v.nodes.length,
      ejemplos: v.nodes.slice(0, 3).map((n) => ({ selector: n.target.join(" "), resumen: n.failureSummary?.split("\n").slice(0, 3).join(" | ") })),
    }));
  });
  return r;
}

async function revisarPantalla(page: Page, nombre: string) {
  await page.waitForLoadState("networkidle").catch(() => undefined);
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(SALIDA, `360-${nombre}.png`), fullPage: true });
  const metricas = await medir360(page);
  const violaciones = await axe(page);
  guardar(`360-${nombre}`, { url: page.url(), ...metricas, axe: violaciones });
  return { metricas, violaciones };
}

/** Recorre N tabs: nombre accesible del foco y si tiene indicador visible (outline o box-shadow). */
async function ordenDeTab(page: Page, pasos = 25) {
  await page.locator("body").click({ position: { x: 1, y: 1 } }).catch(() => undefined);
  const recorrido: { i: number; foco: string; indicador: boolean }[] = [];
  for (let i = 0; i < pasos; i++) {
    await page.keyboard.press("Tab");
    recorrido.push(
      await page.evaluate((i) => {
        const el = document.activeElement as HTMLElement | null;
        if (!el || el === document.body) return { i, foco: "(body)", indicador: false };
        const s = getComputedStyle(el);
        const indicador = (s.outlineStyle !== "none" && parseFloat(s.outlineWidth) > 0) || (s.boxShadow !== "none" && s.boxShadow !== "");
        const n = (el.getAttribute("aria-label") || el.innerText || el.getAttribute("name") || el.tagName).trim().replace(/\s+/g, " ").slice(0, 60);
        return { i, foco: `${el.tagName.toLowerCase()}${el.getAttribute("role") ? `[${el.getAttribute("role")}]` : ""} «${n}»`, indicador };
      }, i),
    );
  }
  return recorrido;
}

test.describe("revisión 360 + axe (usuario de compañía)", () => {
  test("listado, nuevo Masivo/Express, tablero, detalle, configuración y campana", async ({ page, context }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    test.setTimeout(300_000);
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);

    await test.step("listado", async () => {
      await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();
      await revisarPantalla(page, "01-listado");
    });
    await test.step("campana de avisos", async () => {
      const campana = page.getByRole("button", { name: /^Avisos/ });
      if (await campana.count()) {
        await campana.first().click();
        await expect(page.getByRole("region", { name: "Avisos" })).toBeVisible();
        await revisarPantalla(page, "02-campana");
        await page.keyboard.press("Escape");
        guardar("teclado-campana-escape-360", { cerrada: (await page.getByRole("region", { name: "Avisos" }).count()) === 0 });
      } else guardar("360-02-campana", { hallazgo: "No se encontró el botón «Avisos» en el encabezado a 360 px" });
    });
    await test.step("nuevo masivo", async () => {
      await page.goto(`${APP}/diagnosticos/nuevo?modo=masivo`);
      await expect(page.getByRole("radio", { name: "Masivo" })).toHaveAttribute("aria-checked", "true");
      await revisarPantalla(page, "03-nuevo-masivo");
    });
    await test.step("nuevo express", async () => {
      await page.goto(`${APP}/diagnosticos/nuevo?modo=express`);
      await expect(page.getByRole("radio", { name: "Express" })).toHaveAttribute("aria-checked", "true");
      await revisarPantalla(page, "04-nuevo-express");
    });
    let detalleUrl: string | null = null;
    await test.step("tablero", async () => {
      await page.goto(`${APP}/`);
      await lanzarExpress(page, PLACA, `E2E revision 360 ${Date.now()}`);
      const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
      await expect(grilla).toBeVisible();
      await expect(grilla.locator("[data-celda][data-estado]").first()).toHaveAttribute("data-estado", "COMPLETADO", { timeout: 60_000 });
      await revisarPantalla(page, "05-tablero");
      const dictamen = grilla.getByRole("button", { name: `Ver dictamen de ${PLACA}` });
      if (await dictamen.count()) {
        await dictamen.click();
        await expect(page.getByRole("dialog", { name: `Dictamen · ${PLACA}` })).toBeVisible();
        await revisarPantalla(page, "06-tablero-panel-dictamen");
        await page.keyboard.press("Escape");
        guardar("teclado-dictamen-escape-360", { cerrado: (await page.getByRole("dialog").count()) === 0 });
        if (await page.getByRole("dialog").count()) await page.getByRole("dialog").getByRole("button", { name: "Cerrar" }).click();
      }
      const enlace = grilla.getByRole("link", { name: /^Ver detalle de RUNT/ }).first();
      if (await enlace.count()) detalleUrl = new URL((await enlace.getAttribute("href"))!, APP).href;
    });
    await test.step("detalle de consulta", async () => {
      test.skip(!detalleUrl, "Sin enlace de detalle RUNT en el tablero");
      const pestana = await context.newPage();
      await pestana.goto(detalleUrl!);
      await expect(pestana.getByRole("heading", { level: 1 })).toBeVisible();
      await revisarPantalla(pestana, "07-detalle-consulta");
      await pestana.close();
    });
    await test.step("configuración", async () => {
      await page.goto(`${APP}/configuracion`);
      await expect(page.getByRole("heading", { level: 1, name: "Configuración" })).toBeVisible();
      await revisarPantalla(page, "08-configuracion-contrato");
      const op = page.getByRole("tab", { name: "Operación" });
      if (await op.count()) {
        await op.click();
        await revisarPantalla(page, "09-configuracion-operacion");
      }
    });
  });
});

test.describe("revisión 360 + axe (backoffice FLIT)", () => {
  test("lotes por compañía y llenado manual", async ({ page }) => {
    test.skip(!process.env.E2E_BACKOFFICE || !process.env.E2E_CLAVE_BACKOFFICE || !process.env.E2E_EMPRESA, "Faltan E2E_BACKOFFICE / E2E_CLAVE_BACKOFFICE / E2E_EMPRESA");
    test.setTimeout(180_000);
    await entrar(page, process.env.E2E_BACKOFFICE!, process.env.E2E_CLAVE_BACKOFFICE!);
    await page.goto(`${APP}/backoffice/diagnosticos?empresa=${encodeURIComponent(process.env.E2E_EMPRESA!)}`);
    await expect(page.getByRole("heading", { level: 1, name: "Lotes por compañía" })).toBeVisible();
    await revisarPantalla(page, "10-backoffice-lotes");
    const lotes = page.getByRole("table", { name: /lotes de la compañía/i });
    if (await lotes.getByRole("link").count()) {
      await lotes.getByRole("link").first().click();
      await page.waitForURL(/\/backoffice\/diagnosticos\/[0-9a-f-]{36}\?empresa=/i);
      await expect(page.getByRole("table", { name: /semáforo de consultas/i })).toBeVisible();
      await revisarPantalla(page, "11-backoffice-lote");
      const llenar = page.getByRole("button", { name: /^Llenar a mano / }).first();
      if (await llenar.count()) {
        await llenar.click();
        await expect(page.getByRole("dialog", { name: /llenar a mano/i })).toBeVisible();
        await revisarPantalla(page, "12-backoffice-modal-llenado");
        await page.keyboard.press("Escape");
        guardar("teclado-llenado-escape-360", { cerrado: (await page.getByRole("dialog", { name: /llenar a mano/i }).count()) === 0 });
      }
    }
  });
});

test.describe("teclado en escritorio (1280)", () => {
  test.use({ viewport: { width: 1280, height: 800 }, isMobile: false, hasTouch: false, deviceScaleFactor: 1 });

  test("orden de tab, foco visible y Escape en panel de dictamen y campana", async ({ page }) => {
    test.skip(!process.env.E2E_USUARIO || !process.env.E2E_CLAVE, "Faltan E2E_USUARIO / E2E_CLAVE");
    test.setTimeout(240_000);
    await entrar(page, process.env.E2E_USUARIO!, process.env.E2E_CLAVE!);
    await expect(page.getByRole("heading", { level: 1, name: "Diagnóstico vehicular" })).toBeVisible();
    const resultado: Revision = {};
    resultado.listado = await ordenDeTab(page);
    await page.goto(`${APP}/diagnosticos/nuevo?modo=masivo`);
    resultado.nuevoMasivo = await ordenDeTab(page, 20);
    await page.goto(`${APP}/configuracion`);
    resultado.configuracion = await ordenDeTab(page, 20);

    await page.goto(`${APP}/`);
    const campana = page.getByRole("button", { name: /^Avisos/ }).first();
    await campana.waitFor({ state: "visible", timeout: 15_000 }).catch(() => undefined);
    if (await campana.count()) {
      await campana.focus();
      await page.keyboard.press("Enter");
      const abierta = (await page.getByRole("region", { name: "Avisos" }).count()) > 0;
      await page.keyboard.press("Escape");
      resultado.campana = {
        abreConEnter: abierta,
        cierraConEscape: (await page.getByRole("region", { name: "Avisos" }).count()) === 0,
        focoVuelveAlBoton: await campana.evaluate((el) => el === document.activeElement),
      };
    }

    await lanzarExpress(page, PLACA, `E2E revision teclado ${Date.now()}`);
    const grilla = page.getByRole("table", { name: /semáforo de consultas/i });
    await expect(grilla).toBeVisible();
    resultado.tablero = await ordenDeTab(page, 30);
    const dictamen = grilla.getByRole("button", { name: `Ver dictamen de ${PLACA}` });
    if (await dictamen.count()) {
      await dictamen.focus();
      await page.keyboard.press("Enter");
      const panel = page.getByRole("dialog", { name: `Dictamen · ${PLACA}` });
      await expect(panel).toBeVisible();
      const focoDentro = await panel.evaluate((el) => el.contains(document.activeElement));
      // Trampa de foco: tras 15 Tab el foco sigue dentro del panel.
      for (let i = 0; i < 15; i++) await page.keyboard.press("Tab");
      const sigueDentro = await panel.evaluate((el) => el.contains(document.activeElement));
      await page.keyboard.press("Escape");
      resultado.dictamen = {
        focoEntraAlPanel: focoDentro,
        trampaDeFoco: sigueDentro,
        cierraConEscape: (await page.getByRole("dialog").count()) === 0,
        focoVuelveAlDisparador: await dictamen.evaluate((el) => el === document.activeElement),
      };
    }
    guardar("teclado-escritorio", resultado);
    await page.screenshot({ path: path.join(SALIDA, "teclado-tablero-1280.png") });
  });
});
