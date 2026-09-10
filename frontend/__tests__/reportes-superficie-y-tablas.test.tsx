// Las dos correcciones de aspecto que pidió la revisión de Reportes.
//
// Son cambios visuales, y lo visual no se prueba con capturas: lo que sí se puede fijar es la
// DECISIÓN que hay detrás de cada uno, que es lo que se rompería sin darse cuenta.
//
// 1. Reportes del organismo se monta sobre el fondo de la página, no dentro del panel blanco del
//    hub. Sin eso, sus tarjetas —que también son blancas— quedan sobre otra superficie blanca y
//    entre una y otra no queda fondo que las separe: se leen como un solo bloque.
// 2. Las tablas de los dos módulos de reportes usan la receta de «lista de tarjetas» de Trámites
//    —cabecera-píldora con fondo propio y filas con borde a los cuatro lados— y no el rayado que
//    tenían.
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";

const mocks = vi.hoisted(() => ({
  push: vi.fn(),
  replace: vi.fn(),
  fetchTransitOffices: vi.fn(),
  fetchOtProfile: vi.fn(),
  fetchTransitOfficesOperationalStatus: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: mocks.push, replace: mocks.replace }),
}));

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: mocks.fetchTransitOffices,
}));

vi.mock("@/lib/api/admin-ot", () => ({
  fetchOtProfile: mocks.fetchOtProfile,
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficesOperationalStatus: mocks.fetchTransitOfficesOperationalStatus,
}));

import { OtHubLayout } from "@/components/admin/transit-offices/OtHubLayout";
import { Table } from "@/components/admin/transit-offices/_reportes/shared";

beforeEach(() => {
  vi.clearAllMocks();
  mocks.fetchTransitOffices.mockResolvedValue([]);
  mocks.fetchOtProfile.mockResolvedValue({ transitOfficeId: "ot-1" });
  mocks.fetchTransitOfficesOperationalStatus.mockResolvedValue([]);
});

describe("la superficie del hub del organismo", () => {
  it("monta Reportes sobre el fondo de la página y no dentro del panel blanco", () => {
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="reportes" moduleTitle="Reportes" surface="plano">
        <p>contenido</p>
      </OtHubLayout>,
    );

    const superficie = screen.getByTestId("ot-hub-superficie");
    expect(superficie.className).not.toMatch(/bg-card/);
    // Sin borde ni redondeo tampoco: si quedara el marco, seguiría leyéndose como una caja.
    expect(superficie.className).not.toMatch(/rounded-2xl|border/);
  });

  it("conserva el panel blanco para el resto de módulos del hub", () => {
    render(
      <OtHubLayout transitOfficeId="ot-1" activeTab="rules" moduleTitle="Reglas">
        <p>contenido</p>
      </OtHubLayout>,
    );

    expect(screen.getByTestId("ot-hub-superficie").className).toMatch(/bg-card/);
  });
});

describe("las tablas de reportes", () => {
  it("visten cada fila como una tarjeta con su propio borde", () => {
    render(
      <Table
        headers={["Estado", "Trámites"]}
        rows={[{ key: "aprobado", cells: ["Aprobado", "12"] }]}
      />,
    );

    // La cabecera lleva fondo propio (la píldora gris), no solo una raya debajo.
    const cabecera = screen.getByText("Estado");
    expect(cabecera.className).toMatch(/bg-\[color:var\(--table-header-bg\)\]/);

    // La fila se cierra por los cuatro lados: la primera celda pone el lado izquierdo y la
    // última el derecho; sin eso vuelve a ser un renglón subrayado.
    const primera = screen.getByText("Aprobado");
    const ultima = screen.getByText("12");
    expect(primera.className).toMatch(/border-y/);
    expect(primera.className).toMatch(/first:border-l/);
    expect(ultima.className).toMatch(/last:border-r/);
    expect(primera.className).toMatch(/border-\[color:var\(--table-row-border\)\]/);
  });

  it("separa las filas entre sí, que es lo que las convierte en tarjetas sueltas", () => {
    const { container } = render(<Table headers={["A"]} rows={[{ key: "1", cells: ["x"] }]} />);

    const tabla = container.querySelector("table");
    expect(tabla?.className).toMatch(/border-separate/);
    expect(tabla?.className).toMatch(/border-spacing-y-2/);
  });
});
