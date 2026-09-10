// HU #10796 (Feature #10792) — correlación bidireccional trámite ↔ LOG QX:
//  · AC1: enlace "Ver LOG QX" en el detalle del trámite (solo con permiso + radicación) y deep-link
//         que auto-filtra el módulo por instanceId.
//  · AC2: desde una radicación del LOG QX, enlace de vuelta al detalle del trámite.
//  · AC3: sin permiso o sin radicación → no se ofrece enlace (sin enlace roto).
// La capa de datos y los permisos se mockean (sin red real).
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";

// next/link → <a> simple para poder consultar el href sin router de Next.
vi.mock("next/link", () => ({
  default: (props: { href: string; children: ReactNode; "aria-label"?: string; className?: string }) => (
    <a href={props.href} aria-label={props["aria-label"]} className={props.className}>
      {props.children}
    </a>
  ),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() }),
}));

const mocks = vi.hoisted(() => ({
  fetchLogQx: vi.fn(),
  fetchLogQxBandeja: vi.fn(),
  usePermissions: vi.fn(),
}));
vi.mock("@/lib/api/admin-log-qx", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-log-qx")>();
  return {
    ...actual,
    fetchLogQx: mocks.fetchLogQx,
    fetchLogQxBandeja: mocks.fetchLogQxBandeja,
  };
});
vi.mock("@/hooks/usePermissions", () => ({ usePermissions: mocks.usePermissions }));

import { LogQxLink } from "@/components/operacion/LogQxLink";
import { LogQx } from "@/components/atom/modules/LogQx";

const INSTANCE = "22222222-2222-2222-2222-222222222222";


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
  // El módulo se rediseñó como bandeja (HU #11788), pero la correlación desde el detalle del
  // trámite sigue vigente: el deep-link ahora ACOTA la bandeja a ese trámite en vez de disparar
  // una búsqueda por eje.
  const BANDEJA = {
    data: [
      {
        procedureInstanceId: INSTANCE,
        referenceNumber: "TRM-2026-000001",
        plate: "ABC123",
        procedureTypeName: "Traspaso",
        estado: "aprobado" as const,
        clientTenantName: "Renting del Café S.A.S.",
        transitOfficeName: "Bogotá",
        divipoCode: "05001",
        documentoQx: "FLIT_TRM-2026-000001",
        submissionId: "11111111-1111-1111-1111-111111111111",
        intentos: 1,
        attempts: 1,
        pollCount: 1,
        qxRegisterCode: 81,
        qxProcedureCode: 2,
        rejectionReason: null,
        ultimaActividad: "2026-07-01T12:00:00Z",
        esperandoDesde: null,
        horasEsperando: null,
        submissionCreatedAt: "2026-07-01T12:00:00Z",
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 25,
    contadores: [],
  };

  beforeEach(() => {
    mocks.fetchLogQxBandeja.mockReset();
  });

  it("AC1 (deep-link): con initialInstanceId acota la bandeja a ese trámite al montar", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(BANDEJA);

    render(<LogQx initialInstanceId={INSTANCE} />);

    await waitFor(() =>
      expect(mocks.fetchLogQxBandeja).toHaveBeenCalledWith(
        expect.objectContaining({ instanceId: INSTANCE }),
      ),
    );
    expect(await screen.findByText("TRM-2026-000001")).toBeInTheDocument();
  });

  it("AC1 (deep-link): se explica por qué la lista está acotada y se puede quitar el filtro", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(BANDEJA);

    render(<LogQx initialInstanceId={INSTANCE} />);

    expect(await screen.findByText(/Mostrando solo el trámite/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Ver todos/i }));
    await waitFor(() =>
      expect(mocks.fetchLogQxBandeja).toHaveBeenLastCalledWith(
        expect.objectContaining({ instanceId: undefined }),
      ),
    );
  });

  it("AC2 (back-link): el vistazo de la fila enlaza al detalle del trámite /tramites/{id}", async () => {
    mocks.fetchLogQxBandeja.mockResolvedValue(BANDEJA);

    render(<LogQx initialInstanceId={INSTANCE} />);

    // El enlace vive en el vistazo, así que primero hay que expandir la fila.
    fireEvent.click(await screen.findByRole("button", { name: /TRM-2026-000001/i }));

    const link = await screen.findByRole("link", { name: /Ver trámite/i });
    expect(link).toHaveAttribute("href", `/tramites/${INSTANCE}`);
  });
});
