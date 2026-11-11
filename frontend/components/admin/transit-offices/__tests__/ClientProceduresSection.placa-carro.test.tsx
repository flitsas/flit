// HU 2 (registro diferido 2026-11-09) — máscara y texto de formato en el modal «Asignar placa al
// trámite» del OT, solo para carros (clase normalizada igual a AUTOMOVIL, CAMIONETA, …). El resto
// de clases (moto, maquinaria, remolque, vacía) conserva la ayuda con todos los formatos.
//
// Uso de ejemplo: una fila `preasignacion` con `clase: "Automóvil"` abre el modal; el input
// normaliza «abc-12 3» a «ABC123» y «Asignar» solo se habilita con 3 letras + 3 números.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ClientProceduresSection } from "../ClientProceduresSection";
import type { OtClientProcedure } from "@/lib/api/types-ot";

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtClientProcedures: vi.fn(),
  searchOtClientProcedures: vi.fn(),
  fetchOtBandejaFilterFields: vi.fn(),
  fetchOtBandejaHealth: vi.fn(),
  fetchOtProfile: vi.fn(),
  approveOtClientProcedure: vi.fn(),
  rejectOtClientProcedure: vi.fn(),
  generarOtConsolidadoMaestro: vi.fn(),
  fetchOtDocuments: vi.fn(),
  fetchOtAttachmentPreviewUrl: vi.fn(),
  adjuntarOtLicenciaTransito: vi.fn(),
}));

const plateMocks = vi.hoisted(() => ({ assignPlateToProcedure: vi.fn() }));
vi.mock("@/lib/api/admin-plate-ranges", () => ({
  assignPlateToProcedure: plateMocks.assignPlateToProcedure,
  releaseProcedurePlate: vi.fn(),
  updateProcedurePlate: vi.fn(),
}));

vi.mock("@/lib/auth/jwt", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/auth/jwt")>();
  return { ...actual, isSuperAdmin: () => false };
});

vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: { listPublishedProcedureTypes: vi.fn().mockResolvedValue([]) },
}));

import {
  fetchOtBandejaHealth,
  searchOtClientProcedures,
  fetchOtBandejaFilterFields,
  fetchOtProfile,
} from "@/lib/api/admin-ot";

const TEXTO_CARRO =
  "Formato ABC123. Se validará que no esté registrada y quedará en el inventario de la compañía.";
const AVISO_CARRO = "La placa debe tener 3 letras y 3 números, por ejemplo ABC123.";
const AYUDA_ACTUAL =
  "Formatos: ABC123 (carro), ABC12D (moto), 123ABC (motocarro), R12345 (remolque), S12345 (semirremolque) o MC029554 (maquinaria). Se validará que no esté registrada y quedará en el inventario de la compañía.";

function fila(clase: string | null | undefined): OtClientProcedure {
  return {
    id: "proc-7",
    clientTenantId: "client-tenant-aaaa",
    clientTenantName: "Flota Andina S.A.S.",
    procedureTypeId: "matricula_inicial-type-id",
    procedureTypeName: "Matrícula inicial",
    referenceNumber: "RAD-2026-777",
    status: "preasignacion",
    platePreferredLastDigit: "5",
    createdAt: "2026-06-23T09:00:00Z",
    clase,
  };
}

function mockBandeja(row: OtClientProcedure) {
  vi.mocked(searchOtClientProcedures).mockResolvedValue({
    data: [row],
    totalCount: 1,
    page: 1,
    pageSize: 20,
  });
}

async function abrirModal(user: ReturnType<typeof userEvent.setup>) {
  render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
  await screen.findByText("RAD-2026-777");
  await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
  await user.click(await screen.findByRole("menuitem", { name: /Asignar placa/i }));
  return screen.findByLabelText("Placa");
}

describe("ClientProceduresSection — formato de placa de carro (HU 2)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      featureFlags: [],
      revocationWindowBusinessDays: null,
    });
    vi.mocked(fetchOtBandejaFilterFields).mockResolvedValue([]);
    vi.mocked(fetchOtBandejaHealth).mockResolvedValue({
      transitOfficeResolved: true,
      transitOfficeId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      deliveredTotal: 1,
      deliveredWithGrant: 1,
      deliveredWithoutGrant: 0,
      hasDeliveredWithoutGrant: false,
    });
  });

  it("AC1 — carro: el texto bajo el input es exactamente el de formato ABC123", async () => {
    const user = userEvent.setup();
    mockBandeja(fila("Automóvil"));
    const input = await abrirModal(user);

    expect(screen.getByText(TEXTO_CARRO)).toBeInTheDocument();
    expect(screen.queryByText(AYUDA_ACTUAL)).not.toBeInTheDocument();
    expect(input).toHaveAttribute("maxLength", "6");
  });

  it("AC2 — normaliza mientras escribe: «abc-12 3» → «ABC123»", async () => {
    const user = userEvent.setup();
    mockBandeja(fila("CAMIONETA"));
    const input = await abrirModal(user);

    await user.type(input, "abc-12 3");
    expect(input).toHaveValue("ABC123");
  });

  it("AC3 — placa incompleta: aviso ámbar accesible y «Asignar» deshabilitado", async () => {
    const user = userEvent.setup();
    mockBandeja(fila("Camión"));
    const input = await abrirModal(user);

    await user.type(input, "ABC12");
    const aviso = screen.getByText(AVISO_CARRO);
    expect(aviso).toHaveAttribute("role", "alert");
    expect(aviso).toHaveStyle({ color: "#B45309" });
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input.getAttribute("aria-describedby") ?? "").toContain(aviso.id);
    expect(screen.getByRole("button", { name: /^Asignar$/i })).toBeDisabled();
  });

  it("AC3 — formato de moto en un carro (ABC12D) también se rechaza", async () => {
    const user = userEvent.setup();
    mockBandeja(fila("AUTOMOVIL"));
    const input = await abrirModal(user);

    await user.type(input, "ABC12D");
    expect(screen.getByText(AVISO_CARRO)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Asignar$/i })).toBeDisabled();
  });

  it("AC4 — placa válida: sin aviso, «Asignar» habilitado y envía la placa normalizada", async () => {
    const user = userEvent.setup();
    plateMocks.assignPlateToProcedure.mockResolvedValue(undefined);
    mockBandeja(fila("AUTOMOVIL"));
    const input = await abrirModal(user);

    await user.type(input, "abc123");
    expect(screen.queryByText(AVISO_CARRO)).not.toBeInTheDocument();
    expect(input).not.toHaveAttribute("aria-invalid", "true");
    const asignar = screen.getByRole("button", { name: /^Asignar$/i });
    expect(asignar).toBeEnabled();
    await user.click(asignar);
    expect(plateMocks.assignPlateToProcedure).toHaveBeenCalledWith("proc-7", "ABC123");
  });

  it("AC4 — sin texto no hay aviso (el campo vacío no es un error)", async () => {
    const user = userEvent.setup();
    mockBandeja(fila("AUTOMOVIL"));
    await abrirModal(user);
    expect(screen.queryByText(AVISO_CARRO)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Asignar$/i })).toBeDisabled();
  });

  it.each([
    // HU6 — moto y remolque ya tienen su propio formato (ClientProceduresSection.placa-grupos.test.tsx).
    ["maquinaria sin grupoPlaca", "CAMIÓN GRÚA"],
    ["clase vacía", ""],
    ["clase ausente", undefined],
  ])("AC5 — %s conserva la ayuda actual y «Asignar» solo exige texto", async (_n, clase) => {
    const user = userEvent.setup();
    mockBandeja(fila(clase));
    const input = await abrirModal(user);

    expect(screen.getByText(AYUDA_ACTUAL)).toBeInTheDocument();
    expect(screen.queryByText(TEXTO_CARRO)).not.toBeInTheDocument();
    expect(input).not.toHaveAttribute("maxLength");
    await user.type(input, "ABC12D");
    expect(input).toHaveValue("ABC12D");
    expect(screen.queryByText(AVISO_CARRO)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Asignar$/i })).toBeEnabled();
  });
});
