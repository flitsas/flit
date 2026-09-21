import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import {
  DateRangePicker,
  formatRangeDisplay,
  isoToDisplay,
} from "@/components/atom/DateRangePicker";
import { isValidOptionalRange, sinRango, type DateRange } from "@/components/atom/modules/_reportes/range";

describe("DateRangePicker — HU #12724", () => {
  const onChange = vi.fn<(next: DateRange) => void>();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("formato de visualización (AC1)", () => {
    it("formatea ISO a DD/MM/AAAA", () => {
      expect(isoToDisplay("2026-09-07")).toBe("07/09/2026");
    });

    it("muestra rango completo con guión largo", () => {
      expect(formatRangeDisplay({ from: "2026-09-01", to: "2026-09-21" }, "Vacío")).toBe(
        "01/09/2026 – 21/09/2026",
      );
    });

    it("muestra placeholder cuando el rango está vacío", () => {
      render(<DateRangePicker value={sinRango()} onChange={onChange} />);
      expect(screen.getByRole("button", { name: /Rango de fechas/i })).toHaveTextContent(
        "Seleccionar rango",
      );
    });
  });

  describe("popover y teclado (AC1, AC3)", () => {
    it("abre el calendario al hacer clic", async () => {
      const user = userEvent.setup();
      render(<DateRangePicker value={sinRango()} onChange={onChange} />);
      await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
      expect(screen.getByRole("dialog", { name: "Elegir rango de fechas" })).toBeInTheDocument();
    });

    it("abre con Enter y cierra con Escape", async () => {
      const user = userEvent.setup();
      render(<DateRangePicker value={sinRango()} onChange={onChange} />);
      const trigger = screen.getByRole("button", { name: /Rango de fechas/i });
      trigger.focus();
      await user.keyboard("{Enter}");
      expect(screen.getByRole("dialog")).toBeInTheDocument();
      await user.keyboard("{Escape}");
      await waitFor(() => {
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      });
    });

    it("expone aria-label en español", () => {
      render(
        <DateRangePicker
          value={{ from: "2026-09-01", to: "2026-09-15" }}
          onChange={onChange}
        />,
      );
      expect(screen.getByRole("button", { name: /Rango de fechas: 01\/09\/2026 – 15\/09\/2026/i })).toBeInTheDocument();
    });
  });

  describe("rango vacío e inválido (AC2)", () => {
    it("permite limpiar a rango vacío", async () => {
      const user = userEvent.setup();
      render(
        <DateRangePicker value={{ from: "2026-09-01", to: "2026-09-15" }} onChange={onChange} />,
      );
      await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
      await user.click(screen.getByTestId("date-range-clear"));
      expect(onChange).toHaveBeenCalledWith(sinRango());
      expect(isValidOptionalRange(sinRango())).toBe(true);
    });

    it("no emite rango inválido al aplicar y muestra mensaje", async () => {
      const user = userEvent.setup();
      render(
        <DateRangePicker
          value={{ from: "2026-09-20", to: "2026-09-01" }}
          onChange={onChange}
        />,
      );
      await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
      await user.click(screen.getByTestId("date-range-apply"));
      expect(onChange).not.toHaveBeenCalled();
      expect(screen.getByRole("alert")).toHaveTextContent(
        "La fecha final no puede ser anterior a la inicial.",
      );
    });

    it("aplica rango parcial con una sola fecha seleccionada", async () => {
      const user = userEvent.setup();
      render(<DateRangePicker value={sinRango()} onChange={onChange} />);
      await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
      const dialog = screen.getByRole("dialog");
      await user.click(within(dialog).getByTestId("day-2026-09-01"));
      await user.click(within(dialog).getByTestId("date-range-apply"));
      expect(onChange).toHaveBeenCalledWith({ from: "2026-09-01", to: "" });
    });

    it("isValidOptionalRange sigue aceptando rango vacío y parcial", () => {
      expect(isValidOptionalRange(sinRango())).toBe(true);
      expect(isValidOptionalRange({ from: "2026-09-01", to: "" })).toBe(true);
      expect(isValidOptionalRange({ from: "", to: "2026-09-15" })).toBe(true);
      expect(isValidOptionalRange({ from: "2026-09-20", to: "2026-09-01" })).toBe(false);
    });
  });

  describe("DateRangeFilter envoltorio", () => {
    it("conserva el botón Todo el periodo cuando permiteSinRango", async () => {
      const user = userEvent.setup();
      const { DateRangeFilter } = await import("@/components/atom/modules/_reportes/DateRangeFilter");
      const filterChange = vi.fn();
      render(
        <DateRangeFilter
          value={{ from: "2026-09-01", to: "2026-09-15" }}
          onChange={filterChange}
          permiteSinRango
        />,
      );
      await user.click(screen.getByRole("button", { name: "Todo el periodo" }));
      expect(filterChange).toHaveBeenCalledWith(sinRango());
    });
  });

  // --- Cobertura adicional AC1/AC3/AC4/AC5 (dev-tester HU #12724) ---

  describe("contrato DateRange al confirmar (AC1)", () => {
    // Uso de ejemplo: aplicar from≠to → onChange({ from, to }) con ISO YYYY-MM-DD
    it("emite DateRange completo al aplicar dos fechas distintas", async () => {
      const user = userEvent.setup();
      render(<DateRangePicker value={sinRango()} onChange={onChange} />);
      await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
      const dialog = screen.getByRole("dialog");
      await user.click(within(dialog).getByTestId("day-2026-09-01"));
      await user.click(within(dialog).getByTestId("day-2026-09-15"));
      await waitFor(() => {
        expect(onChange).toHaveBeenCalledWith({ from: "2026-09-01", to: "2026-09-15" });
      });
      const emitted = onChange.mock.calls.at(-1)![0];
      expect(emitted).toHaveProperty("from");
      expect(emitted).toHaveProperty("to");
    });
  });

  describe("teclado Espacio y tokens dark (AC3)", () => {
    // Uso de ejemplo: foco + Space abre dialog; popover lleva clases dark:*
    it("abre el calendario con Espacio", async () => {
      const user = userEvent.setup();
      render(<DateRangePicker value={sinRango()} onChange={onChange} />);
      const trigger = screen.getByRole("button", { name: /Rango de fechas/i });
      trigger.focus();
      await user.keyboard(" ");
      expect(screen.getByRole("dialog", { name: "Elegir rango de fechas" })).toBeInTheDocument();
    });

    it("el popover declara tokens dark del sistema", async () => {
      const user = userEvent.setup();
      render(<DateRangePicker value={sinRango()} onChange={onChange} />);
      await user.click(screen.getByRole("button", { name: /Rango de fechas/i }));
      const dialog = screen.getByRole("dialog");
      expect(dialog.className).toMatch(/dark:bg-\[#0B0F14\]/);
      expect(dialog.className).toMatch(/dark:text-white/);
      expect(dialog.className).toMatch(/dark:border-white\/15/);
    });
  });

  describe("migración DateRangeFilter sin inputs date (AC4)", () => {
    // Uso de ejemplo: DateRangeFilter → DateRangePicker; cero input[type=date]
    it("renderiza DateRangePicker y no usa input type=date", async () => {
      const { DateRangeFilter } = await import("@/components/atom/modules/_reportes/DateRangeFilter");
      const { container } = render(
        <DateRangeFilter value={sinRango()} onChange={onChange} />,
      );
      expect(screen.getByTestId("date-range-picker")).toBeInTheDocument();
      expect(container.querySelectorAll('input[type="date"]')).toHaveLength(0);
      expect(screen.getByRole("button", { name: /Rango de fechas/i })).toBeInTheDocument();
    });
  });

  describe("dependencia react-day-picker (AC5)", () => {
    // Uso de ejemplo: package.json declara react-day-picker y no otra lib de fechas de UI
    it("declara react-day-picker y no agrega otra librería de calendario", async () => {
      const pkg = await import("../../../package.json");
      const deps = { ...pkg.default.dependencies, ...pkg.default.devDependencies };
      expect(deps["react-day-picker"]).toBeDefined();
      expect(deps["@mui/x-date-pickers"]).toBeUndefined();
      expect(deps["react-datepicker"]).toBeUndefined();
      expect(deps["flatpickr"]).toBeUndefined();
    });
  });
});
