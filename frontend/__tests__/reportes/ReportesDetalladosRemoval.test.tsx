// HU #11112 — Big-bang: ReportesDetallados eliminado, dock limpio, sin redirect (ADR-0038).
import { describe, expect, it, vi } from "vitest";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { render, screen } from "@testing-library/react";
import { Shell } from "@/components/atom/Shell";
import { ALL_MODULE_IDS, buildValidModules, parseModule } from "@/lib/nav/modules";

vi.mock("next/navigation", () => ({ usePathname: () => "/" }));

const frontendRoot = path.resolve(__dirname, "../..");
const pageSource = readFileSync(path.join(frontendRoot, "app/page.tsx"), "utf8");

describe("HU #11112 AC1 — dock sin reportes-detallados", () => {
  it("no muestra icono reportes-detallados y solo hay un botón Reportes", () => {
    render(
      <Shell
        active="reportes"
        onNav={vi.fn()}
        visibleModuleCodes={[...ALL_MODULE_IDS]}
      >
        <div>contenido</div>
      </Shell>,
    );

    expect(screen.getAllByRole("button", { name: "Reportes" })).toHaveLength(1);
    expect(screen.queryByRole("button", { name: /reportes detallados/i })).not.toBeInTheDocument();
    expect(screen.queryByTestId("dock-reportes-detallados")).not.toBeInTheDocument();
  });

  it("ALL_MODULE_IDS no incluye reportes-detallados", () => {
    expect(ALL_MODULE_IDS).toContain("reportes");
    expect(ALL_MODULE_IDS as string[]).not.toContain("reportes-detallados");
    expect(ALL_MODULE_IDS.filter((id) => id === "reportes")).toHaveLength(1);
  });
});

describe("HU #11112 AC2 — sin archivos del módulo legado", () => {
  it("ReportesDetallados.tsx y detailed-report.ts no existen", () => {
    expect(
      existsSync(path.join(frontendRoot, "components/atom/modules/ReportesDetallados.tsx")),
    ).toBe(false);
    expect(existsSync(path.join(frontendRoot, "lib/api/detailed-report.ts"))).toBe(false);
    expect(
      existsSync(path.join(frontendRoot, "components/atom/modules/_reportesDetallados")),
    ).toBe(false);
  });

  it("page.tsx no importa ReportesDetallados ni detailed-report", () => {
    expect(pageSource).not.toMatch(/ReportesDetallados/);
    expect(pageSource).not.toMatch(/detailed-report/);
    expect(pageSource).not.toMatch(/reportes-detallados/);
  });
});

describe("HU #11112 AC3 — ?m=reportes-detallados sin redirect ni componente", () => {
  it("parseModule cae a dashboard (big-bang sin redirect)", () => {
    const valid = buildValidModules(["dashboard", "reportes", "tramites"]);
    expect(parseModule("reportes-detallados", valid)).toBe("dashboard");
  });

  it("page.tsx no contiene redirect de reportes-detallados", () => {
    expect(pageSource).not.toMatch(/reportes-detallados/);
    expect(pageSource).not.toMatch(/redirect\s*\(/);
  });
});

describe("HU #11112 AC4 — tests del módulo eliminado no bloquean CI", () => {
  it("el spec unitario legado reportes-detallados.test.tsx no existe", () => {
    expect(existsSync(path.join(frontendRoot, "__tests__/reportes-detallados.test.tsx"))).toBe(
      false,
    );
  });
});
