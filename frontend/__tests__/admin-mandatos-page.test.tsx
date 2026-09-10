import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AdminMandatosPage from "@/app/admin/plataforma/mandatos/page";

const push = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push, replace: vi.fn(), prefetch: vi.fn() }),
}));

describe("AdminMandatosPage", () => {
  beforeEach(() => {
    push.mockClear();
  });

  it("renderiza el visualizador en lugar del 404 placeholder", () => {
    render(<AdminMandatosPage />);

    expect(screen.getByRole("heading", { name: /^mandatos$/i })).toBeInTheDocument();
    expect(screen.getByTestId("mandatos-catalog-panel")).toBeInTheDocument();
    expect(screen.queryByTestId("admin-mandatos-not-found")).not.toBeInTheDocument();
  });
});
