// HU #12883 AC4 — título de la página por rol: ot_admin ("solo ordena") ve "Prelación
// documental"; Super Admin conserva "Documentos y prelación". Mismo helper de rol que
// DocumentsSection.tsx (getToken + decodeJwtPayload + isSuperAdmin), aquí resuelto en el propio
// componente cliente de la página (no hay Server Component de por medio).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import OtDocumentsPage from "../page";

vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "ot-1" }),
}));

vi.mock("@/components/admin/transit-offices/DocumentsSection", () => ({
  DocumentsSection: () => <div data-testid="documents-section-stub" />,
}));

vi.mock("@/components/admin/transit-offices/OtHubLayout", () => ({
  OtHubLayout: ({
    moduleTitle,
    children,
  }: {
    moduleTitle: string;
    children: React.ReactNode;
  }) => (
    <div>
      <h1>{moduleTitle}</h1>
      {children}
    </div>
  ),
}));

vi.mock("@/lib/api/client", () => ({
  getToken: vi.fn().mockReturnValue("token"),
}));

const jwtMocks = vi.hoisted(() => ({
  decodeJwtPayload: vi.fn().mockReturnValue({}),
  isSuperAdmin: vi.fn().mockReturnValue(false),
}));

vi.mock("@/lib/auth/jwt", () => jwtMocks);

describe("OtDocumentsPage — HU #12883 AC4 (título por rol)", () => {
  beforeEach(() => {
    jwtMocks.isSuperAdmin.mockReset();
  });

  it("ot_admin ve «Administración OT — Prelación documental»", async () => {
    jwtMocks.isSuperAdmin.mockReturnValue(false);
    render(<OtDocumentsPage />);

    expect(
      await screen.findByRole("heading", { name: "Administración OT — Prelación documental" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Administración OT — Documentos y prelación" }),
    ).not.toBeInTheDocument();
  });

  it("Super Admin conserva «Administración OT — Documentos y prelación»", async () => {
    jwtMocks.isSuperAdmin.mockReturnValue(true);
    render(<OtDocumentsPage />);

    expect(
      await screen.findByRole("heading", { name: "Administración OT — Documentos y prelación" }),
    ).toBeInTheDocument();
  });
});
