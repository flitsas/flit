// HU6 (registro diferido 2026-11-09, plan preasignacion.pdf) — modales «Asignar placa» y «Corregir
// placa» del OT por grupo de clase (CARRO / MOTO / REMOLQUE / MAQUINARIA). El grupo sale del campo
// `grupoPlaca` de la bandeja (catálogo del backend); sin él, de las listas exactas por `clase`
// (CARRO, MOTO, REMOLQUE). Sin grupo: comportamiento actual.
//
// Uso de ejemplo: una fila `preasignacion` con `grupoPlaca: "MOTO"` abre «Asignar placa»; el input
// corta a 6, la ayuda dice «Formato permitido: ABC12D…» y «Asignar» solo se habilita con ABC12D/ABC12.
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

const SUFIJO = " Se validará que no esté registrada y quedará en el inventario de la compañía.";
const AYUDA = {
  CARRO: "Formato ABC123." + SUFIJO,
  MOTO:
    "Formato permitido: ABC12D (3 letras, 2 números y 1 letra) o ABC12 (3 letras y 2 números)." + SUFIJO,
  REMOLQUE:
    "Formato permitido: R (remolque) o S (semirremolque) seguida de 5 o 6 números, por ejemplo R12345 o S54321." +
    SUFIJO,
  MAQUINARIA:
    "Formato permitido: serie identificatoria del Registro Especial de Maquinaria, 2 letras y 6 números, por ejemplo MC029554." +
    SUFIJO,
} as const;
const AVISO = {
  CARRO: "La placa debe tener 3 letras y 3 números, por ejemplo ABC123.",
  MOTO: "La placa debe tener 3 letras y 2 números, con o sin una letra al final, por ejemplo ABC12D o ABC12.",
  REMOLQUE: "La placa debe empezar por R o S seguida de 5 o 6 números, por ejemplo R12345 o S54321.",
  MAQUINARIA: "La placa debe tener 2 letras y 6 números, por ejemplo MC029554.",
} as const;
const AYUDA_SIN_GRUPO =
  "Formatos: ABC123 (carro), ABC12D (moto), R12345 (remolque), S12345 (semirremolque) o MC029554 (maquinaria). Se validará que no esté registrada y quedará en el inventario de la compañía.";

type Grupo = keyof typeof AYUDA;

function fila(over: Partial<OtClientProcedure>): OtClientProcedure {
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
    ...over,
  };
}

function render_(row: OtClientProcedure) {
  vi.mocked(searchOtClientProcedures).mockResolvedValue({ data: [row], totalCount: 1, page: 1, pageSize: 20 });
  render(
    <ToastProvider>
      <ClientProceduresSection />
    </ToastProvider>,
  );
}

async function abrirAsignar(user: ReturnType<typeof userEvent.setup>, row: OtClientProcedure) {
  render_(row);
  await screen.findByText("RAD-2026-777");
  await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
  await user.click(await screen.findByRole("menuitem", { name: /Asignar placa/i }));
  return screen.findByLabelText("Placa");
}

async function abrirCorregir(user: ReturnType<typeof userEvent.setup>, row: OtClientProcedure) {
  render_({
    ...row,
    status: "asignado",
    placa: "ABC12D",
    plateAssignedAt: new Date(Date.now() - 5 * 60_000).toISOString(),
    plateUpdatedAt: null,
  });
  await screen.findByText("RAD-2026-777");
  await user.click(await screen.findByRole("button", { name: /Acciones del trámite/i }));
  await user.click(await screen.findByRole("menuitem", { name: /Actualizar placa/i }));
  return screen.findByLabelText("Placa correcta");
}

describe("ClientProceduresSection — placa por grupo de clase (HU6)", () => {
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

  it.each([
    // grupo, maxLength, escrito, normalizado, inválido, válido
    ["MOTO", "6", "abc-12 dx", "ABC12D", "ABC123", "ABC12"],
    ["REMOLQUE", "7", "r-1234567", "R123456", "R1234", "S54321"],
    ["MAQUINARIA", "8", "mc 0295549", "MC029554", "MC02955", "MC029554"],
  ] as const)(
    "Asignar %s (grupoPlaca) — máscara maxLength %s, ayuda, aviso ámbar y botón",
    async (grupo, maxLength, escrito, normalizado, invalida, valida) => {
      const user = userEvent.setup();
      plateMocks.assignPlateToProcedure.mockResolvedValue(undefined);
      const input = await abrirAsignar(user, fila({ grupoPlaca: grupo, clase: "X" }));

      expect(screen.getByText(AYUDA[grupo])).toBeInTheDocument();
      expect(screen.queryByText(AYUDA_SIN_GRUPO)).not.toBeInTheDocument();
      expect(input).toHaveAttribute("maxLength", maxLength);
      await user.type(input, escrito);
      expect(input).toHaveValue(normalizado);

      await user.clear(input);
      await user.type(input, invalida);
      const aviso = screen.getByText(AVISO[grupo]);
      expect(aviso).toHaveAttribute("role", "alert");
      expect(aviso).toHaveStyle({ color: "#B45309" });
      expect(input).toHaveAttribute("aria-invalid", "true");
      expect(input.getAttribute("aria-describedby") ?? "").toContain(aviso.id);
      expect(screen.getByRole("button", { name: /^Asignar$/i })).toBeDisabled();

      await user.clear(input);
      await user.type(input, valida);
      expect(screen.queryByText(AVISO[grupo])).not.toBeInTheDocument();
      const asignar = screen.getByRole("button", { name: /^Asignar$/i });
      expect(asignar).toBeEnabled();
      await user.click(asignar);
      expect(plateMocks.assignPlateToProcedure).toHaveBeenCalledWith("proc-7", valida);
    },
  );

  it("grupoPlaca manda sobre la clase: CAMION GRUA con grupoPlaca MAQUINARIA es maquinaria", async () => {
    const user = userEvent.setup();
    await abrirAsignar(user, fila({ grupoPlaca: "MAQUINARIA", clase: "CAMION GRUA" }));
    expect(screen.getByText(AYUDA.MAQUINARIA)).toBeInTheDocument();
  });

  it.each([
    ["MOTO", "Motocicleta"],
    ["MOTO", "CICLOMOTOR"],
    ["REMOLQUE", "SEMIRREMOLQUE"],
    ["REMOLQUE", "Remolque y semirremolque"],
    ["CARRO", "Automóvil"],
  ] as const)("fallback sin grupoPlaca — %s desde clase «%s»", async (grupo, clase) => {
    const user = userEvent.setup();
    await abrirAsignar(user, fila({ grupoPlaca: null, clase }));
    expect(screen.getByText(AYUDA[grupo as Grupo])).toBeInTheDocument();
  });

  it.each([
    ["maquinaria sin grupoPlaca", "CAMIÓN GRÚA"],
    ["clase vacía", ""],
    ["clase ausente", undefined],
  ])("sin grupo (%s) — ayuda con todos los formatos e input libre", async (_n, clase) => {
    const user = userEvent.setup();
    const input = await abrirAsignar(user, fila({ clase }));
    expect(screen.getByText(AYUDA_SIN_GRUPO)).toBeInTheDocument();
    expect(input).not.toHaveAttribute("maxLength");
    await user.type(input, "abc-12d");
    expect(input).toHaveValue("abc-12d");
    expect(screen.getByRole("button", { name: /^Asignar$/i })).toBeEnabled();
  });

  it("Corregir MOTO — misma máscara, ayuda y aviso de moto", async () => {
    const user = userEvent.setup();
    plateMocks.updateProcedurePlate.mockResolvedValue(undefined);
    const input = await abrirCorregir(user, fila({ grupoPlaca: "MOTO", clase: "MOTOCICLETA" }));
    expect(screen.getByText(AYUDA.MOTO)).toBeInTheDocument();
    expect(input).toHaveAttribute("maxLength", "6");
    await user.type(input, "ABC1");
    expect(screen.getByText(AVISO.MOTO)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Corregir$/i })).toBeDisabled();
    await user.type(input, "2E");
    const corregir = screen.getByRole("button", { name: /^Corregir$/i });
    expect(corregir).toBeEnabled();
    await user.click(corregir);
    expect(plateMocks.updateProcedurePlate).toHaveBeenCalledWith("proc-7", "ABC12E");
  });

  it("Corregir REMOLQUE (fallback por clase) — aviso de remolque", async () => {
    const user = userEvent.setup();
    const input = await abrirCorregir(user, fila({ clase: "REMOLQUE" }));
    expect(screen.getByText(AYUDA.REMOLQUE)).toBeInTheDocument();
    await user.type(input, "T12345");
    expect(screen.getByText(AVISO.REMOLQUE)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Corregir$/i })).toBeDisabled();
  });

  it("Corregir MAQUINARIA — ayuda y maxLength 8", async () => {
    const user = userEvent.setup();
    const input = await abrirCorregir(user, fila({ grupoPlaca: "MAQUINARIA" }));
    expect(screen.getByText(AYUDA.MAQUINARIA)).toBeInTheDocument();
    expect(input).toHaveAttribute("maxLength", "8");
  });
});

describe("ClientProceduresSection — guía del dígito por grupo (HU6)", () => {
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

  it("MOTO — guía del primer número con el literal exacto", async () => {
    const user = userEvent.setup();
    await abrirAsignar(user, fila({ grupoPlaca: "MOTO" }));
    const guia = screen.getByTestId("ot-guia-digito");
    expect(guia).toHaveTextContent(
      "Dígito numérico de preferencia: el cliente prefiere el número 5 como primer número de la placa (ej. ABC52D o ABC52) — solo guía para elegir la placa; puedes asignar esa u otra cualquiera.",
    );
    expect(guia.querySelector("b")).toHaveTextContent("5");
  });

  it("CARRO y sin grupo — guía actual «termina en»", async () => {
    const user = userEvent.setup();
    await abrirAsignar(user, fila({ grupoPlaca: "CARRO" }));
    expect(screen.getByTestId("ot-guia-digito")).toHaveTextContent(/Dígito de preferencia: termina en 5/);
  });

  it.each(["REMOLQUE", "MAQUINARIA"] as const)("%s — no muestra guía del dígito", async (grupo) => {
    const user = userEvent.setup();
    await abrirAsignar(user, fila({ grupoPlaca: grupo }));
    expect(screen.queryByTestId("ot-guia-digito")).not.toBeInTheDocument();
  });
});
