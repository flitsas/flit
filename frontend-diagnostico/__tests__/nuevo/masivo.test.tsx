import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
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
    expect(screen.getByRole("region", { name: "Parámetros" })).toContainElement(estimado);
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
    await waitFor(() => expect(nav.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
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
    expect(screen.getByLabelText(/archivo de vehículos/i)).toHaveAttribute("aria-invalid", "true");
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);
  });

  it("un formato no admitido se rechaza con el mensaje de siempre, sin llamar a la API", async () => {
    const api = montar();
    await nombre();
    elegir(archivo("flota.pdf", "application/pdf"));
    expect(await screen.findByText(/formato no admitido: sube el archivo en \.xlsx o \.csv/i)).toBeInTheDocument();
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);
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

  it("archivo rechazado (422) muestra el motivo de la API; el siguiente intento usa PUT /diagnosticos/{id} y no otro POST", async () => {
    let veces = 0;
    const api = montar({
      [`POST ${RUTA_CARGA}`]: () =>
        ++veces === 1 ? Response.json({ code: "FORMATO_XLS", error: "Guarda el archivo como .xlsx." }, { status: 422 }) : ANALISIS(),
      [`PUT /diagnosticos/${ID}`]: () => Response.json(diagnostico()),
    });
    await preparar();
    validar();
    expect(await screen.findByText("Guarda el archivo como .xlsx.")).toBeInTheDocument();
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
    expect(await screen.findByText(/diagnóstico no respondió/i)).toBeInTheDocument();
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
      [`PUT /diagnosticos/${ID}`]: () => Response.json({ code: "DIAGNOSTICO_NO_EDITABLE", error: "El diagnóstico ya no se puede editar." }, { status: 409 }),
    });
    await preparar();
    validar();
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await screen.findByText("El diagnóstico ya no se puede editar.")).toBeInTheDocument();
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
    await waitFor(() => expect(nav.push).toHaveBeenCalledWith(`/diagnosticos/${ID}`));
    expect(llamadas(api, "POST", `/diagnosticos/${ID}/vehiculos`)).toHaveLength(2);
  });
});
