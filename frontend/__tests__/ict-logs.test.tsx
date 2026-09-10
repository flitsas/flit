// IctLogs (HU #11619): tras separar Reportes ICT en su propio módulo, "Log ICT" se queda
// solo con las pestañas Logs y Alertas ICT — las Consultas y la Programación viven ahora en
// IctReports (ver ict-reports.test.tsx).
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/lib/api/ict-client", () => ({
  fetchIctLogs: vi.fn().mockResolvedValue({ items: [], total: 0 }),
  fetchIctAlerts: vi.fn().mockResolvedValue({
    stuckInValidation: 0,
    noveltyRatePct: 0,
    webhookDeliveryFailures: 0,
    jobsOutOfSla: 0,
  }),
}));

vi.mock("@/lib/api/analytics-scheduling", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/analytics-scheduling")>();
  return {
    ...actual,
    fetchAlertRules: vi.fn().mockResolvedValue({ items: [] }),
    fetchAlertEvents: vi.fn().mockResolvedValue({ items: [], totalCount: 0 }),
  };
});

import { IctLogs } from "@/components/atom/modules/IctLogs";

describe("IctLogs — solo Logs y Alertas ICT (HU #11619)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("muestra únicamente las pestañas Logs y Alertas ICT, con Logs activa por defecto", () => {
    render(<IctLogs />);
    expect(screen.getByRole("tab", { name: "Logs" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Alertas ICT" })).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Consultas" })).not.toBeInTheDocument();
  });

  it("no ofrece el botón de Programación (se movió a Reportes ICT)", () => {
    render(<IctLogs />);
    expect(screen.queryByTestId("ict-abrir-programacion")).not.toBeInTheDocument();
  });
});
