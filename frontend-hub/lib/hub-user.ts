// Usuario de la barra del hub a partir de la sesión de @flit/auth. Función pura. El nombre del rol lo pone la barra
// (`suiteRoleLabel` de @flit/shell), igual que en los demás productos.
import type { SessionUser } from "@flit/auth/types";
import type { HubUser } from "@/components/HubShell";

export function toHubUser(user: SessionUser): HubUser {
  return {
    email: user.email,
    tenantName: user.tenant.name,
    permissions: user.permissions,
    roles: user.roles.map((r) => r.code),
    isSuperAdmin: user.isSuperAdmin,
  };
}
