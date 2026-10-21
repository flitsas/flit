import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { PanelExpress } from "@/components/express/PanelExpress";
import { FECHA, ID_EXPRESS, configuracion, creadoOk, cuerpoDe, llamadas, simularApi, tiposOk } from "./datos";

// Uso de ejemplo: <PanelExpress identificador="ABC123" onErrorIdentificador={(m) => ...} /> pide GET /configuracion,
// abre la parametrización compacta con «Express ABC123 21/10/2026», el documento opcional del locatario y, al lanzar,
// POST /express; con el 201 navega a /diagnosticos/{id}.

const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/", useRouter: () => router }));

beforeEach(() => {
  router.push.mockReset();
  router.replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

type Rutas = Parameters<typeof simularApi>[0];

function montar(rutas: Rutas = {}, estimado: "NO_MOSTRAR" | "ANTES_DE_LANZAR" = "NO_MOSTRAR") {
  const api = simularApi({
    "GET /configuracion": () => Response.json(configuracion({ operacion: { estimado_consultas: estimado } })),
    "GET /tipos-tramite": tiposOk,
    "POST /express": creadoOk,
    ...rutas,
  });
  const onErrorIdentificador = vi.fn();
  render(<PanelExpress identificador="ABC123" fecha={FECHA} onErrorIdentificador={onErrorIdentificador} />);
  return { api, onErrorIdentificador };
}

const nombre = () => screen.findByRole("textbox", { name: "Nombre del diagnóstico" });
const documento = () => screen.getByRole("textbox", { name: /número de documento del locatario/i });
const tipoDocumento = () => screen.getByRole("combobox", { name: /tipo de documento del locatario/i });
const continuar = () => fireEvent.click(screen.getByRole("button", { name: "Continuar" }));

describe("PanelExpress — estados de la configuración", () => {
  it("cargando: muestra el loader mientras llega la configuración", () => {
    simularApi({ "GET /configuracion": () => new Promise<Response>(() => {}) });
    render(<PanelExpress identificador="ABC123" fecha={FECHA} onErrorIdentificador={vi.fn()} />);
    expect(screen.getByRole("status")).toHaveAccessibleName(/cargando/i);
  });

  it("error: la configuración falla y Reintentar la vuelve a pedir", async () => {
    let veces = 0;
    simularApi({
      "GET /configuracion": () => (++veces === 1 ? Response.json({ code: "ERROR" }, { status: 500 }) : Response.json(configuracion())),
      "GET /tipos-tramite": tiposOk,
    });
    render(<PanelExpress identificador="ABC123" fecha={FECHA} onErrorIdentificador={vi.fn()} />);
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await nombre()).toHaveValue("Express ABC123 21/10/2026");
  });
});

describe("PanelExpress — nombre autogenerado editable (AC 4)", () => {
  it("abre con el nombre autogenerado", async () => {
    montar();
    expect(await nombre()).toHaveValue("Express ABC123 21/10/2026");
  });

  it("con el nombre borrado no lanza; al escribir uno lanza con ese nombre", async () => {
    const { api } = montar();
    fireEvent.change(await nombre(), { target: { value: "" } });
    continuar();
    expect(await nombre()).toHaveAttribute("aria-invalid", "true");
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);

    fireEvent.change(await nombre(), { target: { value: "Revisión flota" } });
    continuar();
    await waitFor(() => expect(llamadas(api, "POST", "/express")).toHaveLength(1));
    expect(cuerpoDe(llamadas(api, "POST", "/express")[0])).toMatchObject({ nombre: "Revisión flota", identificador: "ABC123" });
  });
});

describe("PanelExpress — locatario opcional (AC 5)", () => {
  it("el documento es opcional y lleva el rótulo «Declarado por el cliente»", async () => {
    const { api } = montar();
    await nombre();
    expect(documento()).not.toBeRequired();
    expect(screen.getByText(/declarado por el cliente/i)).toBeInTheDocument();
    continuar();
    await waitFor(() => expect(llamadas(api, "POST", "/express")).toHaveLength(1));
    expect(cuerpoDe(llamadas(api, "POST", "/express")[0])).toMatchObject({ tipo_documento_locatario: null, documento_locatario: null });
  });

  it("tras escribirlo se enmascara, pero viaja completo en la petición y no queda en el almacenamiento ni en la consola", async () => {
    const log = vi.spyOn(console, "log");
    const { api } = montar();
    await nombre();
    fireEvent.change(tipoDocumento(), { target: { value: "CC" } });
    fireEvent.focus(documento());
    fireEvent.change(documento(), { target: { value: "12345678" } });
    fireEvent.blur(documento());
    expect(documento()).toHaveValue("****5678");
    continuar();
    await waitFor(() => expect(llamadas(api, "POST", "/express")).toHaveLength(1));
    expect(cuerpoDe(llamadas(api, "POST", "/express")[0])).toMatchObject({ tipo_documento_locatario: "CC", documento_locatario: "12345678" });
    const guardado = JSON.stringify({ ...localStorage }) + JSON.stringify({ ...sessionStorage });
    expect(guardado).not.toContain("12345678");
    expect(log.mock.calls.flat().join(" ")).not.toContain("12345678");
    log.mockRestore();
  });

  it("documento sin tipo: pide el tipo y no lanza", async () => {
    const { api } = montar();
    await nombre();
    fireEvent.change(documento(), { target: { value: "12345678" } });
    continuar();
    expect(await screen.findByText(/elige el tipo de documento/i)).toBeInTheDocument();
    expect(tipoDocumento()).toHaveAttribute("aria-invalid", "true");
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("un 422 del documento se pinta en su campo", async () => {
    montar({
      "POST /express": () =>
        Response.json({ code: "DOCUMENTO_INVALIDO", error: "El documento no es válido.", campo: "documento_locatario" }, { status: 422 }),
    });
    await nombre();
    fireEvent.change(tipoDocumento(), { target: { value: "CC" } });
    fireEvent.change(documento(), { target: { value: "12" } });
    continuar();
    expect(await screen.findByText("El documento no es válido.")).toBeInTheDocument();
    expect(documento()).toHaveAttribute("aria-invalid", "true");
    expect(router.push).not.toHaveBeenCalled();
  });
});

describe("PanelExpress — estimado antes de lanzar (AC 6)", () => {
  it("con ANTES_DE_LANZAR pide el estimado, lo muestra por fuente y lanza al confirmar", async () => {
    const { api } = montar(
      {
        "POST /express/estimado": () =>
          Response.json({
            mostrar_estimado: true,
            estimado_consultas: [
              { fuente: "RUNT", consultas: 1 },
              { fuente: "SIMIT_VEHICULO", consultas: 1 },
            ],
          }),
      },
      "ANTES_DE_LANZAR",
    );
    await nombre();
    continuar();
    const estimado = await screen.findByRole("region", { name: "Estimado de consultas" });
    expect(estimado).toHaveTextContent("RUNT vehículo");
    expect(estimado).toHaveTextContent("SIMIT vehículo");
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "Lanzar diagnóstico" }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith(`/diagnosticos/${ID_EXPRESS}`));
  });

  it("sin ANTES_DE_LANZAR no pide el estimado y lanza directo", async () => {
    const { api } = montar();
    await nombre();
    continuar();
    await waitFor(() => expect(router.push).toHaveBeenCalledWith(`/diagnosticos/${ID_EXPRESS}`));
    expect(llamadas(api, "POST", "/express/estimado")).toHaveLength(0);
    expect(screen.queryByRole("region", { name: "Estimado de consultas" })).not.toBeInTheDocument();
  });
});

describe("PanelExpress — lanzamiento y errores (AC 1)", () => {
  it("201: navega a /diagnosticos/{id} con la cabecera y el identificador", async () => {
    const { api } = montar();
    await nombre();
    continuar();
    await waitFor(() => expect(router.push).toHaveBeenCalledWith(`/diagnosticos/${ID_EXPRESS}`));
    expect(cuerpoDe(llamadas(api, "POST", "/express")[0])).toEqual({
      nombre: "Express ABC123 21/10/2026",
      modo: "SIMPLE",
      tipo_tramite_codigo: null,
      alcance: { por_vehiculo: ["RUNT"], por_propietario: [], por_locatario: [] },
      identificador: "ABC123",
      tipo_documento_locatario: null,
      documento_locatario: null,
    });
  });

  it("422 del identificador: lo entrega a la captura y no navega", async () => {
    const { onErrorIdentificador } = montar({
      "POST /express": () =>
        Response.json({ code: "IDENTIFICADOR_INVALIDO", error: "No es una placa ni un VIN.", campo: "identificador" }, { status: 422 }),
    });
    await nombre();
    continuar();
    await waitFor(() => expect(onErrorIdentificador).toHaveBeenCalledWith("No es una placa ni un VIN."));
    expect(router.push).not.toHaveBeenCalled();
  });

  it("500: error genérico con Reintentar, que vuelve a lanzar", async () => {
    let veces = 0;
    const { api } = montar({ "POST /express": () => (++veces === 1 ? Response.json({}, { status: 500 }) : creadoOk()) });
    await nombre();
    continuar();
    expect(await screen.findByText(/diagnóstico no respondió/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith(`/diagnosticos/${ID_EXPRESS}`));
    expect(llamadas(api, "POST", "/express")).toHaveLength(2);
  });

  it("403 de sesión lleva a /403 con su código", async () => {
    montar({ "POST /express": () => Response.json({ code: "PRODUCTO_NO_HABILITADO", error: "No habilitado" }, { status: 403 }) });
    await nombre();
    continuar();
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith("/403?code=PRODUCTO_NO_HABILITADO"));
  });

  it("403 sin código (sin permiso de crear): explica el permiso y no navega", async () => {
    montar({ "POST /express": () => new Response(null, { status: 403 }) });
    await nombre();
    continuar();
    expect(await screen.findByText(/no tienes permiso/i)).toBeInTheDocument();
    expect(router.replace).not.toHaveBeenCalled();
    expect(router.push).not.toHaveBeenCalled();
  });
});
