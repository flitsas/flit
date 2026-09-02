import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AdminFurPage from "@/app/admin/plataforma/fur/page";

const push = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push, replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock("@/lib/api/superadmin-client", () => ({
  superadminClient: {
    listProcedureTypes: vi.fn().mockResolvedValue([]),
  },
}));

vi.mock("@/lib/api/admin-plataforma-fur", () => ({
  listFurClassifications: vi.fn().mockResolvedValue([]),
  fetchFurPreview: vi.fn(),
}));

describe("AdminFurPage", () => {
  beforeEach(() => {
    push.mockClear();
  });

  it("renderiza el simulador en lugar del 404 placeholder", () => {
    render(<AdminFurPage />);
    expect(screen.getByRole("heading", { name: /^fur$/i })).toBeInTheDocument();
    expect(screen.getByTestId("fur-simulator-panel")).toBeInTheDocument();
    expect(screen.queryByTestId("admin-fur-not-found")).not.toBeInTheDocument();
  });
});
