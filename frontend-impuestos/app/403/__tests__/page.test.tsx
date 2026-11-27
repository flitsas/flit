import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import Forbidden from "@/app/403/page";

// Uso de ejemplo: /403?code=PRODUCTO_NO_HABILITADO. La página de acceso negado traduce el código a un texto fijo: los
// códigos de la API de Impuestos usan el literal de x-flit-literales.errores (contrato impuestos.v1.yaml 1.1.0); los
// que solo manda el hub conservan su texto; un código desconocido cae en el texto general.

async function montar(code?: string) {
  render(await Forbidden({ searchParams: Promise.resolve(code === undefined ? {} : { code }) }));
}

describe("403 — literales del contrato 1.1.0", () => {
  it.each([
    ["PRODUCTO_NO_HABILITADO", "Impuestos no está activo para tu compañía."],
    ["PRODUCT_NOT_ENABLED", "Impuestos no está activo para tu compañía."],
    ["SIN_EMPRESA", "Tu usuario no tiene una compañía asociada."],
  ])("%s muestra el literal exacto del contrato", async (code, literal) => {
    await montar(code);
    expect(screen.getByText(literal, { exact: true })).toBeInTheDocument();
  });

  it("los códigos que solo vienen del hub conservan su texto", async () => {
    await montar("SESSION_INVALID");
    expect(screen.getByText("Tu cuenta cambió o fue suspendida. Vuelve a iniciar sesión.")).toBeInTheDocument();
  });

  it("un código desconocido (o ninguno) cae en el texto general y nunca muestra el código", async () => {
    await montar("<script>CUALQUIERA</script>");
    expect(screen.getByText("No tienes permiso para abrir esta página.")).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent("CUALQUIERA");
  });

  it("ofrece volver al hub", async () => {
    await montar();
    expect(screen.getByRole("link", { name: "Ir a mis productos" }).getAttribute("href")).toMatch(/\/\?inicio=1$/);
  });
});
