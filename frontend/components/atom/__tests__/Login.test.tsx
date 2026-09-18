import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { loginUser } from "@/lib/api/auth";
import { storeToken } from "@/lib/auth/session";
import { Login } from "../Login";

vi.mock("@/lib/api/auth", () => ({ loginUser: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({
  storeToken: vi.fn(),
  rememberEmail: vi.fn(),
}));

const loginMock = vi.mocked(loginUser);
const storeTokenMock = vi.mocked(storeToken);

function fillCreds() {
  fireEvent.change(screen.getByLabelText(/usuario corporativo/i), { target: { value: "admin@flit.io" } });
  fireEvent.change(screen.getByLabelText(/contraseña/i), { target: { value: "Secret123" } });
}

describe("Login (Feature #10113 — login real)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("AC1 — credenciales válidas → almacena JWT y notifica autenticación", async () => {
    loginMock.mockResolvedValue({ accessToken: "jwt.abc.def", expiresInSeconds: 43200, tokenType: "Bearer" });
    const onAuthenticated = vi.fn();
    render(<Login onAuthenticated={onAuthenticated} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(onAuthenticated).toHaveBeenCalledOnce());
    expect(loginMock).toHaveBeenCalledWith("admin@flit.io", "Secret123");
    expect(storeTokenMock).toHaveBeenCalledWith("jwt.abc.def");
  });

  it("credenciales inválidas (401) → error accesible, sin autenticar", async () => {
    loginMock.mockRejectedValue(Object.assign(new Error("401"), { status: 401 }));
    const onAuthenticated = vi.fn();
    render(<Login onAuthenticated={onAuthenticated} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/incorrectos/i));
    expect(onAuthenticated).not.toHaveBeenCalled();
  });

  it("cuenta bloqueada (403) → panel de acceso restringido", async () => {
    loginMock.mockRejectedValue(Object.assign(new Error("403"), { status: 403 }));
    render(<Login onAuthenticated={vi.fn()} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/acceso restringido/i));
  });

  // HU #10511 AC1 — todos los roles del usuario están inactivos.
  it("todos los roles desactivados (403 ALL_ROLES_INACTIVE) → mensaje específico de rol desactivado", async () => {
    loginMock.mockRejectedValue(
      Object.assign(new Error("403"), { status: 403, body: { code: "ALL_ROLES_INACTIVE" } }),
    );
    render(<Login onAuthenticated={vi.fn()} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent(/rol ha sido desactivado/i),
    );
  });

  // HU #10511 AC2 — el bloqueo temporal (sin ese code) no debe mostrar el mensaje de rol desactivado.
  it("cuenta bloqueada temporalmente sin ALL_ROLES_INACTIVE → sigue mostrando el mensaje de bloqueo, no el de rol", async () => {
    loginMock.mockRejectedValue(Object.assign(new Error("403"), { status: 403, body: { code: "ACCOUNT_TEMPORARILY_BLOCKED" } }));
    render(<Login onAuthenticated={vi.fn()} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/acceso restringido/i));
    expect(screen.queryByText(/rol ha sido desactivado/i)).not.toBeInTheDocument();
  });

  // HU #12424 AC2 — credencial válida de un usuario de red MARCA_BLANCA presentada en el
  // dominio de FLIT: mensaje + enlace directo al dominio propio, sin redirigir automáticamente.
  describe("HU #12424 AC2 — 403 NETWORK_DOMAIN_REQUIRED", () => {
    it("muestra el mensaje y un enlace <a> hacia loginUrl con el dominio de la red", async () => {
      loginMock.mockRejectedValue(
        Object.assign(new Error("403"), {
          status: 403,
          body: {
            error: "NETWORK_DOMAIN_REQUIRED",
            networkDomain: "app.movilidadandina.com",
            loginUrl: "https://app.movilidadandina.com/login",
          },
        }),
      );
      render(<Login onAuthenticated={vi.fn()} />);

      fillCreds();
      fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

      const alertRegion = await screen.findByRole("alert");
      expect(alertRegion).toHaveTextContent(/app\.movilidadandina\.com/i);

      const link = screen.getByRole("link", { name: /ir a app\.movilidadandina\.com/i });
      expect(link).toHaveAttribute("href", "https://app.movilidadandina.com/login");
      expect(link).toHaveAttribute("rel", "noopener");
    });

    it("no navega automáticamente — la decisión de ir al dominio queda en manos del usuario", async () => {
      loginMock.mockRejectedValue(
        Object.assign(new Error("403"), {
          status: 403,
          body: {
            error: "NETWORK_DOMAIN_REQUIRED",
            networkDomain: "app.movilidadandina.com",
            loginUrl: "https://app.movilidadandina.com/login",
          },
        }),
      );
      const hrefBefore = window.location.href;

      render(<Login onAuthenticated={vi.fn()} />);
      fillCreds();
      fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

      const alertRegion = await screen.findByRole("alert");
      // El único elemento navegable es el <a href> explícito dentro del propio mensaje — no hay
      // efecto secundario que mueva `window.location`.
      expect(window.location.href).toBe(hrefBefore);
      const link = alertRegion.querySelector("a[href]");
      expect(link).toHaveAttribute("href", "https://app.movilidadandina.com/login");
    });

    it("el enlace es alcanzable con teclado (foco) y el mensaje se anuncia (aria-live)", async () => {
      loginMock.mockRejectedValue(
        Object.assign(new Error("403"), {
          status: 403,
          body: {
            error: "NETWORK_DOMAIN_REQUIRED",
            networkDomain: "app.movilidadandina.com",
            loginUrl: "https://app.movilidadandina.com/login",
          },
        }),
      );
      render(<Login onAuthenticated={vi.fn()} />);
      fillCreds();
      fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

      const alertRegion = await screen.findByRole("alert");
      expect(alertRegion).toHaveAttribute("aria-live", "assertive");

      const link = screen.getByRole("link", { name: /ir a app\.movilidadandina\.com/i });
      link.focus();
      expect(link).toHaveFocus();
    });
  });

  // HU #12424 AC3 — credencial incorrecta y usuario fuera de la red (mismo cuerpo 401
  // INVALID_CREDENTIALS del backend) deben producir EXACTAMENTE el mismo DOM.
  it("AC3 — 401 credencial incorrecta y 401 fuera de la red son indistinguibles en el DOM", async () => {
    loginMock.mockRejectedValue(
      Object.assign(new Error("401"), { status: 401, body: { error: "INVALID_CREDENTIALS" } }),
    );
    const credencial = render(<Login onAuthenticated={vi.fn()} />);
    fireEvent.change(credencial.getByLabelText(/usuario corporativo/i), { target: { value: "admin@flit.io" } });
    fireEvent.change(credencial.getByLabelText(/contraseña/i), { target: { value: "Secret123" } });
    fireEvent.click(credencial.getByRole("button", { name: /iniciar sesión/i }));
    await waitFor(() => expect(credencial.getByRole("alert")).toBeInTheDocument());
    const credencialHtml = credencial.getByRole("alert").innerHTML;
    credencial.unmount();

    // Mismo cuerpo exacto que recibiría un usuario válido pero fuera de la red de su dominio:
    // el backend responde 401 INVALID_CREDENTIALS en ambos casos (contratos-api.md §5).
    loginMock.mockRejectedValue(
      Object.assign(new Error("401"), { status: 401, body: { error: "INVALID_CREDENTIALS" } }),
    );
    const fueraDeRed = render(<Login onAuthenticated={vi.fn()} />);
    fireEvent.change(fueraDeRed.getByLabelText(/usuario corporativo/i), { target: { value: "otro@red.io" } });
    fireEvent.change(fueraDeRed.getByLabelText(/contraseña/i), { target: { value: "OtraClave1" } });
    fireEvent.click(fueraDeRed.getByRole("button", { name: /iniciar sesión/i }));
    await waitFor(() => expect(fueraDeRed.getByRole("alert")).toBeInTheDocument());
    const fueraDeRedHtml = fueraDeRed.getByRole("alert").innerHTML;

    expect(fueraDeRedHtml).toBe(credencialHtml);
  });

  // HU #12424 AC1 — no hay selector de compañía: no se construye ninguna lista con datos
  // locales (localStorage/estado previo). Documentado como fuera de alcance por diseño
  // (ADR-0060 D3): 1 email = 1 tenant hoy.
  it("AC1 — el login no lee ni construye ningún listado de compañías desde almacenamiento local", async () => {
    const getItemSpy = vi.spyOn(Object.getPrototypeOf(window.localStorage) as Storage, "getItem");
    loginMock.mockResolvedValue({ accessToken: "jwt.abc.def", expiresInSeconds: 43200, tokenType: "Bearer" });
    render(<Login onAuthenticated={vi.fn()} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));
    await waitFor(() => expect(loginMock).toHaveBeenCalledOnce());

    // Ningún acceso a localStorage con una llave de "compañías"/"tenants" — el único estado
    // persistido en login es el token y el correo recordado (rememberEmail, mockeado aparte).
    for (const call of getItemSpy.mock.calls) {
      expect(String(call[0])).not.toMatch(/compan|tenant/i);
    }
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    getItemSpy.mockRestore();
  });
});
