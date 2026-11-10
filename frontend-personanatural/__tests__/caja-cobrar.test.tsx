// HU #14458 — Buscar el trámite, ver el desglose y confirmar el cobro (un test por criterio de aceptación).
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CobrarTramite } from "@/components/caja/CobrarTramite";
import { errorDePrefactura } from "@/components/caja/ConfirmarCobroForm";

const irAlLogin = vi.hoisted(() => vi.fn());
vi.mock("@/lib/caja/navegacion", () => ({ irAlLogin }));

const TRAMITE = {
  codigo: "PN-2026-000123",
  tipoTramite: { codigo: "TRASPASO", nombre: "Traspaso" },
  organismo: { codigo: "5631000", nombre: "Tránsito de Prueba" },
  lineas: [
    { concepto: "Derechos de traspaso", valorCop: 150000 },
    { concepto: "Servicio FLIT", valorCop: 35000 },
  ],
  totalCop: 185000,
  versionTarifa: "2026-1",
  venceEn: "2026-12-01T00:00:00Z",
};

const json = (cuerpo: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(cuerpo), { status, headers: { "content-type": "application/json", ...headers } });
const error = (code: string, message: string, status: number, extra: object = {}, headers: Record<string, string> = {}) => json({ code, message, ...extra }, status, headers);

let respuestas: Array<(url: string, init?: RequestInit) => Response>;
let fetchMock: ReturnType<typeof vi.fn>;

function montar() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <CobrarTramite />
    </QueryClientProvider>,
  );
}

const usuario = {
  type: async (el: HTMLElement, texto: string) => void fireEvent.change(el, { target: { value: texto } }),
  click: async (el: HTMLElement) => void fireEvent.click(el),
  upload: async (el: HTMLElement, archivo: File) => void fireEvent.change(el, { target: { files: [archivo] } }),
};

async function buscar(codigo = "PN-2026-000123") {
  await usuario.type(screen.getByLabelText("ID del trámite"), codigo);
  await usuario.click(screen.getByRole("button", { name: "Buscar" }));
  return usuario;
}

beforeEach(() => {
  respuestas = [];
  irAlLogin.mockClear();
  fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const siguiente = respuestas.shift();
    if (!siguiente) throw new Error(`fetch inesperado: ${url}`);
    return siguiente(url, init);
  });
  vi.stubGlobal("fetch", fetchMock);
});
afterEach(() => vi.unstubAllGlobals());

describe("Caja OT · buscar el trámite", () => {
  it("AC: el foco empieza en el campo del ID y se ve «Buscando el trámite…» mientras responde", async () => {
    let resolver!: (r: Response) => void;
    respuestas.push(() => undefined as unknown as Response);
    fetchMock.mockImplementationOnce(() => new Promise<Response>((r) => (resolver = r)));
    montar();
    expect(screen.getByLabelText("ID del trámite")).toHaveFocus();

    await buscar();
    expect(await screen.findByText("Buscando el trámite…")).toBeInTheDocument();
    resolver(json(TRAMITE));
    expect(await screen.findByText("Total a cobrar")).toBeInTheDocument();
  });

  it("AC: muestra el desglose y el total del servidor, sin campo editable de valor", async () => {
    respuestas.push(() => json(TRAMITE));
    montar();
    await buscar();

    expect(await screen.findByText("Derechos de traspaso")).toBeInTheDocument();
    expect(screen.getByTestId("total-caja")).toHaveTextContent("$ 185.000,00 COP");
    expect(screen.queryByLabelText(/valor|total/i)).not.toBeInTheDocument();
    expect(fetchMock.mock.calls[0][0]).toBe("/api/v1/persona-natural/caja/tramites/PN-2026-000123");
  });

  it("AC: un ID mal escrito dice «No encontramos ese trámite.» sin llamar al servidor", async () => {
    montar();
    await buscar("XYZ");
    expect(await screen.findByText("No encontramos ese trámite.")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["no_encontrado", 404, "No encontramos ese trámite."],
    ["otro_organismo", 403, "Este trámite no es de este organismo."],
    ["ya_pagado", 409, "Este trámite ya fue pagado."],
    ["cobro_vencido", 409, "El pago en caja de este trámite venció."],
  ])("AC: %s muestra su mensaje exacto", async (code, status, texto) => {
    respuestas.push(() => error(code, "x", status));
    montar();
    await buscar();
    expect(await screen.findByText(texto)).toBeInTheDocument();
    expect(screen.queryByText("Total a cobrar")).not.toBeInTheDocument();
  });

  it("AC: «ya pagado» es un aviso (amarillo) y no un error", async () => {
    respuestas.push(() => error("ya_pagado", "x", 409));
    montar();
    await buscar();
    const aviso = await screen.findByText("Este trámite ya fue pagado.");
    expect(aviso.closest("[role=status]")).toHaveClass("bg-warning");
  });

  it("AC: el bloqueo por intentos informa el tiempo de espera y deshabilita la búsqueda", async () => {
    respuestas.push(() => error("demasiados_intentos", "x", 429, {}, { "retry-after": "120" }));
    montar();
    await buscar();

    expect(await screen.findByText("Demasiadas búsquedas sin resultado. Espere 2 minutos.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Buscar" })).toBeDisabled();
  });

  it("AC: sin sesión (401) lleva al login", async () => {
    respuestas.push(() => error("sin_sesion", "x", 401));
    montar();
    await buscar();
    await waitFor(() => expect(irAlLogin).toHaveBeenCalled());
  });
});

describe("Caja OT · confirmar el cobro", () => {
  async function hastaElFormulario() {
    respuestas.push(() => json(TRAMITE));
    montar();
    await buscar();
    await screen.findByText("Total a cobrar");
    return usuario;
  }

  it("AC: sin elegir medio no cobra y pide elegirlo", async () => {
    await hastaElFormulario();
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));

    expect(await screen.findByText("Elija cómo se cobró.")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("AC: «Otro» exige el detalle", async () => {
    await hastaElFormulario();
    await usuario.click(screen.getByLabelText("Otro"));
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));

    expect(await screen.findByText("Escriba el detalle del medio.")).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("AC: confirma con medio, total visto e Idempotency-Key y muestra «Pago registrado.»", async () => {
    await hastaElFormulario();
    respuestas.push(() => json({ codigo: TRAMITE.codigo, valorCop: 185000, medio: "efectivo", pagadoEn: "2026-11-10T15:00:00Z", mensaje: "Pago registrado." }));
    await usuario.click(screen.getByLabelText("Efectivo"));
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));

    const aviso = await screen.findByText("Pago registrado.");
    expect(aviso.closest("[role=status]")).toHaveClass("bg-success");
    const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit];
    expect(url).toBe("/api/v1/persona-natural/caja/tramites/PN-2026-000123/confirmar");
    expect(init.method).toBe("POST");
    expect((init.headers as Record<string, string>)["Idempotency-Key"]).toMatch(/\S{8,}/);
    const form = init.body as FormData;
    expect(form.get("medio")).toBe("efectivo");
    expect(form.get("totalVistoCop")).toBe("185000");
    expect(form.has("prefactura")).toBe(false);
  });

  it("AC: adjunta la prefactura válida y manda el detalle cuando el medio es «Otro»", async () => {
    await hastaElFormulario();
    respuestas.push(() => json({ codigo: TRAMITE.codigo, valorCop: 185000, medio: "otro", medioDetalle: "Nequi", pagadoEn: "2026-11-10T15:00:00Z", mensaje: "Pago registrado." }));
    await usuario.click(screen.getByLabelText("Otro"));
    await usuario.type(screen.getByLabelText("¿Cuál?"), "Nequi");
    const archivo = new File(["%PDF"], "prefactura.pdf", { type: "application/pdf" });
    await usuario.upload(screen.getByLabelText(/Prefactura/), archivo);
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));

    await screen.findByText("Pago registrado.");
    const form = (fetchMock.mock.calls[1] as [string, RequestInit])[1].body as FormData;
    expect(form.get("medioDetalle")).toBe("Nequi");
    expect((form.get("prefactura") as File).name).toBe("prefactura.pdf");
  });

  it("AC: la prefactura de tipo o tamaño no permitido se rechaza en el cliente", async () => {
    expect(errorDePrefactura(new File(["x"], "a.exe", { type: "application/x-msdownload" }))).toBe("La prefactura debe ser PDF, JPG o PNG.");
    const grande = new File([new Uint8Array(5 * 1024 * 1024 + 1)], "a.pdf", { type: "application/pdf" });
    expect(errorDePrefactura(grande)).toBe("La prefactura debe pesar 5 MB o menos.");
    expect(errorDePrefactura(new File(["x"], "a.png", { type: "image/png" }))).toBeNull();

    await hastaElFormulario();
    const campo = screen.getByLabelText(/Prefactura/);
    fireEvent.change(campo, { target: { files: [new File(["x"], "a.exe", { type: "application/x-msdownload" })] } });
    expect(await screen.findByText("La prefactura debe ser PDF, JPG o PNG.")).toBeInTheDocument();
  });

  it("AC: si el total cambió muestra el nuevo valor y pide confirmar de nuevo con otra llave", async () => {
    await hastaElFormulario();
    respuestas.push(() => error("total_cambio", "x", 409, { totalActualCop: 190000 }));
    await usuario.click(screen.getByLabelText("Datáfono"));
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));

    expect(await screen.findByText("El total cambió. Revise el nuevo valor y confirme de nuevo.")).toBeInTheDocument();
    expect(screen.getByTestId("total-caja")).toHaveTextContent("$ 190.000,00 COP");

    respuestas.push(() => json({ codigo: TRAMITE.codigo, valorCop: 190000, medio: "datafono", pagadoEn: "2026-11-10T15:00:00Z", mensaje: "Pago registrado." }));
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    await screen.findByText("Pago registrado.");

    const [primera, segunda] = [1, 2].map((i) => fetchMock.mock.calls[i] as [string, RequestInit]);
    expect((segunda[1].body as FormData).get("totalVistoCop")).toBe("190000");
    expect((segunda[1].headers as Record<string, string>)["Idempotency-Key"]).not.toBe((primera[1].headers as Record<string, string>)["Idempotency-Key"]);
  });

  it("AC: tras un corte de red el reintento usa la misma Idempotency-Key", async () => {
    await hastaElFormulario();
    fetchMock.mockImplementationOnce(async () => {
      throw new TypeError("network");
    });
    await usuario.click(screen.getByLabelText("Efectivo"));
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    await screen.findByText("No pudimos completar la acción. Intente de nuevo.");

    respuestas.push(() => json({ codigo: TRAMITE.codigo, valorCop: 185000, medio: "efectivo", pagadoEn: "2026-11-10T15:00:00Z", mensaje: "Pago registrado." }));
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    await screen.findByText("Pago registrado.");

    const llaves = [1, 2].map((i) => ((fetchMock.mock.calls[i] as [string, RequestInit])[1].headers as Record<string, string>)["Idempotency-Key"]);
    expect(llaves[0]).toBe(llaves[1]);
  });

  it("AC: «Volver» regresa a la búsqueda", async () => {
    await hastaElFormulario();
    await usuario.click(screen.getByRole("button", { name: "Volver" }));
    expect(screen.getByLabelText("ID del trámite")).toBeInTheDocument();
  });

  it("AC: un 403 al confirmar muestra el motivo", async () => {
    await hastaElFormulario();
    respuestas.push(() => error("sin_permiso", "x", 403));
    await usuario.click(screen.getByLabelText("Efectivo"));
    await usuario.click(screen.getByRole("button", { name: "Confirmar cobro" }));
    expect(await screen.findByText("Su usuario no puede cobrar en este organismo.")).toBeInTheDocument();
  });
});
