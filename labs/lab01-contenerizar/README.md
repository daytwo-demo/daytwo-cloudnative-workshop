# Lab 01: Contenerizar TaskFlow

**Día:** lunes · **Branch de referencia:** `main` (`lab01-start`) → `lab01-solution`

## Objetivo

Entender qué hace cada línea de un Dockerfile y por qué existe el patrón
multi-stage, practicándolo primero sobre un proyecto mínimo sin
dependencias (`hello-api`, Parte A) para tener una primera victoria sin
sorpresas. Recién después aplicar exactamente lo mismo sobre TaskFlow
(Parte B), publicar la imagen en el registry interno de OpenShift, y
desplegarla con `Deployment` + `Service` + `Route`.

## Contexto

TaskFlow (`app/TaskFlow.Api/`) todavía no tiene forma de correr en un
contenedor: su `Dockerfile` es de una sola etapa, y su connection string a
PostgreSQL está hardcodeada a `Host=localhost` en `appsettings.json`. Ese
`localhost` es del alumno hoy, y va a ser un problema en cuanto la app
corra en un Pod, no hay ningún PostgreSQL en `localhost` dentro de un
contenedor. Para este lab, la solución **no** es arreglar la
configuración (eso es el lab02): es meter un contenedor de PostgreSQL como
*sidecar* en el mismo Pod, para que "localhost" siga siendo válido dentro
del namespace de red del Pod. Es una solución fea a propósito: el objetivo
es que se sienta el dolor de la config hardcodeada antes de resolverlo
formalmente mañana.

Antes de llegar a esa parte, la Parte A de este lab usa un proyecto
aparte, `hello-api` (sin base de datos, sin nada que pueda fallar), para
que el primer contenedor que construyas, publiques y despliegues funcione
a la primera. El objetivo de esa parte no es TaskFlow: es que la mecánica
de "Dockerfile → build → push → Deployment → Route → 200" quede clara y
sin fricción antes de meterla con un caso real que sí tiene un problema
intencional.

## Conceptos

- **Qué hace `dotnet publish` (si nunca tocaste .NET)**: compila el
  código (`dotnet restore` baja las dependencias del `.csproj`,
  equivalente a un `npm install` o un `mvn install`) y arma una carpeta
  con el ensamblado (`.dll`) más todo lo que necesita para correr.
- **Qué es un *stage* en un Dockerfile**: cada instrucción `FROM` arranca
  un sistema de archivos nuevo e independiente, sin relación con el `FROM`
  anterior salvo que se copie algo explícitamente con `COPY --from=<stage>`.
  Un Dockerfile puede tener uno o varios `FROM`; solo el contenido del
  **último** stage termina en la imagen final. Esto es lo que hace posible
  compilar con una imagen pesada (el SDK) y correr con una liviana (el
  runtime), sin que el compilador viaje a producción.
- **Principios Cloud Native**: diseñar asumiendo que un pod puede morir en
  cualquier momento (la plataforma lo reemplaza, no lo repara), mantener
  el estado fuera del proceso, y declarar en manifiestos lo que la
  plataforma debe garantizar en vez de operarlo a mano.
- **Arquitectura basada en contenedores**: la unidad de despliegue es una
  imagen inmutable versionada, no un servidor que se actualiza en el
  lugar.
- **Responsabilidad de la plataforma vs. del desarrollador**: OpenShift
  garantiza que el contenedor declarado siga corriendo (scheduling, red,
  reinicios); el desarrollador garantiza que la app se comporte bien
  dentro de ese contrato (arranca sin depender de estado local, expone
  sus puertos, sabe reportar si está sana). El `Deployment` de este lab es
  la primera vez que se cruza esa línea explícitamente.
- **`Route` es una conveniencia de OpenShift**, no de Kubernetes: el
  objeto portable equivalente para exponer un `Service` hacia afuera del
  clúster es `Ingress`. Se usa `Route` acá porque es lo nativo del
  clúster del workshop.

## Pasos

> **Antes de empezar:** confirma que ya hiciste `oc login` como tu usuario
> (Paso 0 de `labs/README.md`) en esta terminal, `oc whoami` tiene que
> devolver tu usuario, no un `system:serviceaccount:...`. La sesión no
> persiste entre reinicios del workspace.

### Parte A: mecánica de contenerizar, sin fricción (`hello-api`)

`labs/lab01-contenerizar/hello-api/` es un proyecto .NET mínimo (un único
endpoint, sin base de datos) que ya viene completo: no hay nada que
resolver acá, es para leer, construir y correr.

1. **Leer y construir el Dockerfile de una sola etapa**
   (`hello-api/Dockerfile.una-etapa`):

   ```dockerfile
   FROM mcr.microsoft.com/dotnet/sdk:10.0
   WORKDIR /src
   COPY *.csproj .
   RUN dotnet restore
   COPY . .
   RUN dotnet publish -c Release -o /app/publish --no-restore
   WORKDIR /app/publish
   EXPOSE 8080
   ENTRYPOINT ["dotnet", "HelloApi.dll"]
   ```

   Línea por línea:

   - `FROM mcr.microsoft.com/dotnet/sdk:10.0`: imagen base. Trae el SDK
     completo de .NET 10 (compilador, herramientas de build, runtime):
     todo lo necesario para **compilar**, mucho más de lo necesario para
     **correr**.
   - `WORKDIR /src`: crea (si no existe) y se posiciona en `/src` dentro
     de la imagen; toda instrucción siguiente corre relativa a esa
     carpeta.
   - `COPY *.csproj .`: copia solo el archivo de proyecto, todavía no el
     código. Es a propósito: separar esta copia de la del código fuente
     permite que Podman/Docker cachee la capa de `dotnet restore` (que
     baja paquetes NuGet) y no la repita si solo cambió el código, no las
     dependencias.
   - `RUN dotnet restore`: descarga los paquetes NuGet declarados en el
     `.csproj`.
   - `COPY . .`: ahora sí copia el resto del código fuente.
   - `RUN dotnet publish -c Release -o /app/publish --no-restore`: compila
     en modo Release y deja el resultado (el ensamblado + todo lo
     necesario para ejecutar) en `/app/publish`. `--no-restore` evita
     repetir el restore que ya se hizo arriba.
   - `WORKDIR /app/publish`: cambia el directorio de trabajo al de la
     publicación, para que el `ENTRYPOINT` no necesite rutas absolutas.
   - `EXPOSE 8080`: documenta el puerto donde el contenedor escucha. No
     abre ningún puerto por sí solo (eso lo hace `-p` en `podman run`, o
     el `Service` en Kubernetes): es metadata.
   - `ENTRYPOINT ["dotnet", "HelloApi.dll"]`: el comando que arranca el
     contenedor. Kestrel (el servidor HTTP embebido de .NET) escucha en
     el puerto 8080 por defecto en contenedores .NET 8+, sin configurar
     `ASPNETCORE_URLS`.

   Construirla, correrla, y confirmar que responde:

   ```bash
   cd labs/lab01-contenerizar/hello-api
   podman build -t hello-api:una-etapa -f Dockerfile.una-etapa .
   podman images hello-api:una-etapa   # anotar el tamaño

   podman run --rm -p 8080:8080 hello-api:una-etapa &
   curl http://localhost:8080/
   ```

2. **Leer y construir la versión multi-stage** (`hello-api/Dockerfile`):

   ```dockerfile
   FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
   WORKDIR /src
   COPY *.csproj .
   RUN dotnet restore
   COPY . .
   RUN dotnet publish -c Release -o /app/publish --no-restore

   FROM mcr.microsoft.com/dotnet/aspnet:10.0
   WORKDIR /app
   COPY --from=build /app/publish .
   EXPOSE 8080
   ENTRYPOINT ["dotnet", "HelloApi.dll"]
   ```

   Qué cambió respecto al anterior:

   - `FROM ... AS build`: es el mismo primer stage de antes, solo que
     ahora tiene nombre (`build`) para poder referenciarlo más adelante.
   - `FROM mcr.microsoft.com/dotnet/aspnet:10.0`: un **segundo** `FROM`
     arranca un stage completamente nuevo, desde la imagen de *runtime*
     de ASP.NET (sin SDK, sin compilador, sin herramientas de build: solo
     lo necesario para ejecutar un `.dll` ya compilado).
   - `COPY --from=build /app/publish .`: la única cosa que cruza del
     stage `build` al final es el resultado ya compilado. El compilador,
     el código fuente y la caché de NuGet se quedan atrás, no forman
     parte de la imagen final.
   - El resto (`EXPOSE`/`ENTRYPOINT`) es idéntico.

   Construir bajo otro tag y comparar tamaños:

   ```bash
   podman build -t hello-api:multi-stage -f Dockerfile .
   podman images | grep hello-api
   ```

   La diferencia de tamaño entre ambas filas **es** la razón de ser del
   multi-stage build: nada de lo que el compilador necesitó queda en la
   imagen que corre en producción.

3. **Publicar y desplegar `hello-api`.** Los manifiestos en
   `hello-api/manifests/` ya están completos, no hay nada que editar:

   ```bash
   REGISTRY=default-route-openshift-image-registry.apps.workshop.bg.daytwodemo.com
   NS=$(oc project -q)          # tu namespace actual (<tu-usuario>)
   USUARIO=$(oc whoami)
   TOKEN=$(oc whoami -t)

   podman login -u $USUARIO -p $TOKEN $REGISTRY
   podman tag hello-api:multi-stage $REGISTRY/$NS/hello-api:multi-stage
   podman push $REGISTRY/$NS/hello-api:multi-stage

   sed "s#tu-namespace#${NS}#g" manifests/deployment.yaml | oc apply -f -
   oc apply -f manifests/service.yaml -f manifests/route.yaml

   curl "https://$(oc get route hello-api -o jsonpath='{.spec.host}')/"
   ```

   Si ves `Hola desde un contenedor en OpenShift!`, esa es tu primera
   victoria del workshop: contenerizaste, publicaste y desplegaste algo de
   punta a punta, sin ningún error en el medio. Vuelve a esta parte si algo de
   TaskFlow (Parte B) no funciona, para aislar si el problema es de
   mecánica (que ya probaste que domina) o específico de TaskFlow.

### Parte B: TaskFlow real (con el problema intencional)

4. **Construir el Dockerfile de una sola etapa de TaskFlow**
   (`app/Dockerfile`, ya completo, mismo patrón que el paso 1):

   ```bash
   podman build -t taskflow-api:una-etapa -f app/Dockerfile app
   podman images taskflow-api:una-etapa
   ```

   Anotar el tamaño. Probarla:

   ```bash
   podman run --rm -p 8080:8080 taskflow-api:una-etapa
   ```

   Esta vez sí va a fallar al conectar a Postgres (`EnsureCreated()` tira
   una excepción de conexión), a diferencia de cuando corriste
   `dotnet run --project TaskFlow.Api` directo en la terminal (Paso 0 de
   `labs/README.md`). La diferencia no es casualidad: `dotnet run`
   arranca un *proceso* dentro del mismo contenedor de la terminal, que
   comparte el namespace de red del Pod con el sidecar de Postgres del
   devfile, por eso `localhost:5432` resuelve. `podman run` en cambio
   crea un **contenedor nuevo, con su propio namespace de red aislado**:
   "localhost" ahí adentro es el loopback de ese contenedor recién
   creado, donde no hay ningún Postgres escuchando. Es la misma razón por
   la que el Deployment real necesita su propio sidecar de Postgres en el
   paso 6: cada Pod (o cada contenedor anidado) tiene que resolver
   "localhost" contra algo que esté corriendo ahí mismo.

5. **Convertirlo a multi-stage tú mismo.** Ahora que ya lo hiciste una vez
   con `hello-api`, aplica el mismo patrón a `app/Dockerfile`: agrégale
   `AS build` al primer `FROM`, y debajo un segundo `FROM
   mcr.microsoft.com/dotnet/aspnet:10.0` con `COPY --from=build
   /app/publish .`. No hace falta ningún archivo de referencia nuevo: es
   exactamente la misma transformación del paso 2.

   Reconstruir bajo otro tag y comparar:

   ```bash
   podman build -t taskflow-api:multi-stage -f app/Dockerfile app
   podman images | grep taskflow-api
   ```

   No hace falta todavía usuario non-root, rootfs de solo lectura, ni
   resource limits, eso es lab03.

6. **Publicarla en el registry interno de OpenShift.** Mismo mecanismo que
   usaste para `hello-api` en el paso 3, con tu propio token de OpenShift
   haciendo de password:

   ```bash
   podman build -t $REGISTRY/$NS/taskflow-api:lab01 \
     -f app/Dockerfile app
   podman push $REGISTRY/$NS/taskflow-api:lab01
   ```

   El `push` crea automáticamente un `ImageStream taskflow-api` con el tag
   `lab01` en tu namespace: verifícalo con `oc get is taskflow-api`.

7. **Completar `manifests/deployment.yaml`** (tiene `# TODO`): referencia
   la imagen que acabas de publicar, y agrega un contenedor sidecar de
   PostgreSQL usando `registry.redhat.io/rhel9/postgresql-16:latest` (ya
   preparado para correr con cualquier UID, como exige la SCC
   `restricted-v2` por defecto de OpenShift; se descarga directo del
   registry de Red Hat, no hace falta ningún `ImageStream` ni pull secret
   adicional). Las variables `POSTGRESQL_DATABASE` / `POSTGRESQL_USER` /
   `POSTGRESQL_PASSWORD` del sidecar deben coincidir con lo hardcodeado en
   `appsettings.json` (`taskflow` / `taskflow` / `TaskFlow!2024`).

8. **Completar `manifests/service.yaml` y `manifests/route.yaml`** (tienen
   `# TODO`): `Service` ClusterIP apuntando al puerto 8080 del Deployment,
   `Route` edge apuntando al `Service`.

9. **Desplegar y validar:**

   ```bash
   oc apply -f labs/lab01-contenerizar/manifests/
   oc get pods -w
   curl "https://$(oc get route taskflow-api -o jsonpath='{.spec.host}')/api/tasks"
   ```

## Criterios de "hecho"

- [ ] `hello-api` responde `200` en su propia Route (Parte A, sin
      fricción).
- [ ] Puedes explicar qué copia `COPY --from=build` y por qué la imagen
      final no tiene el SDK.
- [ ] `oc get is taskflow-api` muestra la imagen publicada.
- [ ] El pod de TaskFlow queda en `Running` (no `CrashLoopBackOff`): sin
      esto, algo falló conectando el sidecar de Postgres.
- [ ] `GET /api/tasks` a través de la Route de TaskFlow devuelve `200` con
      `[]`.
- [ ] Puedes explicar por qué hizo falta el sidecar de Postgres en este lab
      puntual (pista: `Host=localhost` en la connection string).

## Pistas

- Si `hello-api` no responde, revisa eso antes de seguir con TaskFlow: es
  la parte sin ninguna dependencia externa, así que un error ahí es de
  mecánica (build/push/deploy), no de configuración de la app.
- Si el pod de TaskFlow queda en `CrashLoopBackOff`, revisar los logs del
  contenedor `taskflow-api` (`oc logs <pod> -c taskflow-api`): el error
  más común es que el sidecar de Postgres todavía no estaba listo cuando
  la app intentó `EnsureCreated()`. Un `restartPolicy` normal de
  Kubernetes ya reintenta esto solo.
- El puerto por defecto de Kestrel en contenedores .NET 8+ es `8080`, no
  hace falta setear `ASPNETCORE_URLS` a mano.
