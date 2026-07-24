// HU #10194 — consolidación de la config de OT en una tabla con menú de acciones. Cubre:
// (a) la tabla lista OT con columnas Organismo/Código/Estado/Acciones; (b) el switch de
// Estado habilita/deshabilita (add/removeTransitGrant, UI optimista); (c) un OT no operable
// no se puede habilitar (switch deshabilitado + badge); (d) el menú "⋯ Acciones" → único
// ítem "Configurar" abre el modal UNIFICADO (bloqueos + restricciones de consulta en el
// mismo modal) y se puede togglear un criterio de cada sección; (e) "Configurar" está
// deshabilitado si el OT no tiene grant. API y toast mockeados.
//
// Uso de ejemplo:
//   render(<OTConfigTablePanel tenantId={TENANT} />);
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OTConfigTablePanel } from "../OTConfigTablePanel";
import type { OtBlockingPolicy, OtConsultationRestriction, TransitOffice } from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";
import type { TransitOfficeOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
  fetchTransitGrants: vi.fn(),
  addTransitGrant: vi.fn(),
  removeTransitGrant: vi.fn(),
  fetchOtBlockingPolicies: vi.fn(),
  setOtBlockingPolicy: vi.fn(),
  fetchOtConsultationRestrictions: vi.fn(),
  setOtConsultationRestriction: vi.fn(),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficesOperationalStatus: vi.fn(),
}));

const show = vi.fn();
vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show }),
}));

import {
  addTransitGrant,
  fetchOtBlockingPolicies,
  fetchOtConsultationRestrictions,
  fetchTransitGrants,
  fetchTransitOffices,
  removeTransitGrant,
  setOtBlockingPolicy,
  setOtConsultationRestriction,
} from "@/lib/api/admin-companies";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

const TENANT = "aaaaaaaa-0000-4000-8000-000000000001";

const offices: TransitOffice[] = [
  { id: "o1", code: "11001", name: "Secretaría de Movilidad Bogotá", departmentCode: "11", cityCode: "11001" },
  { id: "o2", code: "05001", name: "Medellín — Secretaría de Movilidad", departmentCode: "05", cityCode: "05001" },
];

/** Arranca el panel con catálogo completo y los datos dados; el resto en blanco. */
function arrange(opts: {
  grantedIds?: string[];
  operational?: TransitOfficeOperationalStatus[];
  policies?: OtBlockingPolicy[];
  restrictions?: OtConsultationRestriction[];
}) {
  vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
  vi.mocked(fetchTransitGrants).mockResolvedValue({ transitOfficeIds: opts.grantedIds ?? [] });
  vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue(opts.operational ?? []);
  vi.mocked(fetchOtBlockingPolicies).mockResolvedValue(opts.policies ?? []);
  vi.mocked(fetchOtConsultationRestrictions).mockResolvedValue(opts.restrictions ?? []);
}

describe("OTConfigTablePanel (HU #10194 — tabla consolidada de OT)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("(a) lista los OT en una tabla real con columnas Organismo/Código/Estado/Acciones", async () => {
    arrange({ grantedIds: ["o1"] });
    render(<OTConfigTablePanel tenantId={TENANT} />);

    expect(screen.getByTestId("ui-loading")).toBeInTheDocument();

    const table = await screen.findByRole("table", { name: /organismos de tránsito/i });
    expect(screen.queryByTestId("ui-loading")).not.toBeInTheDocument();

    expect(within(table).getByRole("columnheader", { name: /organismo/i })).toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: /código/i })).toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: /estado/i })).toBeInTheDocument();
    expect(within(table).getByRole("columnheader", { name: /acciones/i })).toBeInTheDocument();

    expect(within(table).getByText("Secretaría de Movilidad Bogotá")).toBeInTheDocument();
    expect(within(table).getByText("11001")).toBeInTheDocument();
    expect(within(table).getByText("Medellín — Secretaría de Movilidad")).toBeInTheDocument();
  });

  it("muestra el estado de error si falla la carga", async () => {
    vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
    vi.mocked(fetchTransitGrants).mockRejectedValue(new Error("boom"));
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue([]);
    vi.mocked(fetchOtBlockingPolicies).mockResolvedValue([]);
    vi.mocked(fetchOtConsultationRestrictions).mockResolvedValue([]);
    render(<OTConfigTablePanel tenantId={TENANT} />);

    expect(await screen.findByTestId("ui-error")).toBeInTheDocument();
  });

  it("(b) el switch de Estado habilita (POST) y deshabilita (DELETE) con UI optimista", async () => {
    const user = userEvent.setup();
    arrange({ grantedIds: ["o1"] });
    vi.mocked(addTransitGrant).mockResolvedValue(undefined);
    vi.mocked(removeTransitGrant).mockResolvedValue(undefined);
    render(<OTConfigTablePanel tenantId={TENANT} />);

    await screen.findByText("Secretaría de Movilidad Bogotá");

    const bogotaSwitch = screen.getByRole("switch", { name: /deshabilitar secretaría de movilidad bogotá/i });
    expect(bogotaSwitch).toBeChecked();
    await user.click(bogotaSwitch);
    await waitFor(() => expect(removeTransitGrant).toHaveBeenCalledWith(TENANT, "o1"));
    await waitFor(() => expect(bogotaSwitch).not.toBeChecked());

    const medellinSwitch = screen.getByRole("switch", { name: /habilitar medellín/i });
    await user.click(medellinSwitch);
    await waitFor(() => expect(addTransitGrant).toHaveBeenCalledWith(TENANT, "o2"));
  });

  it("(c) un OT sin alta/inactivo en FLIT no se puede habilitar: switch deshabilitado + badge", async () => {
    arrange({
      grantedIds: [],
      operational: [
        {
          id: "o2",
          code: "05001",
          name: "Medellín",
          departmentCode: "05",
          hasTenant: false,
          tenantId: null,
          estadoActivo: null,
          operationMode: null,
          divipoCode: null,
          quipuxRegistration: false,
          quipuxTransfer: false,
          quipuxOther: false,
        },
      ],
    });
    render(<OTConfigTablePanel tenantId={TENANT} />);

    await screen.findByText("Secretaría de Movilidad Bogotá");

    const medellinSwitch = screen.getByRole("switch", { name: /habilitar medellín/i });
    expect(medellinSwitch).toBeDisabled();
    expect(screen.getByText(/Sin alta en FLIT/i)).toBeInTheDocument();

    const bogotaSwitch = screen.getByRole("switch", { name: /habilitar secretaría de movilidad bogotá/i });
    expect(bogotaSwitch).not.toBeDisabled();
  });

  it("(d) el menú «⋯ Acciones» → «Configurar» abre el modal unificado con ambas secciones y togglea un criterio de cada una", async () => {
    const user = userEvent.setup();
    arrange({ grantedIds: ["o1"] });
    vi.mocked(setOtBlockingPolicy).mockResolvedValue(undefined);
    vi.mocked(setOtConsultationRestriction).mockResolvedValue(undefined);
    render(<OTConfigTablePanel tenantId={TENANT} />);

    await screen.findByText("Secretaría de Movilidad Bogotá");

    await user.click(screen.getByRole("button", { name: /acciones para secretaría de movilidad bogotá/i }));
    await user.click(await screen.findByRole("menuitem", { name: /^configurar$/i }));

    const dialog = await screen.findByRole("dialog", { name: /configurar — secretaría de movilidad bogotá/i });
    // Ambas secciones conviven en el mismo modal.
    expect(within(dialog).getByRole("heading", { name: /^bloqueos$/i })).toBeInTheDocument();
    expect(within(dialog).getByRole("heading", { name: /restricciones de consulta/i })).toBeInTheDocument();

    // Sección Bloqueos: SOAT arranca ON (default bloquea, sin fila configurada).
    const soatSwitch = within(dialog).getByRole("switch", { name: /soat vencido/i });
    expect(soatSwitch).toBeChecked();
    await user.click(soatSwitch);
    await waitFor(() => expect(setOtBlockingPolicy).toHaveBeenCalledWith(TENANT, "o1", "soat", false));
    expect(soatSwitch).not.toBeChecked();

    // Sección Restricciones de consulta: RNMC arranca OFF (opt-in, sin fila configurada). El
    // criterio "RNMC" existe en AMBAS secciones (bloqueos y restricciones) con el mismo label,
    // así que se acota la búsqueda al grupo de restricciones (`ot-restrictions-o1`).
    const restrictionsSection = within(dialog).getByTestId("ot-restrictions-o1");
    const rnmcSwitch = within(restrictionsSection).getByRole("switch", { name: /rnmc/i });
    expect(rnmcSwitch).not.toBeChecked();
    await user.click(rnmcSwitch);
    await waitFor(() =>
      expect(setOtConsultationRestriction).toHaveBeenCalledWith(TENANT, "o1", "rnmc", true),
    );
    expect(rnmcSwitch).toBeChecked();
  });

  it("revierte el switch de bloqueos del modal unificado y avisa si falla la persistencia", async () => {
    const user = userEvent.setup();
    arrange({ grantedIds: ["o1"] });
    const serverMessage = "Este organismo no está habilitado para la compañía.";
    vi.mocked(setOtBlockingPolicy).mockRejectedValue(
      new ApiValidationError([{ field: "transitOfficeId", message: serverMessage }], 422),
    );
    render(<OTConfigTablePanel tenantId={TENANT} />);

    await screen.findByText("Secretaría de Movilidad Bogotá");
    await user.click(screen.getByRole("button", { name: /acciones para secretaría de movilidad bogotá/i }));
    await user.click(await screen.findByRole("menuitem", { name: /^configurar$/i }));

    const dialog = await screen.findByRole("dialog", { name: /configurar — secretaría de movilidad bogotá/i });
    const soatSwitch = within(dialog).getByRole("switch", { name: /soat vencido/i });
    await user.click(soatSwitch);

    await waitFor(() => expect(soatSwitch).toBeChecked());
    expect(show).toHaveBeenCalledWith(serverMessage, "error");
  });

  it("(e) «Configurar» está deshabilitado si el OT no tiene grant", async () => {
    const user = userEvent.setup();
    arrange({ grantedIds: ["o1"] }); // o2 sin grant
    render(<OTConfigTablePanel tenantId={TENANT} />);

    await screen.findByText("Secretaría de Movilidad Bogotá");

    await user.click(screen.getByRole("button", { name: /acciones para medellín/i }));
    const configItem = await screen.findByRole("menuitem", { name: /^configurar$/i });
    expect(configItem).toBeDisabled();

    // El OT habilitado sí tiene la acción disponible.
    await user.click(screen.getByRole("button", { name: /acciones para secretaría de movilidad bogotá/i }));
    expect(await screen.findByRole("menuitem", { name: /^configurar$/i })).not.toBeDisabled();
  });
});
