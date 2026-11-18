import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Pagina from "@/app/(app)/admin/envio-automatico-dp/page";
import { ToastProvider } from "@/components/Toast";
import { ApiError } from "@/lib/api";

// Pantalla del SuperAdmin que enciende el envío automático de documentos legales (Epic #14606, HU #14614). La API se
// simula por ruta; lo que se comprueba es lo que la pantalla manda y lo que muestra.
const apiFetch = vi.fn();

vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), apiFetch: (...a: unknown[]) => apiFetch(...a) }));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion: { usuario: { esSuperAdmin: true } }, cargando: false, error: null }),
}));

interface Opciones {
  modulo?: boolean;
  aprobada?: boolean;
  automatizable?: boolean;
  alPut?: (ruta: string, cuerpo: unknown) => unknown;
}

function simular({ modulo = true, aprobada = false, automatizable = false, alPut }: Opciones = {}) {
  apiFetch.mockImplementation(async (ruta: string, op?: { method?: string; body?: unknown }) => {
    if (op?.method === "PUT" || op?.method === "POST") return alPut ? alPut(ruta, op.body) : {};
    if (ruta === "/admin/empresas") return [{ id: "e1", nombre: "Empresa Uno" }];
    if (ruta === "/admin/empresas/e1/configuracion")
      return { empresaId: "e1", hasRightPetitionModule: modulo, conjuntoPlantillas: "Flit", dpAutomaticoActivo: false, dpAutomaticoTopeDiario: 50 };
    if (ruta === "/admin/tipologias")
      return [
        { id: "t1", codigo: "DPR", nombre: "Prescripción", habilitada: true, automatizable },
        { id: "t9", codigo: "OTRA", nombre: "No automática", habilitada: true, automatizable: false },
      ];
    if (ruta === "/plantillas") return [{ id: "p1", tipologiaId: "t1", publicada: { id: "v1", numeroVersion: 2, publicadaEn: "2026-11-01T10:00:00Z" } }];
    if (ruta === "/plantillas/versiones/v1") return { id: "v1", numeroVersion: 2, publicadaEn: "2026-11-01T10:00:00Z", revisadaJuridicamenteEn: aprobada ? "2026-11-02T10:00:00Z" : null };
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

const puts = () => apiFetch.mock.calls.filter(([, op]) => op?.method === "PUT" || op?.method === "POST");

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
    fireEvent.click(screen.getByRole("button", { name: "Enviar sola" }));
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
    fireEvent.click(screen.getByRole("button", { name: "Aprobar texto" }));
    const dialogo = await screen.findByRole("dialog");
    const confirmar = within(dialogo).getByRole("button", { name: "Aprobar texto" }) as HTMLButtonElement;
    expect(confirmar.disabled).toBe(true);
    const nota = within(dialogo).getByLabelText(/Nota de la revisión/);
    fireEvent.change(nota, { target: { value: "   " } });
    expect(confirmar.disabled).toBe(true);
    expect(puts()).toHaveLength(0);
    fireEvent.change(nota, { target: { value: "Revisado por el abogado." } });
    expect(confirmar.disabled).toBe(false);
    fireEvent.click(confirmar);
    await waitFor(() => expect(puts()).toHaveLength(1));
    expect(puts()[0][0]).toBe("/admin/plantillas/versiones/v1/revision-juridica");
    expect(puts()[0][1]).toMatchObject({ method: "POST", body: { nota: "Revisado por el abogado." } });
  });
});
