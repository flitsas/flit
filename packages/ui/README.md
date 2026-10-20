# @flit/ui

Tokens y átomos de UI compartidos por las apps de la FLIT Suite. Tarea **B-02** del frente B
(`docs/suite/frentes/frente-b-productos-y-hub.md`), contrato de plataforma §8.

- **v0 = mover y reexportar, sin cambio visual.** Los componentes salieron de
  `frontend/components/atom/`; allí quedan archivos que solo reexportan desde aquí, así que ningún
  consumidor de Trámites cambia.
- El código se publica como TypeScript: cada app Next.js lo compila con
  `transpilePackages: ["@flit/ui"]`.
- **Estilos:** la app importa `@flit/ui/tokens.css` desde su CSS global, después de
  `@import "tailwindcss"`, y agrega `@source "<ruta>/packages/ui/src";` para que Tailwind genere las
  clases que usan estos componentes. Sin el `@source`, los estilos desaparecen sin ningún error.
- `Shell.tsx` no vive aquí: el shell común es `@flit/shell` (B-10).

| Exporta | Qué es |
|---|---|
| `tokens`, `tokens.css` | Colores FLIT (`FLIT`) y variables de Tailwind (`@theme`, `:root`, `.dark`) |
| `cn` | Une clases y resuelve conflictos de Tailwind |
| `DataTable`, `UiStateBoundary`, `Pagination`, `table-styles` | Tablas con estados de carga, vacío y error |
| `Modal`, `InlineAlert`, `StatusBadge`, `statusTones`, `SearchableSelect`, `Loader` (`VehicleLoader`) | Átomos genéricos |
