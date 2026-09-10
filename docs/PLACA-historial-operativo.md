# [PLACA] - Módulo de historial operativo por placa y vista en Dashboard

---

## OBJETIVO

Implementar un módulo de consulta de historial operativo por placa que permita a los roles autorizados ver toda la gestión e información de trámites que se ha realizado sobre una placa dentro de FLIT, tanto desde un módulo dedicado como desde el Dashboard de Trámites.

---

## DESCRIPTION

Actualmente FLIT no cuenta con un módulo de historial por placa; solo existe una búsqueda puntual en el módulo de ayuda (DR. FLIT). Se requiere implementar un módulo dedicado donde los roles Admin, Documentador, OperarioFull, Validador y Gestor puedan consultar el historial completo de trámites asociados a una placa, con toda la información disponible de cada trámite. Esta misma información debe estar accesible también desde el Dashboard de Trámites.

---

## CRITERIOS FUNCIONALES

### Módulo de historial por placa
- [ ] Los roles Admin, Documentador, OperarioFull, Validador y Gestor tienen acceso al módulo de historial de placas
- [ ] El módulo permite buscar por número de placa
- [ ] Los resultados muestran todos los trámites históricos asociados a esa placa dentro de FLIT
- [ ] Cada trámite en el historial muestra su información completa: ID, tipo, estado, fecha de radicación, fecha de última actualización, gestor, OT, comprador, vendedor, VIN y demás campos disponibles
- [ ] El historial se ordena cronológicamente (más reciente primero por defecto)
- [ ] El módulo es independiente del módulo de ayuda/DR. FLIT existente

### Vista en Dashboard
- [ ] Desde el Dashboard de Trámites se puede acceder al historial de la placa asociada a cada trámite
- [ ] La información mostrada en el Dashboard es la misma que en el módulo dedicado
- [ ] El acceso desde el Dashboard respeta los roles autorizados para consultar el historial

---

*🤖 Borrador generado por feature-creator bajo la supervisión de Andrés Jiménez (andres.jimenez@flitsas.com) — Proyecto: FLIT - FLITO*
