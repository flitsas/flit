# @flit/shell

Barra común de la FLIT Suite (tarea **B-10**, contrato de plataforma v1 §8): marca del host, nombre del producto,
menú de productos (▦), menú de cuenta y el dock inferior del producto. **No importa nada de ningún producto**: cada
producto le entrega su catálogo de navegación (una prueba lo vigila).

```tsx
"use client";
import { SuiteShell, type NavCatalog } from "@flit/shell/SuiteShell";

const nav: NavCatalog = {
  sections: [{ id: "operacion", label: "Operación", icon: FileText, side: "left" }],
  items: [{ key: "tramites", label: "Trámites", href: "/tramites", section: "operacion", icon: FileText, permission: "tramites.read" }],
};

<SuiteShell productCode="comparendos" productName="Comparendos" nav={nav} user={{ email, tenantName, permissions, roles, isSuperAdmin }}
  accountUrl={tramitesUrl}>
  {children}
</SuiteShell>
```

- **Catálogo (Contrato A de `GUIA-DOCK-INFERIOR-FLOTANTE.md`):** el orden del dock sale de `sections`, nunca de `items`.
  Un ítem puede tener un nivel de submenú (`children`) y `href` absoluto hacia otro producto.
- **Quién ve qué (Contrato B), en un solo sitio (`buildDock`):** `permission`, `roles` (cualquiera) o
  `superAdminOnly`; el SuperAdmin ve todo. Dock de escritorio y menú móvil usan los mismos grupos.
- **Menú de productos:** `GET /api/v1/platform/me/apps` por el proxy de la app (o `apps` ya cargadas). Cada producto
  abre en su host; la sesión del hub evita volver a iniciar sesión.
- **Barra igual en todos los productos (no se copia):** logo, nombre del producto, ▦, rol/empresa/nombre, avatar y el
  menú ⋮. El menú trae siempre Ayuda y Cambio de contraseña (`suiteAccountLinks`) y el rol sale de `roles`
  (`suiteRoleLabel`, «Super Admin», «Admin de Compañía»…). Esas pantallas viven hoy en Trámites: cualquier otra app pasa
  `accountUrl` con la URL de Trámites (hasta B-12). `accountLinks` es solo para opciones propias del producto, que van
  después de las de la suite; `headerActions`, para controles propios (Trámites pone ahí el tema claro/oscuro).
- **Puntero:** `@import "@flit/ui/cursor.css"` (flecha de marca y mano en lo clicable), igual que en las demás apps.
- **Cerrar sesión («Salir de la plataforma»):** `/auth/logout` de la app (`@flit/auth`), o `onLogout` si la app cierra
  sesión por su cuenta.
- **Estilos:** `@import "@flit/ui/tokens.css"` y luego `@import "@flit/shell/shell.css"` en el CSS global, más
  `@source "<ruta>/packages/shell/src";`. El botón central del dock usa `/assets/favicon.svg` de la app (`homeIconSrc`).
