import { expect, test, type Locator, type Page } from "@playwright/test";
import {
  API,
  APP,
  BACKOFFICE,
  CLIENTE,
  EMPRESA,
  entrar,
  esperarTerminados,
  exigirBackoffice,
  exigirCliente,
  placaFicticia,
  prefijoFicticio,
  sembrarMasivo,
} from "./saneamiento-comun";
import { abrirAcciones } from "./ola-ux-comun";
import {
  DOC_FICTICIO,
  RUTA_REQ,
  TEXTO_CAMBIO_PROPIETARIO,
  capturar,
  esperarTablaPorDefecto,
  esperarTooltip,
  estadoTooltip,
  reescribirGet,
  revisarTemasReq,
  textoDeXlsx,
  variarGrilla,
} from "./ola-requerimiento-comun";

/**
 * HU #14585 · Las 7 tablas con el diseño por defecto, icono de cambio de propietario, columnas de documento retiradas y
 * «Locatario» → «Propietario actual (opcional)» (oleada «requerimiento.pdf», 2026-11-16). spec-ui §1, §6.5, §7 (A7.1–A7.3),
 * §8 (A8.1–A8.2); contrato-api.md §1 (C1, C2, C5); SP-4, SP-7.
 *
 * Modo de la API: suite local real con simulación, con be1 integrado (plantilla con cabeceras nuevas, ENCABEZADO_AMBIGUO).
 * Siembra un Masivo de 2 placas ficticias (terminadas en 1 y en 2). `page.route` solo para variar respuestas REALES dentro
 * del contrato 1.6.0: `cambio_propietario` en la grilla y en GET /vehiculos (true/false/null), porque la simulación no
 * declara propietario. Las tablas que dependen de estados de saneamiento (RenglonesGestion, PanelDeudaSaneamiento) se
 * revisan si la compañía de prueba los tiene; si no, el test lo anota y se omite esa tabla.
 * Variables: E2E_USUARIO / E2E_CLAVE (compañía, diagnosticos.crear y ver) y E2E_BACKOFFICE_USUARIO / E2E_BACKOFFICE_CLAVE /
 * E2E_EMPRESA (SuperAdmin FLIT y el uuid de la compañía de E2E_USUARIO).
 *
 * «Diseño por defecto» se mide, no se mira la clase: contenedor con borde y radio ≥ 12 px (CONTENEDOR_TABLA_CLS), fondo y
 * texto de la cabecera iguales a `--table-header-bg/fg` y cabecera ≥ 12 px, en claro y oscuro.
 *
 * Lista congelada:
 *   - table «Diagnósticos de la compañía» (referencia) · «Vehículos de la compañía» · «/semáforo de consultas/i» ·
 *     «Lotes de la compañía» · «Saneamientos» · «Filas con errores» (o la tabla dentro de esa región) ·
 *     «SIMIT vehículo» u otra de SeccionesDeuda (dentro del dialog «Deuda y saneamiento») · «Conceptos del saneamiento»
 *   - button «Cambio de propietario» (en el rowheader / celda de placa)
 *   - heading «Propietario actual (opcional)» · combobox «Tipo de documento del propietario actual» ·
 *     textbox «/Número de documento del propietario actual/» · texto «Declarado por el cliente»
 *   - combobox «Tipo de documento del propietario actual de la fila N» · textbox «Documento del propietario actual de la fila N»
 *   - link «Descargar plantilla»
 */
test.describe.configure({ mode: "serial" });

const SPEC = "tablas";

let lote = "";
let V1 = "";
let V2 = "";

const grilla = (page: Page) => page.getByRole("table", { name: /semáforo de consultas/i });
const iconoEn = (_page: Page, contenedor: Locator) => contenedor.getByRole("button", { name: "Cambio de propietario", exact: true });

test.describe("HU #14585 tablas, propietario actual y etiquetas", () => {
  test("siembra + grilla: sin columnas de documento e icono solo con true (cliente)", async ({ page }) => {
    exigirCliente();
    test.setTimeout(420_000);
    const prefijo = prefijoFicticio();
    [V1, V2] = [placaFicticia(prefijo, "1"), placaFicticia(prefijo, "2")];
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    ({ diagnosticoId: lote } = await sembrarMasivo(page, [V1, V2], { nombre: `E2E tablas propietario ${Date.now()}` }));
    await esperarTerminados(page, lote, [V1, V2]);

    // La respuesta real ya no trae documentos (C5); se comprueba antes de variarla.
    const real = await page.request.get(`${API}/diagnosticos/${lote}/grilla?desde=0`);
    const cuerpo = (await real.json()) as { vehiculos: Record<string, unknown>[] };
    for (const v of cuerpo.vehiculos) {
      expect(v, "C5: FilaVehiculo sin documento_declarado").not.toHaveProperty("documento_declarado");
      expect(v, "C5: FilaVehiculo sin propietarios").not.toHaveProperty("propietarios");
      expect(v, "C4: cambio_propietario requerido").toHaveProperty("cambio_propietario");
    }

    await variarGrilla(page, { porPlaca: { [V1]: { cambio_propietario: true }, [V2]: { cambio_propietario: false } }, sinPedir: [] });
    await page.goto(`${APP}/diagnosticos/${lote}`);
    await expect(grilla(page)).toBeVisible();
    for (const nombre of ["Documento declarado", "Documento del propietario"]) await expect(grilla(page).getByRole("columnheader", { name: nombre })).toHaveCount(0);
    const rowheader = (p: string) => grilla(page).getByRole("rowheader", { name: new RegExp(p) });
    await expect(iconoEn(page, rowheader(V1))).toHaveCount(1);
    await expect(iconoEn(page, rowheader(V2))).toHaveCount(0);
    await esperarTablaPorDefecto(page, grilla(page), "GrillaSemaforos");
    await capturar(page, SPEC, 1, "grilla-icono-sin-columnas-documento", { paginaCompleta: true });
  });

  test("Vehículos (/diagnosticos?vista=vehiculos): diseño por defecto e icono junto a la placa solo con true", async ({ page }) => {
    exigirCliente();
    test.skip(!V1, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    // Variación de GET /vehiculos dentro del contrato: V1 true, V2 false, el resto null.
    await reescribirGet<{ vehiculos: { placa: string | null; cambio_propietario?: boolean | null }[] }>(page, RUTA_REQ.vehiculos, (l) => ({
      ...l,
      vehiculos: l.vehiculos.map((v) => ({ ...v, cambio_propietario: v.placa === V1 ? true : v.placa === V2 ? false : null })),
    }));
    await page.goto(`${APP}/diagnosticos?vista=vehiculos&placa=${encodeURIComponent(V1.slice(0, 3))}`);
    const tabla = page.getByRole("table", { name: "Vehículos de la compañía" });
    await expect(tabla).toBeVisible();
    const filaDe = (p: string) => tabla.getByRole("row").filter({ hasText: p });
    await expect(iconoEn(page, filaDe(V1).first())).toHaveCount(1);
    await expect(iconoEn(page, filaDe(V2).first())).toHaveCount(0);
    const icono = iconoEn(page, filaDe(V1).first());
    const caja = (await icono.boundingBox())!;
    expect(Math.min(caja.width, caja.height), "toggletip 24×24").toBeGreaterThanOrEqual(24);
    await page.keyboard.press("Shift");
    await icono.focus();
    await esperarTooltip(icono, true, 400);
    expect((await estadoTooltip(icono)).texto).toMatch(TEXTO_CAMBIO_PROPIETARIO);
    await capturar(page, SPEC, 2, "vehiculos-icono-tooltip");
    await page.keyboard.press("Escape");
    for (const nombre of ["Documento declarado", "Documento del propietario"]) await expect(tabla.getByRole("columnheader", { name: nombre })).toHaveCount(0);
    await esperarTablaPorDefecto(page, tabla, "TablaVehiculos");
    await capturar(page, SPEC, 3, "vehiculos-diseno-por-defecto", { paginaCompleta: true });
    // A7.2: la referencia (listado principal) con la misma receta.
    await page.goto(`${APP}/diagnosticos`);
    await esperarTablaPorDefecto(page, page.getByRole("table", { name: "Diagnósticos de la compañía" }), "ListadoDiagnosticos (referencia)");
    await capturar(page, SPEC, 4, "listado-referencia", { paginaCompleta: true });
    await revisarTemasReq(page, "listado-referencia-1280", { captura: "TP-1" });
  });

  test("Deuda y saneamiento: SeccionesDeuda (y Conceptos del saneamiento si existe) con el diseño por defecto", async ({ page }) => {
    exigirCliente();
    test.skip(!lote, "Depende de la siembra");
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, `/diagnosticos/${lote}`);
    await page.goto(`${APP}/diagnosticos/${lote}`);
    const { menu } = await abrirAcciones(page, V2);
    await menu.getByRole("menuitem", { name: /^Ver deuda/ }).click();
    const dialogo = page.getByRole("dialog", { name: "Deuda y saneamiento" });
    await expect(dialogo).toBeVisible();
    const secciones = dialogo.getByRole("table");
    await expect(secciones.first()).toBeVisible();
    await esperarTablaPorDefecto(page, secciones.first(), "SeccionesDeuda");
    // §8: el motivo del locatario ya no habla del documento declarado.
    await expect(dialogo.getByText(/documento del locatario que declara/i)).toHaveCount(0);
    const conceptos = dialogo.getByRole("table", { name: "Conceptos del saneamiento" });
    if (await conceptos.count()) await esperarTablaPorDefecto(page, conceptos, "PanelDeudaSaneamiento");
    else test.info().annotations.push({ type: "omitido", description: "PanelDeudaSaneamiento: la placa no tiene saneamiento con conceptos" });
    await capturar(page, SPEC, 5, "deuda-secciones-diseno-por-defecto");
    await revisarTemasReq(page, "deuda-tablas-640", { captura: "TP-2", paginaCompleta: false });
  });

  test("backoffice: Lotes de la compañía, Saneamientos y Conceptos a sanear con el diseño por defecto; grilla sin documentos", async ({ page }) => {
    exigirBackoffice();
    await page.setViewportSize({ width: 1280, height: 800 });
    await entrar(page, BACKOFFICE.usuario!, BACKOFFICE.clave!, "/");
    await page.goto(`${APP}/backoffice/diagnosticos?empresa=${encodeURIComponent(EMPRESA!)}`);
    await esperarTablaPorDefecto(page, page.getByRole("table", { name: /lotes de la compañía/i }), "ListaLotesBackoffice");
    await capturar(page, SPEC, 6, "backoffice-lotes", { paginaCompleta: true });

    if (lote) {
      await variarGrilla(page, { porPlaca: { [V1]: { cambio_propietario: true }, [V2]: { cambio_propietario: null } }, sinPedir: [], backoffice: true });
      await page.goto(`${APP}/backoffice/diagnosticos/${lote}?empresa=${encodeURIComponent(EMPRESA!)}`);
      await expect(grilla(page)).toBeVisible();
      for (const nombre of ["Documento declarado", "Documento del propietario"]) await expect(grilla(page).getByRole("columnheader", { name: nombre })).toHaveCount(0);
      await expect(iconoEn(page, grilla(page).getByRole("rowheader", { name: new RegExp(V1) }))).toHaveCount(1);
      await expect(iconoEn(page, grilla(page).getByRole("rowheader", { name: new RegExp(V2) }))).toHaveCount(0);
      await capturar(page, SPEC, 7, "backoffice-grilla-icono", { paginaCompleta: true });
    }

    await page.goto(`${APP}/backoffice/saneamientos`);
    const bandeja = page.getByRole("table", { name: "Saneamientos" });
    const vacio = page.getByText(/no hay saneamientos|ningún saneamiento/i);
    await expect(bandeja.or(vacio).first()).toBeVisible();
    if (!(await bandeja.count())) {
      test.info().annotations.push({ type: "omitido", description: "BandejaSaneamientos/RenglonesGestion: la bandeja está vacía en esta compañía" });
      return;
    }
    await esperarTablaPorDefecto(page, bandeja, "BandejaSaneamientos");
    await capturar(page, SPEC, 8, "backoffice-saneamientos", { paginaCompleta: true });
    await revisarTemasReq(page, "bandeja-1280", { captura: "TP-3" });
    // Conceptos a sanear (RenglonesGestion): en el detalle de un saneamiento EN_GESTION, si lo hay.
    const enGestion = bandeja.getByRole("link").first();
    if (await enGestion.count()) {
      await enGestion.click();
      const renglones = page.getByRole("table").filter({ hasText: /Concepto/ }).first();
      if (await renglones.count()) {
        await esperarTablaPorDefecto(page, renglones, "RenglonesGestion");
        await capturar(page, SPEC, 9, "backoffice-conceptos-a-sanear", { paginaCompleta: true });
      }
      else test.info().annotations.push({ type: "omitido", description: "RenglonesGestion: el saneamiento abierto no tiene conceptos" });
    }
  });

  test("Express: «Propietario actual (opcional)» en vez de «Locatario», con la insignia y la ayuda nuevas", async ({ page }) => {
    exigirCliente();
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    await page.goto(`${APP}/diagnosticos/nuevo?modo=express`);
    const vehiculos = page.getByRole("region", { name: "Vehículos" });
    await expect(vehiculos.getByRole("heading", { name: "Propietario actual (opcional)", exact: true })).toBeVisible();
    await expect(vehiculos.getByRole("heading", { name: "Locatario", exact: true })).toHaveCount(0);
    await expect(vehiculos.getByText("Declarado por el cliente", { exact: true })).toBeVisible();
    // A8.2: etiqueta visible y nombre accesible coinciden.
    const tipo = vehiculos.getByRole("combobox", { name: "Tipo de documento del propietario actual" });
    const numero = vehiculos.getByRole("textbox", { name: /^Número de documento del propietario actual/ });
    await expect(tipo).toBeVisible();
    await expect(numero).toBeVisible();
    await expect(vehiculos.getByText(/lo comparamos con el propietario actual que reporta el RUNT/)).toBeVisible();
    await expect(vehiculos.getByText(/del locatario/i)).toHaveCount(0);
    await capturar(page, SPEC, 10, "express-propietario-actual");
    await revisarTemasReq(page, "express-propietario-1280", { captura: "TP-4" });

    // C1 / SP-4: la respuesta del estimado o del Express no devuelve el documento (se observa sin lanzar).
    const respuestas: string[] = [];
    page.on("response", async (r) => {
      if (/\/express(\/estimado)?$/.test(new URL(r.url()).pathname)) respuestas.push(await r.text().catch(() => ""));
    });
    await tipo.selectOption({ index: 1 });
    await numero.fill(DOC_FICTICIO.numero);
    await numero.blur();
    // El número queda enmascarado tras escribirlo (T7).
    await expect(vehiculos.getByText(DOC_FICTICIO.numero, { exact: false })).toHaveCount(0);
    for (const cuerpo of respuestas) expect(cuerpo).not.toContain(DOC_FICTICIO.numero);
    await capturar(page, SPEC, 11, "express-documento-enmascarado");
  });

  test("plantilla Excel: cabeceras «Tipo_Documento_Propietario» / «Documento_Propietario» y ayuda «propietario actual (opcional)»", async ({ page }) => {
    exigirCliente();
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    await page.goto(`${APP}/diagnosticos/nuevo?modo=masivo`);
    // SubidaArchivo: la ayuda nombra al propietario actual.
    await expect(page.getByText(/documento del propietario actual \(opcional\)/)).toBeVisible();
    const enlace = page.getByRole("link", { name: /Descargar plantilla/ });
    await expect(enlace).toBeVisible();
    const r = await page.request.get(new URL((await enlace.getAttribute("href"))!, APP).href);
    expect(r.ok(), `GET plantilla ${r.status()}`).toBeTruthy();
    const texto = textoDeXlsx(await r.body());
    expect(texto).toContain("Identificador_Vehiculo");
    expect(texto).toContain("Tipo_Documento_Propietario");
    expect(texto).toContain("Documento_Propietario");
    expect(texto).not.toContain("Tipo_Documento_Locatario");
    expect(texto).not.toContain("Documento_Locatario");
    expect(texto).toMatch(/[Pp]ropietario actual \(opcional\)/);
    await capturar(page, SPEC, 12, "masivo-ayuda-y-plantilla");
  });

  test("carga masiva: cabeceras nuevas → filas corregibles con «propietario actual»; las dos parejas → ENCABEZADO_AMBIGUO", async ({ page }) => {
    exigirCliente();
    test.setTimeout(180_000);
    await entrar(page, CLIENTE.usuario!, CLIENTE.clave!, "/diagnosticos");
    const placa = placaFicticia(prefijoFicticio(), "1");

    const subir = async (csv: string, nombre: string) => {
      await page.goto(`${APP}/diagnosticos/nuevo?modo=masivo`);
      await page.getByRole("textbox", { name: "Nombre del diagnóstico" }).fill(`E2E propietario ${nombre} ${Date.now()}`);
      await page.getByLabel(/archivo de vehículos/i).setInputFiles({ name: `${nombre}.csv`, mimeType: "text/csv", buffer: Buffer.from(csv) });
      const [resp] = await Promise.all([
        page.waitForResponse((x) => x.request().method() === "POST" && RUTA_REQ.carga.test(x.url())),
        page.getByRole("button", { name: "Validar archivo" }).click(),
      ]);
      return resp;
    };

    // Cabeceras nuevas con un documento inválido en la fila 2 (letras en una CC): la tabla corregible usa el texto nuevo.
    const ok = await subir(`Identificador_Vehiculo,Tipo_Documento_Propietario,Documento_Propietario\n${placa},CC,12AB34\n`, "nuevas");
    expect(ok.status()).toBe(200);
    // SP-4: la vista previa solo trae el eco enmascarado, nunca el número completo.
    expect(await ok.text()).not.toContain("12AB34");
    const filas = page.getByRole("region", { name: "Filas con errores" });
    await expect(filas).toBeVisible();
    await expect(filas.getByRole("combobox", { name: /^Tipo de documento del propietario actual de la fila \d+$/ })).toBeVisible();
    await expect(filas.getByRole("textbox", { name: /^Documento del propietario actual de la fila \d+$/ })).toBeVisible();
    await expect(filas.getByRole("columnheader", { name: /propietario actual/i }).first()).toBeVisible();
    await expect(filas.getByText(/locatario/i)).toHaveCount(0);
    await esperarTablaPorDefecto(page, filas.getByRole("table").first(), "TablaFilasCorregibles");
    await capturar(page, SPEC, 13, "filas-corregibles-propietario-actual", { paginaCompleta: true });
    await revisarTemasReq(page, "filas-corregibles-1280", { captura: "TP-5" });

    // C2: cabeceras viejas aceptadas como alias.
    const alias = await subir(`Identificador_Vehiculo,Tipo_Documento_Locatario,Documento_Locatario\n${placa}\n`, "alias");
    expect(alias.status()).toBe(200);

    // SP-7 / C2: las dos parejas → 422 ENCABEZADO_AMBIGUO, con un error de archivo visible (no se fusionan en silencio).
    const ambiguo = await subir(
      `Identificador_Vehiculo,Tipo_Documento_Propietario,Documento_Propietario,Tipo_Documento_Locatario,Documento_Locatario\n${placa},,,,\n`,
      "ambiguo",
    );
    expect(ambiguo.status()).toBe(422);
    expect(((await ambiguo.json()) as { code: string }).code).toBe("ENCABEZADO_AMBIGUO");
    await expect(page.getByRole("region", { name: "Resumen de validación" })).toHaveCount(0);
    await expect(page.getByRole("alert").first()).toBeVisible();
    await capturar(page, SPEC, 14, "carga-encabezado-ambiguo");
  });
});
