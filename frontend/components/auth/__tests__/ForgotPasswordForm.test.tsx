import { readFileSync } from "node:fs";
import path from "node:path";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { forgotPassword } from "@/lib/api/auth";
import { ForgotPasswordForm } from "../ForgotPasswordForm";

vi.mock("@/lib/api/auth", () => ({ forgotPassword: vi.fn() }));
const forgotMock = vi.mocked(forgotPassword);

describe("ForgotPasswordForm (HU #10173)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("envía la solicitud y muestra confirmación genérica", async () => {
    forgotMock.mockResolvedValue(undefined);
    render(<ForgotPasswordForm />);
    fireEvent.change(screen.getByLabelText(/correo/i), { target: { value: "demo@flit.local" } });
    fireEvent.click(screen.getByRole("button", { name: /enviar/i }));

    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent(/si el correo está registrado/i));
    expect(forgotMock).toHaveBeenCalledWith("demo@flit.local");
  });

  it("muestra el mismo mensaje genérico aunque la API falle (anti-enumeración)", async () => {
    forgotMock.mockRejectedValue({ status: 500 });
    render(<ForgotPasswordForm />);
    fireEvent.change(screen.getByLabelText(/correo/i), { target: { value: "x@y.z" } });
    fireEvent.click(screen.getByRole("button", { name: /enviar/i }));

    expect(await screen.findByRole("status")).toHaveTextContent(/si el correo está registrado/i);
  });

  it("valida correo vacío sin llamar a la API", () => {
    render(<ForgotPasswordForm />);
    fireEvent.click(screen.getByRole("button", { name: /enviar/i }));
    expect(screen.getByRole("alert")).toHaveTextContent(/ingresa tu correo/i);
    expect(forgotMock).not.toHaveBeenCalled();
  });

  // HU #12424 AC4 — la confirmación tras enviar es SIEMPRE la misma, sin importar si el correo
  // existe/pertenece a una red o si la API falla: el DOM de la confirmación debe ser idéntico.
  it("AC4 — la confirmación es el mismo DOM exista o no el correo (anti-enumeración de red)", async () => {
    forgotMock.mockResolvedValue(undefined);
    const exito = render(<ForgotPasswordForm />);
    fireEvent.change(exito.getByLabelText(/correo/i), { target: { value: "existe@red.io" } });
    fireEvent.click(exito.getByRole("button", { name: /enviar/i }));
    await waitFor(() => expect(exito.getByRole("status")).toBeInTheDocument());
    const exitoHtml = exito.getByRole("status").innerHTML;
    exito.unmount();

    forgotMock.mockRejectedValue({ status: 500 });
    const fallo = render(<ForgotPasswordForm />);
    fireEvent.change(fallo.getByLabelText(/correo/i), { target: { value: "no-existe@otra-red.io" } });
    fireEvent.click(fallo.getByRole("button", { name: /enviar/i }));
    const status = await fallo.findByRole("status");
    const falloHtml = status.innerHTML;

    expect(falloHtml).toBe(exitoHtml);
  });

  it("el enlace de recuperación, si lo hay, siempre viene del backend — el cliente no arma URLs de red", () => {
    // El componente no importa lib/brand/hosts ni construye ningún href: el enlace del correo lo
    // decide `INetworkUrlBaseResolver` en el servidor (#12423). Verificación estática del módulo.
    const source = readFileSync(path.join(__dirname, "..", "ForgotPasswordForm.tsx"), "utf8");
    expect(source).not.toMatch(/lib\/brand\/hosts/);
    expect(source).not.toMatch(/window\.location/);
  });
});
