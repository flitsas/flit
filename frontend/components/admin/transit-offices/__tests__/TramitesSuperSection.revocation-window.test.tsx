// HU #12569 — Campo "Ventana de revocatoria (días hábiles)" en la súper-sección Trámites OT.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { TramitesSuperSection, parseRevocationWindowInput } from "../TramitesSuperSection";
import type { OtClientProcedure, OtProfile } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: vi.fn(),
  updateOtProfile: vi.fn(),
  updateOtFeatureFlag: vi.fn(),
  fetchOtClientProcedures: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/admin-transit-office-tenants")>()),
  fetchQuipuxCola: vi.fn(),
  retryQuipuxSubmission: vi.fn(),
  cancelQuipuxSubmission: vi.fn(),
}));

import { fetchOtClientProcedures, fetchOtProfile, updateOtProfile } from "@/lib/api/admin-ot";
import { fetchQuipuxCola } from "@/lib/api/admin-transit-office-tenants";

const OT_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

const sampleProcedure: OtClientProcedure = {
  id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  clientTenantId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
  procedureTypeId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
  referenceNumber: "REF-001",
  status: "entregado",
  createdAt: "2026-06-23T12:00:00Z",
};

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
      <TramitesSuperSection transitOfficeId={OT_ID} />
    </ToastProvider>,
  );
}

describe("parseRevocationWindowInput — HU #12569", () => {
  it("vacío es válido y significa sin límite (null)", () => {
    expect(parseRevocationWindowInput("")).toEqual({ ok: true, value: null });
    expect(parseRevocationWindowInput("   ")).toEqual({ ok: true, value: null });
  });

  it("un entero positivo es válido", () => {
    expect(parseRevocationWindowInput("15")).toEqual({ ok: true, value: 15 });
  });

  it("rechaza valores negativos", () => {
    const result = parseRevocationWindowInput("-5");
    expect(result.ok).toBe(false);
  });

  it("rechaza cero", () => {
    const result = parseRevocationWindowInput("0");
    expect(result.ok).toBe(false);
  });

  it("rechaza valores no numéricos", () => {
    const result = parseRevocationWindowInput("abc");
    expect(result.ok).toBe(false);
  });

  it("rechaza decimales", () => {
    const result = parseRevocationWindowInput("3.5");
    expect(result.ok).toBe(false);
  });
});

describe("TramitesSuperSection — ventana de revocatoria (HU #12569)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue(baseProfile);
    vi.mocked(fetchOtClientProcedures).mockResolvedValue({
      data: [sampleProcedure],
      totalCount: 1,
      page: 1,
      pageSize: 20,
    });
    vi.mocked(fetchQuipuxCola).mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it("AC1 edita y guarda un valor válido: confirma y persiste", async () => {
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
    expect(input).toHaveValue("10");
  });

  it("AC2 guarda vacío y confirma explícitamente sin límite (no como error)", async () => {
    const user = userEvent.setup();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      ...baseProfile,
      revocationWindowBusinessDays: 8,
    });
    vi.mocked(updateOtProfile).mockResolvedValue({
      ...baseProfile,
      revocationWindowBusinessDays: null,
    });
    renderSection();

    const input = await screen.findByLabelText(/Ventana de revocatoria \(días hábiles\)/i);
    expect(input).toHaveValue("8");
    await user.clear(input);
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));

    await waitFor(() => {
      expect(updateOtProfile).toHaveBeenCalledWith({ revocationWindowBusinessDays: null });
    });
    const confirmation = await screen.findByText(/sin límite de ventana/i);
    expect(confirmation).toBeInTheDocument();
    // La confirmación no debe quedar marcada como estado de error.
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("AC3 valor inválido (negativo) bloquea el guardado sin llamar al backend", async () => {
    const user = userEvent.setup();
    renderSection();

    const input = await screen.findByLabelText(/Ventana de revocatoria \(días hábiles\)/i);
    await user.clear(input);
    await user.type(input, "-3");
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/mayor a 0/i);
    expect(updateOtProfile).not.toHaveBeenCalled();
  });

  it("AC3 valor inválido (no numérico) bloquea el guardado sin llamar al backend", async () => {
    const user = userEvent.setup();
    renderSection();

    const input = await screen.findByLabelText(/Ventana de revocatoria \(días hábiles\)/i);
    await user.clear(input);
    await user.type(input, "abc");
    await user.click(screen.getByRole("button", { name: /^Guardar$/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/número entero/i);
    expect(updateOtProfile).not.toHaveBeenCalled();
  });

  it("inicializa el campo desde GET profile con el valor persistido", async () => {
    vi.mocked(fetchOtProfile).mockResolvedValue({
      ...baseProfile,
      revocationWindowBusinessDays: 5,
    });
    renderSection();

    const input = await screen.findByLabelText(/Ventana de revocatoria \(días hábiles\)/i);
    expect(input).toHaveValue("5");
  });
});
