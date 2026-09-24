// Pedido del usuario (2026-09-16) — "Configuración" del organismo, punto de entrada real para el
// modo Dashboard/QX, la ventana de revocatoria (HU #12569) y los feature flags operativos, que
// antes solo vivían en una ruta legacy sin enlace en ningún menú. `TramitesSuperSection` (y su
// suite `.revocation-window.test.tsx`) se retiraron en HU #12857 (Feature #12846) — este archivo
// pasa a cubrir también `parseRevocationWindowInput`, movida aquí porque este componente es ahora
// su única consumidora.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtConfiguracionSection, parseRevocationWindowInput } from "../OtConfiguracionSection";
import type { OtProfile } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: vi.fn(),
  updateOtProfile: vi.fn(),
  updateOtFeatureFlag: vi.fn(),
}));

import { fetchOtProfile, updateOtProfile, updateOtFeatureFlag } from "@/lib/api/admin-ot";

const OT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

const baseProfile: OtProfile = {
  operationMode: "dashboard",
  quipuxReadOnly: false,
  transitOfficeId: OT_ID,
  featureFlags: [],
  revocationWindowBusinessDays: null,
};

function renderSection() {
  return render(
    <ToastProvider>
      <OtConfiguracionSection transitOfficeId={OT_ID} />
    </ToastProvider>,
  );
}

// HU #12857 — movidas desde `TramitesSuperSection.revocation-window.test.tsx` (componente
// retirado): mismas 6 aserciones sobre la función pura, ahora exportada por este módulo.
describe("parseRevocationWindowInput — HU #12569", () => {
  it("vacío es válido y significa sin límite (null)", () => {
    expect(parseRevocationWindowInput("")).toEqual({ ok: true, value: null });
    expect(parseRevocationWindowInput("   ")).toEqual({ ok: true, value: null });
  });

  it("un entero positivo es válido", () => {
    expect(parseRevocationWindowInput("15")).toEqual({ ok: true, value: 15 });
  });

  it("rechaza valores negativos", () => {
    expect(parseRevocationWindowInput("-5").ok).toBe(false);
  });

  it("rechaza cero", () => {
    expect(parseRevocationWindowInput("0").ok).toBe(false);
  });

  it("rechaza valores no numéricos", () => {
    expect(parseRevocationWindowInput("abc").ok).toBe(false);
  });

  it("rechaza decimales", () => {
    expect(parseRevocationWindowInput("3.5").ok).toBe(false);
  });
});

describe("OtConfiguracionSection", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue(baseProfile);
  });

  it("inicializa el campo de ventana desde GET profile con el valor persistido", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue({ ...baseProfile, revocationWindowBusinessDays: 5 });
    renderSection();

    const input = await screen.findByLabelText(/Ventana de revocatoria \(días hábiles\)/i);
    expect(input).toHaveValue("5");
  });

  it("HU12856 (adicional) — edita y guarda un valor válido: PATCH /profile lleva el scope del SuperAdmin", async () => {
    const user = userEvent.setup();
    vi.mocked(updateOtProfile).mockResolvedValue({
      ...baseProfile,
      revocationWindowBusinessDays: 10,
    });
    renderSection();

    const input = await screen.findByLabelText(/Ventana de revocatoria \(días hábiles\)/i);
    await user.clear(input);
    await user.type(input, "10");
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));

    await waitFor(() => {
      expect(updateOtProfile).toHaveBeenCalledWith(
        { revocationWindowBusinessDays: 10 },
        { transitOfficeId: OT_ID },
      );
    });
    expect(await screen.findByText(/Ventana de revocatoria guardada: 10 día/i)).toBeInTheDocument();
  });

  it("valor inválido bloquea el guardado sin llamar al backend", async () => {
    const user = userEvent.setup();
    renderSection();

    const input = await screen.findByLabelText(/Ventana de revocatoria \(días hábiles\)/i);
    await user.clear(input);
    await user.type(input, "-3");
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/mayor a 0/i);
    expect(updateOtProfile).not.toHaveBeenCalled();
  });

  it("HU12856 (adicional) — muestra y permite alternar el modo Dashboard/QX con scope", async () => {
    const user = userEvent.setup();
    vi.mocked(updateOtProfile).mockResolvedValue({ ...baseProfile, operationMode: "quipux" });
    renderSection();

    const toggle = await screen.findByLabelText(/Consola en solo lectura \(opera en Quipux\)/i);
    await user.click(toggle);

    await waitFor(() => {
      expect(updateOtProfile).toHaveBeenCalledWith(
        { operationMode: "quipux" },
        { transitOfficeId: OT_ID },
      );
    });
  });

  it("HU12856 (adicional) — lista y permite alternar los feature flags operativos con scope", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      ...baseProfile,
      featureFlags: [
        { id: "flag-1", flagKey: "ot.consolidado.autogenerar", isEnabled: false, config: "" },
      ],
    });
    vi.mocked(updateOtFeatureFlag).mockResolvedValue({
      id: "flag-1",
      flagKey: "ot.consolidado.autogenerar",
      isEnabled: true,
      config: "",
    });
    renderSection();

    const toggle = await screen.findByLabelText(/ot\.consolidado\.autogenerar/i);
    await user.click(toggle);

    await waitFor(() => {
      expect(updateOtFeatureFlag).toHaveBeenCalledWith(
        "flag-1",
        { isEnabled: true },
        { transitOfficeId: OT_ID },
      );
    });
  });
});
