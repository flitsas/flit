# ADR-0067: Clientes de integración externos propios de core-api, calcados de ICT y sin compañía

**Fecha**: 2026-09-29
**Status**: Propuesto
**Deciders**: David Alejandro Chica Hernandez (PO), Claude Code (agente de implementación). Aceptación: Líder Técnico.
**Tags**: arquitectura, backend, seguridad, modelo-de-datos, integraciones
**Épica / Feature**: #12737 / #13065 (HU #13084, #13087, #13088) · **Contrato**: `docs/integraciones/external-api-tramites-sync.md` (v3.1, §2)

## Contexto

Flito necesita autenticarse contra `/api/v1/external/*` para leer el feed de trámites de **todas** las
compañías. Hace falta una identidad de máquina con estas propiedades:

1. no es una persona ni un usuario de la plataforma, y no pertenece a una compañía;
2. tiene permisos explícitos (lectura de trámites, datos personales sin enmascarar);
3. su secreto rota con ventana de gracia, se bloquea tras intentos fallidos y se puede desactivar;
4. lo administra el superadministrador y el secreto se muestra una sola vez.

ICT ya resuelve algo muy parecido con `ict.integration_clients` en core-ict, pero allí cada cliente
pertenece a una compañía y su pase solo ve esa compañía.

## Decisión

1. **Tabla propia en core-api**: `integrations.external_clients` (DDL 125), que calca el modelo de
   `ict.integration_clients` —secreto generado por el sistema y guardado solo como hash Argon2id,
   hash anterior para la ventana de rotación, `must_rotate`, intentos fallidos y bloqueo, permisos en
   JSON, activación— **sin `tenant_id` ni RLS**.
2. **Excepción documentada al checklist A4/A10** (tabla de negocio con `tenant_id` y RLS): es una
   entidad de plataforma, como `admin.banners` (ADR-0058) o la propia `ict.integration_clients`. El
   login busca por `client_id` sin conocer compañía, y la administran solo superadministradores.
3. **La lectura entre compañías no la da la tabla sino el permiso**: el endpoint de sincronización
   abre un ámbito de lectura exclusivo (HU #13076) solo si el pase trae `external.tramites.read`.
4. **Pase propio**: JWT RS256 de 30 minutos con par de claves dedicado y esquema de autenticación
   separado (HU #13087). Un pase de usuario de la plataforma o de un cliente ICT no sirve en el
   prefijo externo, ni al revés.
5. **Nombres del contrato v3.1** en el login (`POST /api/v1/external/auth/token`, `clientId`,
   `clientSecret`), no los de ICT (`username`, `password`): el contrato ya estaba acordado con Flito.

## Alternativas consideradas

### A. Usuario de servicio en el login de la plataforma (descartada)
Reutiliza `POST /api/v1/auth/login` y el sistema de permisos por rol. Pero el usuario pertenece a una
compañía, comparte flujo con personas (cambio obligatorio de contraseña, dominios de red, sesión de
12 h) y no tiene rotación con ventana. Mezclar identidades de máquina y de personas complica la
auditoría y el endurecimiento de cada una.

### B. Reutilizar `ict.integration_clients` (descartada)
Acopla dos servicios (core-ict emite, core-api valida), hereda el ámbito por compañía, que aquí es
justo lo que no sirve, y obliga a cambiar la tabla de ICT para un consumidor que no es de ICT.

### C. API key estática por cabecera (descartada)
Más simple, pero sin caducidad del pase, sin permisos en el token y sin bloqueo por intentos; una
clave filtrada vale hasta que alguien la rote a mano.

## Tradeoff aceptado

- **Código parecido en dos servicios** (core-ict y core-api) para el login, la rotación y el bloqueo.
  Se acepta por aislamiento: cada servicio evoluciona su modelo sin romper al otro.
- **La auditoría técnica** (`audit.audit_logs`) guarda la fila con los hashes (no el secreto en claro).
  Un volcado de esa tabla no revela el secreto, pero los hashes se tratan como PII alta.

## Consecuencias

- Nuevo schema `integrations`; aquí vivirá también la bitácora de accesos externos (HU #13086).
- Los identificadores de cliente (`flito-dev`, `flito-qa`, `flito-pdn`) son únicos y no se reutilizan,
  ni tras dar de baja al cliente.
- La entrega del secreto a Flito es manual y fuera de banda; nunca por las herramientas de trabajo.

## ADRs relacionados

- ADR-0058 (tabla global sin tenant, excepción documentada).
- ADR-0066 (marca de agua de sincronización): el feed que estos clientes consumen.
