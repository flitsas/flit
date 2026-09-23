# Valor de FLIT 2 — lo práctico, las bondades y por qué es mejor

> Briefing de producto. Fecha: 2026-09-18.  
> Fuente: código y documentación vigente del repositorio (`docs/contexto-funcional-flit.md`, migrador V1→V2, ADRs de operación).

Lo práctico de FLIT 2 no es “un stack más moderno”. Es que **un operador puede radicar un trámite vehicular de punta a punta**, con datos oficiales, identidad, documentos y expediente, **sin que el sistema se los trague o los regenere cada vez**.

En una frase para un cliente o un inversionista:

> **FLIT 2 no digitaliza el formulario: digitaliza el expediente completo del trámite vehicular, con validación oficial, identidad, OT y evidencia, y lo deja listo para operar como SaaS multi-empresa.**

---

## Qué genera valor hoy (lo que se puede mostrar)

### 1. El trámite deja de ser un formulario suelto y pasa a ser un expediente vivo

Matrícula inicial y traspaso corren en un wizard guiado: el backend dice qué paso sigue, qué falta y qué bloquea la radicación. El operador no “adivina” si puede enviar.

### 2. Consultas oficiales antes de radicar (ahorro de rechazos)

RUNT, SIMIT y RNMC entran al flujo con semáforo (verde / amarillo / rojo). Eso recorta idas y vueltas con el Organismo de Tránsito: se ve el problema **antes** de radicar, no después.

### 3. Identidad biométrica y portal del ciudadano

Kyverum + portal público: el comprador/vendedor valida identidad y firma **sin estar en la oficina**. Eso es tiempo de ciclo y cobertura comercial, no un detalle técnico.

### 4. Documentos que sí quedan

FUR, compraventa, consolidado y adjuntos se **persisten**. En FLIT 1 muchos PDFs se armaban “en caliente” y se descartaban; al apagar V1 se perdían. En producción se midió que **1.682 de 20.148 traspasos aprobados** no tenían consolidado guardado. FLIT 2 nace para que el expediente **exista** al día siguiente.

### 5. Multi-empresa de verdad (SaaS)

Cada compañía (concesionario, renting, gestor) es un tenant: usuarios, roles, OTs habilitadas, políticas (por ejemplo, si puede o no hacer matrícula inicial). FLIT opera varias empresas en una sola plataforma, no un sistema “por cliente”.

### 6. El Organismo de Tránsito opera dentro de FLIT

Bandeja, reglas, documentos, requisitos, preasignación de placas, aprobación/rechazo/subsanación. El valor no es solo radicar: es **cerrar el ciclo con el OT**.

### 7. Continuidad del negocio

Hay migrador V1 → V2 (datos, adjuntos y documentos generados). No es un “arrancar de cero y perder historial”.

### 8. Control y evidencia

Reportes, auditoría, LOG QX/ICT, historial de estados. Sirve para gerencia, soporte y OT: quién hizo qué, cuándo y con qué resultado.

---

## Bondades que sí se pueden destacar (sin humo)

| Bondad | Por qué importa al negocio |
|---|---|
| **Menos rechazos** | Preflight + gates de radicación (docs, identidad, OT habilitado) |
| **Menos retrabajo** | Wizard server-driven: las reglas viven en un solo lugar |
| **Expediente auditable** | El PDF no “aparece y desaparece”; queda en S3 con trazabilidad |
| **FUR alineado a norma** | Overlay del Formulario Único (Anexo 46 / Resolución 20233040017145), no un PDF genérico |
| **Trabajo remoto de partes** | Identidad y firma fuera de la ventanilla |
| **Parametrizable** | Tipos de trámite, documentos y reglas se configuran; no hay que reescribir el producto por cada modalidad |
| **No atados a un solo proveedor** | Consultas multi-proveedor (Verifik / Intempo): se cambia el cable, no el motor |
| **Roles claros** | SuperAdmin, Admin de compañía, OT, radicador: cada uno ve lo suyo |
| **Operación a escala** | Varias compañías, varias OTs, políticas por tenant |

---

## Qué hace a FLIT 2 mejor que FLIT 1

No es “más pantallas”. Es **corregir fallas de operación que ya costaron dinero y tiempo**.

1. **El expediente sobrevive.** V1 regeneraba y a menudo no guardaba. V2 trata el consolidado y el FUR como artefactos del trámite.
2. **El trámite tiene estados y reglas, no un flujo rígido en código.** Plantillas globales + overrides por compañía/OT. Escalar a otro tipo de trámite es configuración, no un fork.
3. **La radicación tiene frenos.** En V1 se podía avanzar con huecos; en V2 hay `SubmitGate`: documentos, biométrica vigente, OT concedido, reglas del organismo.
4. **La integración con Quipux/OT se vuelve operable.** En V1 un trámite real llegó a **1.065 eventos** (un ping cada 10 minutos) mostrados en 107 páginas sin filtro. V2 está diseñado para ver **hitos del trámite**, no el latido de sondeo.
5. **Seguridad y aislamiento.** JWT, RBAC, aislamiento por tenant. V1 era un producto de operación; V2 es una plataforma que se puede vender a varias empresas sin mezclar datos.
6. **El ciudadano entra al flujo.** Portal + biométrica remota. V1 estaba más centrado en el operador interno.
7. **Observabilidad.** Reportes, telemetría de uso, bitácora de correos y de integraciones. Se puede gestionar el negocio, no solo “sacar el trámite”.

---

## Honestidad útil (para no sobrevender)

Lo que **ya genera valor** es radicación + consultas + identidad + documentos persistidos + OT + multi-tenant + migración.

Lo que **sigue madurando** (y no conviene vender como “ya listo en PDN” sin verificar ambiente): jerarquía concesión/marca blanca, algunos correos de negocio del trámite, y paridad total de todos los PDF que V1 generaba en caliente.
