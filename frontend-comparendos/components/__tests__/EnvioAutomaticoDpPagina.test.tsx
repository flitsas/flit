import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Pagina from "@/app/(app)/admin/envio-automatico-dp/page";
import { ToastProvider } from "@/components/Toast";
import { ApiError } from "@/lib/api";

// Pantalla del SuperAdmin que enciende el envío automático de documentos legales (Epic #14606, HU #14614). La API se
// simula por ruta; lo que se comprueba es lo que la pantalla manda y lo que muestra.
const apiFetch = vi.fn();
const apiPostBlob = vi.fn();
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof import("@/lib/api")>()),
  apiFetch: (...a: unknown[]) => apiFetch(...a),
  apiPostBlob: (...a: unknown[]) => apiPostBlob(...a),
}));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion: { usuario: { esSuperAdmin: true } }, cargando: false, error: null }),
}));

interface Opciones {
  modulo?: boolean;
  aprobada?: boolean;
  automatizable?: boolean;
  habilitada?: boolean;
  alPut?: (ruta: string, cuerpo: unknown) => unknown;
}

function simular({ modulo = true, aprobada = false, automatizable = false, habilitada = true, alPut }: Opciones = {}) {
  apiPostBlob.mockResolvedValue(new Blob(["%PDF"], { type: "application/pdf" }));
  apiFetch.mockImplementation(async (ruta: string, op?: { method?: string; body?: unknown; query?: { tipo?: string } }) => {
    if (ruta === "/plantillas/vista-previa") return { asunto: "Derecho de petición CAD-1", html: "<p>Cuerpo del correo de ejemplo</p>", desconocidas: [] };
    if (op?.method === "PUT" || op?.method === "POST") return alPut ? alPut(ruta, op.body) : {};
    if (ruta === "/admin/empresas") return [{ id: "e1", nombre: "Empresa Uno" }];
    if (ruta === "/admin/empresas/e1/configuracion")
      return { empresaId: "e1", hasRightPetitionModule: modulo, conjuntoPlantillas: "Flit", dpAutomaticoActivo: false, dpAutomaticoTopeDiario: 50 };
    if (ruta === "/admin/tipologias")
      return [
        { id: "t1", codigo: "DPR", nombre: "Prescripción", habilitada, automatizable },
        { id: "t9", codigo: "OTRA", nombre: "No automática", habilitada: true, automatizable: false },
      ];
    if (ruta === "/plantillas" && op?.query?.tipo === "CorreoDpOt") return [{ id: "pc", tipologiaId: null, publicada: { id: "vc", numeroVersion: 1, publicadaEn: "2026-11-01T10:00:00Z" } }];
    if (ruta === "/plantillas/versiones/vc") return { id: "vc", numeroVersion: 1, asunto: "Derecho de petición {{dp.radicado}}", cuerpoHtml: "<p>Hola</p>", publicadaEn: "2026-11-01T10:00:00Z" };
    if (ruta === "/plantillas") return [{ id: "p1", tipologiaId: "t1", publicada: { id: "v1", numeroVersion: 2, publicadaEn: "2026-11-01T10:00:00Z" } }];
    if (ruta === "/plantillas/versiones/v1") return { id: "v1", numeroVersion: 2, publicadaEn: "2026-11-01T10:00:00Z", cuerpoHtml: "<p>Texto del DP</p>", revisadaJuridicamenteEn: aprobada ? "2026-11-02T10:00:00Z" : null, revisadaPor: aprobada ? "Ana Abogada" : null, notaRevisionJuridica: aprobada ? "Texto conforme" : null };
    throw new Error(`ruta sin simular: ${String(ruta)}`);
  });
}

async function abrir() {
  render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
  fireEvent.change(await screen.findByLabelText("Empresa"), { target: { value: "e1" } });
  await screen.findByText("Envío automático de la empresa");
}

/** Abre el menú «Acciones» de la fila DPR (el kit lo pinta en un portal, fuera de la tabla). */
async function abrirMenu() {
  fireEvent.click(await screen.findByRole("button", { name: /^Acciones de DPR/ }));
  return screen.findByRole("menu");
}
const opcion = (menu: HTMLElement, nombre: string | RegExp) => within(menu).queryByRole("menuitem", { name: nombre });

const puts = () => apiFetch.mock.calls.filter(([ruta, op]) => ruta !== "/plantillas/vista-previa" && (op?.method === "PUT" || op?.method === "POST"));

URL.createObjectURL = () => "blob:vista-previa";
URL.revokeObjectURL = () => {};

describe("pantalla del envío automático de DP (HU #14614)", () => {
  // Con llaves: un beforeEach que devuelve el mock lo vuelve «hook de limpieza» y vitest lo llama sin argumentos.
  beforeEach(() => {
    apiFetch.mockReset();
  });
  afterEach(cleanup);

  it("(a) nace apagada y con el tope por defecto", async () => {
    simular();
    await abrir();
    const interruptor = screen.getByRole("switch", { name: "Apagado" });
    expect(interruptor.getAttribute("aria-checked")).toBe("false");
    expect((interruptor as HTMLButtonElement).disabled).toBe(false);
    expect((screen.getByLabelText(/Tope diario/) as HTMLInputElement).value).toBe("50");
    expect((screen.getByRole("button", { name: "Guardar" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("(a) sin el módulo de DP el interruptor está deshabilitado y se explica", async () => {
    simular({ modulo: false });
    await abrir();
    expect((screen.getByRole("switch", { name: "Apagado" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText(/no tiene el módulo de derechos de petición/)).toBeTruthy();
    expect((screen.getByRole("button", { name: "Guardar" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("(b) un tope fuera de 1–500 muestra el error y no se envía", async () => {
    simular();
    await abrir();
    for (const malo of ["0", "501", ""]) {
      fireEvent.change(screen.getByLabelText(/Tope diario/), { target: { value: malo } });
      expect(screen.getAllByText(/entre 1 y 500/).length).toBeGreaterThan(0);
      expect((screen.getByRole("button", { name: "Guardar" }) as HTMLButtonElement).disabled).toBe(true);
    }
    expect(puts()).toHaveLength(0);
  });

  it("un tope válido con el interruptor encendido manda activo y topeDiario", async () => {
    simular();
    await abrir();
    fireEvent.click(screen.getByRole("switch", { name: "Apagado" }));
    fireEvent.change(screen.getByLabelText(/Tope diario/), { target: { value: "80" } });
    fireEvent.click(screen.getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(puts()).toHaveLength(1));
    expect(puts()[0][0]).toBe("/admin/empresas/e1/envio-automatico-dp");
    expect(puts()[0][1]).toMatchObject({ method: "PUT", body: { activo: true, topeDiario: 80 } });
  });

  it("solo lista las tipologías con regla automática", async () => {
    simular();
    await abrir();
    expect(screen.getByText(/Prescripción/)).toBeTruthy();
    expect(screen.queryByText(/No automática/)).toBeNull();
  });

  it("(c) un 409 sin revisión jurídica muestra su mensaje en español y la tipología sigue a mano", async () => {
    simular({
      aprobada: true,
      alPut: () => {
        throw new ApiError(409, "texto crudo de la API", "TIPOLOGIA_SIN_REVISION_JURIDICA");
      },
    });
    await abrir();
    await screen.findByText("A mano");
    fireEvent.click(opcion(await abrirMenu(), "Activar envío automático")!);
    await screen.findByText(/todavía no está aprobado por el abogado/);
    expect(document.body.textContent).not.toContain("TIPOLOGIA_SIN_REVISION_JURIDICA");
    expect(document.body.textContent).not.toContain("texto crudo de la API");
    expect(screen.getByText("A mano")).toBeTruthy();
    expect(screen.queryByText("Automático")).toBeNull();
    expect(puts()[0][1]).toMatchObject({ body: { automatizable: true } });
  });

  it("(d) «Aprobar texto» no deja enviar sin nota y manda la nota al aprobar", async () => {
    simular();
    await abrir();
    await screen.findByText("Texto sin aprobar");
    fireEvent.click(opcion(await abrirMenu(), "Aprobar texto")!);
    const dialogo = await screen.findByRole("dialog");
    const confirmar = within(dialogo).getByRole("button", { name: "Aprobar texto" }) as HTMLButtonElement;
    expect(confirmar.disabled).toBe(true);
    const nota = within(dialogo).getByLabelText(/Nota de la revisión/);
    fireEvent.change(nota, { target: { value: "   " } });
    expect(confirmar.disabled).toBe(true);
    expect(puts()).toHaveLength(0);
    fireEvent.change(nota, { target: { value: "Revisado por el abogado." } });
    await waitFor(() => expect(confirmar.disabled).toBe(false));
    fireEvent.click(confirmar);
    await waitFor(() => expect(puts()).toHaveLength(1));
    expect(puts()[0][0]).toBe("/admin/plantillas/versiones/v1/revision-juridica");
    expect(puts()[0][1]).toMatchObject({ method: "POST", body: { nota: "Revisado por el abogado." } });
  });
});

describe("«Activar envío automático» explica su bloqueo y deja destrabarlo (HU #14617)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });
  afterEach(cleanup);

  it("AC1: tipología no habilitada → botón bloqueado con la razón visible", async () => {
    simular({ habilitada: false, aprobada: true });
    await abrir();
    const menu = await abrirMenu();
    const activar = opcion(menu, /^Activar envío automático/)!;
    expect(activar.getAttribute("aria-disabled")).toBe("true");
    expect(within(menu).getByText(/Primero habilítala para esta empresa/)).toBeTruthy();
    fireEvent.click(activar);
    expect(puts()).toHaveLength(0);
  });

  it("AC2: «Habilitar para esta empresa» llama al PUT existente con {habilitada:true} y refresca la fila", async () => {
    let habilitada = false;
    simular({ habilitada: false, aprobada: true, alPut: () => ((habilitada = true), {}) });
    const base = apiFetch.getMockImplementation()!;
    apiFetch.mockImplementation(async (ruta: string, op?: { method?: string }) => {
      const r = await base(ruta, op);
      return ruta === "/admin/tipologias" ? r.map((t: { id: string }) => (t.id === "t1" ? { ...t, habilitada } : t)) : r;
    });
    await abrir();
    fireEvent.click(opcion(await abrirMenu(), "Habilitar para esta empresa")!);
    await waitFor(() => expect(puts()).toHaveLength(1));
    expect(puts()[0][0]).toBe("/admin/empresas/e1/tipologias/t1");
    expect(puts()[0][1]).toMatchObject({ method: "PUT", body: { habilitada: true } });
    await waitFor(async () => {
      const menu = await abrirMenu();
      expect(opcion(menu, "Activar envío automático")?.getAttribute("aria-disabled")).toBeNull();
      expect(opcion(menu, "Habilitar para esta empresa")).toBeNull();
    });
  });

  it("AC3: habilitada pero sin texto aprobado → «Primero aprueba el texto» y el botón sigue bloqueado", async () => {
    simular({ habilitada: true, aprobada: false });
    await abrir();
    const menu = await abrirMenu();
    expect(within(menu).getByText(/Primero aprueba el texto/)).toBeTruthy();
    expect(opcion(menu, /^Activar envío automático/)!.getAttribute("aria-disabled")).toBe("true");
    expect(opcion(menu, "Habilitar para esta empresa")).toBeNull();
    expect(opcion(menu, "Aprobar texto")!.getAttribute("aria-disabled")).toBeNull();
  });

  it("AC4: nombres sin ambigüedad y errores del contrato en español", async () => {
    simular({
      habilitada: false,
      aprobada: true,
      alPut: () => {
        throw new ApiError(409, "crudo", "TIPOLOGIA_INACTIVA");
      },
    });
    await abrir();
    fireEvent.click(opcion(await abrirMenu(), "Habilitar para esta empresa")!);
    await screen.findByText(/desactivada en el catálogo/);
    expect(document.body.textContent).not.toContain("TIPOLOGIA_INACTIVA");
    cleanup();
    apiFetch.mockReset();
    simular({ automatizable: true, aprobada: true });
    await abrir();
    const menu = await abrirMenu();
    expect(opcion(menu, "Volver a enviar a mano")).toBeTruthy();
    expect(opcion(menu, /Enviar sola/)).toBeNull();
  });
});

describe("«Aprobar texto» deja ver el documento y el correo, y registra quién aprobó (HU #14616)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
    apiPostBlob.mockReset();
  });
  afterEach(cleanup);

  async function abrirAprobar() {
    await abrir();
    await screen.findByText("Texto sin aprobar");
    fireEvent.click(opcion(await abrirMenu(), "Aprobar texto")!);
    return screen.findByRole("dialog");
  }

  it("AC1: muestra el documento en PDF y el correo (asunto y cuerpo) y dice que no se envía nada", async () => {
    simular();
    const dialogo = await abrirAprobar();
    await waitFor(() => expect(within(dialogo).getByTitle("Vista previa del documento")).toBeTruthy());
    const correo = within(dialogo).getByTitle("Vista previa del correo") as HTMLIFrameElement;
    expect(within(dialogo).getByText(/Derecho de petición CAD-1/)).toBeTruthy();
    expect(correo.getAttribute("srcdoc")).toContain("Cuerpo del correo de ejemplo");
    expect(within(dialogo).getByText(/no se envía nada a nadie/)).toBeTruthy();
    const llamadas = apiFetch.mock.calls.filter(([r]) => r === "/plantillas/vista-previa").map(([, op]) => op.body);
    expect(llamadas[0]).toMatchObject({ tipo: "CorreoDpOt", comparendoId: null, formato: "html" });
    expect(apiPostBlob.mock.calls[0][1]).toMatchObject({ tipo: "DpDocumento", comparendoId: null, formato: "pdf" });
  });

  it("AC2: el correo va aislado en un iframe con sandbox sin permisos (nunca HTML crudo en la página)", async () => {
    simular();
    const dialogo = await abrirAprobar();
    const correo = (await within(dialogo).findByTitle("Vista previa del correo")) as HTMLIFrameElement;
    expect(correo.getAttribute("sandbox")).toBe("");
    expect(within(dialogo).queryByText("Cuerpo del correo de ejemplo")).toBeNull();
  });

  it("AC3: «Aprobar texto» sigue bloqueado con la razón visible mientras carga o si la vista previa falla", async () => {
    simular();
    apiPostBlob.mockImplementation(() => new Promise(() => {}));
    let dialogo = await abrirAprobar();
    fireEvent.change(within(dialogo).getByLabelText(/Nota de la revisión/), { target: { value: "Revisado." } });
    expect(within(dialogo).getByText(/Espera a que se arme la vista previa/)).toBeTruthy();
    expect((within(dialogo).getByRole("button", { name: "Aprobar texto" }) as HTMLButtonElement).disabled).toBe(true);
    cleanup();
    simular();
    apiPostBlob.mockRejectedValue(new Error("No se pudo armar el documento."));
    dialogo = await abrirAprobar();
    fireEvent.change(within(dialogo).getByLabelText(/Nota de la revisión/), { target: { value: "Revisado." } });
    await within(dialogo).findByText(/No se pudo armar la vista previa/);
    expect((within(dialogo).getByRole("button", { name: "Aprobar texto" }) as HTMLButtonElement).disabled).toBe(true);
    expect(puts()).toHaveLength(0);
  });

  it("AC4: la fila muestra quién aprobó, cuándo y la nota", async () => {
    simular({ aprobada: true });
    await abrir();
    await screen.findByText("Texto aprobado");
    expect(screen.getByText(/Aprobado por Ana Abogada el 02 de nov de 2026 · «Texto conforme»/)).toBeTruthy();
  });
});

describe("acciones en un solo menú y bloque de envío en una fila (HU #14620)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });
  afterEach(cleanup);

  it("AC1: la fila tiene UN solo botón «Acciones» y no botones sueltos de acción", async () => {
    simular();
    await abrir();
    await screen.findByText("Texto sin aprobar");
    expect(screen.getAllByRole("button", { name: /^Acciones de / })).toHaveLength(1);
    for (const n of ["Aprobar texto", "Habilitar para esta empresa", "Activar envío automático"]) expect(screen.queryByRole("button", { name: n })).toBeNull();
  });

  it("AC2: el menú trae las opciones según el estado y cada bloqueada dice su razón", async () => {
    simular({ habilitada: false });
    await abrir();
    const menu = await abrirMenu();
    expect(opcion(menu, "Aprobar texto")).toBeTruthy();
    expect(opcion(menu, "Habilitar para esta empresa")).toBeTruthy();
    expect(within(menu).getByText(/Activar envío automático · Primero habilítala para esta empresa/)).toBeTruthy();
  });

  it("AC3: el menú se abre y se cierra con el teclado (Escape devuelve el foco al botón)", async () => {
    simular();
    await abrir();
    const boton = await screen.findByRole("button", { name: /^Acciones de DPR/ });
    fireEvent.click(boton);
    await screen.findByRole("menu");
    expect(boton.getAttribute("aria-expanded")).toBe("true");
    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("menu")).toBeNull());
    expect(document.activeElement).toBe(boton);
  });

  it("AC4: interruptor, tope y Guardar comparten una fila que se apila en pantallas chicas", async () => {
    simular();
    await abrir();
    const interruptor = screen.getByRole("switch", { name: "Apagado" });
    const fila = interruptor.closest("div.flex-col")!;
    expect(fila.className).toContain("md:flex-row");
    expect(fila.contains(screen.getByLabelText(/Tope diario/))).toBe(true);
    expect(fila.contains(screen.getByRole("button", { name: "Guardar" }))).toBe(true);
  });
});
