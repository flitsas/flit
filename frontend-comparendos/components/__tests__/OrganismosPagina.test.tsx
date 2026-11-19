import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import Pagina from "@/app/(app)/admin/organismos/page";
import { ToastProvider } from "@/components/Toast";
import { ApiError } from "@/lib/api";
import { avisoAdmiteSinCorreo, cambiosOrganismo } from "@/lib/organismos";

// HU #14611: interruptor «Admite derechos de petición por correo» del organismo. API simulada por ruta.
const apiFetch = vi.fn();
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), apiFetch: (...a: unknown[]) => apiFetch(...a) }));
vi.mock("@/components/ComparendosShell", () => ({
  useSesion: () => ({ sesion: { usuario: { esSuperAdmin: true } }, cargando: false, error: null }),
}));

const base = { id: "o1", codigo: "11001", nombre: "Secretaría de Movilidad", municipio: "Bogotá", departamento: "Cundinamarca", permiteCambioContraventor: true, activoPlataforma: true };

function simular(ot: Record<string, unknown>, alPatch?: () => unknown) {
  apiFetch.mockImplementation(async (ruta: string, op?: { method?: string }) => {
    if (op?.method === "PATCH") return alPatch ? alPatch() : {};
    if (ruta === "/organismos") return { items: [{ ...base, ...ot }], total: 1 };
    throw new Error(`ruta sin simular: ${String(ruta)}`);
  });
}

async function abrirModal() {
  render(
    <ToastProvider>
      <Pagina />
    </ToastProvider>,
  );
  fireEvent.click(await screen.findByText("Secretaría de Movilidad"));
  const dialogo = await screen.findByRole("dialog");
  return { dialogo, interruptor: within(dialogo).getByRole("switch", { name: "Admite derechos de petición por correo" }) };
}

const patches = () => apiFetch.mock.calls.filter(([, op]) => op?.method === "PATCH");

describe("organismo: admite DP por correo (HU #14611)", () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });
  afterEach(cleanup);

  it("nace apagado cuando el organismo no trae el campo, y explica para qué sirve", async () => {
    simular({ correosRadicacion: ["radicacion@movilidad.gov.co"] });
    const { dialogo, interruptor } = await abrirModal();
    expect(interruptor.getAttribute("aria-checked")).toBe("false");
    expect(within(dialogo).getByText(/Actívalo solo si esta secretaría recibe peticiones en ese correo/)).toBeTruthy();
  });

  it("el PATCH manda solo admiteDpPorCorreo cuando es lo único que cambió", async () => {
    simular({ correosRadicacion: ["radicacion@movilidad.gov.co"], admiteDpPorCorreo: false });
    const { dialogo, interruptor } = await abrirModal();
    fireEvent.click(interruptor);
    fireEvent.click(within(dialogo).getByRole("button", { name: "Guardar" }));
    await waitFor(() => expect(patches()).toHaveLength(1));
    expect(patches()[0][0]).toBe("/admin/organismos/o1");
    expect(patches()[0][1].body).toEqual({ admiteDpPorCorreo: true });
  });

  it("sin correo de radicación, marcarlo muestra el problema (no lo esconde)", async () => {
    simular({ correosRadicacion: [] });
    const { dialogo, interruptor } = await abrirModal();
    expect(within(dialogo).queryByText(/la va a omitir/)).toBeNull();
    fireEvent.click(interruptor);
    expect(within(dialogo).getByText(/no tiene correo de radicación/)).toBeTruthy();
  });

  it("un error del PATCH se muestra en español", async () => {
    simular({ correosRadicacion: ["a@x.co"] }, () => {
      throw new ApiError(422, "No se pudo guardar el organismo: revisa los datos.", "ORGANISMO_INVALIDO");
    });
    const { dialogo, interruptor } = await abrirModal();
    fireEvent.click(interruptor);
    fireEvent.click(within(dialogo).getByRole("button", { name: "Guardar" }));
    await within(dialogo).findByText("No se pudo guardar el organismo: revisa los datos.");
  });
});

describe("lib/organismos (HU #14611)", () => {
  const ot = { ...base, correosRadicacion: ["a@x.co"] };
  it("manda solo lo que cambió", () => {
    expect(cambiosOrganismo(ot, { correos: "a@x.co", permiteCambioContraventor: true, admiteDpPorCorreo: false })).toEqual({});
    expect(cambiosOrganismo(ot, { correos: "a@x.co\nb@x.co", permiteCambioContraventor: false, admiteDpPorCorreo: true })).toEqual({
      correosRadicacion: ["a@x.co", "b@x.co"],
      permiteCambioContraventor: false,
      admiteDpPorCorreo: true,
    });
  });
  it("avisa solo si admite y no hay correos", () => {
    expect(avisoAdmiteSinCorreo({ correos: "  ", admiteDpPorCorreo: true })).toMatch(/omitir/);
    expect(avisoAdmiteSinCorreo({ correos: "", admiteDpPorCorreo: false })).toBeNull();
    expect(avisoAdmiteSinCorreo({ correos: "a@x.co", admiteDpPorCorreo: true })).toBeNull();
  });
});
