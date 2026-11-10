// HU #14476 — componentes de @flit/ui con el aspecto del prototipo «Diseño Inteligente» (rama `propuesta`).
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { FileText } from "lucide-react";
import { StatusBadge } from "@flit/ui/StatusBadge";
import { Button, buttonClass } from "@flit/ui/Button";
import { Placa, formatoPlaca } from "@flit/ui/Placa";
import { Modal } from "@flit/ui/Modal";

const marcaDe = (el: HTMLElement) => el.querySelector("[data-marca]")?.getAttribute("data-marca");

describe("StatusBadge — el estado nunca solo por color", () => {
  it.each([
    ["success", "check"],
    ["warning", "punto"],
    ["danger", "equis"],
    ["info", "punto"],
    ["neutral", "punto"],
  ] as const)("%s lleva la marca %s antes del texto", (tone, marca) => {
    render(<StatusBadge tone={tone} label="Estado" />);
    const chip = screen.getByRole("status", { name: "Estado: Estado" });
    expect(marcaDe(chip)).toBe(marca);
    expect(chip.firstElementChild?.getAttribute("aria-hidden")).toBe("true");
    expect(chip.textContent).toBe("Estado");
  });

  it("el punto de pendiente es naranja y el de info toma el color del texto", () => {
    const { unmount } = render(<StatusBadge tone="warning" label="Pendiente" />);
    expect((screen.getByRole("status").querySelector("[data-marca]") as HTMLElement).style.background).toBe(
      "var(--di-alerta-punto)",
    );
    unmount();
    render(<StatusBadge tone="info" label="Info" />);
    expect((screen.getByRole("status").querySelector("[data-marca]") as HTMLElement).style.background).toBe(
      "currentcolor",
    );
  });
});

describe("Button — familia .btn", () => {
  it("el principal es píldora con degradado y texto por token", () => {
    const cls = buttonClass();
    expect(cls).toContain("rounded-full");
    expect(cls).toContain("[background:var(--di-gradient-action)]");
    expect(cls).toContain("text-[color:var(--di-on-action)]");
    expect(cls).not.toContain("text-white");
  });

  it("el secundario lleva contorno y no degradado", () => {
    const cls = buttonClass({ variant: "secondary" });
    expect(cls).toContain("border-[color:var(--di-border-control)]");
    expect(cls).not.toContain("gradient");
  });

  it("es type=button por defecto y deja pasar los handlers", () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Guardar</Button>);
    const btn = screen.getByRole("button", { name: "Guardar" });
    expect(btn.getAttribute("type")).toBe("button");
    btn.click();
    expect(onClick).toHaveBeenCalledTimes(1);
  });
});

describe("Placa", () => {
  it("separa letras y números como el prototipo y respeta otros formatos", () => {
    expect(formatoPlaca("klm482")).toBe("KLM 482");
    expect(formatoPlaca("ABC12D")).toBe("ABC 12D");
    expect(formatoPlaca("R12345")).toBe("R12345");
  });

  it("muestra los caracteres como texto y la ciudad solo si viene", () => {
    const { container, rerender } = render(<Placa placa="KLM482" ciudad="Bogotá D.C." />);
    expect(screen.getByText("KLM 482")).toBeTruthy();
    expect(screen.getByText("Bogotá D.C.")).toBeTruthy();
    const caja = container.firstElementChild as HTMLElement;
    expect(caja.style.background).toContain("var(--placa-fondo)");
    expect(caja.className).toContain("[font-family:var(--font-placa)]");
    rerender(<Placa placa="KLM482" />);
    expect(caja.children).toHaveLength(1);
  });
});

describe("Modal — franja tinta", () => {
  it("pinta título, ícono y X sobre la franja con el color sobre acento, aunque el llamador pase otro color", () => {
    render(
      <Modal open onClose={() => {}} title="Nuevo rol" icon={FileText} titleClassName="text-lg font-bold text-flit-primary">
        cuerpo
      </Modal>,
    );
    const titulo = screen.getByRole("heading", { name: "Nuevo rol" });
    expect(titulo.className).toContain("text-[color:var(--di-on-accent)]");
    expect(titulo.className).not.toContain("text-flit-primary");
    expect(titulo.className).toContain("text-lg");
    expect(screen.getByRole("dialog", { name: "Nuevo rol" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Cerrar" }).className).toContain("rounded-full");
  });
});

describe("CabeceraModulo — la cabecera compartida de las 4 apps", () => {
  it("pinta antetítulo, título con su id, descripción, ilustración decorativa y acciones dentro", async () => {
    const { CabeceraModulo } = await import("@flit/ui/CabeceraModulo");
    const { container } = render(
      <CabeceraModulo
        as="section"
        aria-labelledby="t"
        tituloId="t"
        antetitulo="Gestión integral"
        titulo="Trámites"
        descripcion="Del borrador a la entrega."
        encima={<nav>migas</nav>}
        ilustracion="/assets/ilustraciones/tramites.webp"
        acciones={<button type="button">Nuevo</button>}
      />,
    );
    const region = screen.getByRole("region", { name: "Trámites" });
    expect(region.tagName).toBe("SECTION");
    expect(screen.getByRole("heading", { level: 1, name: "Trámites" }).id).toBe("t");
    expect(region.textContent).toContain("Gestión integral");
    expect(region.textContent).toContain("Del borrador a la entrega.");
    expect(within(region).getByRole("button", { name: "Nuevo" })).toBeTruthy();
    const img = container.querySelector("img");
    expect(img?.getAttribute("src")).toBe("/assets/ilustraciones/tramites.webp");
    expect(img?.getAttribute("alt")).toBe("");
  });

  it("es un header y sin ilustración no pinta imagen", async () => {
    const { CabeceraModulo } = await import("@flit/ui/CabeceraModulo");
    const { container } = render(<CabeceraModulo titulo="Solo título" />);
    expect(container.firstElementChild?.tagName).toBe("HEADER");
    expect(container.querySelector("img")).toBeNull();
  });
});
