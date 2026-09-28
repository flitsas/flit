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

<SuiteShell productCode="tramites" productName="Trámites" nav={nav} user={{ email, tenantName, permissions, roles, isSuperAdmin }}>
  {children}
</SuiteShell>
```

- **Catálogo (Contrato A de `GUIA-DOCK-INFERIOR-FLOTANTE.md`):** el orden del dock sale de `sections`, nunca de `items`.
  Un ítem puede tener un nivel de submenú (`children`) y `href` absoluto hacia otro producto.
- **Quién ve qué (Contrato B), en un solo sitio (`buildDock`):** `permission`, `roles` (cualquiera) o
  `superAdminOnly`; el SuperAdmin ve todo. Dock de escritorio y menú móvil usan los mismos grupos.
- **Menú de productos:** `GET /api/v1/platform/me/apps` por el proxy de la app (o `apps` ya cargadas). Cada producto
  abre en su host; la sesión del hub evita volver a iniciar sesión.
- **Cerrar sesión:** `/auth/logout` de la app (`@flit/auth`).
- **Estilos:** `@import "@flit/ui/tokens.css"` y luego `@import "@flit/shell/shell.css"` en el CSS global, más
  `@source "<ruta>/packages/shell/src";`. El botón central del dock usa `/assets/favicon.svg` de la app (`homeIconSrc`).
