// RF01/RF02/RF03 — listado de OT con estado operativo + activar/desactivar/alta.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { TransitOfficesList } from "../TransitOfficesList";
import type { TransitOfficeOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

const mockPush = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mockPush }),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-transit-office-tenants")>();
  return {
    ...actual,
    fetchTransitOfficesOperationalStatus: vi.fn(),
    setTransitOfficeTenantStatus: vi.fn(),
  };
});

import {
  fetchTransitOfficesOperationalStatus,
  setTransitOfficeTenantStatus,
} from "@/lib/api/admin-transit-office-tenants";

const OFFICE_SIN_ALTA = "aaaaaaaa-0001-4000-8000-000000000001";
const OFFICE_ACTIVO = "aaaaaaaa-0001-4000-8000-000000000002";
const OFFICE_INACTIVO = "aaaaaaaa-0001-4000-8000-000000000003";
const TENANT_ACTIVO = "bbbbbbbb-0001-4000-8000-000000000002";
const TENANT_INACTIVO = "bbbbbbbb-0001-4000-8000-000000000003";

const offices: TransitOfficeOperationalStatus[] = [
  {
    id: OFFICE_SIN_ALTA,
    code: "11001",
    name: "Secretaría de Movilidad Bogotá",
    departmentCode: "11",
    hasTenant: false,
    tenantId: null,
    estadoActivo: null,
    operationMode: null,
  },
  {
    id: OFFICE_ACTIVO,
    code: "05001",
    name: "Medellín — Secretaría de Movilidad",
    departmentCode: "05",
    hasTenant: true,
    tenantId: TENANT_ACTIVO,
    estadoActivo: true,
    operationMode: "dashboard",
  },
  {
    id: OFFICE_INACTIVO,
    code: "76001",
    name: "Cali — Secretaría de Movilidad",
    departmentCode: "76",
    hasTenant: true,
    tenantId: TENANT_INACTIVO,
    estadoActivo: false,
    operationMode: "quipux",
  },
];

function renderList(props?: Parameters<typeof TransitOfficesList>[0]) {
  return render(
    <ToastProvider>
      <TransitOfficesList {...props} />
    </ToastProvider>,
  );
}

/** Localiza la fila de la tabla que contiene el nombre dado. */
function rowFor(name: string): HTMLElement {
  return screen.getByText(name).closest("tr") as HTMLElement;
}

describe("TransitOfficesList — RF01/RF02/RF03", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue(offices);
    vi.mocked(setTransitOfficeTenantStatus).mockResolvedValue({
      id: TENANT_ACTIVO,
      estadoActivo: false,
    });
  });

  it("RF01 muestra el estado operativo por OT (Sin alta | Activo | Inactivo)", async () => {
    renderList();
    await screen.findByText("Secretaría de Movilidad Bogotá");

    expect(within(rowFor("Secretaría de Movilidad Bogotá")).getByText("Sin alta")).toBeInTheDocument();
    expect(within(rowFor("Medellín — Secretaría de Movilidad")).getByText("Activo")).toBeInTheDocument();
    expect(within(rowFor("Cali — Secretaría de Movilidad")).getByText("Inactivo")).toBeInTheDocument();
  });

  it("RF01 filtra por nombre en tiempo real", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");
    await user.type(screen.getByPlaceholderText(/Buscar por nombre/i), "bogota");
    await waitFor(() => {
      expect(screen.getByText("Secretaría de Movilidad Bogotá")).toBeInTheDocument();
      expect(screen.queryByText("Medellín — Secretaría de Movilidad")).not.toBeInTheDocument();
    });
  });

  it("sin alta ofrece «Dar de alta» y llama onCreateTenant con la oficina", async () => {
    const onCreateTenant = vi.fn();
    const user = userEvent.setup();
    renderList({ onCreateTenant });
    await screen.findByText("Secretaría de Movilidad Bogotá");

    await user.click(screen.getByRole("button", { name: /Dar de alta Secretaría de Movilidad Bogotá/ }));
    expect(onCreateTenant).toHaveBeenCalledWith(
      expect.objectContaining({ id: OFFICE_SIN_ALTA, hasTenant: false }),
    );
    // Una oficina sin alta no puede administrarse.
    expect(
      screen.queryByRole("button", { name: /Administrar Secretaría de Movilidad Bogotá/ }),
    ).not.toBeInTheDocument();
  });

  it("con tenant navega al hub al administrar", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");
    await user.click(screen.getByRole("button", { name: /Administrar Medellín/ }));
    expect(mockPush).toHaveBeenCalledWith(`/admin/transit-offices/${OFFICE_ACTIVO}/tramites`);
  });

  it("RF03 desactiva un OT activo con confirmación", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    await user.click(screen.getByRole("button", { name: /Desactivar Medellín/ }));
    // Modal de confirmación.
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^Desactivar$/ }));

    await waitFor(() => {
      expect(setTransitOfficeTenantStatus).toHaveBeenCalledWith(TENANT_ACTIVO, false);
    });
    // La fila pasa a Inactivo y aparece la acción Activar.
    await waitFor(() => {
      expect(within(rowFor("Medellín — Secretaría de Movilidad")).getByText("Inactivo")).toBeInTheDocument();
    });
  });

  it("RF02 activa un OT inactivo con confirmación", async () => {
    vi.mocked(setTransitOfficeTenantStatus).mockResolvedValue({
      id: TENANT_INACTIVO,
      estadoActivo: true,
    });
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Cali — Secretaría de Movilidad");

    await user.click(screen.getByRole("button", { name: /Activar Cali/ }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^Activar$/ }));

    await waitFor(() => {
      expect(setTransitOfficeTenantStatus).toHaveBeenCalledWith(TENANT_INACTIVO, true);
    });
    await waitFor(() => {
      expect(within(rowFor("Cali — Secretaría de Movilidad")).getByText("Activo")).toBeInTheDocument();
    });
  });

  it("muestra estado de error cuando la carga falla", async () => {
    vi.mocked(fetchTransitOfficesOperationalStatus).mockRejectedValue(new Error("boom"));
    renderList();
    expect(
      await screen.findByText(/No se pudo cargar el catálogo de organismos de tránsito/),
    ).toBeInTheDocument();
  });
});
