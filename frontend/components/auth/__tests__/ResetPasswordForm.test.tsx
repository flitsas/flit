import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { resetPassword } from "@/lib/api/auth";
import { getToken } from "@/lib/api/client";
import { clearToken } from "@/lib/auth/session";
import { ResetPasswordForm } from "../ResetPasswordForm";

vi.mock("@/lib/api/auth", () => ({ resetPassword: vi.fn() }));
vi.mock("@/lib/api/client", () => ({ getToken: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({ clearToken: vi.fn() }));
const resetMock = vi.mocked(resetPassword);
const getTokenMock = vi.mocked(getToken);
const clearTokenMock = vi.mocked(clearToken);

function fill(pw: string, confirm: string) {
  fireEvent.change(screen.getByLabelText(/nueva contraseña/i), { target: { value: pw } });
  fireEvent.change(screen.getByLabelText(/confirmar/i), { target: { value: confirm } });
}

describe("ResetPasswordForm (HU #10173)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getTokenMock.mockReturnValue(null);
  });

  it("AC2 — sin token muestra error claro sin formulario", () => {
    render(<ResetPasswordForm token={null} />);
    expect(screen.getByRole("alert")).toHaveTextContent(/inválido o expiró/i);
    expect(screen.queryByLabelText(/nueva contraseña/i)).toBeNull();
  });

  it("AC1 — token válido + política + coincidencia → éxito y enlace a login", async () => {
    resetMock.mockResolvedValue(undefined);
    render(<ResetPasswordForm token="tok123" />);
    fill("NewPass123", "NewPass123");
    fireEvent.click(screen.getByRole("button", { name: /restablecer/i }));

    await waitFor(() => expect(screen.getByRole("status")).toBeInTheDocument());
    expect(resetMock).toHaveBeenCalledWith("tok123", "NewPass123");
    expect(screen.getByRole("link", { name: /iniciar sesión/i })).toHaveAttribute("href", "/login");
  });

  // Caso reportado: restablecer desde un navegador con otra sesión ya abierta debe
  // limpiarla y avisar, para que /login no rebote a la sesión vieja.
  it("si había una sesión activa en el navegador, la limpia y muestra el aviso", async () => {
    resetMock.mockResolvedValue(undefined);
    getTokenMock.mockReturnValue("admin-jwt");
    render(<ResetPasswordForm token="tok123" />);
    fill("NewPass123", "NewPass123");
    fireEvent.click(screen.getByRole("button", { name: /restablecer/i }));

    await waitFor(() => expect(clearTokenMock).toHaveBeenCalled());
    expect(screen.getByText(/cerramos la sesión que estaba activa/i)).toBeInTheDocument();
  });

  it("sin sesión previa activa, no muestra el aviso", async () => {
    resetMock.mockResolvedValue(undefined);
    getTokenMock.mockReturnValue(null);
    render(<ResetPasswordForm token="tok123" />);
    fill("NewPass123", "NewPass123");
    fireEvent.click(screen.getByRole("button", { name: /restablecer/i }));

    await waitFor(() => expect(clearTokenMock).toHaveBeenCalled());
    expect(screen.queryByText(/cerramos la sesión que estaba activa/i)).toBeNull();
  });

  it("rechaza contraseña que no cumple política (sin llamar API)", () => {
    render(<ResetPasswordForm token="tok123" />);
    fill("weak", "weak");
    fireEvent.click(screen.getByRole("button", { name: /restablecer/i }));
    expect(screen.getByRole("alert")).toHaveTextContent(/mínimo 8/i);
    expect(resetMock).not.toHaveBeenCalled();
  });

  it("rechaza cuando las contraseñas no coinciden", () => {
    render(<ResetPasswordForm token="tok123" />);
    fill("NewPass123", "Other123");
    fireEvent.click(screen.getByRole("button", { name: /restablecer/i }));
    expect(screen.getByRole("alert")).toHaveTextContent(/no coinciden/i);
    expect(resetMock).not.toHaveBeenCalled();
  });

  it("AC2 — token inválido/usado (400) muestra error sin exponer datos", async () => {
    resetMock.mockRejectedValue({ status: 400 });
    render(<ResetPasswordForm token="used" />);
    fill("NewPass123", "NewPass123");
    fireEvent.click(screen.getByRole("button", { name: /restablecer/i }));
    expect(await screen.findByRole("alert")).toHaveTextContent(/inválido o expiró/i);
  });
});
