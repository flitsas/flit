import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { loginUser } from "@/lib/api/auth";
import { storeToken } from "@/lib/auth/session";
import { LoginForm } from "../LoginForm";

vi.mock("@/lib/api/auth", () => ({ loginUser: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({
  storeToken: vi.fn(),
  rememberEmail: vi.fn(),
  getRememberedEmail: vi.fn(() => ""),
}));

const loginMock = vi.mocked(loginUser);

describe("LoginForm (HU #10172 AC1)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("envía credenciales, almacena el token y notifica éxito", async () => {
    loginMock.mockResolvedValue({ accessToken: "jwt", expiresInSeconds: 43200, tokenType: "Bearer" });
    const onSuccess = vi.fn();
    render(<LoginForm onSuccess={onSuccess} />);

    fireEvent.change(screen.getByLabelText(/correo/i), { target: { value: "demo@flit.local" } });
    fireEvent.change(screen.getByLabelText(/contraseña/i), { target: { value: "DemoPass1!" } });
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect(loginMock).toHaveBeenCalledWith("demo@flit.local", "DemoPass1!");
    expect(storeToken).toHaveBeenCalledWith("jwt");
  });

  it("muestra error con credenciales inválidas (401)", async () => {
    loginMock.mockRejectedValue({ status: 401 });
    render(<LoginForm />);

    fireEvent.change(screen.getByLabelText(/correo/i), { target: { value: "x@y.z" } });
    fireEvent.change(screen.getByLabelText(/contraseña/i), { target: { value: "bad" } });
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/incorrectos/i);
  });

  it("muestra mensaje de cuenta bloqueada temporalmente (403)", async () => {
    loginMock.mockRejectedValue({ status: 403 });
    render(<LoginForm />);

    fireEvent.change(screen.getByLabelText(/correo/i), { target: { value: "x@y.z" } });
    fireEvent.change(screen.getByLabelText(/contraseña/i), { target: { value: "p" } });
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/bloqueada/i);
  });

  it("valida campos vacíos sin llamar a la API", () => {
    render(<LoginForm />);
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    expect(screen.getByRole("alert")).toHaveTextContent(/ingresa tu correo/i);
    expect(loginMock).not.toHaveBeenCalled();
  });
});
