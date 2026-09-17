# Épica #12237 — Marca Blanca · criterios propuestos

> Generado: 2026-09-10 · rama `develop` @ `1480beb4` · **propuesta de redacción, NADA modificado en ADO**
> · Contrasta la épica #12237 (`[MARCA BLANCA]`, Sprint 6, `New`) contra el código verificado y contra
> las decisiones del PO humano tomadas el 2026-09-10.
> Complementa `docs/analisis-jerarquia-companias-concesionario.md` (épica #12235).

Marcas: ✅ igual que hoy · ✏️ corregido · ➕ nuevo

---

## OBJETIVO (✏️)

Dotar a una compañía de la **capacidad de Marca Blanca**: además del comportamiento de red de la
Concesión (gestión de clientes hijos, usuarios, trámites, estadísticas y reportes), puede personalizar
la identidad visual de la plataforma —colores, logo y nombre— y operar bajo una URL propia, de modo
que sus clientes hijos experimenten la plataforma con la identidad de la Marca Blanca y no con la de
FLIT.

## DESCRIPCIÓN (✏️)

La Marca Blanca **es una Concesión con marca propia**: reutiliza íntegro el mecanismo de red definido
en la épica #12235 y añade sobre él dos cosas —identidad visual heredable y acceso por URL dedicada—.
No se duplica la lógica de red ni se crea un tipo de cliente nuevo: la capacidad se activa sobre una
compañía, igual que la capacidad de ser cabeza de grupo.

Un administrador de FLIT activa la capacidad sobre la compañía, configura los parámetros visuales
iniciales (colores corporativos, logo y nombre de la plataforma) y la URL personalizada. Una vez
creada, la Marca Blanca administra su propia configuración visual, que sus clientes hijos heredan
automáticamente y no pueden anular.

---

## CRITERIOS FUNCIONALES

### Configuración de la capacidad Marca Blanca

- ✏️ Una compañía puede marcarse como **Marca Blanca**. Es una capacidad que se activa sobre una
  compañía existente, **no un tipo de cliente nuevo**: no se crean valores nuevos de tipo de cliente
  ni se modifican los existentes.
- ✏️ La Marca Blanca **hereda íntegro el alcance funcional de la Concesión** (#12235) y añade sobre él
  identidad visual y URL dedicada. No se duplica el mecanismo de red.
- ✅ Un administrador FLIT activa la capacidad y realiza la configuración visual e inicial de la plataforma.
- ✅ Una vez creada, la Marca Blanca puede autogestionar su configuración visual desde su panel de administración.

### Personalización visual

- ✅ La Marca Blanca puede configurar el **logo** de la plataforma.
- ✅ La Marca Blanca puede configurar la **paleta de colores** corporativos de la plataforma.
- ✅ La Marca Blanca puede configurar el **nombre de la plataforma** que se muestra a sus usuarios.
- ✅ La personalización aplica a toda la interfaz que ven sus usuarios y los de sus clientes hijos.
- ➕ La identidad visual se resuelve **antes del inicio de sesión**: la pantalla de acceso ya muestra la
  marca correspondiente a la URL por la que se entra.
- ➕ La configuración visual vive **únicamente en la Marca Blanca padre**. Los clientes hijos no tienen
  configuración visual propia ni pueden anular la heredada.

### Acceso por URL dedicada

- ✅ El cliente Marca Blanca opera bajo una URL propia configurada por el administrador FLIT.
- ✅ Los usuarios de la Marca Blanca y sus clientes hijos acceden **únicamente** a través de esa URL.
- ➕ **Una URL por red**: los clientes hijos acceden por la URL de su Marca Blanca padre, no por una propia.
- ➕ **La URL acota la autenticación**: en la URL de una Marca Blanca solo pueden autenticar usuarios de
  esa red (el padre o sus hijos). Un usuario de una red Marca Blanca **no** puede acceder por el
  dominio de FLIT.
- ➕ Un usuario con asignaciones en varias compañías solo dispone, en cada URL, de las de **esa** red.
  Si no tiene ninguna en esa red, no autentica.
- ➕ **Anti-enumeración:** ni la pantalla de acceso ni la resolución de identidad visual permiten
  averiguar desde fuera qué compañías o qué correos pertenecen a una red. Una URL desconocida
  presenta la identidad de FLIT sin revelar nada.
- ➕ **Dar de alta una Marca Blanca no requiere un despliegue**: registrar su URL en la configuración
  basta para que quede operativa.
- ➕ Los correos de **invitación** y de **recuperación de contraseña** dirigen al usuario a la URL de su
  red. (Un enlace al dominio de FLIT dejaría al usuario sin poder activar su cuenta.)

### Clientes hijos y herencia visual

- ✅ La Marca Blanca puede crear y gestionar clientes hijos con el mismo alcance que la Concesión.
- ✅ Los clientes hijos heredan automáticamente la configuración visual de su Marca Blanca padre.
- ✅ Un cliente hijo no puede pertenecer a más de una Marca Blanca.
- ✅ La Marca Blanca puede crear y gestionar usuarios tanto para sí misma como para sus clientes hijos.

### Visibilidad consolidada y reportes

- ✅ La Marca Blanca tiene acceso a los trámites y estadísticas de todos sus clientes hijos de forma consolidada.
- ✏️ Sobre los **trámites** de sus hijos el acceso es de **solo lectura**; sobre la **configuración** y los
  **usuarios** de sus hijos hay escritura. (Criterio ya decidido en #12235.)
- ➕ Los **artefactos documentales** de los hijos (FUR, mandato, compraventa, certificados de identidad
  y anexos) quedan **fuera de alcance**.
- ✅ La Marca Blanca puede generar reportes filtrables por cliente hijo o con información agregada de toda su red.
- ✅ Los datos mostrados corresponden únicamente a su propia red de clientes hijos.
- ➕ Queda **traza auditable** de qué usuario de la Marca Blanca consultó qué trámite de qué hijo y cuándo.

---

## DEPENDENCIAS Y RESTRICCIONES (➕ sección nueva)

- **Depende de la épica #12235 (Concesión)**, en concreto de su bloque de fundación (F0) y de su bloque
  de administración delegada (A). #12237 **no puede entregarse antes** que esos dos.
- **Sin afectar el comportamiento actual del aplicativo:** una compañía sin capacidad de Marca Blanca
  se comporta exactamente igual que hoy, incluida su entrada por el dominio de FLIT.
- El SuperAdmin de FLIT conserva acceso global sin cambios de ningún tipo.

## FUERA DE ALCANCE (➕ sección nueva)

- **Marca en los correos:** los correos conservan la identidad de FLIT. Solo el *enlace* apunta a la URL de la red.
- **Marca en los documentos generados** (mandato, compraventa, certificados).
- **El FUR no admite personalización:** es formato oficial (Resolución 20233040017145 de 2023, Anexo 46).
- Configuración visual propia, o anulación de la heredada, por parte de los clientes hijos.
- Más de un nivel de jerarquía: un cliente hijo no puede tener clientes hijos.

## DECISIÓN PENDIENTE (➕)

**¿Qué es exactamente «URL propia»?** Un subdominio de FLIT (`cliente.flit.co`) y un dominio propio del
cliente (`app.cliente.com`) son dos productos distintos: el segundo es lo que espera quien compra una
marca blanca, y arrastra gestión de certificados por cliente. Debe decidirse antes de estimar.

---

## Cambios propuestos de metadatos

| Qué | Estado hoy | Propuesta |
|---|---|---|
| Vínculo con #12235 | **Ninguno** | Enlace de dependencia explícito (#12237 depende de #12235) |
| Título | `[MARCA BLANCA] - Tipo de cliente Marca Blanca con personalización visual y URL propia` | Opcional: sustituir «Tipo de cliente» por «Capacidad de», por coherencia con el criterio corregido |
| Tag `DOR` | Ausente | Obligatorio antes de pasar a `Active` |
