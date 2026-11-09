// Decisión del usuario 2026-11-09 — el modal «Corregir placa» del OT aplica la misma máscara de
// carro que «Asignar placa» (HU 2): normaliza, maxLength 6, texto «Formato ABC123…», aviso ámbar y
// «Corregir» deshabilitado mientras no cumpla. Lo que no es carro conserva el comportamiento actual
// (sin texto de ayuda; «Corregir» solo exige texto).
//
// Uso de ejemplo: una fila `asignado` con `clase: "Automóvil"` y la placa asignada hace 5 min abre
// «Actualizar placa (… min)»; «abc12» deja «Corregir» deshabilitado y «ABC124» lo habilita.
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

const plateMocks = vi.hoisted(() => ({ assignPlateToProcedure: vi.fn(), updateProcedurePlate: vi.fn() }));
vi.mock("@/lib/api/admin-plate-ranges", () => ({
  assignPlateToProcedure: plateMocks.assignPlateToProcedure,
  releaseProcedurePlate: vi.fn(),
  updateProcedurePlate: plateMocks.updateProcedurePlate,
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

function fila(clase: string | null | undefined): OtClientProcedure {
  return {
    id: "proc-9",
    clientTenantId: "client-tenant-aaaa",
    clientTenantName: "Flota Andina S.A.S.",
    procedureTypeId: "matricula_inicial-type-id",
    procedureTypeName: "Matrícula inicial",
    referenceNumber: "RAD-2026-999",
    status: "asignado",
    placa: "ABC123",
    plateAssignedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
    plateUpdatedAt: null,
    createdAt: "2026-06-23T09:00:00Z",
    clase,
  };
}

async function abrirCorregir(user: ReturnType<typeof userEvent.setup>, clase: string | null | undefined) {
  vi.mocked(searchOtClientProcedures).mockResolvedValue({
    data: [fila(clase)],
    totalCount: 1,
    page: 1,
    pageSize: 20,
  });
  render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
  await screen.findByText("RAD-2026-999");
  await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
  await user.click(await screen.findByRole("menuitem", { name: /Actualizar placa/i }));
  return screen.findByLabelText("Placa correcta");
}

describe("ClientProceduresSection — «Corregir placa» con máscara de carro", () => {
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

  it("carro — texto de formato, maxLength 6 y normalización mientras escribe", async () => {
    const user = userEvent.setup();
    const input = await abrirCorregir(user, "Automóvil");

    expect(screen.getByText(TEXTO_CARRO)).toBeInTheDocument();
    expect(input).toHaveAttribute("maxLength", "6");
    await user.type(input, "abc-12 4");
    expect(input).toHaveValue("ABC124");
  });

  it("carro inválido — aviso ámbar accesible y «Corregir» deshabilitado", async () => {
    const user = userEvent.setup();
    const input = await abrirCorregir(user, "CAMIONETA");

    await user.type(input, "ABC12D");
    const aviso = screen.getByText(AVISO_CARRO);
    expect(aviso).toHaveAttribute("role", "alert");
    expect(aviso).toHaveStyle({ color: "#B45309" });
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input.getAttribute("aria-describedby") ?? "").toContain(aviso.id);
    expect(screen.getByRole("button", { name: /^Corregir$/i })).toBeDisabled();
  });

  it("carro válido — sin aviso, «Corregir» habilitado y envía la placa normalizada", async () => {
    const user = userEvent.setup();
    plateMocks.updateProcedurePlate.mockResolvedValue(undefined);
    const input = await abrirCorregir(user, "AUTOMOVIL");

    await user.type(input, "abc124");
    expect(screen.queryByText(AVISO_CARRO)).not.toBeInTheDocument();
    expect(input).not.toHaveAttribute("aria-invalid", "true");
    const corregir = screen.getByRole("button", { name: /^Corregir$/i });
    expect(corregir).toBeEnabled();
    await user.click(corregir);
    expect(plateMocks.updateProcedurePlate).toHaveBeenCalledWith("proc-9", "ABC124");
  });

  it("ids únicos — ayuda y aviso de «Corregir» no chocan con los de «Asignar»", async () => {
    const user = userEvent.setup();
    const input = await abrirCorregir(user, "AUTOMOVIL");
    await user.type(input, "AB");
    const aviso = screen.getByText(AVISO_CARRO);
    const ayuda = screen.getByText(TEXTO_CARRO);
    expect(aviso.id).toBeTruthy();
    expect(ayuda.id).toBeTruthy();
    expect(aviso.id).not.toBe("asignar-placa-aviso");
    expect(ayuda.id).not.toBe("asignar-placa-ayuda");
  });

  it.each([
    // HU6 — la moto ya tiene su propio formato (ClientProceduresSection.placa-grupos.test.tsx).
    ["maquinaria sin grupoPlaca", "CAMIÓN GRÚA"],
    ["clase vacía", ""],
    ["clase ausente", undefined],
  ])("no carro (%s) — comportamiento actual intacto", async (_n, clase) => {
    const user = userEvent.setup();
    plateMocks.updateProcedurePlate.mockResolvedValue(undefined);
    const input = await abrirCorregir(user, clase);

    expect(screen.queryByText(TEXTO_CARRO)).not.toBeInTheDocument();
    expect(screen.queryByText(/^Formatos:/)).not.toBeInTheDocument();
    expect(input).not.toHaveAttribute("maxLength");
    await user.type(input, "ABC12D");
    expect(input).toHaveValue("ABC12D");
    expect(screen.queryByText(AVISO_CARRO)).not.toBeInTheDocument();
    const corregir = screen.getByRole("button", { name: /^Corregir$/i });
    expect(corregir).toBeEnabled();
    await user.click(corregir);
    expect(plateMocks.updateProcedurePlate).toHaveBeenCalledWith("proc-9", "ABC12D");
  });
});
