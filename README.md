# Biozin Royale — Backend

API REST y servidor de tiempo real de **Biozin Royale**, una plataforma de casino en línea.
Concentra toda la lógica de negocio del sistema: autenticación, billetera, motor de juegos,
soporte y administración. El cliente únicamente presenta información — ninguna decisión de
juego, saldo o pago se resuelve fuera de este servicio.

- **Frontend:** [Biozin-Royale-Frontend](https://github.com/Ronald-Vargas/Biozin-Royale-Frontend)

---

## Arquitectura

Solución .NET organizada en cinco proyectos, con dependencias en una sola dirección
(`API → LógicaNegocio → Dominio ← AccesoDatos`):

| Proyecto | Responsabilidad |
|---|---|
| **`.API`** | Controladores REST, hubs de SignalR, servicios en segundo plano, autenticación y limitación de peticiones. Es el único ensamblado ejecutable. |
| **`.LogicaNegocio`** | Reglas de negocio: validaciones, cálculo de premios, motor de blackjack, flujos de depósito y retiro. |
| **`.Dominio`** | Entidades, DTOs (`TypedEntities`) e interfaces de los repositorios y de la lógica de negocio. No depende de nadie. |
| **`.AccesoDatos`** | Contexto de Entity Framework, repositorio genérico y unidad de trabajo. |
| **`.Utilidades`** | Auxiliares transversales: envoltura de respuestas, generación de credenciales y códigos, comprobantes, lectura de dispositivo y país. |

El acceso a datos usa **repositorio genérico + unidad de trabajo**: la lógica de negocio nunca
toca `DbContext` directamente y todos los cambios de una operación se confirman en un único
`SaveChanges`.

## Stack

- **.NET 10** · ASP.NET Core
- **Entity Framework Core 10** con **Npgsql** (PostgreSQL)
- **SignalR** para blackjack multijugador y chats de soporte
- **JWT** (`JwtBearer`) y **BCrypt** para contraseñas y PIN
- **Stripe.net** y **PayPal** (REST) para pagos
- **MailKit / MimeKit** para correo transaccional vía SMTP
- **QuestPDF** y **ClosedXML** para reportes en PDF y Excel
- **OpenAPI** para la descripción del contrato de la API

## Requisitos

- SDK de .NET 10 o superior
- Una base de datos PostgreSQL (el proyecto usa Supabase)
- Credenciales de Stripe, PayPal, un servidor SMTP y The Odds API

## Configuración

`appsettings.json` se versiona **como plantilla**: todos los valores sensibles aparecen como
`REEMPLAZAR_CON_*` y deben sobrescribirse por entorno. Nunca se suben credenciales reales al
repositorio.

En desarrollo, sobrescribí los valores con **User Secrets** (quedan fuera del árbol del proyecto):

```bash
cd Biozin-Royale-Backend.API
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:SupabaseConnection" "<cadena de conexión>"
dotnet user-secrets set "Payments:Stripe:SecretKey" "<clave secreta de Stripe>"
```

Como alternativa podés crear `appsettings.Development.json`, que ya está en `.gitignore`.
En producción los valores se definen como **configuración de aplicación de Azure App Service**.

### Claves requeridas

| Clave | Descripción |
|---|---|
| `ConnectionStrings:SupabaseConnection` | Cadena de conexión de PostgreSQL |
| `Jwt:LocalSigningKey` | Clave de firma de los tokens (mínimo 32 caracteres) |
| `Jwt:LocalIssuer` | Emisor de los tokens |
| `Supabase:Url` · `Supabase:AvatarsBucketBaseUrl` | Proyecto de Supabase y bucket público de avatares |
| `Mail:Smtp` · `Puerto` · `Usuario` · `Password` · `Remitente` | Servidor SMTP saliente |
| `Payments:Stripe:SecretKey` · `PublishableKey` · `WebhookSecret` | Credenciales de Stripe |
| `Payments:PayPal:ClientId` · `ClientSecret` · `BaseUrl` | Credenciales de PayPal |
| `OddsApi:Key` · `BaseUrl` | Proveedor de cuotas deportivas |

## Ejecución

```bash
dotnet restore
dotnet build
dotnet run --project Biozin-Royale-Backend.API
```

En desarrollo se publica el documento OpenAPI en `/openapi/v1.json`. En producción no se expone.

## Módulos

Los controladores cubren las siguientes áreas:

**Cuenta y seguridad** — `Auth`, `Profile`, `Avatars`, `Users`, `Staff`
**Dinero** — `Wallet`, `Depositos`, `Retiros`, `MetodosPago`, `Webhooks`
**Juegos** — `Ruleta`, `Slots`, `Sports`, `Bets`, `GamesHistory`
**Soporte y operación** — `Tickets`, `InternalRequests`, `Promotion`, `Reportes`

## Tiempo real

Dos hubs de SignalR, ambos autenticados por JWT (el token viaja en la cadena de consulta,
porque los WebSockets no admiten el encabezado `Authorization`):

- **`/hubs/blackjack`** — mesas multijugador. `BlackjackRoomManager` es un *singleton* que
  mantiene el estado de las salas y la máquina de estados de cada ronda.
- **`/hubs/chat`** — mensajes y notificaciones de tickets y solicitudes internas. El hub solo
  gestiona la pertenencia a los grupos; los eventos los emiten los controladores REST después
  de persistir, de modo que la API HTTP sigue siendo la única fuente de verdad.

> **Importante para el despliegue.** El estado de las mesas de blackjack vive en memoria y no
> existe un *backplane* de SignalR. El servicio **debe ejecutarse en una sola instancia** y con
> la afinidad de sesión activada. Con dos o más instancias los jugadores caerían en mesas
> distintas sin verse entre sí.

## Servicios en segundo plano

| Servicio | Frecuencia | Función |
|---|---|---|
| `BetSettlementService` | 10 min | Liquida las apuestas deportivas cuyos eventos ya finalizaron |
| `BonusExpirationService` | 30 min | Vence los bonos cuyo plazo se cumplió |
| `BlackjackRefundService` | Al iniciar | Reintegra apuestas de rondas interrumpidas por un reinicio |

## Seguridad

- Contraseñas y PIN almacenados con **BCrypt**; nunca se guardan ni registran en texto plano.
- **Limitación de peticiones** por política: `auth` y `auth-codes` se particionan por dirección
  IP; `sensitive`, `twofa-code` y `payments` por identificador de usuario, para no penalizar a
  quienes comparten una IP.
- Verificación en dos pasos por correo, bloqueo temporal tras intentos fallidos y control de
  sesiones activas con revocación individual.
- Los webhooks de pago **verifican la firma** del proveedor antes de acreditar cualquier monto.
- CORS restringido a los orígenes declarados; SignalR negocia con credenciales y exige
  orígenes explícitos.

## Despliegue

El servicio se publica en **Azure App Service**. Antes de desplegar, verificá que estén
activados los **WebSockets** y que el número de instancias sea **exactamente uno**.
