import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CapturaExpress } from "@/components/express/CapturaExpress";
import { llamadas, simularApi } from "./datos";

// Uso de ejemplo (HU #14085): <CapturaExpress /> en la tarjeta del título del inicio: el usuario escribe «tst 123», ve
// «TST123» y con «Diagnosticar» (misma regla de Trámites y rechazo por longitud de siempre) llega a
// /diagnosticos/nuevo?modo=express&placa=TST123, donde ajusta los parámetros. No llama a la API.

const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock("next/navigation", () => ({ usePathname: () => "/", useSearchParams: () => new URLSearchParams(), useRouter: () => router }));

beforeEach(() => router.push.mockReset());
afterEach(() => vi.unstubAllGlobals());

function montar() {
  const api = simularApi({});
  render(<CapturaExpress />);
  return api;
}

const campo = () => screen.getByRole("textbox", { name: "Placa o VIN" });
const escribir = (valor: string) => fireEvent.change(campo(), { target: { value: valor } });
const diagnosticar = () => fireEvent.click(screen.getByRole("button", { name: "Diagnosticar" }));

describe("CapturaExpress — normalización en pantalla (AC6)", () => {
  it("«tst 123» se muestra como TST123", () => {
    montar();
    escribir("tst 123");
    expect(campo()).toHaveValue("TST123");
  });

  it("no recorta: una entrada larga se ve completa para poder corregirla", () => {
    montar();
    escribir("ts 1234 5678");
    expect(campo()).toHaveValue("TS12345678");
  });
});

describe("CapturaExpress — valida como hoy antes de abrir la creación (AC6)", () => {
  it("vacío: pide la placa o el VIN, enfoca el campo y no navega", () => {
    const api = montar();
    diagnosticar();
    expect(campo()).toHaveAttribute("aria-invalid", "true");
    expect(campo()).toHaveAccessibleDescription(/escribe la placa o el vin/i);
    expect(campo()).toHaveFocus();
    expect(router.push).not.toHaveBeenCalled();
    expect(api).not.toHaveBeenCalled();
  });

  it("ni placa ni VIN: muestra el error en el campo y no navega", () => {
    const api = montar();
    escribir("12");
    diagnosticar();
    expect(campo()).toHaveAccessibleDescription(/no es una placa ni un vin/i);
    expect(router.push).not.toHaveBeenCalled();
    expect(llamadas(api, "POST", "/express")).toHaveLength(0);
  });

  it("placa de más de 8 caracteres: EXCEDE_LONGITUD, sin recortar", () => {
    montar();
    escribir("TS12345678");
    diagnosticar();
    expect(campo()).toHaveAccessibleDescription(/la placa tiene 10 caracteres y el máximo es 8/i);
    expect(router.push).not.toHaveBeenCalled();
  });

  it("al corregir, el error se limpia y con un VIN válido navega con el VIN codificado", () => {
    montar();
    diagnosticar();
    escribir("1hgcm82633a004352");
    expect(campo()).not.toHaveAttribute("aria-invalid");
    diagnosticar();
    expect(router.push).toHaveBeenCalledWith("/diagnosticos/nuevo?modo=express&placa=1HGCM82633A004352");
  });
});
