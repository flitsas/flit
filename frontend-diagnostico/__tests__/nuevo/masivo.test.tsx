import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { ERRORES_CARGA } from "@/lib/carga/api";
import { ERRORES_CABECERA } from "@/lib/diagnosticos/api";
import { ID, TIPOS, archivo, correccionesDe, cuerpoDe, diagnostico, fila, formularioDe, indiceDe, llamadas, montarNuevo, resumen, type Navegacion } from "./datos";

// Uso de ejemplo: /diagnosticos/nuevo?modo=masivo (HU #14085): el usuario escribe el nombre, elige fuentes y modo, elige
// el archivo y pulsa «Validar archivo»: POST /diagnosticos crea la cabecera y luego POST /diagnosticos/{id}/carga la
// analiza; el resumen, la corrección de filas y la confirmación quedan en la misma pantalla. Un reintento con la
// cabecera ya creada usa PUT /diagnosticos/{id}.

const nav = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn(), params: new URLSearchParams() }));
vi.mock("next/navigation", () => ({
  usePathname: () => "/diagnosticos/nuevo",
  useSearchParams: () => nav.params,
  useRouter: () => nav,
}));

beforeEach(() => {
  nav.push.mockReset();
  nav.replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

const navegacion = nav as unknown as Navegacion;
const SECRETO = "mensaje del servidor que no debe verse";
type Rutas = NonNullable<Parameters<typeof montarNuevo>[1]>["rutas"];

const CREADO = () => Response.json(diagnostico(), { status: 201 });
const ANALISIS = () => Response.json(resumen());
const CONFIRMADO = () => Response.json({ diagnostico_id: ID, confirmados: 2, invalidos: 0, duplicados: 0, limite_vehiculos_lote: 500, vehiculos: [] }, { status: 202 });
const RUTA_CARGA = `/diagnosticos/${ID}/carga`;

const montar = (rutas: Rutas = {}) =>
  montarNuevo(navegacion, { query: "modo=masivo", rutas: { "POST /diagnosticos": CREADO, [`POST ${RUTA_CARGA}`]: ANALISIS, ...rutas } });

const nombre = () => screen.findByRole("textbox", { name: "Nombre del diagnóstico" });
const elegir = (f = archivo("flota.csv", "text/csv")) => {
  fireEvent.change(screen.getByLabelText(/archivo de vehículos/i), { target: { files: [f] } });
  return f;
};
const validar = () => fireEvent.click(screen.getByRole("button", { name: "Validar archivo" }));
async function preparar(nombreLote = "Flota norte") {
  fireEvent.change(await nombre(), { target: { value: nombreLote } });
  return elegir();
}

describe("Masivo — un solo paso (AC3)", () => {
  it("«Validar archivo» crea la cabecera con POST /diagnosticos y luego sube el archivo, en ese orden", async () => {
    const api = montar();
    const subido = await preparar();
    validar();
    await screen.findByRole("region", { name: "Resumen de validación" });
    const post = indiceDe(api, "POST", "/diagnosticos");
    const carga = indiceDe(api, "POST", RUTA_CARGA);
    expect(post).toBeGreaterThanOrEqual(0);
    expect(carga).toBeGreaterThan(post);
    expect(cuerpoDe(api.mock.calls[post])).toEqual({
      nombre: "Flota norte",
      modo: "SIMPLE",
      tipo_tramite_codigo: null,
      alcance: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] },
    });
    // analizarCarga adjunta el archivo con su nombre (multipart lo envuelve en otro File): se compara por nombre.
    expect((formularioDe(api.mock.calls[carga]).get("archivo") as File).name).toBe(subido.name);
  });

  it("el resumen, la tabla de filas erróneas y el estimado quedan en la misma pantalla, sin navegar", async () => {
    montar({
      [`POST ${RUTA_CARGA}`]: () => Response.json(resumen({ estimado_consultas: [{ fuente: "RUNT", consultas: 2 }] })),
    });
    await preparar();
    validar();
    expect(await screen.findByRole("region", { name: "Resumen de validación" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Identificador de la fila 3" })).toBeInTheDocument();
    const estimado = screen.getByRole("region", { name: "Estimado de consultas" });
    // FE-3: el estimado va en la barra final de la pantalla (S5), no dentro de Parámetros.
    expect(document.querySelector("[data-parte='pie-creacion']")).toContainElement(estimado);
    expect(nav.push).not.toHaveBeenCalled();
  });

  it("corrige una fila y confirma con POST /vehiculos reenviando el mismo archivo; navega al tablero", async () => {
    const api = montar({
      [`POST ${RUTA_CARGA}/validar-filas`]: () => Response.json(resumen({ filas: [fila({ fila: 3, identificador: "TST124", identificador_norm: "TST124" })] })),
      [`POST /diagnosticos/${ID}/vehiculos`]: CONFIRMADO,
    });
    const subido = await preparar();
    validar();
    const region = await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.change(screen.getByRole("textbox", { name: "Identificador de la fila 3" }), { target: { value: "TST124" } });
    await waitFor(() => expect(within(region).getByText("Válidos").nextSibling).toHaveTextContent("3"));
    fireEvent.click(screen.getByRole("button", { name: /continuar solo con los válidos/i }));
    expect(await screen.findByRole("heading", { name: "Diagnóstico iniciado con 2 vehículos" })).toBeInTheDocument();
    const [confirmacion] = llamadas(api, "POST", `/diagnosticos/${ID}/vehiculos`);
    expect(formularioDe(confirmacion).get("archivo")).toBe(subido);
    expect(correccionesDe(confirmacion)).toEqual([{ fila: 3, identificador: "TST124" }]);
  });

  it("con el resumen a la vista los parámetros quedan bloqueados; «Subir otro archivo» los libera", async () => {
    montar();
    await preparar();
    validar();
    await screen.findByRole("region", { name: "Resumen de validación" });
    expect(await nombre()).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Validar archivo" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Subir otro archivo" }));
    expect(await nombre()).toBeEnabled();
    expect(screen.getByRole("button", { name: "Validar archivo" })).toBeInTheDocument();
  });
});

describe("Masivo — validaciones en pantalla (AC4)", () => {
  it("sin archivo, «Validar archivo» lo pide en la zona de archivo y no llama a la API", async () => {
    const api = montar();
    fireEvent.change(await nombre(), { target: { value: "Flota norte" } });
    validar();
    expect(await screen.findByText(/elige el archivo de vehículos/i)).toBeInTheDocument();
    // ZonaArrastre del kit (HU #14562 AC10): el error queda asociado a «Elegir archivo».
    expect(screen.getByRole("button", { name: /elegir archivo/i })).toHaveAccessibleDescription(/elige el archivo de vehículos/i);
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);
  });

  it("un formato no admitido lo rechaza la ZonaArrastre del kit, sin llamar a la API", async () => {
    const api = montar();
    await nombre();
    elegir(archivo("flota.pdf", "application/pdf"));
    expect(await screen.findByRole("alert")).toHaveTextContent(/formato no admitido.*\.xlsx.*\.csv/i);
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);
  });

  it("FE-3: «Validar archivo» queda al final, después de la zona de archivo, y envía el formulario de Parámetros", async () => {
    montar();
    await nombre();
    const vehiculos = screen.getByRole("region", { name: "Vehículos" });
    const validarBoton = screen.getByRole("button", { name: "Validar archivo" });
    expect(vehiculos.compareDocumentPosition(validarBoton) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const form = screen.getByRole("form", { name: "Parámetros del diagnóstico masivo" });
    expect(form.contains(validarBoton)).toBe(false);
    expect(validarBoton).toHaveAttribute("form", form.id);
  });

  it("nombre vacío o de 151 caracteres: error en el campo y no crea la cabecera", async () => {
    const api = montar();
    await nombre();
    elegir();
    validar();
    expect(await nombre()).toHaveAttribute("aria-invalid", "true");
    expect(await nombre()).toHaveAccessibleDescription(/escribe el nombre del diagnóstico/i);
    expect(await nombre()).toHaveFocus();
    fireEvent.change(await nombre(), { target: { value: "x".repeat(151) } });
    validar();
    expect(await nombre()).toHaveAccessibleDescription(/hasta 150 caracteres/i);
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);
  });

  it("Con Análisis IA ofrece los tipos del catálogo y no crea sin elegir uno; las fuentes obligatorias quedan marcadas y se envían", async () => {
    const api = montar();
    await preparar();
    const simit = screen.getByRole("checkbox", { name: "SIMIT vehículo" });
    fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));
    const combo = await screen.findByRole("combobox", { name: /tipo de trámite/i });
    expect(within(combo).getAllByRole("option").map((o) => o.getAttribute("value"))).toEqual(["", ...TIPOS.map((t) => t.codigo)]);
    validar();
    expect(combo).toHaveAttribute("aria-invalid", "true");
    expect(combo).toHaveAccessibleDescription(/elige el tipo de trámite/i);
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);

    fireEvent.change(combo, { target: { value: "TRASPASO_UNILATERAL" } });
    expect(simit).toBeChecked();
    expect(simit).toHaveAttribute("aria-disabled", "true");
    expect(simit).toHaveAccessibleDescription(/obligatoria para el tipo de trámite/i);
    validar();
    await waitFor(() => expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(1));
    expect(cuerpoDe(llamadas(api, "POST", "/diagnosticos")[0])).toMatchObject({
      modo: "CON_ANALISIS",
      tipo_tramite_codigo: "TRASPASO_UNILATERAL",
      alcance: { por_vehiculo: ["RUNT", "SIMIT_VEHICULO"] },
    });
  });

  it("el 503 del catálogo muestra el error y deja reintentar al vencer Retry-After", async () => {
    let caido = true;
    const api = montar({
      "GET /tipos-tramite": () =>
        caido
          ? Response.json({ code: "CATALOGO_NO_DISPONIBLE", error: "Catálogo caído" }, { status: 503, headers: { "Retry-After": "1" } })
          : Response.json({ tipos: TIPOS }),
    });
    await nombre();
    fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));
    expect(await screen.findByText(/el catálogo de tipos de trámite no está disponible/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar en 1 s" })).toBeDisabled();
    caido = false;
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }, { timeout: 2500 }));
    expect(await screen.findByRole("combobox", { name: /tipo de trámite/i })).toBeInTheDocument();
    expect(llamadas(api, "GET", "/tipos-tramite")).toHaveLength(2);
  });
});

describe("Masivo — errores de la API y reintento con PUT (AC4)", () => {
  it.each([
    ["nombre", "NOMBRE_INVALIDO", "El nombre no es válido."],
    ["alcance.por_vehiculo", "FUENTE_NO_HABILITADA", "SIMIT vehículo no está habilitada."],
  ])("el 422 de %s se pinta en su campo y conserva lo digitado", async (campo, code, mensaje) => {
    montar({ "POST /diagnosticos": () => Response.json({ code, error: mensaje, campo }, { status: 422 }) });
    await preparar();
    validar();
    if (campo === "nombre") {
      await waitFor(async () => expect(await nombre()).toHaveAccessibleDescription(mensaje));
      expect(await nombre()).toHaveAttribute("aria-invalid", "true");
    } else {
      await waitFor(() => expect(screen.getByRole("group", { name: "Alcance" })).toHaveAccessibleDescription(mensaje));
    }
    expect(await nombre()).toHaveValue("Flota norte");
  });

  it("el 422 del tipo de trámite se pinta en el combobox", async () => {
    montar({
      "POST /diagnosticos": () =>
        Response.json({ code: "TIPO_TRAMITE_NO_DISPONIBLE", error: "El tipo ya no está disponible.", campo: "tipo_tramite_codigo" }, { status: 422 }),
    });
    await preparar();
    fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));
    const combo = await screen.findByRole("combobox", { name: /tipo de trámite/i });
    fireEvent.change(combo, { target: { value: "TRASPASO_STANDARD" } });
    validar();
    await waitFor(() => expect(combo).toHaveAccessibleDescription("El tipo ya no está disponible."));
  });

  it("un 403 de la cabecera se muestra como aviso general sin perder lo digitado ni el modo", async () => {
    montar({ "POST /diagnosticos": () => new Response(null, { status: 403 }) });
    await preparar();
    validar();
    expect(await screen.findByText("No se guardó el diagnóstico")).toBeInTheDocument();
    expect(screen.getByText(/no tienes permiso para esta acción/i)).toBeInTheDocument();
    expect(await nombre()).toHaveValue("Flota norte");
    expect(within(screen.getByRole("radiogroup", { name: "Tipo de diagnóstico" })).getByRole("radio", { name: "Masivo" })).toHaveAttribute("aria-checked", "true");
  });

  // AP-9 (SF-6): reescrito. Antes afirmaba el mensaje de la API; ahora el texto fijo del code del contrato.
  it("AP-9: archivo rechazado (422) muestra el texto fijo del code; el siguiente intento usa PUT /diagnosticos/{id} y no otro POST", async () => {
    let veces = 0;
    const api = montar({
      [`POST ${RUTA_CARGA}`]: () =>
        ++veces === 1 ? Response.json({ code: "FORMATO_XLS_NO_ADMITIDO", error: SECRETO }, { status: 422 }) : ANALISIS(),
      [`PUT /diagnosticos/${ID}`]: () => Response.json(diagnostico()),
    });
    await preparar();
    validar();
    expect(await screen.findByText(ERRORES_CARGA.FORMATO_XLS_NO_ADMITIDO)).toBeInTheDocument();
    expect(screen.queryByText(SECRETO)).not.toBeInTheDocument();
    expect(screen.getByText("No se pudo analizar el archivo")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();

    elegir(archivo("flota-2.csv", "text/csv"));
    validar();
    await screen.findByRole("region", { name: "Resumen de validación" });
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(1);
    expect(llamadas(api, "PUT", `/diagnosticos/${ID}`)).toHaveLength(1);
    // Orden: la primera subida, luego el PUT de la cabecera y después la segunda subida.
    const subidas = api.mock.calls.flatMap(([url, init], i) =>
      url === `/api/v1/diagnostico${RUTA_CARGA}` && (init as RequestInit | undefined)?.method === "POST" ? [i] : [],
    );
    const put = indiceDe(api, "PUT", `/diagnosticos/${ID}`);
    expect(put).toBeGreaterThan(subidas[0]);
    expect(put).toBeLessThan(subidas[1]);
  });

  it("500 o red en la subida: error con Reintentar, que guarda con PUT y vuelve a subir el mismo archivo", async () => {
    let veces = 0;
    const api = montar({
      [`POST ${RUTA_CARGA}`]: () => (++veces === 1 ? new Response("", { status: 500 }) : ANALISIS()),
      [`PUT /diagnosticos/${ID}`]: () => Response.json(diagnostico()),
    });
    const subido = await preparar();
    validar();
    // AP-9 (SF-6): reescrito. Un 5xx cae en el respaldo fijo del sitio, no en el message del ApiError.
    expect(await screen.findByText("No se pudo analizar el archivo.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await screen.findByRole("region", { name: "Resumen de validación" });
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(1);
    expect(llamadas(api, "PUT", `/diagnosticos/${ID}`)).toHaveLength(1);
    const subidas = llamadas(api, "POST", RUTA_CARGA);
    expect(subidas).toHaveLength(2);
    expect((formularioDe(subidas[1]).get("archivo") as File).name).toBe(subido.name);
  });

  it("un 409 del PUT avisa que el diagnóstico ya no es editable", async () => {
    let veces = 0;
    montar({
      [`POST ${RUTA_CARGA}`]: () => (++veces === 1 ? new Response("", { status: 500 }) : ANALISIS()),
      [`PUT /diagnosticos/${ID}`]: () => Response.json({ code: "DIAGNOSTICO_NO_EDITABLE", error: SECRETO }, { status: 409 }),
    });
    await preparar();
    validar();
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    // AP-9 (SF-6): reescrito. Antes afirmaba el mensaje de la API.
    expect(await screen.findByText(ERRORES_CABECERA.DIAGNOSTICO_NO_EDITABLE)).toBeInTheDocument();
    expect(screen.queryByText(SECRETO)).not.toBeInTheDocument();
  });

  it("AP-9: un 503 CATALOGO_NO_DISPONIBLE al guardar la cabecera muestra el texto fijo del code", async () => {
    montar({ "POST /diagnosticos": () => Response.json({ code: "CATALOGO_NO_DISPONIBLE", error: SECRETO }, { status: 503 }) });
    await preparar();
    validar();
    expect(await screen.findByText(ERRORES_CABECERA.CATALOGO_NO_DISPONIBLE)).toBeInTheDocument();
    expect(screen.queryByText(SECRETO)).not.toBeInTheDocument();
  });

  it("AP-9: un 500 al guardar la cabecera cae en el respaldo fijo", async () => {
    montar({ "POST /diagnosticos": () => Response.json({ error: SECRETO }, { status: 500 }) });
    await preparar();
    validar();
    expect(await screen.findByText("No se pudo guardar el diagnóstico.")).toBeInTheDocument();
  });

  it("AP-9: un 422 LIMITE_SUPERADO al confirmar muestra el texto fijo del code, sin el mensaje del servidor", async () => {
    montar({ [`POST /diagnosticos/${ID}/vehiculos`]: () => Response.json({ code: "LIMITE_SUPERADO", error: SECRETO, campo: "vehiculos" }, { status: 422 }) });
    await preparar();
    validar();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(screen.getByRole("button", { name: /continuar solo con los válidos/i }));
    expect(await screen.findByText(ERRORES_CARGA.LIMITE_SUPERADO)).toBeInTheDocument();
    expect(screen.queryByText(SECRETO)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
  });

  it("un 403 de sesión en la subida lleva a la página 403 con su código", async () => {
    montar({ [`POST ${RUTA_CARGA}`]: () => Response.json({ code: "SIN_EMPRESA", error: "Sin empresa" }, { status: 403 }) });
    await preparar();
    validar();
    await waitFor(() => expect(nav.replace).toHaveBeenCalledWith("/403?code=SIN_EMPRESA"));
  });

  it("un 500 al confirmar es un error genérico con Reintentar, que reenvía el mismo archivo", async () => {
    let veces = 0;
    const api = montar({ [`POST /diagnosticos/${ID}/vehiculos`]: () => (++veces === 1 ? new Response("", { status: 500 }) : CONFIRMADO()) });
    await preparar();
    validar();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(screen.getByRole("button", { name: /continuar solo con los válidos/i }));
    expect(await screen.findByText("No se confirmaron los vehículos")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("heading", { name: "Diagnóstico iniciado con 2 vehículos" })).toBeInTheDocument();
    expect(llamadas(api, "POST", `/diagnosticos/${ID}/vehiculos`)).toHaveLength(2);
  });
});

describe("Masivo — resultado «Diagnóstico iniciado con N vehículos» (AC18, AC19, AC16)", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"], shouldAdvanceTime: true });
  });
  afterEach(() => vi.useRealTimers());

  it("AC18: al confirmar muestra el resultado con los N confirmados, oculta el selector y a los 5 s navega al tablero", async () => {
    montar({ [`POST /diagnosticos/${ID}/vehiculos`]: CONFIRMADO });
    await preparar();
    validar();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(screen.getByRole("button", { name: /continuar solo con los válidos/i }));
    expect(await screen.findByRole("heading", { name: "Diagnóstico iniciado con 2 vehículos" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ver tablero" })).toHaveAttribute("href", `/diagnosticos/${ID}`);
    expect(screen.queryByRole("radiogroup", { name: "Tipo de diagnóstico" })).toBeNull();
    expect(nav.push).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(5000);
    expect(nav.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`);
  });

  it("AC19: «Quedarme aquí» detiene la redirección del masivo", async () => {
    montar({ [`POST /diagnosticos/${ID}/vehiculos`]: CONFIRMADO });
    await preparar();
    validar();
    await screen.findByRole("region", { name: "Resumen de validación" });
    fireEvent.click(screen.getByRole("button", { name: /continuar solo con los válidos/i }));
    fireEvent.click(await screen.findByRole("button", { name: "Quedarme aquí" }));
    await vi.advanceTimersByTimeAsync(6000);
    expect(nav.push).not.toHaveBeenCalled();
  });

  it("AC16: el pie del masivo ofrece Cancelar hacia el inicio", async () => {
    montar();
    await nombre();
    // FE-3: Cancelar va en la barra final, fuera de Parámetros. FE-4: vuelve a /diagnosticos.
    expect(within(document.querySelector("[data-parte='pie-creacion']") as HTMLElement).getByRole("link", { name: "Cancelar" })).toHaveAttribute("href", "/diagnosticos");
  });
});
