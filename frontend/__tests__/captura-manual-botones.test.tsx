import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CapturaManualFlow } from "@/app/captura-manual/[token]/_components/CapturaManualFlow";
import { StepBar } from "@/app/captura-manual/[token]/_components/StepBar";
import { BRAND_BTN } from "@/lib/captura-manual/styles";
import { initialStepsState } from "@/lib/captura-manual/steps";
import type { ManualCaptureClient } from "@/lib/captura-manual/types";

const client: ManualCaptureClient = {
  getManualCapture: vi.fn().mockResolvedValue({
    fullName: "Persona de Prueba",
    documentType: "CC",
    documentNumber: "1000000000",
    productName: "FLIT 2.0",
    expiresAt: "2030-01-01T00:00:00Z",
    consentTextVersion: "manual-ley1581-v1",
  }),
  postConsent: vi.fn().mockResolvedValue(undefined),
  submit: vi.fn().mockResolvedValue({ status: "pendiente_revision_manual" }),
};

describe("botones azules de la captura manual (texto blanco, como Kyverum)", () => {
  it("la clase compartida usa texto blanco, bold y 16px sobre el azul de marca", () => {
    expect(BRAND_BTN).toContain("bg-flit-brand");
    expect(BRAND_BTN).toContain("text-white");
    expect(BRAND_BTN).toContain("text-base");
    expect(BRAND_BTN).toContain("font-bold");
    expect(BRAND_BTN).not.toContain("text-flit-primary");
  });

  it("«Iniciar verificación» usa texto blanco", async () => {
    render(<CapturaManualFlow token="ok" client={client} />);
    const btn = await screen.findByRole("button", { name: "Iniciar verificación" });
    expect(btn).toHaveClass("bg-flit-brand", "text-white", "font-bold");
    expect(btn).not.toHaveClass("text-flit-primary");
    fireEvent.click(screen.getByRole("checkbox"));
  });

  it("el círculo del paso activo usa texto blanco", () => {
    render(<StepBar state={initialStepsState} />);
    const active = screen.getAllByRole("listitem")[0].firstElementChild;
    expect(active).toHaveClass("bg-flit-brand", "text-white");
    expect(active).not.toHaveClass("text-flit-primary");
  });
});
