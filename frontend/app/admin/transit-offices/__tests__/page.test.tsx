// Refactor adminOT — AdminTransitOfficesPage: ot_admin salta a su hub;
// SuperAdmin ve el catálogo y activa OT sin modal (HU #11224).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import AdminTransitOfficesPage from "../page";

const mockPush = vi.fn();
const mockReplace = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush, replace: mockReplace }),
}));

vi.mock("@/lib/api/client", () => ({
  getToken: vi.fn(),
}));

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: vi.fn(),
}));

const createTransitOfficeTenant = vi.fn();
const fetchTransitOfficesOperationalStatus = vi.fn();

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  createTransitOfficeTenant: (...args: unknown[]) => createTransitOfficeTenant(...args),
  fetchTransitOfficesOperationalStatus: (...args: unknown[]) =>
    fetchTransitOfficesOperationalStatus(...args),
  hasQuipuxFlagsWithoutDivipo: () => false,
  isQuipuxElegible: () => false,
}));

import { getToken } from "@/lib/api/client";
import { fetchOtProfile } from "@/lib/api/admin-ot";

function makeToken(payload: Record<string, unknown>): string {
  const header = Buffer.from(JSON.stringify({ alg: "none", typ: "JWT" })).toString("base64url");
  const body = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `${header}.${body}.`;
}

const OFFICE = {
  id: "office-1",
  code: "11001",
  name: "Secretaría de Movilidad Bogotá",
  departmentCode: "11",
  hasTenant: false,
  tenantId: null,
  estadoActivo: null,
  divipoCode: null,
  quipuxRegistration: false,
  quipuxTransfer: false,
  quipuxOther: false,
};

describe("AdminTransitOfficesPage — refactor adminOT", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchTransitOfficesOperationalStatus.mockResolvedValue([OFFICE]);
  });

  it("ot_admin: se redirige directo a su propio hub sin ver el catálogo", async () => {
    vi.mocked(getToken).mockReturnValue(makeToken({ sub: "u1", role: "ot_admin" }));
    vi.mocked(fetchOtProfile).mockResolvedValue({
      operationMode: "dashboard",
      quipuxReadOnly: false,
      transitOfficeId: "ot-own-id",
      featureFlags: [],
    });

    render(<AdminTransitOfficesPage />);

    await waitFor(() =>
      expect(mockReplace).toHaveBeenCalledWith("/admin/transit-offices/ot-own-id/client-procedures"),
    );
    expect(screen.queryByText("Administración de organismos de tránsito")).not.toBeInTheDocument();
  });

  it("SuperAdmin: ve el catálogo sin modal de activación en cabecera", async () => {
    vi.mocked(getToken).mockReturnValue(makeToken({ sub: "u1", role: "SuperAdmin" }));

    render(<AdminTransitOfficesPage />);

    expect(
      await screen.findByText("Administración de organismos de tránsito"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Activar Organismo de Tránsito/i })).not.toBeInTheDocument();
    expect(fetchOtProfile).not.toHaveBeenCalled();
  });

  it("HU #11224: Activar en fila envía auto-fill sin abrir modal y navega al hub", async () => {
    const user = userEvent.setup();
    vi.mocked(getToken).mockReturnValue(makeToken({ sub: "u1", role: "SuperAdmin" }));
    createTransitOfficeTenant.mockResolvedValue({
      id: "tenant-1",
      transitOfficeId: "office-1",
      legalName: OFFICE.name,
      taxId: OFFICE.code,
      code: OFFICE.code,
      estadoActivo: true,
    });

    render(<AdminTransitOfficesPage />);
    await screen.findByText(OFFICE.name);

    await user.click(screen.getByRole("button", { name: new RegExp(`Activar ${OFFICE.name}`) }));

    await waitFor(() =>
      expect(createTransitOfficeTenant).toHaveBeenCalledWith({
        transitOfficeId: "office-1",
        legalName: OFFICE.name,
        taxId: "11001",
        code: "11001",
      }),
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(mockPush).toHaveBeenCalledWith("/admin/transit-offices/office-1/client-procedures");
  });
});
