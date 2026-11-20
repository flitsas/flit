import path from "node:path";
import { expect, test } from "@playwright/test";
import { SALIDA } from "./playwright.config";

/**
 * Humo responsive por ruta y perfil (contrato .trees/responsive-CONTRATO.md):
 *  1. Nada visible se sale del ancho de la pantalla (scroll lateral). Como `html, body` tienen `overflow-x: clip`, el
 *     `scrollWidth` ya no delata el desborde: se mide cada elemento contra el ancho visible, salvo que un ancestro con
 *     overflow propio (tabla con scroll, carrusel) lo contenga dentro de la pantalla.
 *  2. Por debajo de 640 px ningún campo tiene letra menor a 16 px (iOS hace zoom al enfocarlo).
 *  3. Ningún elemento fijo visible (dock, toasts, pie) queda fuera del área visible.
 *  4. Captura de página completa para la revisión visual a ojo.
 *
 * Lo que la emulación NO cubre: la barra de Safari y el notch. En Playwright `100vh` mide lo mismo que el área visible y
 * `env(safe-area-inset-*)` vale 0, así que esos bugs solo se ven con el `rg` del contrato y en el iPhone o iPad real.
 */
const RUTAS = (process.env.RESP_RUTAS ?? "/").split(",").map((r) => r.trim()).filter(Boolean);

for (const ruta of RUTAS) {
  test(`responsive ${ruta}`, async ({ page }, info) => {
    await page.goto(ruta);
    await page.waitForLoadState("networkidle").catch(() => undefined);

    const hallazgos = await page.evaluate(() => {
      const ancho = window.innerWidth;
      const alto = window.visualViewport?.height ?? window.innerHeight;
      const oculto = (el: Element) => {
        const cs = getComputedStyle(el);
        return (
          cs.display === "none" ||
          cs.visibility === "hidden" ||
          Number(cs.opacity) === 0 ||
          el.closest("[inert],[aria-hidden='true']") !== null
        );
      };
      const nombre = (el: Element) => {
        const id = el.id ? `#${el.id}` : "";
        const cls = typeof el.className === "string" ? el.className.trim().split(/\s+/).slice(0, 4).join(".") : "";
        return `${el.tagName.toLowerCase()}${id}${cls ? "." + cls : ""}`;
      };
      const contenido = (el: Element) => {
        for (let p = el.parentElement; p && p !== document.body && p !== document.documentElement; p = p.parentElement) {
          const ox = getComputedStyle(p).overflowX;
          if (ox !== "visible" && p.getBoundingClientRect().right <= ancho + 1) return true;
        }
        return false;
      };

      const desbordes: string[] = [];
      const camposChicos: string[] = [];
      const fijosFuera: string[] = [];
      for (const el of Array.from(document.body.querySelectorAll("*"))) {
        const r = el.getBoundingClientRect();
        if (r.width === 0 || r.height === 0 || oculto(el)) continue;
        if ((r.right > ancho + 1 || r.left < -1) && !contenido(el)) desbordes.push(`${nombre(el)} (${Math.round(r.left)}→${Math.round(r.right)} de ${ancho})`);
        if (getComputedStyle(el).position === "fixed" && (r.bottom > alto + 1 || r.right > ancho + 1)) {
          fijosFuera.push(`${nombre(el)} (abajo ${Math.round(r.bottom)} de ${Math.round(alto)})`);
        }
      }
      if (ancho < 640) {
        const campos = document.querySelectorAll<HTMLElement>(
          "input:not([type=checkbox]):not([type=radio]):not([type=range]):not([type=file]):not([type=hidden]):not([type=color]), select, textarea, [contenteditable='true']",
        );
        for (const c of Array.from(campos)) {
          if (oculto(c) || c.getBoundingClientRect().width === 0) continue;
          const px = parseFloat(getComputedStyle(c).fontSize);
          if (px < 16) camposChicos.push(`${nombre(c)} ${px}px`);
        }
      }
      // Solo el elemento más externo de cada desborde: sus hijos repiten el mismo problema.
      return { desbordes: desbordes.slice(0, 8), camposChicos: camposChicos.slice(0, 8), fijosFuera: fijosFuera.slice(0, 8) };
    });

    const archivo = `${info.project.name}${ruta.replace(/[^a-z0-9]+/gi, "_")}.png`;
    await page.screenshot({ path: path.join(SALIDA, "capturas", archivo), fullPage: true });

    expect.soft(hallazgos.desbordes, "elementos que se salen del ancho (scroll lateral)").toEqual([]);
    expect.soft(hallazgos.camposChicos, "campos < 16px en móvil (zoom de iOS al enfocar)").toEqual([]);
    expect.soft(hallazgos.fijosFuera, "elementos fijos fuera del área visible").toEqual([]);
  });
}
