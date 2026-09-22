# Auditoría de dependencia — `react-day-picker` (HU #12724 · D4)

- **Fecha:** 2026-09-21
- **Decisión:** APROBADA para uso en `DateRangePicker` (bloque B.2)
- **Versiones instaladas:** `react-day-picker@9.14.0`, `date-fns@4.4.0` (peer para locale `es`)

## Resumen ejecutivo

| Criterio | Resultado |
|----------|-----------|
| Licencia | MIT — compatible con uso comercial |
| Mantenimiento | Activo (releases 2025–2026, repo `gpbl/react-day-picker`) |
| Dependencias directas runtime | 0 en el paquete publicado; `date-fns` es peer/dev del upstream |
| Superficie FLIT | Un componente (`DateRangePicker`) + CSS importado |
| Alternativa si se revoca | Calendario propio con popover (`useDisclosureNav` / `usePopoverDismiss`) |

## Licencia

- **SPDX:** MIT
- **Obligaciones:** conservar aviso de copyright en distribución (cumplido vía `node_modules` + lockfile)
- **Patentes / copyleft:** ninguna restricción adicional

## Mantenimiento y comunidad

- Repositorio oficial: https://github.com/gpbl/react-day-picker
- Documentación v9: https://daypicker.dev
- Historial: biblioteca madura (>10 años), API estable en v9 con soporte de rango, localización (`es`) y accesibilidad documentada (roles ARIA, teclado)
- Issue tracker activo; versiones menores frecuentes en 2025–2026

## Seguridad (regla 18 · skill externa auditada)

Comando ejecutado en la sesión de implementación:

```bash
pnpm audit --filter @flit/frontend
```

- **Hallazgos directos sobre `react-day-picker@9.14.0`:** ninguno en el informe de la sesión
- **Transitive:** el paquete no arrastra runtime deps adicionales al bundle de producción
- **Revisión manual:** sin `eval`, sin `postinstall` sospechoso, sin acceso a red en runtime
- **Recomendación CI:** mantener gate `dependency-audit` del monorepo en el PR que integre esta HU

## Accesibilidad y UX (motivo de elección)

- Modo `range` nativo con navegación por flechas en el calendario
- Localización española vía `react-day-picker/locale` (`es`)
- Compatible con patrón popover FLIT (Escape cierra, foco al trigger)
- Estilos sobrescritos con tokens FLIT (`#557EFF`, `#162744`, dark `#0B0F14`) para contraste ≥ 4.5:1 en texto del campo

## Riesgos residuales

| Riesgo | Mitigación |
|--------|------------|
| Drift visual respecto al prototipo | `flit-design-guardian` + classNames/token overrides en `DateRangePicker` |
| CSS global de la librería | Import acotado `react-day-picker/style.css`; variables `--rdp-*` sobre `.flit-date-range-picker` |
| Actualización major v10 | Pin semver `^9.7.0`; revisar changelog antes de bump |

## Veredicto

**APROBADA** — cumple licencia MIT, mantenimiento activo, sin vulnerabilidades reportadas en la auditoría local, y reduce superficie frente a un calendario propio manteniendo WCAG en teclado/ARIA.
