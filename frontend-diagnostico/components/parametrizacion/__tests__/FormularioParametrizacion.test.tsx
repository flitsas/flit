import { afterEach, describe, expect, it } from "vitest";
import { vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { FormularioParametrizacion } from "@/components/parametrizacion/FormularioParametrizacion";
import { ID, TIPOS, configuracion, cuerpoDe, diagnostico, llamadas, simularApi, tiposOk } from "./datos";

// Uso de ejemplo: <FormularioParametrizacion configuracion={config} onGuardado={(d) => ...} /> pide GET /tipos-tramite,
// valida en pantalla y crea la cabecera con POST /diagnosticos (las siguientes ediciones van por PUT /diagnosticos/{id}).

afterEach(() => vi.unstubAllGlobals());

const creado = () => Response.json(diagnostico(), { status: 201 });

function montar(config = configuracion()) {
  return render(<FormularioParametrizacion configuracion={config} />);
}

const nombre = () => screen.getByRole("textbox", { name: "Nombre del diagnóstico" });
const crear = () => fireEvent.click(screen.getByRole("button", { name: "Crear diagnóstico" }));

async function elegirConAnalisis() {
  fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));
  return screen.findByRole("combobox", { name: /tipo de trámite/i });
}

describe("FormularioParametrizacion — nombre obligatorio (AC 1)", () => {
  it("con el nombre vacío muestra el error en el campo y no envía la petición", async () => {
    const api = simularApi({ "GET /tipos-tramite": tiposOk, "POST /diagnosticos": creado });
    montar();

    crear();

    const campo = nombre();
    expect(campo).toHaveAttribute("aria-invalid", "true");
    expect(campo).toHaveAccessibleDescription(/escribe el nombre del diagnóstico/i);
    expect(campo).toHaveFocus();
    await waitFor(() => expect(llamadas(api, "GET", "/tipos-tramite")).toHaveLength(1));
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);
  });

  it("un nombre de solo espacios cuenta como vacío", () => {
    const api = simularApi({ "GET /tipos-tramite": tiposOk, "POST /diagnosticos": creado });
    montar();
    fireEvent.change(nombre(), { target: { value: "   " } });
    crear();
    expect(nombre()).toHaveAttribute("aria-invalid", "true");
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);
  });

  it("un nombre de 151 caracteres bloquea el envío", () => {
    const api = simularApi({ "GET /tipos-tramite": tiposOk, "POST /diagnosticos": creado });
    montar();
    fireEvent.change(nombre(), { target: { value: "x".repeat(151) } });
    crear();
    expect(nombre()).toHaveAccessibleDescription(/hasta 150 caracteres/i);
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);
  });

  it("con 150 caracteres en Simple envía la cabecera con RUNT y las fuentes por defecto", async () => {
    const onGuardado = vi.fn();
    const api = simularApi({ "GET /tipos-tramite": tiposOk, "POST /diagnosticos": creado });
    render(<FormularioParametrizacion configuracion={configuracion({ operacion: { fuentes_por_defecto: ["RUNT", "AVALUO"] } })} onGuardado={onGuardado} />);
    fireEvent.change(nombre(), { target: { value: "y".repeat(150) } });
    crear();

    await screen.findByText(/diagnóstico creado/i);
    const [post] = llamadas(api, "POST", "/diagnosticos");
    expect(cuerpoDe(post)).toEqual({
      nombre: "y".repeat(150),
      modo: "SIMPLE",
      tipo_tramite_codigo: null,
      alcance: { por_vehiculo: ["RUNT", "AVALUO"], por_propietario: [], por_locatario: [] },
    });
    expect(onGuardado).toHaveBeenCalledWith(expect.objectContaining({ id: ID }));
  });
});

describe("FormularioParametrizacion — errores 422 por campo", () => {
  it("el 422 del nombre se pinta en el campo nombre", async () => {
    simularApi({
      "GET /tipos-tramite": tiposOk,
      "POST /diagnosticos": () => Response.json({ code: "NOMBRE_INVALIDO", error: "El nombre no es válido.", campo: "nombre" }, { status: 422 }),
    });
    montar();
    fireEvent.change(nombre(), { target: { value: "Flota norte" } });
    crear();

    await waitFor(() => expect(nombre()).toHaveAttribute("aria-invalid", "true"));
    expect(nombre()).toHaveAccessibleDescription("El nombre no es válido.");
    expect(nombre()).toHaveValue("Flota norte");
  });

  it("el 422 de un grupo del alcance se pinta en el alcance", async () => {
    simularApi({
      "GET /tipos-tramite": tiposOk,
      "POST /diagnosticos": () =>
        Response.json({ code: "FUENTE_NO_HABILITADA", error: "SIMIT vehículo no está habilitada.", campo: "alcance.por_vehiculo" }, { status: 422 }),
    });
    montar();
    fireEvent.change(nombre(), { target: { value: "Flota norte" } });
    crear();

    await waitFor(() => expect(screen.getByRole("group", { name: "Alcance" })).toHaveAccessibleDescription(/simit vehículo no está habilitada/i));
  });

  it("el 422 del tipo de trámite se pinta en el combobox", async () => {
    simularApi({
      "GET /tipos-tramite": tiposOk,
      "POST /diagnosticos": () =>
        Response.json({ code: "TIPO_TRAMITE_NO_DISPONIBLE", error: "El tipo ya no está disponible.", campo: "tipo_tramite_codigo" }, { status: 422 }),
    });
    montar();
    fireEvent.change(nombre(), { target: { value: "Flota norte" } });
    const combo = await elegirConAnalisis();
    fireEvent.change(combo, { target: { value: "TRASPASO_STANDARD" } });
    crear();

    await waitFor(() => expect(combo).toHaveAccessibleDescription("El tipo ya no está disponible."));
  });

  it("un 403 se muestra como aviso general sin perder lo digitado", async () => {
    simularApi({
      "GET /tipos-tramite": tiposOk,
      "POST /diagnosticos": () => new Response(null, { status: 403 }),
    });
    montar();
    fireEvent.change(nombre(), { target: { value: "Flota norte" } });
    crear();

    expect(await screen.findByText(/no tienes permiso para esta acción/i)).toBeInTheDocument();
    expect(nombre()).toHaveValue("Flota norte");
  });
});

describe("FormularioParametrizacion — modo y tipo (AC 4)", () => {
  it("con Con Análisis IA ofrece los tipos del catálogo y no guarda sin elegir uno", async () => {
    const api = simularApi({ "GET /tipos-tramite": tiposOk, "POST /diagnosticos": creado });
    montar();
    fireEvent.change(nombre(), { target: { value: "Flota norte" } });
    const combo = await elegirConAnalisis();
    expect(within(combo).getAllByRole("option").map((o) => o.getAttribute("value"))).toEqual(["", ...TIPOS.map((t) => t.codigo)]);

    crear();
    expect(combo).toHaveAttribute("aria-invalid", "true");
    expect(combo).toHaveAccessibleDescription(/elige el tipo de trámite/i);
    expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(0);

    fireEvent.change(combo, { target: { value: "TRASPASO_STANDARD" } });
    crear();
    await waitFor(() => expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(1));
    expect(cuerpoDe(llamadas(api, "POST", "/diagnosticos")[0])).toMatchObject({ modo: "CON_ANALISIS", tipo_tramite_codigo: "TRASPASO_STANDARD" });
  });

  it("el 503 del catálogo muestra el error y deja reintentar al vencer Retry-After", async () => {
    let caido = true;
    const api = simularApi({
      "GET /tipos-tramite": () =>
        caido
          ? Response.json({ code: "CATALOGO_NO_DISPONIBLE", error: "Catálogo caído" }, { status: 503, headers: { "Retry-After": "1" } })
          : Response.json({ tipos: TIPOS }),
    });
    montar();
    fireEvent.click(screen.getByRole("radio", { name: "Con Análisis IA" }));

    expect(await screen.findByText(/el catálogo de tipos de trámite no está disponible/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reintentar en 1 s" })).toBeDisabled();

    caido = false;
    const reintentar = await screen.findByRole("button", { name: "Reintentar" }, { timeout: 2500 });
    fireEvent.click(reintentar);
    expect(await screen.findByRole("combobox", { name: /tipo de trámite/i })).toBeInTheDocument();
    expect(llamadas(api, "GET", "/tipos-tramite")).toHaveLength(2);
  });
});

describe("FormularioParametrizacion — fuentes obligatorias del tipo (AC 5)", () => {
  it("al elegir un tipo con fuentes obligatorias quedan marcadas, bloqueadas y se envían", async () => {
    const api = simularApi({ "GET /tipos-tramite": tiposOk, "POST /diagnosticos": creado });
    montar();
    fireEvent.change(nombre(), { target: { value: "Flota norte" } });
    const simit = screen.getByRole("checkbox", { name: "SIMIT vehículo" });
    expect(simit).not.toBeChecked();

    const combo = await elegirConAnalisis();
    fireEvent.change(combo, { target: { value: "TRASPASO_UNILATERAL" } });

    expect(simit).toBeChecked();
    expect(simit).toHaveAttribute("aria-disabled", "true");
    expect(simit).toHaveAccessibleDescription(/obligatoria para el tipo de trámite/i);

    crear();
    await waitFor(() => expect(llamadas(api, "POST", "/diagnosticos")).toHaveLength(1));
    expect(cuerpoDe(llamadas(api, "POST", "/diagnosticos")[0])).toMatchObject({ alcance: { por_vehiculo: ["RUNT", "SIMIT_VEHICULO"] } });

    fireEvent.click(screen.getByRole("radio", { name: "Simple" }));
    expect(screen.getByRole("checkbox", { name: "SIMIT vehículo" })).not.toBeChecked();
  });
});

describe("FormularioParametrizacion — edición de la cabecera", () => {
  it("tras crear, guardar usa PUT /diagnosticos/{id} y un 409 avisa que ya no es editable", async () => {
    const api = simularApi({
      "GET /tipos-tramite": tiposOk,
      "POST /diagnosticos": creado,
      [`PUT /diagnosticos/${ID}`]: () =>
        Response.json({ code: "DIAGNOSTICO_NO_EDITABLE", error: "El diagnóstico ya no se puede editar." }, { status: 409 }),
    });
    montar();
    fireEvent.change(nombre(), { target: { value: "Flota norte" } });
    crear();
    await screen.findByText(/diagnóstico creado/i);

    fireEvent.change(nombre(), { target: { value: "Flota sur" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar cambios" }));

    expect(await screen.findByText("El diagnóstico ya no se puede editar.")).toBeInTheDocument();
    expect(cuerpoDe(llamadas(api, "PUT", `/diagnosticos/${ID}`)[0])).toMatchObject({ nombre: "Flota sur" });
  });
});
