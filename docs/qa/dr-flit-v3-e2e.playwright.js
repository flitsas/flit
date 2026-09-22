/* E2E DR-FLIT v3 — Playwright + Chromium local contra http://localhost:3000 (stack `pnpm dev`). */
const { chromium } = require("playwright");
const path = require("path");
const fs = require("fs");

const BASE = "http://localhost:3000";
const OUT = path.join(__dirname, "..", "e2e");
fs.mkdirSync(OUT, { recursive: true });
const exe = path.join(process.env.LOCALAPPDATA, "ms-playwright", "chromium-1228", "chrome-win64", "chrome.exe");

const USERS = {
  superadmin: { email: "demo@flit.local", pass: "DemoPass1!" },
  ot: { email: "otadmin@flit.local", pass: "OtAdminPass1!" },
  gestor: { email: "radicador@empresa.local", pass: "RadicadorPass1!" },
  admin: { email: "admin@empresa.local", pass: "AdminPass1!" },
};

const results = [];
let shot = 0;
async function snap(page, name) {
  shot += 1;
  const file = path.join(OUT, `${String(shot).padStart(2, "0")}-${name}.png`);
  await page.screenshot({ path: file, fullPage: false });
  return path.basename(file);
}
async function step(id, name, fn) {
  const t0 = Date.now();
  try {
    const detail = await fn();
    results.push({ id, name, status: "PASS", detail: detail ?? "", ms: Date.now() - t0 });
    console.log(`PASS ${id} ${name}${detail ? " — " + detail : ""}`);
  } catch (e) {
    results.push({ id, name, status: "FAIL", detail: String(e.message || e).slice(0, 400), ms: Date.now() - t0 });
    console.log(`FAIL ${id} ${name} — ${String(e.message || e).slice(0, 300)}`);
    if (currentPage) await recover(currentPage).catch(() => {});
  }
}
let currentPage = null;
/** Tras un fallo, deja el chat en el menú raíz para que el siguiente paso no herede el estado. */
async function recover(page) {
  const d = page.getByRole("dialog");
  if (!(await d.count())) return;
  const back = d.getByRole("button", { name: "Volver al menú" });
  if (await back.count()) { await back.first().click(); return; }
  const end = d.getByRole("button", { name: "Terminar chat" });
  if (await end.count()) await end.first().click();
}

async function login(page, u) {
  await page.goto(`${BASE}/login`, { waitUntil: "networkidle" });
  await page.fill("#login-email", u.email);
  await page.fill("#login-password", u.pass);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await page.waitForURL((url) => !url.pathname.startsWith("/login"), { timeout: 30000 });
  await page.waitForLoadState("networkidle");
}
const fab = (page) => page.getByRole("button", { name: "Abrir DR. FLIT" });
const dialog = (page) => page.getByRole("dialog");
async function openChat(page) {
  await fab(page).click();
  await dialog(page).waitFor({ state: "visible", timeout: 15000 });
}
async function chip(page, name) {
  await dialog(page).getByRole("button", { name, exact: false }).first().click();
}
async function send(page, text) {
  const input = page.getByPlaceholder("Pregúntale a DR. FLIT...");
  await input.fill(text);
  await input.press("Enter");
}
async function expectText(page, text, timeout = 20000) {
  await dialog(page).getByText(text, { exact: false }).first().waitFor({ state: "visible", timeout });
}
async function tramiteCards(page) {
  const list = dialog(page).getByLabel("Resultados de trámites");
  await list.waitFor({ state: "visible", timeout: 20000 });
  return list.locator("li");
}
async function waitPopup(context, action) {
  const [popup] = await Promise.all([context.waitForEvent("page", { timeout: 15000 }), action()]);
  await popup.waitForLoadState("domcontentloaded");
  const url = popup.url();
  const title = await popup.title().catch(() => "");
  await popup.close();
  return { url, title };
}

(async () => {
  const browser = await chromium.launch({ executablePath: exe });

  // ─────────────────────────── SUPER ADMIN (único con datos: 3 trámites) ───────────────────────────
  {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    currentPage = page;
    const apiCalls = [];
    page.on("request", (r) => { if (r.url().includes("/api/v1/")) apiCalls.push(`${r.method()} ${new URL(r.url()).pathname}`); });

    await step("SA-01", "Login SuperAdmin y FAB DR. FLIT visible", async () => {
      await login(page, USERS.superadmin);
      await fab(page).waitFor({ state: "visible", timeout: 15000 });
      await snap(page, "sa-dashboard");
      return page.url();
    });

    await step("SA-02", "Abrir chat: saludo con nombre, menú Gestión (4 intents con copy canónico) y Ayuda (2)", async () => {
      await openChat(page);
      await expectText(page, "soy DR. FLIT");
      const gestion = dialog(page).getByLabel("Gestión");
      const labels = await gestion.getByRole("button").allTextContents();
      const expected = ["Buscar por placa", "Buscar por VIN", "Buscar por trámite", "Buscar por cliente"];
      for (const e of expected) if (!labels.some((l) => l.includes(e))) throw new Error(`falta chip «${e}»: ${labels.join(" | ")}`);
      const ayuda = dialog(page).getByLabel("Ayuda");
      const a = await ayuda.getByRole("button").allTextContents();
      if (!a.some((l) => l.includes("Necesito ayuda")) || !a.some((l) => l.includes("Soporte"))) throw new Error(`ayuda: ${a.join(" | ")}`);
      await snap(page, "sa-menu");
      return labels.join(" | ");
    });

    await step("SA-03", "Edge: texto libre en menú raíz → pista «Elige una opción»", async () => {
      await send(page, "hola");
      await expectText(page, "Elige una opción de Gestión o Ayuda");
    });

    await step("SA-04", "Edge: entrada vacía / solo espacios no envía nada", async () => {
      const before = await dialog(page).locator("[class*='bubble'], p").count();
      await send(page, "   ");
      await page.waitForTimeout(500);
      const after = await dialog(page).locator("[class*='bubble'], p").count();
      if (after !== before) throw new Error(`se añadió un mensaje con entrada vacía (${before}→${after})`);
    });

    await step("SA-05", "HU-B: «Buscar por trámite» pide RADICADO (no GUID); «3» → FT1-0000003", async () => {
      await chip(page, "Buscar por trámite");
      await expectText(page, "número de radicado");
      const promptText = await dialog(page).getByText("número de radicado").first().textContent();
      if (/GUID/.test(promptText)) throw new Error("el prompt aún menciona GUID");
      apiCalls.length = 0;
      await send(page, "3");
      const cards = await tramiteCards(page);
      const n = await cards.count();
      if (n !== 1) throw new Error(`esperaba 1 tarjeta, hay ${n}`);
      const txt = await cards.first().textContent();
      if (!/Radicado\s*FT1-0000003/.test(txt)) throw new Error(`sin radicado: ${txt}`);
      if (!/Fecha radicación/.test(txt)) throw new Error("sin etiqueta canónica «Fecha radicación»");
      if (!/\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}/.test(txt)) throw new Error(`fecha no estándar: ${txt}`);
      if (!/Renting Andino/.test(txt)) throw new Error("SuperAdmin debe ver la compañía en la tarjeta");
      if (!/Entregado/.test(txt)) throw new Error("sin chip de estado");
      if (!/En revisión del organismo/.test(txt)) throw new Error("sin pista de acción por estado (HU-E)");
      const post = apiCalls.find((c) => c.includes("POST /api/v1/tramites/instances/search"));
      if (!post) throw new Error(`no usó POST /instances/search: ${apiCalls.join(", ")}`);
      await snap(page, "sa-tramite-por-consecutivo");
      return `1 tarjeta · ${post}`;
    });

    await step("SA-06", "Tarjeta: «Ver trámite» abre el detalle en pestaña nueva", async () => {
      const r = await waitPopup(context, () => dialog(page).getByRole("button", { name: "Ver trámite" }).first().click());
      if (!/\/tramites\/bbbbbbbb-0001-4000-8000-000000000012/.test(r.url)) throw new Error(`popup: ${r.url}`);
      return r.url;
    });

    await step("SA-07", "«Volver al menú» limpia resultados y vuelve al menú", async () => {
      await chip(page, "Volver al menú");
      await expectText(page, "Elige otra opción de Gestión o Ayuda");
      if (await dialog(page).getByLabel("Resultados de trámites").count()) throw new Error("los resultados siguen visibles");
    });

    await step("SA-08", "HU-B: radicado con prefijo y espacios «ft1 0000003» → mismo trámite", async () => {
      await chip(page, "Buscar por trámite");
      await send(page, "ft1 0000003");
      const cards = await tramiteCards(page);
      if ((await cards.count()) !== 1) throw new Error("esperaba 1");
      if (!/FT1-0000003/.test(await cards.first().textContent())) throw new Error("no es FT1-0000003");
      await chip(page, "Volver al menú");
    });

    await step("SA-09", "HU-B edge: GUID como Super Admin sin compañía activa → mensaje accionable (no error técnico)", async () => {
      await chip(page, "Buscar por trámite");
      await send(page, "bbbbbbbb-0001-4000-8000-000000000011");
      await expectText(page, "elige primero la compañía");
      if (await dialog(page).getByText("X-Tenant-Id").count()) throw new Error("se filtró el error técnico");
      await snap(page, "sa-guid-sin-tenant");
      await chip(page, "Volver al menú");
    });

    await step("SA-10", "Edge: radicado inexistente «999999» → mensaje de vacío, sin error", async () => {
      await chip(page, "Buscar por trámite");
      await send(page, "999999");
      await expectText(page, "No encontré trámites asociados a");
      await expectText(page, "radicado");
      await snap(page, "sa-radicado-inexistente");
      await chip(page, "Volver al menú");
    });

    await step("SA-11", "Edge: texto con HTML «<b>x</b>» se muestra literal (sin inyección)", async () => {
      await chip(page, "Buscar por trámite");
      await send(page, "<b>x</b>");
      await expectText(page, "No encontré trámites");
      const bold = await dialog(page).locator("b", { hasText: "x" }).count();
      if (bold) throw new Error("se renderizó HTML del usuario");
      await chip(page, "Volver al menú");
    });

    await step("SA-12", "HU-D: cliente «Renting» → rama → «Ver trámites» → 2 resultados con compañía", async () => {
      await chip(page, "Buscar por cliente");
      await expectText(page, "documento o nombre");
      await send(page, "Renting");
      await expectText(page, "¿Qué deseas consultar para");
      await dialog(page).getByLabel("Opciones por cliente").getByRole("button", { name: "Ver trámites" }).click();
      const cards = await tramiteCards(page);
      const n = await cards.count();
      if (n !== 2) throw new Error(`esperaba 2, hay ${n}`);
      await expectText(page, "Encontré");
      await expectText(page, "2 trámites");
      await snap(page, "sa-cliente-tramites");
      await chip(page, "Volver al menú");
      return `${n} tarjetas`;
    });

    await step("SA-13", "HU-D: cliente → «Ver validación de identidad» → resultado o vacío + enlace a Validaciones", async () => {
      await chip(page, "Buscar por cliente");
      await send(page, "Renting");
      await dialog(page).getByLabel("Opciones por cliente").getByRole("button", { name: "Ver validación de identidad" }).click();
      await expectText(page, "validaci", 20000);
      await dialog(page).getByRole("button", { name: "Ir a Validaciones" }).waitFor({ state: "visible", timeout: 15000 });
      await snap(page, "sa-cliente-validaciones");
      await chip(page, "Volver al menú");
    });

    await step("SA-14", "HU-D edge: documento con puntos «1.234.567» viaja compactado y no rompe", async () => {
      apiCalls.length = 0;
      const bodies = [];
      const handler = (r) => { if (r.method() === "POST" && r.url().includes("/instances/search")) bodies.push(r.postData() || ""); };
      page.on("request", handler);
      await chip(page, "Buscar por cliente");
      await send(page, "1.234.567");
      await dialog(page).getByLabel("Opciones por cliente").getByRole("button", { name: "Ver trámites" }).click();
      await expectText(page, "No encontré trámites");
      page.off("request", handler);
      const body = bodies.find((b) => b.includes("busqueda"));
      if (!body || !body.includes('"busqueda":"1234567"')) throw new Error(`cuerpo: ${bodies.join(" || ")}`);
      await chip(page, "Volver al menú");
      return body;
    });

    await step("SA-15", "Placa y VIN: sin datos en seed → vacío correcto; sin chip de historial con 0 resultados", async () => {
      await chip(page, "Buscar por placa");
      await expectText(page, "valor de placa");
      await send(page, "abc123");
      await expectText(page, "No encontré trámites asociados a");
      if (await dialog(page).getByRole("button", { name: "Ver historial completo de la placa" }).count()) throw new Error("chip de historial con 0 resultados");
      await chip(page, "Volver al menú");
      await chip(page, "Buscar por VIN");
      await send(page, "9BWZZZ377VT004251");
      await expectText(page, "No encontré trámites asociados a");
      await chip(page, "Volver al menú");
    });

    await step("SA-16", "Persistencia: la conversación sobrevive a cambiar de módulo (panel se cierra, historial sigue)", async () => {
      await chip(page, "Buscar por trámite");
      await send(page, "3");
      await tramiteCards(page);
      await page.goto(`${BASE}/?m=ayuda`, { waitUntil: "networkidle" });
      if (await dialog(page).count()) throw new Error("el panel debería arrancar cerrado tras navegar");
      await openChat(page);
      await tramiteCards(page);
      await snap(page, "sa-persistencia");
    });

    await step("SA-17", "«Terminar chat» borra la conversación y vuelve al saludo", async () => {
      await dialog(page).getByRole("button", { name: "Terminar chat" }).click();
      await expectText(page, "soy DR. FLIT");
      if (await dialog(page).getByLabel("Resultados de trámites").count()) throw new Error("quedaron resultados");
    });

    await step("SA-18", "HU-G: en /tramites, «Necesito ayuda» sugiere «Seguimiento…» y deja la pregunta abierta", async () => {
      await page.goto(`${BASE}/tramites`, { waitUntil: "networkidle" });
      await openChat(page);
      await chip(page, "Necesito ayuda");
      await expectText(page, "Estás en un módulo con documentación");
      await dialog(page).getByLabel("Artículos del manual").getByRole("button", { name: "Seguimiento, búsqueda y estados" }).waitFor({ timeout: 10000 });
      const input = page.getByPlaceholder("Pregúntale a DR. FLIT...");
      if (await input.isDisabled()) throw new Error("composer deshabilitado");
      await snap(page, "sa-ayuda-contextual");
    });

    await step("SA-19", "HU-F: pregunta libre «cómo solicito una revocatoria» → chip del manual → abre /manual/… (v2)", async () => {
      await send(page, "cómo solicito una revocatoria");
      await expectText(page, "en la documentación");
      const list = dialog(page).getByLabel("Artículos del manual");
      const btn = list.getByRole("button", { name: "Solicitar la revocatoria" });
      await btn.waitFor({ timeout: 10000 });
      const r = await waitPopup(context, () => btn.click());
      if (!/\/manual\/1-gestor\/10-revocatorias/.test(r.url)) throw new Error(`popup: ${r.url}`);
      await snap(page, "sa-ayuda-revocatoria");
      return r.url;
    });

    await step("SA-20", "HU-F: SuperAdmin ve documentación de todos los perfiles («liberar placa» → artículo OT)", async () => {
      await chip(page, "Volver al menú");
      await chip(page, "Necesito ayuda");
      await send(page, "liberar placa");
      await dialog(page).getByLabel("Artículos del manual").getByRole("button", { name: "Bandeja de trámites (OT)" }).waitFor({ timeout: 10000 });
    });

    await step("SA-21", "Edge: pregunta sin coincidencia → «No encontré un artículo» + botón al Centro de Ayuda", async () => {
      await chip(page, "Volver al menú");
      await chip(page, "Necesito ayuda");
      await send(page, "zzzz qqqq xxxx");
      await expectText(page, "No encontré un artículo del manual");
      await dialog(page).getByRole("button", { name: /Centro de Ayuda/i }).waitFor({ timeout: 10000 });
      await snap(page, "sa-ayuda-sin-match");
    });

    await step("SA-22", "HU-F: Soporte muestra correo, NO muestra teléfono placeholder, y radica caso en pestaña nueva", async () => {
      await chip(page, "Volver al menú");
      await chip(page, "Soporte");
      await expectText(page, "soporte@flitsas.com");
      if (await dialog(page).getByText("Línea de atención").count()) throw new Error("se muestra «Línea de atención» sin teléfono configurado");
      if (await dialog(page).getByText("300 000 0000").count()) throw new Error("teléfono placeholder visible");
      const r = await waitPopup(context, () => dialog(page).getByRole("button", { name: "Generar un caso de soporte" }).click());
      if (!/flitsas\.com\.co\/SOPORTE/.test(r.url)) throw new Error(`popup: ${r.url}`);
      await snap(page, "sa-soporte");
      return r.url;
    });

    await step("SA-24", "Normativa: chip → tarjeta de la resolución (fuente principal) → PDF en pestaña nueva (application/pdf)", async () => {
      await chip(page, "Volver al menú");
      await chip(page, "Normativa");
      await expectText(page, "Resolución 20233040017145 de 2023");
      const list = dialog(page).getByLabel("Artículos del manual");
      await list.getByRole("button", { name: /Resolución 20233040017145 de 2023/ }).waitFor({ timeout: 10000 });
      const r = await waitPopup(context, () => list.getByRole("button", { name: "Abrir la norma (PDF)" }).click());
      if (!/\/legal\/resolucion-20233040017145-2023-mintransporte\.pdf$/.test(r.url)) throw new Error(`popup: ${r.url}`);
      // El visor PDF de Chromium no expone cabeceras por el evento `response`: se verifica por GET directo.
      const resp = await page.request.get(r.url);
      const ct = resp.headers()["content-type"] || "";
      if (resp.status() !== 200 || !/application\/pdf/.test(ct)) throw new Error(`PDF: ${resp.status()} ${ct}`);
      await page.bringToFront();
      await snap(page, "sa-normativa");
      return `${r.url} · ${ct}`;
    });

    await step("SA-25", "Normativa: pregunta «qué dice la norma sobre la preasignación» → la resolución PRIMERO con su PDF", async () => {
      await chip(page, "Volver al menú");
      await chip(page, "Necesito ayuda");
      await send(page, "qué dice la norma sobre la preasignación de placa");
      const list = dialog(page).getByLabel("Artículos del manual");
      const first = list.locator("li").first();
      await first.waitFor({ timeout: 10000 });
      const txt = await first.textContent();
      if (!/20233040017145/.test(txt)) throw new Error(`primer resultado: ${txt}`);
      if (!/Abrir la norma \(PDF\)/.test(txt)) throw new Error("sin enlace al PDF");
      await snap(page, "sa-normativa-pregunta");
    });

    await step("SA-26", "Normativa no secuestra lo operativo: «cómo creo un trámite» sigue devolviendo el how-to primero", async () => {
      await chip(page, "Volver al menú");
      await chip(page, "Necesito ayuda");
      await send(page, "cómo creo un trámite");
      const first = dialog(page).getByLabel("Artículos del manual").locator("li").first();
      await first.waitFor({ timeout: 10000 });
      const txt = await first.textContent();
      if (!/Cómo crear un trámite/.test(txt)) throw new Error(`primer resultado: ${txt}`);
    });

    await step("SA-23", "HU-G: en /admin/rbac sugiere «RBAC, usuarios y auditoría»", async () => {
      await chip(page, "Volver al menú");
      await page.goto(`${BASE}/admin/rbac`, { waitUntil: "networkidle" }).catch(() => {});
      await page.waitForTimeout(1000);
      await openChat(page);
      await chip(page, "Necesito ayuda");
      await dialog(page).getByLabel("Artículos del manual").getByRole("button", { name: "RBAC, usuarios y auditoría" }).waitFor({ timeout: 10000 });
      await snap(page, "sa-ayuda-rbac");
    });

    await context.close();
  }

  // ─────────────────────────── ADMIN OT ───────────────────────────
  {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    currentPage = page;
    const apiCalls = [];
    page.on("request", (r) => { if (r.url().includes("/api/v1/")) apiCalls.push(`${r.method()} ${new URL(r.url()).pathname}`); });

    await step("OT-01", "Login OT y búsqueda por radicado «3» va a la bandeja (POST client-procedures/search) con compañía cliente", async () => {
      await login(page, USERS.ot);
      await openChat(page);
      await chip(page, "Buscar por trámite");
      apiCalls.length = 0;
      await send(page, "3");
      const cards = await tramiteCards(page);
      const txt = await cards.first().textContent();
      if (!/FT1-0000003/.test(txt)) throw new Error(txt);
      if (!/Renting Andino/.test(txt)) throw new Error("OT debe ver la compañía radicadora");
      const post = apiCalls.find((c) => c.includes("client-procedures/search"));
      if (!post) throw new Error(`no fue por la bandeja: ${apiCalls.join(", ")}`);
      await snap(page, "ot-radicado");
      return post;
    });

    await step("OT-02", "OT: cliente «Renting» → 2 (busqueda aplicada por POST, no la bandeja entera)", async () => {
      await chip(page, "Volver al menú");
      await chip(page, "Buscar por cliente");
      await send(page, "Renting");
      await dialog(page).getByLabel("Opciones por cliente").getByRole("button", { name: "Ver trámites" }).click();
      const cards = await tramiteCards(page);
      const n = await cards.count();
      if (n !== 2) throw new Error(`esperaba 2 (filtrado), hay ${n}`);
      await chip(page, "Volver al menú");
      return `${n}`;
    });

    await step("OT-03", "HU-F: OT pregunta «asignar placa» → Bandeja OT; «cómo creo un trámite» NO devuelve artículo del Gestor", async () => {
      await chip(page, "Necesito ayuda");
      await send(page, "asignar placa");
      await dialog(page).getByLabel("Artículos del manual").getByRole("button", { name: "Bandeja de trámites (OT)" }).waitFor({ timeout: 10000 });
      await chip(page, "Volver al menú");
      await chip(page, "Necesito ayuda");
      await send(page, "cómo creo un trámite");
      await page.waitForTimeout(800);
      const gestor = await dialog(page).getByLabel("Artículos del manual").getByRole("button", { name: "Cómo crear un trámite" }).count();
      if (gestor) throw new Error("el OT recibió un artículo del Gestor");
      await snap(page, "ot-ayuda-filtrada");
    });

    await step("OT-05", "Normativa aplica a Todos: el OT la recibe con «marco legal traspaso»", async () => {
      await chip(page, "Volver al menú");
      await chip(page, "Necesito ayuda");
      await send(page, "marco legal requisitos del traspaso");
      const first = dialog(page).getByLabel("Artículos del manual").locator("li").first();
      await first.waitFor({ timeout: 10000 });
      if (!/20233040017145/.test(await first.textContent())) throw new Error("el OT no recibió la norma primero");
      await snap(page, "ot-normativa");
    });

    await step("OT-04", "HU-G: en el hub OT (bandeja) sugiere «Bandeja de trámites (OT)»", async () => {
      await chip(page, "Volver al menú");
      await page.getByRole("dialog").getByRole("button", { name: "Cerrar DR. FLIT" }).click().catch(() => {});
      // Dock → Trámites lleva al hub del organismo.
      await page.getByRole("button", { name: /^Trámites$/ }).first().click().catch(async () => {
        await page.getByText("Trámites", { exact: true }).first().click();
      });
      await page.waitForURL(/transit-offices\/.+\/client-procedures/, { timeout: 20000 });
      await page.waitForLoadState("networkidle");
      await openChat(page);
      await chip(page, "Necesito ayuda");
      await dialog(page).getByLabel("Artículos del manual").getByRole("button", { name: "Bandeja de trámites (OT)" }).waitFor({ timeout: 10000 });
      await snap(page, "ot-contextual-bandeja");
      return page.url();
    });

    await context.close();
  }

  // ─────────────────────────── GESTOR (Radicador, tenant sin trámites) ───────────────────────────
  {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    currentPage = page;

    await step("GE-01", "Login Radicador: búsquedas devuelven vacío de su tenant (no ve otras compañías)", async () => {
      await login(page, USERS.gestor);
      await openChat(page);
      await chip(page, "Buscar por trámite");
      await send(page, "3");
      await expectText(page, "No encontré trámites");
      if (await dialog(page).getByLabel("Resultados de trámites").locator("li").count()) throw new Error("un Radicador vio trámites ajenos");
      await snap(page, "ge-vacio");
      await chip(page, "Volver al menú");
    });

    await step("GE-02", "HU-F: Gestor pregunta «preasignación de placas» → solo artículos Gestor (ruta de placa), no OT", async () => {
      await chip(page, "Necesito ayuda");
      await send(page, "preasignación de placas");
      const list = dialog(page).getByLabel("Artículos del manual");
      await list.getByRole("button", { name: "Matrícula inicial: ruta de placa" }).waitFor({ timeout: 10000 });
      if (await list.getByRole("button", { name: "Preasignación de placas" }).count()) throw new Error("apareció el artículo del OT");
      const badges = await list.allTextContents();
      if (badges.join(" ").includes("Organismo de Tránsito")) throw new Error("audiencia OT filtrada");
      await snap(page, "ge-ayuda-filtrada");
    });

    await step("GE-03", "HU-G: Radicador en /tramites recibe «Seguimiento…» como sugerencia", async () => {
      await chip(page, "Volver al menú");
      await page.getByRole("dialog").getByRole("button", { name: "Cerrar DR. FLIT" }).click().catch(() => {});
      await page.goto(`${BASE}/tramites`, { waitUntil: "networkidle" });
      await openChat(page);
      await chip(page, "Necesito ayuda");
      await dialog(page).getByLabel("Artículos del manual").getByRole("button", { name: "Seguimiento, búsqueda y estados" }).waitFor({ timeout: 10000 });
    });

    await context.close();
  }

  // ─────────────────────────── ADMIN COMPAÑÍA ───────────────────────────
  {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    currentPage = page;

    await step("AC-01", "Login AdminCompany: en su ficha (/admin/companies/{id}) sugiere «Consola de administración»", async () => {
      await login(page, USERS.admin);
      await page.goto(`${BASE}/admin/companies`, { waitUntil: "networkidle" });
      await page.waitForURL(/\/admin\/companies\/[0-9a-f-]+/, { timeout: 20000 }).catch(() => {});
      await page.waitForLoadState("networkidle");
      await openChat(page);
      await chip(page, "Necesito ayuda");
      await dialog(page).getByLabel("Artículos del manual").getByRole("button", { name: "Consola de administración" }).waitFor({ timeout: 10000 });
      await snap(page, "ac-contextual-consola");
      return page.url();
    });

    await step("AC-02", "HU-F: AdminCompany pregunta «red de clientes» → artículo Admin; y también ve los del Gestor", async () => {
      await send(page, "red de clientes");
      const list = dialog(page).getByLabel("Artículos del manual");
      await list.getByRole("button", { name: "Red de clientes" }).waitFor({ timeout: 10000 });
      await chip(page, "Volver al menú");
      await chip(page, "Necesito ayuda");
      await send(page, "cómo creo un trámite");
      await list.getByRole("button", { name: "Cómo crear un trámite" }).waitFor({ timeout: 10000 });
      await snap(page, "ac-ayuda");
    });

    await context.close();
  }

  // ─────────────────────────── PORTAL PÚBLICO /manual ───────────────────────────
  {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    currentPage = null;

    await step("MA-01", "/manual sin login: versión 2.0.0 · Septiembre 2026 y 5 secciones en el sidebar", async () => {
      await page.goto(`${BASE}/manual`, { waitUntil: "networkidle" });
      const body = await page.locator("body").innerText();
      if (!/Versión 2\.1\.0/.test(body)) throw new Error("no muestra Versión 2.1.0");
      if (!/Septiembre 2026/.test(body)) throw new Error("no muestra Septiembre 2026");
      const lower = body.toLowerCase();
      for (const s of ["Introducción", "Normativa", "Gestor", "Organismo de Tránsito", "Administración de compañía", "Super Admin"]) {
        if (!lower.includes(s.toLowerCase())) throw new Error(`falta sección ${s}`);
      }
      if (/No incluye consolas exclusivas|quedan fuera/i.test(body)) throw new Error("la bienvenida sigue diciendo que admin/SuperAdmin quedan fuera");
      if (!lower.includes("administración de compañía y super admin")) throw new Error("la bienvenida no menciona las secciones nuevas");
      await snap(page, "manual-home");
    });

    await step("MA-02", "Artículos nuevos renderizan con «Aplica para»: OT, Admin de Compañía, Super Admin", async () => {
      const cases = [
        ["2-ot/12-configuracion", "Organismo de Tránsito"],
        ["3-admin-company/3-red-de-clientes", "Admin de Compañía"],
        ["4-superadmin/3-plataforma", "Super Admin"],
        ["1-gestor/7-ruta-placa", "Gestor"],
      ];
      for (const [slug, aud] of cases) {
        await page.goto(`${BASE}/manual/${slug}`, { waitUntil: "networkidle" });
        const body = await page.locator("body").innerText();
        if (!body.includes(`Aplica para: ${aud}`)) throw new Error(`${slug}: sin «Aplica para: ${aud}»`);
      }
      await snap(page, "manual-articulo-ruta-placa");
    });

    await step("MA-04", "Portal: sección Normativa en el sidebar, artículo con bloque «Fuentes» y PDF descargable", async () => {
      await page.goto(`${BASE}/manual/5-normativa/1-resolucion-20233040017145-2023`, { waitUntil: "networkidle" });
      const body = await page.locator("body").innerText();
      if (!body.toLowerCase().includes("normativa")) throw new Error("sin sección Normativa");
      if (!/Aplica para: Todos/.test(body)) throw new Error("audiencia incorrecta");
      if (!/fuentes/i.test(body)) throw new Error("sin bloque Fuentes");
      if (!/Diario Oficial 52386/.test(body)) throw new Error("sin referencia al Diario Oficial");
      const href = await page.getByRole("link", { name: /texto completo \(PDF/ }).getAttribute("href");
      const resp = await page.request.get(`${BASE}${href}`);
      const ct = resp.headers()["content-type"] || "";
      if (resp.status() !== 200 || !/application\/pdf/.test(ct)) throw new Error(`PDF: ${resp.status()} ${ct}`);
      const versionOk = /Versión 2\.1\.0/.test(body);
      if (!versionOk) throw new Error("no muestra Versión 2.1.0");
      await snap(page, "manual-normativa");
      return `${href} · ${ct} · ${(await resp.body()).length}B`;
    });

    await step("MA-03", "Búsqueda del portal (sin filtro de rol) encuentra «liberar placa» y «validar impronta»", async () => {
      await page.goto(`${BASE}/manual`, { waitUntil: "networkidle" });
      const input = page.getByRole("searchbox").or(page.locator("input[type='search'], input[placeholder*='Buscar']")).first();
      await input.fill("validar impronta");
      await page.waitForTimeout(600);
      const body = await page.locator("body").innerText();
      if (!body.includes("Validar impronta")) throw new Error("la búsqueda del portal no encontró «Validar impronta»");
      await snap(page, "manual-busqueda");
    });

    await context.close();
  }

  await browser.close();
  fs.writeFileSync(path.join(OUT, "results.json"), JSON.stringify(results, null, 2));
  const pass = results.filter((r) => r.status === "PASS").length;
  console.log(`\nRESUMEN: ${pass}/${results.length} PASS · capturas en ${OUT}`);
  process.exit(pass === results.length ? 0 : 1);
})().catch((e) => { console.error("FATAL", e); process.exit(2); });
