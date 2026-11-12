// HU #14357 — la tabla de organismos de Compañías ya no ofrece el convenio (columna, interruptor,
// filtro ni peticiones). Se simula `apiFetch` y NO `admin-companies`: así el AC5 mira las rutas HTTP
// reales y sigue siendo válido cuando las funciones del cliente desaparecen.
//
// Uso de ejemplo:
//   render(<OTConfigTablePanel tenantId="t-1" mode="superadmin-concession" />);
//   → columnas Organismo, Código, Estado y Acciones; ninguna petición a /transit-agreements.
import { readFileSync } from "node:fs";
import path from "node:path";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { TransitOffice, TenantSettings } from "@/lib/api/types";
import { CompanyConfigTabs } from "@/components/admin/companies/CompanyConfigTabs";
import { OTConfigTablePanel } from "../OTConfigTablePanel";
import { TransitBlocksPanel } from "../TransitBlocksPanel";

const apiFetch = vi.hoisted(() => vi.fn());
vi.mock("@/lib/api/client", () => ({ apiFetch }));
vi.mock("@/components/admin/Toast", () => ({ useToast: () => ({ show: vi.fn() }) }));
vi.mock("@/lib/api/platform", () => ({
  listTenantProducts: vi.fn().mockResolvedValue([]),
  setTenantProduct: vi.fn(),
}));

const TENANT = "t-1";

// 13 organismos: el último cae fuera de la primera página (10 filas por defecto).
const offices: TransitOffice[] = Array.from({ length: 13 }, (_, i) => ({
  id: `o${i}`,
  code: `900${String(i).padStart(2, "0")}`,
  name: i === 12 ? "Tránsito de Chía" : `Organismo ${i}`,
  departmentCode: "11",
  cityCode: "11001",
}));

/** o0 habilitado; el resto no. Convenio: si alguien lo pide, responde error para hacerlo evidente. */
function arrangeApi() {
  apiFetch.mockImplementation((ruta: string, init?: { method?: string; body?: unknown }) => {
    if (ruta.includes("transit-agreements")) {
      return Promise.reject(new Error("La ruta de convenios ya no se debe invocar"));
    }
    if (ruta.includes("transit-grants")) {
      return init?.method && init.method !== "GET"
        ? Promise.resolve(undefined)
        : Promise.resolve({ transitOfficeIds: ["o0"] });
    }
    if (ruta.includes("transit-blocks")) {
      return Promise.resolve({ transitOfficeIds: [] });
    }
    if (ruta.includes("operational-status")) {
      return Promise.resolve(
        offices.map((o) => ({ id: o.id, hasTenant: true, estadoActivo: true })),
      );
    }
    if (ruta.endsWith("/api/v1/admin/transit-offices")) {
      return Promise.resolve(offices);
    }
    return Promise.resolve([]);
  });
}

function columnasFiltrables(): string[] {
  return screen
    .getAllByRole("button", { name: /^filtrar por /i })
    .map((b) => (b.getAttribute("aria-label") ?? "").replace(/^Filtrar por /i, ""));
}

async function aplicarFiltro(
  user: ReturnType<typeof userEvent.setup>,
  columna: string,
  valor: string | string[],
) {
  await user.click(
    screen.getByRole("button", { name: new RegExp(`^Filtrar por ${columna}`, "i") }),
  );
  const dialog = await screen.findByRole("dialog", { name: `Filtro de ${columna}` });
  if (typeof valor === "string") {
    await user.type(within(dialog).getByLabelText(new RegExp(`${columna} contiene`, "i")), valor);
  } else {
    for (const opcion of valor) {
      await user.click(within(dialog).getByLabelText(opcion));
    }
  }
  await user.click(within(dialog).getByRole("button", { name: /^aplicar$/i }));
}

function rutasInvocadas(): string[] {
  return apiFetch.mock.calls.map(([ruta]) => String(ruta));
}

beforeEach(() => {
  vi.clearAllMocks();
  window.localStorage.clear();
  arrangeApi();
});

describe("HU #14357 AC1 — sin columna ni interruptor de convenio", () => {
  it.each(["editable", "superadmin-concession", "superadmin-marca-blanca"] as const)(
    "modo %s: no hay columna Convenio ni ningún control para marcarlo o quitarlo",
    async (mode) => {
      render(<OTConfigTablePanel tenantId={TENANT} mode={mode} />);
      await screen.findByText("Organismo 0");

      expect(screen.queryByRole("columnheader", { name: /convenio/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("switch", { name: /convenio/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /convenio/i })).not.toBeInTheDocument();
      expect(screen.queryByText(/convenio/i)).not.toBeInTheDocument();
    },
  );
});

describe("HU #14357 AC2 — sin filtro de convenio y con los demás filtros intactos", () => {
  it("el embudo ofrece Organismo, Código y Estado, no Convenio", async () => {
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Organismo 0");

    expect(columnasFiltrables()).toEqual(["Organismo", "Código", "Estado"]);
    expect(screen.queryByText(/con convenio|sin convenio/i)).not.toBeInTheDocument();
  });

  it("caracterización: el buscador alcanza organismos fuera de la página visible", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Organismo 0");
    expect(screen.queryByText("Tránsito de Chía")).not.toBeInTheDocument();

    await user.type(screen.getByLabelText("Buscar organismo de tránsito"), "transito de chia");

    expect(await screen.findByText("Tránsito de Chía")).toBeInTheDocument();
    expect(screen.queryByText("Organismo 0")).not.toBeInTheDocument();
  });

  it("caracterización: el embudo de Organismo alcanza filas fuera de la página visible", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Organismo 0");

    await aplicarFiltro(user, "Organismo", "chia");

    expect(await screen.findByText("Tránsito de Chía")).toBeInTheDocument();
    expect(screen.queryByText("Organismo 0")).not.toBeInTheDocument();
  });

  it("caracterización: el embudo de Código alcanza filas fuera de la página visible", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Organismo 0");

    await aplicarFiltro(user, "Código", "90012");

    expect(await screen.findByText("Tránsito de Chía")).toBeInTheDocument();
    expect(screen.queryByText("Organismo 0")).not.toBeInTheDocument();
  });

  it("caracterización: el embudo de Estado filtra todos los organismos, no solo la página", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Organismo 0");
    // o0 es el único habilitado; «No habilitado» trae a Chía desde la última página.
    await aplicarFiltro(user, "Estado", ["No habilitado"]);

    await waitFor(() => expect(screen.queryByText("Organismo 0")).not.toBeInTheDocument());
    expect(screen.getByText("Organismo 1")).toBeInTheDocument();
    await aplicarFiltro(user, "Organismo", "chia");
    expect(await screen.findByText("Tránsito de Chía")).toBeInTheDocument();
  });

  it("caracterización: Bloqueos conserva su filtro de Estado en red", async () => {
    render(<TransitBlocksPanel tenantId={TENANT} />);
    await screen.findByText("Organismo 0");

    expect(columnasFiltrables()).toEqual(["Organismo", "Código", "Estado en red"]);
  });
});

describe("HU #14357 AC3 — habilitar y configurar no cambian", () => {
  it("caracterización: habilitar un organismo persiste el grant", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Organismo 1");

    await user.click(screen.getByRole("switch", { name: /^habilitar organismo 1$/i }));

    await waitFor(() =>
      expect(apiFetch).toHaveBeenCalledWith(
        expect.stringContaining(`${TENANT}/transit-grants`),
        expect.objectContaining({ method: "POST", body: { transitOfficeId: "o1" } }),
      ),
    );
  });

  it("caracterización: «Configurar» abre el modal de un organismo habilitado", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Organismo 0");

    await user.click(screen.getByRole("button", { name: /acciones para organismo 0/i }));
    await user.click(await screen.findByRole("menuitem", { name: /^configurar$/i }));

    expect(
      await screen.findByRole("dialog", { name: /configurar — organismo 0/i }),
    ).toBeInTheDocument();
  });

  it("caracterización: Firma y documentos conserva el baúl y las pestañas siguen completas", async () => {
    const user = userEvent.setup();
    render(
      <CompanyConfigTabs
        settings={settings}
        onSaveSettings={vi.fn()}
        otSlot={<div>panel-organismos</div>}
      />,
    );

    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Productos",
      "Trámites",
      "Configuración Empresa",
      "Representantes legales",
      "Mandatarios",
      "Historial de Cambios",
    ]);

    await user.click(screen.getByRole("tab", { name: "Configuración Empresa" }));
    expect(screen.getAllByText("Firma y documentos").length).toBeGreaterThan(0);
    expect(screen.getByText("Firma precargada (baúl)")).toBeInTheDocument();
    expect(screen.getByText("panel-organismos")).toBeInTheDocument();
  });
});

describe("HU #14357 AC4 — Administrador de compañía", () => {
  it("caracterización: no monta la sección de organismos y entra a Representantes y Mandatarios", async () => {
    const user = userEvent.setup();
    render(
      <CompanyConfigTabs
        settings={settings}
        onSaveSettings={vi.fn()}
        restrictedToRepresentatives
        otSlot={<div>panel-organismos</div>}
        legalRepresentativesSlot={<div>panel-representantes</div>}
        mandatariosSlot={<div>panel-mandatarios</div>}
      />,
    );

    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Representantes legales",
      "Mandatarios",
    ]);
    expect(screen.queryByText("panel-organismos")).not.toBeInTheDocument();
    expect(screen.getByText("panel-representantes")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /mandatarios/i }));
    expect(screen.getByText("panel-mandatarios")).toBeInTheDocument();
  });
});

describe("HU #14357 AC5 — la tabla carga sin tocar las rutas de convenios", () => {
  it.each(["editable", "superadmin-concession", "superadmin-marca-blanca"] as const)(
    "modo %s: tabla completa, sin mensaje de error y sin peticiones a transit-agreements",
    async (mode) => {
      render(<OTConfigTablePanel tenantId={TENANT} mode={mode} />);
      await screen.findByText("Organismo 0");

      expect(rutasInvocadas().filter((r) => r.includes("transit-agreements"))).toEqual([]);
      expect(
        screen.queryByText(/no se pudieron cargar los organismos/i),
      ).not.toBeInTheDocument();
      expect(screen.getByRole("table")).toBeInTheDocument();
    },
  );

  it("el cliente de la API ya no expone funciones de convenio", async () => {
    const cliente: Record<string, unknown> = await import("@/lib/api/admin-companies");

    expect(cliente.fetchTransitAgreements).toBeUndefined();
    expect(cliente.setTransitAgreement).toBeUndefined();
    expect(cliente.fetchTransitGrants).toBeTypeOf("function");
  });
});

describe("HU #14357 AC7 — el wizard habla del permiso del organismo y no cambia", () => {
  // Guarda de texto y no render: `OrganismoInfoCard` es interna de FirmaFurStep y montar el paso de
  // firma exige todo el trámite. El aviso del paso 1 se verifica renderizado en hu11199-secretaria-paso-uno.
  const fuente = (relativa: string) =>
    readFileSync(path.resolve(__dirname, "../../../../..", relativa), "utf8");

  it("caracterización: «Convenio activo» sigue en el paso de firma", () => {
    expect(fuente("components/operacion/FirmaFurStep.tsx")).toContain(
      "Convenio activo · Radicación electrónica habilitada",
    );
  });

  it("caracterización: «activación del convenio» sigue en el asistente", () => {
    expect(fuente("components/operacion/TramiteWizard.tsx")).toContain(
      "Solicita al administrador la activación del convenio.",
    );
  });
});

const settings: TenantSettings = {
  tenantId: "t1",
  switchesMatricula: {
    allowInitialRegistration: true,
    allowMiscNewVehicles: false,
    onlyOwnVehicles: true,
    onlyOwnVehiclesByFamily: { matriculas: true, traspaso: true, otros: true },
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  },
  baulFirmasActivo: true,
  preasignacionPlacaActiva: false,
  validarSoatConRunt: false,
  enrutamientoSMTP: "FLIT_SMTP",
  notificationTarget: "COMPRADOR",
  metodosRecaudo: ["Pasarela FLIT"],
};
