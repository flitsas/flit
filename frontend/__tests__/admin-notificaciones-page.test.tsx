import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AdminNotificacionesPage from "@/app/admin/plataforma/notificaciones/page";

const push = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push, replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock("@/lib/api/admin-plataforma-notificaciones", () => ({
  listNotificationTemplates: vi.fn().mockResolvedValue([]),
  listNotificationChannels: vi.fn().mockResolvedValue([]),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchCompaniesIndex: vi.fn().mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 50 }),
}));

describe("AdminNotificacionesPage", () => {
  beforeEach(() => {
    push.mockClear();
  });

  it("renderiza el panel del banco de pruebas en lugar del 404 placeholder", () => {
    render(<AdminNotificacionesPage />);

    expect(screen.getByRole("heading", { name: /^notificaciones$/i })).toBeInTheDocument();
    expect(screen.getByTestId("notificaciones-bank-panel")).toBeInTheDocument();
    expect(screen.queryByTestId("admin-notificaciones-not-found")).not.toBeInTheDocument();
  });
});
