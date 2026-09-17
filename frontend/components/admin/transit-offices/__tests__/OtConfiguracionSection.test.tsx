// Pedido del usuario (2026-09-16) — "Configuración" del organismo, punto de entrada real para el
// modo Dashboard/QX, la ventana de revocatoria (HU #12569) y los feature flags operativos, que
// antes solo vivían en una ruta legacy sin enlace en ningún menú. Mismos escenarios que ya cubría
// `TramitesSuperSection.revocation-window.test.tsx`, sobre el componente extraído — la validación
// (`parseRevocationWindowInput`) se reusa de ahí, no se duplica.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { OtConfiguracionSection } from "../OtConfiguracionSection";
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

  it("edita y guarda un valor válido: confirma y persiste", async () => {
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
      expect(updateOtProfile).toHaveBeenCalledWith({ revocationWindowBusinessDays: 10 });
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

  it("muestra y permite alternar el modo Dashboard/QX", async () => {
    const user = userEvent.setup();
    vi.mocked(updateOtProfile).mockResolvedValue({ ...baseProfile, operationMode: "quipux" });
    renderSection();

    const toggle = await screen.findByLabelText(/Consola en solo lectura \(opera en Quipux\)/i);
    await user.click(toggle);

    await waitFor(() => {
      expect(updateOtProfile).toHaveBeenCalledWith({ operationMode: "quipux" });
    });
  });

  it("lista y permite alternar los feature flags operativos", async () => {
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
      expect(updateOtFeatureFlag).toHaveBeenCalledWith("flag-1", { isEnabled: true });
    });
  });
});
