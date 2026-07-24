// HU #10796 (Feature #10792) — correlación bidireccional trámite ↔ LOG QX:
//  · AC1: enlace "Ver LOG QX" en el detalle del trámite (solo con permiso + radicación) y deep-link
//         que auto-filtra el módulo por instanceId.
//  · AC2: desde una radicación del LOG QX, enlace de vuelta al detalle del trámite.
//  · AC3: sin permiso o sin radicación → no se ofrece enlace (sin enlace roto).
// La capa de datos y los permisos se mockean (sin red real).
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import type { LogQxEntry, LogQxPage } from "@/lib/api/admin-log-qx";

// next/link → <a> simple para poder consultar el href sin router de Next.
vi.mock("next/link", () => ({
  default: (props: { href: string; children: ReactNode; "aria-label"?: string; className?: string }) => (
    <a href={props.href} aria-label={props["aria-label"]} className={props.className}>
      {props.children}
    </a>
  ),
}));

const mocks = vi.hoisted(() => ({ fetchLogQx: vi.fn(), usePermissions: vi.fn() }));
vi.mock("@/lib/api/admin-log-qx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-log-qx")>();
  return { ...actual, fetchLogQx: mocks.fetchLogQx };
});
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: mocks.usePermissions }));

import { LogQxLink } from "@/components/operacion/LogQxLink";
import { LogQx } from "@/components/atom/modules/LogQx";

const INSTANCE = "22222222-2222-2222-2222-222222222222";

const ENTRY: LogQxEntry = {
  id: "11111111-1111-1111-1111-111111111111",
  procedureInstanceId: INSTANCE,
  referenceNumber: "TRM-2026-000001",
  procedureTypeName: "Traspaso",
  clientTenantName: "Renting del Café S.A.S.",
  plate: "ABC123",
  documentName: "FLIT_TRM-2026-000001",
  divipoCode: "05001",
  status: "aprobado",
  attempts: 1,
  pollCount: 1,
  qxRegisterCode: 81,
  qxProcedureCode: 2,
  rejectionReason: null,
  createdAt: "2026-07-01T12:00:00Z",
  registeredAt: null,
  lastPolledAt: null,
  completedAt: null,
  updatedAt: null,
  events: [
    {
      stage: "registro_respuesta",
      outcome: "ok",
      detail: { codigo: 81 },
      durationMs: 1234,
      origin: "quipux_register",
      responseCode: 81,
      correlationId: null,
      occurredAt: "2026-07-01T12:00:00Z",
    },
  ],
};

const PAGE: LogQxPage = { data: [ENTRY], totalCount: 1, page: 1, pageSize: 20 };

function setPerms(p: { permissions?: string[]; isSuperAdmin?: boolean }) {
  mocks.usePermissions.mockReturnValue({
    permissions: p.permissions ?? [],
    isSuperAdmin: p.isSuperAdmin ?? false,
    isAdminCompany: false,
    isOtAdmin: false,
    tenantId: null,
    userId: null,
    roleId: null,
    roleCode: null,
  });
}

describe("LogQxLink — enlace 'Ver LOG QX' del detalle del trámite (HU #10796, AC1/AC3)", () => {
  beforeEach(() => {
    mocks.fetchLogQx.mockReset();
    mocks.usePermissions.mockReset();
  });

  it("AC1: con logqx.read y radicación existente, enlaza a /?m=log-qx&instanceId=", async () => {
    setPerms({ permissions: ["logqx.read"] });
    mocks.fetchLogQx.mockResolvedValue({ data: [], totalCount: 1, page: 1, pageSize: 1 });

    render(<LogQxLink instanceId={INSTANCE} />);

    const link = await screen.findByRole("link", { name: /Ver el LOG QX/i });
    expect(link).toHaveAttribute("href", `/?m=log-qx&instanceId=${INSTANCE}`);
    // La existencia se comprueba reutilizando el endpoint del LOG QX (sin backend nuevo).
    expect(mocks.fetchLogQx).toHaveBeenCalledWith(
      expect.objectContaining({ instanceId: INSTANCE, pageSize: 1 }),
      expect.anything(),
    );
  });

  it("AC3: con permiso pero SIN radicación (totalCount=0) → no ofrece enlace", async () => {
    setPerms({ permissions: ["logqx.read"] });
    mocks.fetchLogQx.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 1 });

    render(<LogQxLink instanceId={INSTANCE} />);

    await waitFor(() => expect(mocks.fetchLogQx).toHaveBeenCalled());
    expect(screen.queryByRole("link", { name: /Ver el LOG QX/i })).not.toBeInTheDocument();
  });

  it("AC3/gate: sin el permiso logqx.read → no ofrece enlace y NO consulta la API", () => {
    setPerms({ permissions: ["tramites.read"] });

    render(<LogQxLink instanceId={INSTANCE} />);

    expect(screen.queryByRole("link", { name: /Ver el LOG QX/i })).not.toBeInTheDocument();
    expect(mocks.fetchLogQx).not.toHaveBeenCalled();
  });

  it("SuperAdmin (bypass) con radicación → muestra el enlace", async () => {
    setPerms({ isSuperAdmin: true });
    mocks.fetchLogQx.mockResolvedValue({ data: [], totalCount: 3, page: 1, pageSize: 1 });

    render(<LogQxLink instanceId={INSTANCE} />);

    expect(await screen.findByRole("link", { name: /Ver el LOG QX/i })).toBeInTheDocument();
  });
});

describe("LogQx — deep-link y back-link (HU #10796, AC1/AC2)", () => {
  beforeEach(() => {
    mocks.fetchLogQx.mockReset();
  });

  it("AC1 (deep-link): con initialInstanceId auto-aplica la búsqueda por instanceId al montar", async () => {
    mocks.fetchLogQx.mockResolvedValue(PAGE);

    render(<LogQx initialInstanceId={INSTANCE} />);

    await waitFor(() =>
      expect(mocks.fetchLogQx).toHaveBeenCalledWith(expect.objectContaining({ instanceId: INSTANCE })),
    );
    expect(await screen.findByText("TRM-2026-000001")).toBeInTheDocument();
  });

  it("AC2 (back-link): cada radicación enlaza al detalle del trámite /tramites/{id}", async () => {
    mocks.fetchLogQx.mockResolvedValue(PAGE);

    render(<LogQx initialInstanceId={INSTANCE} />);

    const link = await screen.findByRole("link", { name: /Ver el trámite TRM-2026-000001/i });
    expect(link).toHaveAttribute("href", `/tramites/${INSTANCE}`);
  });
});
