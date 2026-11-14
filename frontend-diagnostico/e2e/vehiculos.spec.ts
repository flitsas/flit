import { expect, test, type Page, type Route } from "@playwright/test";
import {
  APP,
  CLIENTE,
  ancho,
  descargar,
  entrar,
  esperarTerminados,
  exigirCliente,
  fallarUnaVez,
  placaFicticia,
  prefijoFicticio,
  recorrerConTab,
  retener,
  revisarTemas,
  sembrarMasivo,
  sinDesbordeHorizontal,
  vinFicticio,
} from "./saneamiento-comun";

/**
 * HU #14577 — PS-3 pestaña «Vehículos» del inicio (AC20–AC23) con AC40 (cuatro estados), AC41 y AC42 (axe en claro y
 * oscuro, Tab a 360 px, sin desplazamiento horizontal) y las capturas C-7 y C-8 de .claude/state/saneamiento/auditoria-ui.md.
 *
 * Necesita la suite local con simulación (scripts/dev/levantar-diagnostico-local.sh simulado). Siembra un Masivo con tres
 * placas ficticias y un VIN, todos con el prefijo de la corrida (el filtro «Placa» es por prefijo del identificador).
 * Variables: E2E_USUARIO / E2E_CLAVE — usuario de la compañía con diagnosticos.crear, diagnosticos.ver, reportes.descargar y
 * saneamiento.solicitar.
 *
 * Lista congelada: nombres accesibles de los que depende este spec. Si uno cambia por decisión de UX, el spec se ajusta en
 * el mismo PR de la pantalla.
 *   - tablist «Vista del inicio» · tab «Diagnósticos» · tab «Vehículos» · tabpanel «Vehículos» · heading (h2) «Vehículos»
 *   - group «Filtros de vehículos» · textbox «Placa» · combobox «Estado del vehículo» · combobox «Saneamiento»
 *   - etiqueta «Desde» · button «Limpiar filtros» · button «Actualizar»
 *   - table «Vehículos de la compañía» · rowheader «<placa o VIN>»
 *   - checkbox «Seleccionar la placa <placa>» · button «Seleccionar todos los elegibles» · button «Quitar selección»
 *   - button «/^Sanear \(\d+\)$/» · button «/^Descargar deuda \(\d+\)$/»
 *   - button «Solicitar saneamiento de la placa <placa>» · link «Abrir el diagnóstico <nombre>» · button «Ver deuda de la placa <placa>»
 *   - navigation «Paginación de vehículos» · combobox «Filas por página»
 *   - texto «Ningún vehículo coincide» · «Aún no hay vehículos» · «No pudimos cargar los vehículos» · button «Reintentar»
 *   - texto «Cargando vehículos…» · «Faltan consultas por terminar»
 */

// Sesión única por usuario: dos tests a la vez con el mismo usuario se invalidan la sesión (bucle de reautenticación en
// «Abriendo Diagnóstico…»). Los tests de este archivo van en serie en un mismo worker (ver cabecera de playwright.config.ts).
test.describe.configure({ mode: "serial" });

const tabla = (page: Page) => page.getByRole("table", { name: "Vehículos de la compañía" });
const casilla = (page: Page, id: string) => page.getByRole("checkbox", { name: `Seleccionar la placa ${id}` });
const filtros = (page: Page) => page.getByRole("group", { name: "Filtros de vehículos" });
const LISTA = /\/api\/v1\/diagnostico\/vehiculos\?/;
const hoy = () => new Intl.DateTimeFormat("en-CA", { timeZone: "America/Bogota" }).format(new Date());

test.describe("PS-3 pestaña Vehículos", () => {
  test("pestañas y ?vista=, filtros en la URL, paginación del kit, selección con «Descargar deuda (N)» y fila sin placa", async ({ page }) => {
    exigirCliente();
    test.setTimeout(480_000);
    const prefijo = prefijoFicticio();
    const [V1, V2, V7, VIN] = [placaFicticia(prefijo, "1"), placaFicticia(prefijo, "2"), placaFicticia(prefijo, "7"), vinFicticio(prefijo)];
    const nombre = `E2E vehiculos ${Date.now()}`;
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!);
    const { diagnosticoId: lote } = await sembrarMasivo(page, [V1, V2, V7, VIN], { nombre });
    await esperarTerminados(page, lote, [V1, V2, VIN]);

    await test.step("AC20: «Diagnósticos» por defecto; «Vehículos» pasa a ?vista=vehiculos y sobrevive a la recarga", async () => {
      await page.goto(`${APP}/`);
      const pestanas = page.getByRole("tablist", { name: "Vista del inicio" });
      const diagnosticos = pestanas.getByRole("tab", { name: "Diagnósticos" });
      const vehiculos = pestanas.getByRole("tab", { name: "Vehículos" });
      await expect(diagnosticos).toHaveAttribute("aria-selected", "true");
      // La pestaña y el título de su panel se llaman igual: se distinguen por rol.
      await expect(page.getByRole("heading", { level: 2, name: "Diagnósticos" })).toBeVisible();
      await vehiculos.click();
      await expect(page).toHaveURL(/[?&]vista=vehiculos(&|$)/);
      await expect(vehiculos).toHaveAttribute("aria-selected", "true");
      await expect(page.getByRole("tabpanel", { name: "Vehículos" }).getByRole("heading", { level: 2, name: "Vehículos" })).toBeVisible();
      await page.reload();
      await expect(vehiculos).toHaveAttribute("aria-selected", "true");
      await expect(tabla(page)).toBeVisible();
      await diagnosticos.click();
      await expect(page).not.toHaveURL(/vista=/);
      await expect(page.getByRole("table", { name: "Diagnósticos de la compañía" })).toBeVisible();
    });

    await test.step("AC21: filtros en la URL; Enter aplica la placa; recarga los conserva; «Limpiar filtros»", async () => {
      await page.goto(`${APP}/?vista=vehiculos`);
      const placa = filtros(page).getByRole("textbox", { name: "Placa", exact: true });
      // Se escribe en minúsculas: el campo la pinta en mayúsculas.
      await placa.fill(prefijo.toLowerCase());
      await placa.press("Enter");
      await expect(page).toHaveURL(new RegExp(`[?&]placa=${prefijo}(&|$)`));
      await expect(placa).toHaveValue(prefijo);
      await expect(tabla(page).getByRole("rowheader")).toHaveCount(4);
      await filtros(page).getByRole("combobox", { name: "Estado del vehículo" }).selectOption("TERMINADO");
      await expect(page).toHaveURL(/[?&]estado=TERMINADO(&|$)/);
      await expect(tabla(page).getByRole("rowheader")).toHaveCount(3);
      await filtros(page).getByRole("combobox", { name: "Saneamiento" }).selectOption("SIN");
      await expect(page).toHaveURL(/[?&]saneamiento=SIN(&|$)/);
      await filtros(page).getByLabel("Desde").fill(hoy());
      await expect(page).toHaveURL(new RegExp(`[?&]desde=${hoy()}(&|$)`));
      await page.reload();
      await expect(filtros(page).getByRole("combobox", { name: "Estado del vehículo" })).toHaveValue("TERMINADO");
      await expect(filtros(page).getByRole("combobox", { name: "Saneamiento" })).toHaveValue("SIN");
      await expect(filtros(page).getByRole("textbox", { name: "Placa", exact: true })).toHaveValue(prefijo);
      const pedido = page.waitForRequest((r) => r.method() === "GET" && LISTA.test(r.url()));
      await filtros(page).getByRole("button", { name: "Actualizar" }).click();
      await pedido;
      await filtros(page).getByRole("button", { name: "Limpiar filtros" }).click();
      await expect(page).toHaveURL(`${APP}/?vista=vehiculos`);
    });

    await test.step("AC21: «Filas por página» del kit (10/20/30/50) en la URL", async () => {
      const filas = page.getByRole("combobox", { name: "Filas por página" });
      await expect(filas.locator("option")).toHaveText(["10", "20", "30", "50"]);
      await expect(filas).toHaveValue("10");
      await filas.selectOption("20");
      await expect(page).toHaveURL(/[?&]tamano=20(&|$)/);
      await filas.selectOption("10");
      await expect(page).not.toHaveURL(/tamano=/);
      await expect(page.getByRole("navigation", { name: "Paginación de vehículos" })).toBeVisible();
      await expect(page.getByText(/^\d[\d.]* vehículos?$/)).toBeVisible();
    });

    await test.step("UA-5: la fila sin placa se nombra por su VIN, nunca con un nombre vacío", async () => {
      await page.goto(`${APP}/?vista=vehiculos&placa=${prefijo}`);
      await expect(tabla(page).getByRole("rowheader")).toHaveCount(4);
      for (const c of await tabla(page).getByRole("checkbox").all()) {
        await expect(c).toHaveAccessibleName(/^Seleccionar (la placa \S+|el vehículo sin placa)$/);
      }
      const filaVin = tabla(page).getByRole("rowheader", { name: new RegExp(VIN) });
      if (await filaVin.count()) {
        await expect(casilla(page, VIN)).toBeVisible();
        await expect(page.getByRole("button", { name: `Ver deuda de la placa ${VIN}` })).toBeVisible();
      } else {
        test.info().annotations.push({ type: "observación", description: "El VIN llegó con placa del RUNT simulado: la fila se nombra por esa placa" });
      }
    });

    await test.step("AC23: acciones por fila", async () => {
      await expect(page.getByRole("link", { name: `Abrir el diagnóstico ${nombre}` }).first()).toHaveAttribute("href", `/diagnosticos/${lote}`);
      await expect(page.getByRole("button", { name: `Solicitar saneamiento de la placa ${V2}` })).toHaveAttribute("aria-pressed", "false");
      await expect(page.getByRole("button", { name: `Ver deuda de la placa ${V2}` })).toBeVisible();
      await expect(page.getByRole("button", { name: `Ver deuda de la placa ${V7}` })).toHaveCount(0);
      await expect(casilla(page, V7)).toBeDisabled();
      await expect(casilla(page, V7)).toHaveAccessibleDescription(/Faltan consultas por terminar/);
    });

    await test.step("AC22: «Descargar deuda (2)» baja un solo Excel y «Sanear (2)» solicita los dos", async () => {
      await casilla(page, V1).check();
      await casilla(page, V2).check();
      await expect(page.getByText("2 vehículos seleccionados", { exact: true })).toBeVisible();
      await descargar(page, page.getByRole("button", { name: /^Descargar deuda \(2\)$/ }), /\.xlsx$/i);
      await page.getByRole("button", { name: /^Sanear \(2\)$/ }).click();
      await expect(page.locator(":focus")).toContainText("Se solicitó el saneamiento de 2 vehículos.");
      await expect(tabla(page).getByRole("row").filter({ hasText: V1 })).toContainText("Saneamiento solicitado");
      await expect(tabla(page).getByRole("row").filter({ hasText: V2 })).toContainText("Saneamiento solicitado");
      await revisarTemas(page, "vehiculos-resultado-1280", { captura: "C-7" });
    });

    await test.step("La selección se limpia al cambiar los filtros", async () => {
      await casilla(page, V1).check();
      await expect(page.getByText("1 vehículo seleccionado", { exact: true })).toBeVisible();
      await filtros(page).getByRole("combobox", { name: "Estado del vehículo" }).selectOption("TERMINADO");
      await expect(casilla(page, V1)).not.toBeChecked();
    });

    await test.step("C-7 / AC41 / AC42: 1280 y 360 con selección; Tab a 360; sin desplazamiento de página", async () => {
      await page.goto(`${APP}/?vista=vehiculos&placa=${prefijo}`);
      await casilla(page, V1).check();
      await revisarTemas(page, "vehiculos-1280", { captura: "C-7" });
      await sinDesbordeHorizontal(page, "vehiculos-1280");
      await ancho(page, 360);
      await revisarTemas(page, "vehiculos-360", { captura: "C-7" });
      await sinDesbordeHorizontal(page, "vehiculos-360");
      await recorrerConTab(page, "vehiculos-360", { pasos: 80 });
      await ancho(page, 1280);
    });
  });

  test("AC40 / C-8: cargando, vacío sin filtros, vacío con filtros y error con «Reintentar»", async ({ page }) => {
    exigirCliente();
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!);

    await test.step("cargando (respuesta real retenida)", async () => {
      const espera = await retener(page, LISTA);
      await page.goto(`${APP}/?vista=vehiculos`);
      await expect(page.getByText("Cargando vehículos…").first()).toBeVisible();
      await revisarTemas(page, "vehiculos-cargando-1280", { captura: "C-8" });
      await espera.soltar();
      await expect(tabla(page).or(page.getByText("Aún no hay vehículos"))).toBeVisible();
    });

    await test.step("vacío sin filtros (página vacía del contrato ListaVehiculos)", async () => {
      const vacia = (route: Route) => route.fulfill({ json: { pagina: 1, tamano: 10, total: 0, vehiculos: [] } });
      await page.route(LISTA, vacia);
      await page.goto(`${APP}/?vista=vehiculos`);
      await expect(page.getByText("Aún no hay vehículos")).toBeVisible();
      await expect(page.getByText("Aquí verás todas las placas de tus diagnósticos, en proceso y terminadas.")).toBeVisible();
      await revisarTemas(page, "vehiculos-vacio-1280", { captura: "C-8" });
      await page.unroute(LISTA, vacia);
    });

    await test.step("vacío con filtros", async () => {
      await page.goto(`${APP}/?vista=vehiculos&placa=ZZZ`);
      await expect(page.getByText("Ningún vehículo coincide")).toBeVisible();
      await expect(page.getByText("Prueba con otros filtros.")).toBeVisible();
      await revisarTemas(page, "vehiculos-vacio-filtros-1280", { captura: "C-8" });
      await page.getByRole("button", { name: "Limpiar filtros" }).last().click();
      await expect(page).toHaveURL(`${APP}/?vista=vehiculos`);
    });

    await test.step("error con «Reintentar» y sin código técnico (AC43)", async () => {
      const fallo = await fallarUnaVez(page, "GET", LISTA, { status: 500, code: "ERROR_INTERNO", error: "Fallo de prueba." }, { siempre: true });
      await page.reload();
      await expect(page.getByText("No pudimos cargar los vehículos")).toBeVisible();
      await expect(page.getByText("ERROR_INTERNO")).toHaveCount(0);
      await expect(page.getByText(/\b500\b/)).toHaveCount(0);
      await revisarTemas(page, "vehiculos-error-1280", { captura: "C-8" });
      await fallo.quitar();
      await page.getByRole("button", { name: "Reintentar" }).click();
      await expect(tabla(page).or(page.getByText("Aún no hay vehículos"))).toBeVisible();
    });
  });
});
