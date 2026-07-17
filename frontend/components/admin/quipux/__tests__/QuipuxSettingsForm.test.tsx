// HU #10710 — configuración global de Quipux: carga, valores por defecto y manejo de secretos.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { QuipuxSettingsForm } from "../QuipuxSettingsForm";
import type { QuipuxSettings } from "@/lib/api/admin-quipux-settings";

vi.mock("@/lib/api/admin-quipux-settings", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-quipux-settings")>()),
  fetchQuipuxSettings: vi.fn(),
  saveQuipuxSettings: vi.fn(),
}));

import { fetchQuipuxSettings, saveQuipuxSettings } from "@/lib/api/admin-quipux-settings";

function settings(overrides: Partial<QuipuxSettings> = {}): QuipuxSettings {
  return {
    enabled: true,
    urlLogin: "https://qx/login",
    urlRegisterDocument: "https://qx/registroDocumento",
    urlValidateStatus: "https://qx/validarEstado",
    username: "flit",
    hasPassword: true,
    consumerCode: "1003",
    bucket: "qxinterconnect",
    s3Prefix: "FLIT/",
    awsRegion: "us-east-1",
    awsAccessKeyId: "AKIAEXAMPLE",
    hasAwsSecretAccessKey: true,
    officerDocumentType: 3,
    officerDocumentNumber: "900123456",
    registerIntervalMinutes: 15,
    pollIntervalMinutes: 15,
    batchSize: 20,
    maxAttempts: 5,
    maxPolls: 500,
    timeoutSeconds: 60,
    estaCompleta: true,
    updatedAt: "2026-07-17T10:00:00Z",
    ...overrides,
  };
}

function renderForm() {
  return render(
    <ToastProvider>
      <QuipuxSettingsForm />
    </ToastProvider>,
  );
}

describe("QuipuxSettingsForm — HU #10710", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(saveQuipuxSettings).mockImplementation(async () => settings());
  });

  it("carga y muestra la configuración vigente", async () => {
    vi.mocked(fetchQuipuxSettings).mockResolvedValue(settings());
    renderForm();

    expect(await screen.findByLabelText("URL de login")).toHaveValue("https://qx/login");
    expect(screen.getByLabelText("Usuario")).toHaveValue("flit");
    expect(screen.getByLabelText("Código de consumidor")).toHaveValue("1003");
  });

  it("un secreto ya cargado no se precarga, pero avisa que existe", async () => {
    vi.mocked(fetchQuipuxSettings).mockResolvedValue(settings({ hasPassword: true }));
    renderForm();

    const pwd = await screen.findByLabelText("Contraseña");
    expect(pwd).toHaveValue("");
    expect(pwd).toHaveAttribute("placeholder", expect.stringContaining("guardado"));
  });

  it("el tipo de documento es un desplegable con etiquetas (3 = NIT)", async () => {
    vi.mocked(fetchQuipuxSettings).mockResolvedValue(settings({ officerDocumentType: 3 }));
    renderForm();

    const select = await screen.findByLabelText("Tipo de documento");
    expect(select).toHaveValue("3");
    const nit = screen.getByRole("option", { name: /NIT/ }) as HTMLOptionElement;
    expect(nit.selected).toBe(true);
  });

  it("estado inicial sin fila: usa los valores por defecto", async () => {
    vi.mocked(fetchQuipuxSettings).mockResolvedValue(null);
    renderForm();

    expect(await screen.findByLabelText("Prefijo de la key")).toHaveValue("FLIT/");
    expect(screen.getByLabelText("Región AWS")).toHaveValue("us-east-1");
    expect(screen.getByLabelText("Tamaño de lote")).toHaveValue(20);
  });

  it("al guardar sin tocar los secretos, NO los reenvía (conserva los cifrados)", async () => {
    vi.mocked(fetchQuipuxSettings).mockResolvedValue(settings());
    renderForm();
    await screen.findByLabelText("URL de login");

    await userEvent.click(screen.getByRole("button", { name: /guardar configuración/i }));

    await waitFor(() => expect(saveQuipuxSettings).toHaveBeenCalledTimes(1));
    const body = vi.mocked(saveQuipuxSettings).mock.calls[0][0];
    expect(body.password).toBeUndefined();
    expect(body.awsSecretAccessKey).toBeUndefined();
  });

  it("al escribir una contraseña nueva, la envía en el PUT", async () => {
    vi.mocked(fetchQuipuxSettings).mockResolvedValue(settings());
    renderForm();

    const pwd = await screen.findByLabelText("Contraseña");
    await userEvent.type(pwd, "clave-nueva");
    await userEvent.click(screen.getByRole("button", { name: /guardar configuración/i }));

    await waitFor(() => expect(saveQuipuxSettings).toHaveBeenCalled());
    const body = vi.mocked(saveQuipuxSettings).mock.calls[0][0];
    expect(body.password).toBe("clave-nueva");
  });
});
