import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CamposOverrideDictamen } from "@/components/backoffice/CamposOverrideDictamen";

// Uso de ejemplo: <CamposOverrideDictamen elegido="BLOQUEADO" motivo="" onMotivo={fn} … /> pinta el veredicto, el
// motivo obligatorio (D9, 10 a 500 con contador y la advertencia de PII) y la confirmación.

const base = { elegido: "BLOQUEADO" as const, onElegir: vi.fn(), confirmado: false, onConfirmar: vi.fn(), aviso: null };

describe("HU14562 AC45 motivo del ajuste", () => {
  it("motivo obligatorio con advertencia, contador y mínimo explicado", () => {
    const onMotivo = vi.fn();
    render(<CamposOverrideDictamen {...base} motivo="corto" onMotivo={onMotivo} />);
    const campo = screen.getByRole("textbox", { name: /motivo/i });
    expect(campo).toBeRequired();
    expect(campo).toHaveAttribute("maxlength", "500");
    expect(screen.getByText("No escribas documentos ni nombres")).toBeInTheDocument();
    expect(screen.getByText("5 / 500")).toBeInTheDocument();
    expect(campo).toHaveAccessibleDescription(/al menos 10/);
    expect(campo).toHaveAttribute("aria-invalid", "true");
    fireEvent.change(campo, { target: { value: "Motivo de soporte" } });
    expect(onMotivo).toHaveBeenCalledWith("Motivo de soporte");
  });

  it("un error del API sobre un campo (P3) se muestra en el campo, no como aviso general", () => {
    render(<CamposOverrideDictamen {...base} motivo="Motivo suficiente" onMotivo={vi.fn()} errorMotivo="El texto no puede llevar secuencias de 6 o más dígitos" />);
    expect(screen.getByRole("textbox", { name: /motivo/i })).toHaveAccessibleDescription(/6 o más dígitos/);
  });

  it("sin onMotivo (consumidor previo) no pinta el motivo", () => {
    render(<CamposOverrideDictamen {...base} />);
    expect(screen.queryByRole("textbox", { name: /motivo/i })).toBeNull();
  });

  it("HU14562 AC45: con onAlertamientos pinta la lista editable de alertamientos", () => {
    render(
      <CamposOverrideDictamen
        {...base}
        motivo="Motivo suficiente"
        onMotivo={vi.fn()}
        alertamientos={[{ clave: "Alertamiento 1", codigo: "AJUSTE_FLIT", texto: "Revisión manual", efecto: "BLOQUEA", fuente: "RUNT" }]}
        onAlertamientos={vi.fn()}
      />,
    );
    expect(screen.getByRole("table", { name: "Alertamientos del ajuste" })).toBeInTheDocument();
  });
});
