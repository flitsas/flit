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
    updateTransitOfficeQuipuxSettings: vi.fn(),
  };
});

import {
  fetchTransitOfficesOperationalStatus,
  setTransitOfficeTenantStatus,
  updateTransitOfficeQuipuxSettings,
} from "@/lib/api/admin-transit-office-tenants";

const OFFICE_SIN_ALTA = "aaaaaaaa-0001-4000-8000-000000000001";
const OFFICE_ACTIVO = "aaaaaaaa-0001-4000-8000-000000000002";
const OFFICE_INACTIVO = "aaaaaaaa-0001-4000-8000-000000000003";
const TENANT_ACTIVO = "bbbbbbbb-0001-4000-8000-000000000002";
const TENANT_INACTIVO = "bbbbbbbb-0001-4000-8000-000000000003";

// El `code` es el traffic_agency_code del RUNT y NO coincide con el DIVIPO: pierde el cero a
// la izquierda (Medellín es code '5001000' con DIVIPO '05001'). Por eso son columnas aparte.
const offices: TransitOfficeOperationalStatus[] = [
  {
    id: OFFICE_SIN_ALTA,
    code: "11001000",
    name: "Secretaría de Movilidad Bogotá",
    departmentCode: "11",
    hasTenant: false,
    tenantId: null,
    estadoActivo: null,
    operationMode: null,
    // Sin alta en FLIT pero SÍ parametrizada para radicar: son ejes independientes.
    divipoCode: "11001",
    quipuxRegistration: true,
    quipuxTransfer: false,
    quipuxOther: false,
  },
  {
    id: OFFICE_ACTIVO,
    code: "5001000",
    name: "Medellín — Secretaría de Movilidad",
    departmentCode: "05",
    hasTenant: true,
    tenantId: TENANT_ACTIVO,
    estadoActivo: true,
    operationMode: "dashboard",
    // El caso mayoritario: aún sin DIVIPO cargado (311 de 317).
    divipoCode: null,
    quipuxRegistration: false,
    quipuxTransfer: false,
    quipuxOther: false,
  },
  {
    id: OFFICE_INACTIVO,
    code: "76001000",
    name: "Cali — Secretaría de Movilidad",
    departmentCode: "76",
    hasTenant: true,
    tenantId: TENANT_INACTIVO,
    estadoActivo: false,
    operationMode: "quipux",
    // Estado inconsistente: banderas activas sin DIVIPO → se declara pero no se radica.
    divipoCode: null,
    quipuxRegistration: false,
    quipuxTransfer: true,
    quipuxOther: false,
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
    expect(mockPush).toHaveBeenCalledWith(`/admin/transit-offices/${OFFICE_ACTIVO}/client-procedures`);
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

  it("traduce el código DANE del departamento a su nombre", async () => {
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");
    // departmentCode "05" → Antioquia, en la celda de la fila (no la opción del filtro).
    const row = within(rowFor("Medellín — Secretaría de Movilidad"));
    expect(row.getByText("Antioquia")).toBeInTheDocument();
  });

  it("filtra por estado (solo activos)", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    await user.selectOptions(screen.getByLabelText("Filtrar por estado"), "activo");
    await waitFor(() => {
      expect(screen.getByText("Medellín — Secretaría de Movilidad")).toBeInTheDocument();
      expect(screen.queryByText("Secretaría de Movilidad Bogotá")).not.toBeInTheDocument();
      expect(screen.queryByText("Cali — Secretaría de Movilidad")).not.toBeInTheDocument();
    });
  });

  it("filtra por departamento", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    // "76" → Valle del Cauca (Cali).
    await user.selectOptions(screen.getByLabelText("Filtrar por departamento"), "76");
    await waitFor(() => {
      expect(screen.getByText("Cali — Secretaría de Movilidad")).toBeInTheDocument();
      expect(screen.queryByText("Medellín — Secretaría de Movilidad")).not.toBeInTheDocument();
    });
  });

  it("filtra por radicación Quipux (solo elegibles)", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Secretaría de Movilidad Bogotá");

    // Solo Bogotá tiene DIVIPO + bandera activa → elegible.
    await user.selectOptions(screen.getByLabelText("Filtrar por radicación Quipux"), "elegible");
    await waitFor(() => {
      expect(screen.getByText("Secretaría de Movilidad Bogotá")).toBeInTheDocument();
      expect(screen.queryByText("Medellín — Secretaría de Movilidad")).not.toBeInTheDocument();
      expect(screen.queryByText("Cali — Secretaría de Movilidad")).not.toBeInTheDocument();
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

// HU #10710 — parametrización de la radicación Quipux de la secretaría DESTINO. Ojo: es un eje
// distinto del tenant OT (`operationMode`), que describe al OT-CLIENTE de FLIT.
describe("TransitOfficesList — radicación Quipux (HU #10710)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue(offices);
    vi.mocked(updateTransitOfficeQuipuxSettings).mockResolvedValue({
      transitOfficeId: OFFICE_ACTIVO,
      divipoCode: "05001",
      quipuxRegistration: true,
      quipuxTransfer: false,
      quipuxOther: false,
      elegible: true,
    });
  });

  it("muestra el DIVIPO cargado y las familias de trámite habilitadas", async () => {
    renderList();
    await screen.findByText("Secretaría de Movilidad Bogotá");

    const row = within(rowFor("Secretaría de Movilidad Bogotá"));
    expect(row.getByText("11001")).toBeInTheDocument();
    expect(row.getByText(/Matrículas/)).toBeInTheDocument();
  });

  it("marca «Sin DIVIPO» como estado neutro, no como error", async () => {
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    const badge = within(rowFor("Medellín — Secretaría de Movilidad")).getByText("Sin DIVIPO");
    expect(badge).toBeInTheDocument();
    // Un DIVIPO vacío es el estado normal de una secretaría no integrada: no se anuncia
    // como alerta ni se pinta con el color de error.
    expect(badge).not.toHaveAttribute("role", "alert");
    expect(badge).toHaveStyle({ backgroundColor: "#E2E8F0" });
  });

  it("avisa cuando hay banderas activas sin DIVIPO (se declara pero no se radica)", async () => {
    renderList();
    await screen.findByText("Cali — Secretaría de Movilidad");

    const row = within(rowFor("Cali — Secretaría de Movilidad"));
    expect(row.getByText("Sin DIVIPO")).toBeInTheDocument();
    expect(row.getByText(/Traspasos/)).toBeInTheDocument();
    expect(row.getByText(/Sin DIVIPO no se radica/i)).toBeInTheDocument();
  });

  it("resume cuántas secretarías faltan por parametrizar", async () => {
    renderList();
    // 2 de 3 del fixture no tienen DIVIPO.
    expect(await screen.findByTestId("divipo-summary")).toHaveTextContent(
      /2 de 3 secretarías aún no tienen código DIVIPO/i,
    );
  });

  it("filtra solo las secretarías sin DIVIPO desde el eje de radicación", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Secretaría de Movilidad Bogotá");

    await user.selectOptions(
      screen.getByLabelText("Filtrar por radicación Quipux"),
      "sin-divipo",
    );

    await waitFor(() => {
      // Bogotá sí tiene DIVIPO → sale del listado.
      expect(screen.queryByText("Secretaría de Movilidad Bogotá")).not.toBeInTheDocument();
    });
    expect(screen.getByText("Medellín — Secretaría de Movilidad")).toBeInTheDocument();
    expect(screen.getByText("Cali — Secretaría de Movilidad")).toBeInTheDocument();
  });

  it("la parametrización está disponible también para secretarías sin alta en FLIT", async () => {
    renderList();
    await screen.findByText("Secretaría de Movilidad Bogotá");

    // El destino de radicación no depende de que la secretaría sea cliente de FLIT.
    expect(
      screen.getByRole("button", {
        name: /Parametrizar radicación Quipux de Secretaría de Movilidad Bogotá/,
      }),
    ).toBeInTheDocument();
  });

  it("guarda el DIVIPO y las banderas, y refresca la fila", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    await user.click(
      screen.getByRole("button", { name: /Parametrizar radicación Quipux de Medellín/ }),
    );

    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(/Código DIVIPO/i), "05001");
    await user.click(within(dialog).getByRole("switch", { name: /Matrículas/i }));
    await user.click(within(dialog).getByRole("button", { name: /^Guardar$/ }));

    await waitFor(() => {
      expect(updateTransitOfficeQuipuxSettings).toHaveBeenCalledWith(OFFICE_ACTIVO, {
        divipoCode: "05001",
        quipuxRegistration: true,
        quipuxTransfer: false,
        quipuxOther: false,
      });
    });
    // La fila refleja el DIVIPO recién cargado sin recargar el catálogo.
    await waitFor(() => {
      expect(within(rowFor("Medellín — Secretaría de Movilidad")).getByText("05001")).toBeInTheDocument();
    });
  });

  it("un DIVIPO vacío se envía como null y no bloquea el guardado", async () => {
    vi.mocked(updateTransitOfficeQuipuxSettings).mockResolvedValue({
      transitOfficeId: OFFICE_ACTIVO,
      divipoCode: null,
      quipuxRegistration: false,
      quipuxTransfer: false,
      quipuxOther: false,
      elegible: false,
    });
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    await user.click(
      screen.getByRole("button", { name: /Parametrizar radicación Quipux de Medellín/ }),
    );
    const dialog = await screen.findByRole("dialog");
    // Sin escribir DIVIPO: es un estado guardable, no un error.
    await user.click(within(dialog).getByRole("button", { name: /^Guardar$/ }));

    await waitFor(() => {
      expect(updateTransitOfficeQuipuxSettings).toHaveBeenCalledWith(
        OFFICE_ACTIVO,
        expect.objectContaining({ divipoCode: null }),
      );
    });
  });

  it("bloquea el guardado al activar una bandera sin DIVIPO", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    await user.click(
      screen.getByRole("button", { name: /Parametrizar radicación Quipux de Medellín/ }),
    );
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("switch", { name: /Traspasos/i }));

    expect(await within(dialog).findByText(/Falta el código DIVIPO/i)).toBeInTheDocument();
    // Estado inconsistente: sin DIVIPO no se puede radicar → Guardar deshabilitado.
    expect(within(dialog).getByRole("button", { name: /^Guardar$/ })).toBeDisabled();
    expect(updateTransitOfficeQuipuxSettings).not.toHaveBeenCalled();
  });

  it("rechaza un DIVIPO no numérico sin llamar a la API", async () => {
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    await user.click(
      screen.getByRole("button", { name: /Parametrizar radicación Quipux de Medellín/ }),
    );
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(/Código DIVIPO/i), "05-001");

    expect(await within(dialog).findByText(/debe ser numérico/i)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /^Guardar$/ })).toBeDisabled();
    expect(updateTransitOfficeQuipuxSettings).not.toHaveBeenCalled();
  });

  it("muestra el error y no cierra el diálogo si la API falla", async () => {
    vi.mocked(updateTransitOfficeQuipuxSettings).mockRejectedValue(
      Object.assign(new Error("boom"), { status: 500 }),
    );
    const user = userEvent.setup();
    renderList();
    await screen.findByText("Medellín — Secretaría de Movilidad");

    await user.click(
      screen.getByRole("button", { name: /Parametrizar radicación Quipux de Medellín/ }),
    );
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(/Código DIVIPO/i), "05001");
    await user.click(within(dialog).getByRole("button", { name: /^Guardar$/ }));

    expect(
      await within(dialog).findByText(/No se pudo guardar la parametrización de Quipux/i),
    ).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
