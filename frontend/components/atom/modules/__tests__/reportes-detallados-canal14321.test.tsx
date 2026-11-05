import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { ApiError } from "@/lib/api/types";
import type { DetailedReportPage } from "@/lib/api/detailed-report";
import { CANAL_LABEL, CANALES_ORDENADOS } from "@/lib/tramites/tramites-row-labels";
import { defaultDetailedFilters, toQueryParams } from "../_reportesDetallados/filters";

/**
 * HU #14321 (Feature #14316, Épica #14296) — Reportes Detallados muestra y filtra el canal.
 *
 * Uso de ejemplo: un gestor abre Reportes Detallados, elige Canal «Persona Natural OT», aparece
 * «OT del punto», elige un OT, busca y la tabla trae la columna Canal con el nombre del OT; el Excel
 * sale con los mismos filtros. Un SuperAdmin además puede elegir «Todas las compañías».
 */

const mocks = vi.hoisted(() => ({
  fetchDetailedReport: vi.fn(),
  exportDetailedReport: vi.fn(),
  fetchNetworkDetailedReport: vi.fn(),
  exportNetworkDetailedReport: vi.fn(),
  fetchAllCompanies: vi.fn(),
  fetchTransitOffices: vi.fn(),
  fetchNetworkChildren: vi.fn(),
  listPublishedProcedureTypes: vi.fn(),
  listTransitOffices: vi.fn(),
  usePermissions: vi.fn(),
  prefsGet: vi.fn(),
  prefsPut: vi.fn(),
}));

vi.mock("@/lib/api/detailed-report", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/detailed-report")>()),
  fetchDetailedReport: mocks.fetchDetailedReport,
  exportDetailedReport: mocks.exportDetailedReport,
  fetchNetworkDetailedReport: mocks.fetchNetworkDetailedReport,
  exportNetworkDetailedReport: mocks.exportNetworkDetailedReport,
}));
vi.mock("@/lib/api/admin-companies", () => ({
  fetchAllCompanies: mocks.fetchAllCompanies,
  fetchTransitOffices: mocks.fetchTransitOffices,
}));
vi.mock("@/lib/api/tramites-client", () => ({
  tramitesClient: {
    listPublishedProcedureTypes: mocks.listPublishedProcedureTypes,
    listTransitOffices: mocks.listTransitOffices,
  },
  fetchNetworkChildren: mocks.fetchNetworkChildren,
}));
vi.mock("@/lib/api/ui-preferences", () => ({
  uiPreferencesClient: { get: mocks.prefsGet, put: mocks.prefsPut },
}));
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: mocks.usePermissions }));

import { DetailedReportPanel } from "../_reportesDetallados/DetailedReportPanel";

const TENANT = "11111111-1111-1111-1111-111111111111";
const EMPRESA = "33333333-3333-3333-3333-333333333333";
const OT = "44444444-4444-4444-4444-444444444444";
const OT2 = "55555555-5555-5555-5555-555555555555";

const FILA = {
  id: "p1",
  referenceNumber: "TR-1",
  procedureTypeName: "Traspaso",
  category: "traspasos",
  status: "entregado",
  createdByDisplayName: "Gestor",
  submittedAt: null,
  completedAt: null,
  personDocument: "123",
  personFullName: "Persona de prueba",
  hasTransformation: false,
  transformationDetail: null,
  isLeasing: false,
  paymentType: "",
  transferType: "Normal",
};
const resumen = { totalCount: 1, byStatus: [], byCategory: [], byProcedureType: [] };
function pagina(items: Record<string, unknown>[]): DetailedReportPage {
  return { items: items as never, totalCount: items.length, page: 1, pageSize: 10, summary: resumen };
}

function permisos(over: Record<string, unknown> = {}) {
  mocks.usePermissions.mockReturnValue({
    permissions: ["reportes.detallados.read", "reportes.detallados.export"],
    isSuperAdmin: false,
    isAdminCompany: true,
    isOtAdmin: false,
    isGroupParent: false,
    hasParentTenant: false,
    parentTenantId: null,
    tenantId: TENANT,
    userId: "user-1",
    roleId: "r1",
    roleCode: "AdminCompany",
    ...over,
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  window.history.replaceState({}, "", "/");
  permisos();
  mocks.fetchDetailedReport.mockResolvedValue(pagina([FILA]));
  mocks.fetchNetworkDetailedReport.mockResolvedValue(pagina([FILA]));
  mocks.exportDetailedReport.mockResolvedValue(undefined);
  mocks.exportNetworkDetailedReport.mockResolvedValue(undefined);
  mocks.fetchAllCompanies.mockResolvedValue([{ id: EMPRESA, razonSocial: "Empresa Uno SAS", nit: "900111" }]);
  mocks.listPublishedProcedureTypes.mockResolvedValue([]);
  mocks.listTransitOffices.mockResolvedValue([
    { id: OT, code: "OT01", name: "Tránsito de Envigado", cityCode: "05266" },
  ]);
  mocks.fetchTransitOffices.mockResolvedValue([
    { id: OT, code: "OT01", name: "Tránsito de Envigado", departmentCode: "05", cityCode: "05266" },
    { id: OT2, code: "OT02", name: "Tránsito de Cali", departmentCode: "76", cityCode: "76001" },
  ]);
  mocks.prefsPut.mockResolvedValue(undefined);
  mocks.prefsGet.mockResolvedValue(null);
  mocks.fetchNetworkChildren.mockResolvedValue([]);
});

async function abrir() {
  const user = userEvent.setup();
  render(<DetailedReportPanel />);
  await screen.findByRole("table");
  return user;
}

const ultimaConsulta = () => mocks.fetchDetailedReport.mock.calls.at(-1)?.[0];

describe("HU #14321 AC1 — columna Canal", () => {
  it("cada fila muestra el nombre del canal; un backend sin el campo se lee como Trámites", async () => {
    mocks.fetchDetailedReport.mockResolvedValue(
      pagina([
        { ...FILA, id: "a", referenceNumber: "A", channel: "pn_web" },
        { ...FILA, id: "b", referenceNumber: "B" },
        { ...FILA, id: "c", referenceNumber: "C", channel: "integracion" },
      ]),
    );
    await abrir();
    expect(screen.getByRole("columnheader", { name: "Canal" })).toBeInTheDocument();
    const canales = screen.getAllByTestId("detallado-fila-canal").map((c) => c.textContent);
    expect(canales).toEqual(["Persona Natural Web", "Trámites", "Integración"]);
  });

  it("Persona Natural OT muestra además el nombre del OT; otro canal no", async () => {
    mocks.fetchDetailedReport.mockResolvedValue(
      pagina([
        {
          ...FILA,
          id: "a",
          channel: "pn_punto",
          channelTransitOfficeId: OT,
          channelTransitOfficeName: "Tránsito de Envigado",
        },
        { ...FILA, id: "b", channel: "pn_web", channelTransitOfficeName: "No debe verse" },
      ]),
    );
    await abrir();
    const [punto, web] = screen.getAllByTestId("detallado-fila-canal");
    expect(punto).toHaveTextContent("Persona Natural OT");
    expect(punto).toHaveTextContent("Tránsito de Envigado");
    expect(web).toHaveTextContent("Persona Natural Web");
    expect(web).not.toHaveTextContent("No debe verse");
  });
});

describe("HU #14321 AC2 — filtro por canal", () => {
  it("ofrece los cinco canales, con los nombres de CANAL_LABEL y en su orden", async () => {
    await abrir();
    const select = screen.getByLabelText("Canal");
    const opciones = within(select)
      .getAllByRole("option")
      .map((o) => o.textContent);
    expect(opciones).toEqual(["Todos los canales", ...CANALES_ORDENADOS.map((c) => CANAL_LABEL[c])]);
    expect(opciones.slice(1)).toEqual([
      "Trámites",
      "Persona Natural Web",
      "Persona Natural OT",
      "Integración",
      "Aplicación",
    ]);
  });

  it("al buscar con un canal la consulta lleva `channel` con el código", async () => {
    const user = await abrir();
    expect(ultimaConsulta().channel).toBeUndefined();
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_web");
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await waitFor(() => expect(ultimaConsulta().channel).toBe("pn_web"));
    expect(ultimaConsulta().channelTransitOfficeId).toBeUndefined();
  });

  it("el filtro «Organismo de tránsito» sigue igual y convive con el canal", async () => {
    const user = await abrir();
    await waitFor(() => expect(screen.getByLabelText("Organismo de tránsito")).toBeEnabled());
    await user.selectOptions(screen.getByLabelText("Organismo de tránsito"), OT);
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await waitFor(() => expect(ultimaConsulta().transitOfficeId).toBe(OT));
    expect(ultimaConsulta().channelTransitOfficeId).toBeUndefined();
  });
});

describe("HU #14321 AC3 — OT del punto condicional", () => {
  it("solo aparece con Persona Natural OT", async () => {
    const user = await abrir();
    expect(screen.queryByLabelText("OT del punto")).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_web");
    expect(screen.queryByLabelText("OT del punto")).not.toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_punto");
    expect(screen.getByLabelText("OT del punto")).toBeInTheDocument();
  });

  it("con Persona Natural OT la consulta lleva channel y channelTransitOfficeId", async () => {
    const user = await abrir();
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_punto");
    await waitFor(() => expect(screen.getByLabelText("OT del punto")).toBeEnabled());
    await user.selectOptions(screen.getByLabelText("OT del punto"), OT);
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await waitFor(() => expect(ultimaConsulta().channelTransitOfficeId).toBe(OT));
    expect(ultimaConsulta().channel).toBe("pn_punto");
  });

  it("al elegir otro canal desaparece y su valor se limpia (no viaja ni reaparece)", async () => {
    const user = await abrir();
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_punto");
    await waitFor(() => expect(screen.getByLabelText("OT del punto")).toBeEnabled());
    await user.selectOptions(screen.getByLabelText("OT del punto"), OT);

    await user.selectOptions(screen.getByLabelText("Canal"), "integracion");
    expect(screen.queryByLabelText("OT del punto")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await waitFor(() => expect(ultimaConsulta().channel).toBe("integracion"));
    expect(ultimaConsulta().channelTransitOfficeId).toBeUndefined();

    await user.selectOptions(screen.getByLabelText("Canal"), "pn_punto");
    expect(screen.getByLabelText("OT del punto")).toHaveValue("");
  });

  it("toQueryParams nunca envía el OT del punto con un canal distinto de pn_punto", () => {
    const base = { ...defaultDetailedFilters(), channelTransitOfficeId: OT };
    expect(toQueryParams({ ...base, channel: "pn_web" }).channelTransitOfficeId).toBeUndefined();
    expect(toQueryParams(base).channelTransitOfficeId).toBeUndefined();
    expect(toQueryParams({ ...base, channel: "pn_punto" })).toEqual(
      expect.objectContaining({ channel: "pn_punto", channelTransitOfficeId: OT }),
    );
  });
});

describe("HU #14321 AC4 — estados de pantalla", () => {
  it("cargando: dice «Armando el reporte…»", async () => {
    mocks.fetchDetailedReport.mockReturnValue(new Promise(() => {}));
    render(<DetailedReportPanel />);
    expect(await screen.findByText("Armando el reporte…")).toBeInTheDocument();
    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();
  });

  it("vacío con filtro de canal: «No hay trámites con ese canal.»", async () => {
    const user = await abrir();
    mocks.fetchDetailedReport.mockResolvedValue(pagina([]));
    await user.selectOptions(screen.getByLabelText("Canal"), "aplicacion");
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    expect(await screen.findByTestId("ui-empty")).toHaveTextContent("No hay trámites con ese canal.");
  });

  it("vacío sin filtro de canal conserva el mensaje actual", async () => {
    mocks.fetchDetailedReport.mockResolvedValue(pagina([]));
    render(<DetailedReportPanel />);
    expect(await screen.findByTestId("ui-empty")).toHaveTextContent(
      "No hay trámites en el rango de fechas seleccionado.",
    );
  });

  it("error: muestra el motivo exacto que entrega la API", async () => {
    mocks.fetchDetailedReport.mockRejectedValue(new ApiError(400, "El canal «xx» no existe."));
    render(<DetailedReportPanel />);
    expect(await screen.findByTestId("ui-error")).toHaveTextContent("El canal «xx» no existe.");
  });

  it("error que no viene de la API cae al mensaje genérico", async () => {
    mocks.fetchDetailedReport.mockRejectedValue(new Error("boom"));
    render(<DetailedReportPanel />);
    expect(await screen.findByTestId("ui-error")).toHaveTextContent("No se pudo cargar el reporte detallado.");
  });

  it("lleno: conserva Pagination con «Filas por página» y sin «Mostrando X de Y»", async () => {
    await abrir();
    expect(screen.getByText(/Filas por página/i)).toBeInTheDocument();
    expect(screen.queryByText(/Mostrando \d+ de \d+/)).not.toBeInTheDocument();
  });
});

describe("HU #14321 AC5 — Todas las compañías (SuperAdmin)", () => {
  function comoSuper() {
    permisos({ isSuperAdmin: true, isAdminCompany: false, roleCode: "SuperAdmin" });
  }

  async function elegirTodas(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByRole("combobox", { name: /Compañía/ }));
    await user.click(await screen.findByRole("option", { name: "Todas las compañías" }));
  }

  it("sin elegir compañía no consulta y pide elegir una", async () => {
    comoSuper();
    render(<DetailedReportPanel />);
    expect(await screen.findByTestId("ui-empty")).toHaveTextContent("Selecciona una compañía");
    expect(mocks.fetchDetailedReport).not.toHaveBeenCalled();
  });

  it("al elegirla consulta con allCompanies=true, sin tenantId, y la tabla trae la compañía de cada fila", async () => {
    comoSuper();
    mocks.fetchDetailedReport.mockResolvedValue(
      pagina([
        { ...FILA, id: "a", referenceNumber: "A", tenantId: EMPRESA, tenantName: "Empresa Uno SAS" },
        { ...FILA, id: "b", referenceNumber: "B", tenantId: TENANT, tenantName: "Empresa Dos SAS" },
      ]),
    );
    const user = userEvent.setup();
    render(<DetailedReportPanel />);
    await elegirTodas(user);
    await user.click(screen.getByRole("button", { name: "Buscar" }));

    await waitFor(() => expect(mocks.fetchDetailedReport).toHaveBeenCalled());
    const params = ultimaConsulta();
    expect(params.allCompanies).toBe(true);
    expect(params.tenantId).toBeUndefined();
    expect(await screen.findByRole("columnheader", { name: "Compañía" })).toBeInTheDocument();
    expect(screen.getAllByTestId("detallado-fila-cliente").map((c) => c.textContent)).toEqual([
      "Empresa Uno SAS",
      "Empresa Dos SAS",
    ]);
  });

  it("elegir una compañía concreta después quita allCompanies y la columna", async () => {
    comoSuper();
    mocks.fetchDetailedReport.mockResolvedValue(pagina([{ ...FILA, tenantName: "Empresa Uno SAS" }]));
    const user = userEvent.setup();
    render(<DetailedReportPanel />);
    await elegirTodas(user);
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await screen.findByRole("columnheader", { name: "Compañía" });

    await user.click(screen.getByRole("combobox", { name: /Compañía/ }));
    await user.click(await screen.findByRole("option", { name: /Empresa Uno SAS/ }));
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await waitFor(() => expect(ultimaConsulta().tenantId).toBe(EMPRESA));
    expect(ultimaConsulta().allCompanies).toBeUndefined();
    await waitFor(() => expect(screen.queryByRole("columnheader", { name: "Compañía" })).not.toBeInTheDocument());
  });

  it("con Todas las compañías y Persona Natural OT, «OT del punto» muestra el catálogo global y la consulta lleva allCompanies y el OT", async () => {
    comoSuper();
    const user = userEvent.setup();
    render(<DetailedReportPanel />);
    await elegirTodas(user);
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_punto");

    const selector = screen.getByLabelText("OT del punto");
    await waitFor(() => expect(selector).toBeEnabled());
    expect(mocks.fetchTransitOffices).toHaveBeenCalledWith(undefined, expect.anything());
    expect(
      within(selector)
        .getAllByRole("option")
        .map((o) => o.textContent),
    ).toEqual(["Todos los OT", "OT01 — Tránsito de Envigado", "OT02 — Tránsito de Cali"]);

    await user.selectOptions(selector, OT2);
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await waitFor(() => expect(ultimaConsulta().channelTransitOfficeId).toBe(OT2));
    expect(ultimaConsulta().allCompanies).toBe(true);
    expect(ultimaConsulta().tenantId).toBeUndefined();
    expect(ultimaConsulta().channel).toBe("pn_punto");
  });

  it("con una compañía concreta no se usa el catálogo global: sigue listTransitOffices", async () => {
    comoSuper();
    const user = userEvent.setup();
    render(<DetailedReportPanel />);
    await user.click(screen.getByRole("combobox", { name: /Compañía/ }));
    await user.click(await screen.findByRole("option", { name: /Empresa Uno SAS/ }));
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_punto");
    await waitFor(() => expect(mocks.listTransitOffices).toHaveBeenCalledWith(EMPRESA));
    expect(mocks.fetchTransitOffices).not.toHaveBeenCalled();
  });
});

describe("HU #14321 AC6 — usuario de empresa", () => {
  it.each([
    ["AdminCompany", { isAdminCompany: true }],
    ["gestor", { isAdminCompany: false, roleCode: "Gestor" }],
  ])("%s no ve selector de compañía ni la opción «Todas las compañías»", async (_rol, over) => {
    permisos(over);
    await abrir();
    expect(screen.queryByRole("combobox", { name: /Compañía/ })).not.toBeInTheDocument();
    expect(screen.queryByText("Todas las compañías")).not.toBeInTheDocument();
    expect(ultimaConsulta().allCompanies).toBeUndefined();
  });
});

describe("HU #14321 AC7 — Excel con los filtros de canal", () => {
  it("exporta con channel y channelTransitOfficeId aplicados", async () => {
    const user = await abrir();
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_punto");
    await waitFor(() => expect(screen.getByLabelText("OT del punto")).toBeEnabled());
    await user.selectOptions(screen.getByLabelText("OT del punto"), OT);
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await waitFor(() => expect(ultimaConsulta().channelTransitOfficeId).toBe(OT));

    await user.click(screen.getByRole("button", { name: /Descargar Excel/ }));
    await waitFor(() => expect(mocks.exportDetailedReport).toHaveBeenCalledTimes(1));
    expect(mocks.exportDetailedReport.mock.calls[0][0]).toEqual(
      expect.objectContaining({ channel: "pn_punto", channelTransitOfficeId: OT }),
    );
  });

  it("no exporta el canal escrito en el formulario si no se aplicó con Buscar", async () => {
    const user = await abrir();
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_web");
    await user.click(screen.getByRole("button", { name: /Descargar Excel/ }));
    await waitFor(() => expect(mocks.exportDetailedReport).toHaveBeenCalled());
    expect(mocks.exportDetailedReport.mock.calls[0][0].channel).toBeUndefined();
  });

  it("SuperAdmin con todas las compañías exporta allCompanies=true sin tenantId", async () => {
    permisos({ isSuperAdmin: true, isAdminCompany: false, roleCode: "SuperAdmin" });
    const user = userEvent.setup();
    render(<DetailedReportPanel />);
    await user.click(screen.getByRole("combobox", { name: /Compañía/ }));
    await user.click(await screen.findByRole("option", { name: "Todas las compañías" }));
    await user.click(screen.getByRole("button", { name: "Buscar" }));
    await screen.findByRole("table");
    await user.click(screen.getByRole("button", { name: /Descargar Excel/ }));
    await waitFor(() => expect(mocks.exportDetailedReport).toHaveBeenCalled());
    const params = mocks.exportDetailedReport.mock.calls[0][0];
    expect(params.allCompanies).toBe(true);
    expect(params.tenantId).toBeUndefined();
  });
});

describe("HU #14321 — la consulta de red lleva los mismos parámetros de canal", () => {
  it("la cabeza de red envía channel y channelTransitOfficeId (y nunca allCompanies) a la ruta de red", async () => {
    permisos({ isGroupParent: true });
    mocks.prefsGet.mockResolvedValue({ value: { mode: "network" } });
    const user = userEvent.setup();
    render(<DetailedReportPanel />);
    await screen.findByRole("table");
    await user.selectOptions(screen.getByLabelText("Canal"), "pn_punto");
    await waitFor(() => expect(screen.getByLabelText("OT del punto")).toBeEnabled());
    await user.selectOptions(screen.getByLabelText("OT del punto"), OT);
    await user.click(screen.getByRole("button", { name: "Buscar" }));

    await waitFor(() =>
      expect(mocks.fetchNetworkDetailedReport.mock.calls.at(-1)?.[0]).toEqual(
        expect.objectContaining({ channel: "pn_punto", channelTransitOfficeId: OT }),
      ),
    );
    expect(mocks.fetchNetworkDetailedReport.mock.calls.at(-1)?.[0]).not.toHaveProperty("allCompanies");

    await user.click(screen.getByRole("button", { name: /Descargar Excel/ }));
    await waitFor(() => expect(mocks.exportNetworkDetailedReport).toHaveBeenCalled());
    expect(mocks.exportNetworkDetailedReport.mock.calls[0][0]).toEqual(
      expect.objectContaining({ channel: "pn_punto", channelTransitOfficeId: OT }),
    );
  });
});

describe("HU #14321 — CANAL_LABEL es la única fuente de los textos", () => {
  it("los códigos y nombres coinciden con el catálogo de la épica", () => {
    expect(CANAL_LABEL).toEqual({
      tramites: "Trámites",
      pn_web: "Persona Natural Web",
      pn_punto: "Persona Natural OT",
      integracion: "Integración",
      aplicacion: "Aplicación",
    });
  });
});
