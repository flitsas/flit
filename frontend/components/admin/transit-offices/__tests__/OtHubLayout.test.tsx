// HU #10236 — Layout hub con pestañas de navegación.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OtHubLayout } from "../OtHubLayout";

const mockPush = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush }),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn().mockResolvedValue([
    {
      id: "ot-1",
      code: "11001",
      name: "Bogotá",
      departmentCode: "11",
      cityCode: "11001",
    },
  ]),
}));

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: vi.fn().mockResolvedValue({
    operationMode: "dashboard",
    quipuxReadOnly: false,
    transitOfficeId: "ot-1",
    featureFlags: [],
  }),
}));

import {
  fetchTransitOfficesOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";

const ACTIVE_STATUS = [
  {
    id: "ot-1",
    code: "11001",
    name: "Bogotá",
    departmentCode: "11",
    hasTenant: true,
    tenantId: "t-1",
    estadoActivo: true,
    divipoCode: null,
    quipuxRegistration: false,
    quipuxTransfer: false,
    quipuxOther: false,
  },
];

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficesOperationalStatus: vi.fn(),
}));

vi.mock("@/lib/api/client", () => ({
  getToken: vi.fn().mockReturnValue(null),
}));

vi.mock("@/lib/auth/jwt", () => ({
  decodeJwtPayload: vi.fn().mockReturnValue(null),
  isSuperAdmin: vi.fn().mockReturnValue(false),
}));

describe("OtHubLayout — HU #10236", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue(ACTIVE_STATUS);
  });

  it("AC2 renderiza pestañas de módulos OT", () => {
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="client-procedures" moduleTitle="Test OT">
        <p>Contenido módulo</p>
      </OtHubLayout>,
    );
    expect(screen.getByRole("tab", { name: "Trámites clientes" }))
      .toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Reglas" })).toBeInTheDocument();
    expect(screen.getByText("Contenido módulo")).toBeInTheDocument();
  });

  it("no ofrece las pestañas retiradas de la consola", () => {
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="client-procedures" moduleTitle="Test OT">
        <p>Contenido módulo</p>
      </OtHubLayout>,
    );
    expect(screen.queryByRole("tab", { name: "Trámites" })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Webhooks" })).not.toBeInTheDocument();
  });

  it("AC2 cambia de módulo al seleccionar pestaña", async () => {
    const user = userEvent.setup();
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="client-procedures" moduleTitle="Test OT">
        <p>Contenido</p>
      </OtHubLayout>,
    );
    await user.click(screen.getByRole("tab", { name: "Reglas" }));
    expect(mockPush).toHaveBeenCalledWith("/admin/transit-offices/ot-1/rules");
  });

  it("AC5 tablist accesible", () => {
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="documents" moduleTitle="Test OT">
        <p>Docs</p>
      </OtHubLayout>,
    );
    expect(screen.getByRole("tablist", { name: "Módulos de administración OT" })).toBeInTheDocument();
  });

  it("HU #11225: muestra banner cuando el tenant OT está inactivo", async () => {
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue([
      {
        id: "ot-1",
        code: "11001",
        name: "Bogotá",
        departmentCode: "11",
        hasTenant: true,
        tenantId: "t-1",
        estadoActivo: false,
        divipoCode: null,
        quipuxRegistration: false,
        quipuxTransfer: false,
        quipuxOther: false,
      },
    ]);

    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="client-procedures" moduleTitle="Test OT">
        <p>Config visible</p>
      </OtHubLayout>,
    );

    expect(await screen.findByTestId("ot-inactive-banner")).toHaveTextContent(/Organismo inactivo/i);
    expect(screen.getByText("Config visible")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Reglas" })).toBeInTheDocument();
  });
});
