// Feature #14420 / ADR-0077 — cliente de la invitación masiva por Excel (HU #14426).
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

const mocks = vi.hoisted(() => ({ getToken: vi.fn(() => "jwt-test"), downloadFile: vi.fn() }));
vi.mock("../client", async () => {
  const actual = await vi.importActual<typeof import("../client")>("../client");
  return { ...actual, getToken: mocks.getToken };
});
vi.mock("../download", () => ({ downloadFile: mocks.downloadFile }));

import {
  BULK_INVITE_MAX_FILE_BYTES,
  BULK_INVITE_MAX_ROWS,
  BulkInvitationsNetworkError,
  createOtBulkInvitationsClient,
  createSecurityBulkInvitationsClient,
  normalizeBulkInvitationReport,
} from "../bulk-invitations-client";
import { ApiError } from "../types";

const originalFetch = global.fetch;

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const file = () => new File(["x"], "usuarios.xlsx");

beforeEach(() => {
  vi.clearAllMocks();
  mocks.getToken.mockReturnValue("jwt-test");
});
afterEach(() => {
  global.fetch = originalFetch;
  vi.useRealTimers();
});

describe("constantes alineadas con ADR-0077", () => {
  it("50 filas y 1 MB", () => {
    expect(BULK_INVITE_MAX_ROWS).toBe(50);
    expect(BULK_INVITE_MAX_FILE_BYTES).toBe(1024 * 1024);
  });
});

describe("cliente Security (perfil Gestor)", () => {
  it("POST /security/invitations/bulk?dryRun=true con multipart `file`, JWT y targetTenantId del SuperAdmin", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ total: 1, rows: [] }));
    global.fetch = fetchMock as never;

    await createSecurityBulkInvitationsClient({ targetTenantId: "tenant-9" }).submit(file(), { dryRun: true });

    const [url, init] = fetchMock.mock.calls[0];
    const parsed = new URL(String(url));
    expect(parsed.pathname).toBe("/api/v1/security/invitations/bulk");
    expect(parsed.searchParams.get("dryRun")).toBe("true");
    expect(parsed.searchParams.get("targetTenantId")).toBe("tenant-9");
    expect(init.method).toBe("POST");
    expect(init.headers).toEqual({ Authorization: "Bearer jwt-test" });
    expect((init.body as FormData).get("file")).toBeInstanceOf(File);
  });

  it("el commit manda dryRun=false y AdminCompany no manda targetTenantId", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ total: 0, rows: [] }));
    global.fetch = fetchMock as never;

    await createSecurityBulkInvitationsClient().submit(file(), { dryRun: false });

    const parsed = new URL(String(fetchMock.mock.calls[0][0]));
    expect(parsed.searchParams.get("dryRun")).toBe("false");
    expect(parsed.searchParams.has("targetTenantId")).toBe(false);
  });

  it("descarga la plantilla en GET /security/invitations/bulk/template acotada al destino", async () => {
    await createSecurityBulkInvitationsClient({ targetTenantId: "tenant-9" }).downloadTemplate();

    expect(mocks.downloadFile).toHaveBeenCalledWith(
      "/api/v1/security/invitations/bulk/template",
      expect.objectContaining({ query: { targetTenantId: "tenant-9" } }),
    );
  });
});

describe("cliente OT", () => {
  it("usa /admin/ot/users/invite/bulk con transitOfficeId", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ total: 0, rows: [] }));
    global.fetch = fetchMock as never;
    const client = createOtBulkInvitationsClient({ transitOfficeId: "ot-1" });

    await client.submit(file(), { dryRun: true });
    await client.downloadTemplate();

    const parsed = new URL(String(fetchMock.mock.calls[0][0]));
    expect(parsed.pathname).toBe("/api/v1/admin/ot/users/invite/bulk");
    expect(parsed.searchParams.get("transitOfficeId")).toBe("ot-1");
    expect(mocks.downloadFile).toHaveBeenCalledWith(
      "/api/v1/admin/ot/users/invite/bulk/template",
      expect.objectContaining({ query: { transitOfficeId: "ot-1" } }),
    );
  });
});

describe("errores", () => {
  it("un 403 llega como ApiError con el cuerpo del backend y sin datos del archivo", async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ error: "FORBIDDEN" }, 403)) as never;

    const err = await createOtBulkInvitationsClient()
      .submit(file(), { dryRun: true })
      .catch((e) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(403);
    expect(err.body).toMatchObject({ error: "FORBIDDEN" });
  });

  it("una respuesta no-JSON (504 del nginx) también llega como ApiError", async () => {
    global.fetch = vi.fn().mockResolvedValue(new Response("<html>504</html>", { status: 504 })) as never;

    const err = await createSecurityBulkInvitationsClient()
      .submit(file(), { dryRun: false })
      .catch((e) => e);

    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(504);
  });

  it("una caída de red se convierte en BulkInvitationsNetworkError(network)", async () => {
    global.fetch = vi.fn().mockRejectedValue(new TypeError("Failed to fetch")) as never;

    const err = await createSecurityBulkInvitationsClient()
      .submit(file(), { dryRun: false })
      .catch((e) => e);

    expect(err).toBeInstanceOf(BulkInvitationsNetworkError);
    expect(err.kind).toBe("network");
  });

  it("agotado el tiempo máximo aborta y reporta timeout", async () => {
    vi.useFakeTimers();
    global.fetch = vi.fn(
      (_url: string, init?: RequestInit) =>
        new Promise((_resolve, reject) => {
          init?.signal?.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
        }),
    ) as never;

    const pending = createSecurityBulkInvitationsClient()
      .submit(file(), { dryRun: false })
      .catch((e) => e);
    await vi.advanceTimersByTimeAsync(70_000);

    const err = await pending;
    expect(err).toBeInstanceOf(BulkInvitationsNetworkError);
    expect(err.kind).toBe("timeout");
  });

  it("si el llamador aborta (cambio de contexto) se propaga el AbortError, no un error de red", async () => {
    const controller = new AbortController();
    global.fetch = vi.fn(
      (_url: string, init?: RequestInit) =>
        new Promise((_resolve, reject) => {
          init?.signal?.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
        }),
    ) as never;

    const pending = createSecurityBulkInvitationsClient()
      .submit(file(), { dryRun: true, signal: controller.signal })
      .catch((e) => e);
    controller.abort();

    const err = await pending;
    expect(err).not.toBeInstanceOf(BulkInvitationsNetworkError);
    expect((err as Error).name).toBe("AbortError");
  });
});

describe("normalizeBulkInvitationReport", () => {
  it("lee total/creadas/rechazadas y el detalle por fila", () => {
    const report = normalizeBulkInvitationReport(
      {
        total: 3,
        created: 2,
        rejected: 1,
        rows: [
          { rowNumber: 2, email: "a@x.co", fullName: "A", roleName: "Radicador", status: "created" },
          { rowNumber: 3, email: "b@x.co", status: "created" },
          { rowNumber: 4, email: "c@x.co", status: "rejected", errorCode: "EMAIL_ALREADY_IN_USE" },
        ],
      },
      false,
    );

    expect(report).toMatchObject({ dryRun: false, total: 3, created: 2, rejected: 1, notProcessed: 0, valid: 2 });
    expect(report.rows[2]).toMatchObject({ rowNumber: 4, status: "rejected", errorCode: "EMAIL_ALREADY_IN_USE" });
  });

  it("deriva los conteos de las filas cuando faltan y en dryRun nada está «creado»", () => {
    const report = normalizeBulkInvitationReport(
      {
        items: [
          { rowNumber: 2, email: "a@x.co" },
          { rowNumber: 3, email: "b@x.co", code: "invalid_email" },
          { rowNumber: 4, email: "c@x.co", status: "not_processed" },
        ],
      },
      true,
    );

    expect(report).toMatchObject({ dryRun: true, total: 3, valid: 1, created: 0, rejected: 1, notProcessed: 1 });
    expect(report.rows.map((r) => r.status)).toEqual(["valid", "rejected", "not_processed"]);
  });

  it("tolera un cuerpo vacío o inválido", () => {
    expect(normalizeBulkInvitationReport(null, true)).toMatchObject({ total: 0, rows: [] });
    expect(normalizeBulkInvitationReport("x", false)).toMatchObject({ total: 0, created: 0, rows: [] });
  });
});
