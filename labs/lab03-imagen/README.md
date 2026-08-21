# Lab 03: Imagen productiva y non-root

**Día:** martes · **Branch de referencia:** `lab03-start` → `lab03-solution`

## Objetivo

Convertir el Dockerfile básico del lab01 en uno productivo: multi-stage
más ajustado, usuario non-root explícito, rootfs de solo lectura donde
aplique, y `resources.requests/limits` en el Deployment, resolviendo los
problemas de SCC (Security Context Constraints) que van a aparecer en el
camino.

## Contexto

OpenShift, por defecto, corre los pods bajo la SCC `restricted-v2`: fuerza
un UID arbitrario asignado por namespace (`MustRunAsRange`), **sin
importar** qué usuario declare la imagen con `USER`, y siempre agrega ese
UID al grupo `0` (root group). Esto significa dos cosas:

- Declarar un `USER` numérico en el Dockerfile no garantiza que el
  contenedor corra con ese UID en OpenShift, pero sí es buena práctica
  para Kubernetes en general y para pasar el check `runAsNonRoot`.
- Lo que realmente hace que la imagen funcione con **cualquier** UID
  arbitrario es que los archivos que la app necesita leer/escribir tengan
  permisos de grupo (`g=u`) con `chgrp 0`, porque ese grupo `0` sí es
  consistente pase lo que pase.

### Pod Security Admission vs. SCC

Kubernetes, sin OpenShift, impone políticas de seguridad de pods vía
**Pod Security Admission**: labels a nivel de namespace
(`pod-security.kubernetes.io/enforce: restricted`) que rechazan cualquier
pod que no cumpla el perfil `restricted` (sin privilegios,
`runAsNonRoot`, sin `hostNetwork`, etc.). Es el mecanismo portable,
funciona igual en cualquier clúster Kubernetes conformant.

SCC es la conveniencia que agrega OpenShift encima de eso: más granular
(fuerza además el UID arbitrario que motiva el `chgrp 0` de arriba) y
evaluada antes de que Pod Security Admission entre en juego. Todo lo que
este lab resuelve para pasar `restricted-v2` deja la imagen lista también
para el perfil `restricted` de Pod Security Admission, sin cambiar una
línea del Dockerfile: se verifica en el lab07.

## Pasos

> **Antes de empezar:** confirma que ya hiciste `oc login` como tu usuario
> (Paso 0 de `labs/README.md`) en esta terminal, `oc whoami` tiene que
> devolver tu usuario, no un `system:serviceaccount:...`. La sesión no
> persiste entre reinicios del workspace.

1. **Actualizar `app/Dockerfile`** con una versión
   productiva:
   ```dockerfile
   FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
   WORKDIR /src
   COPY TaskFlow.Api/*.csproj .
   RUN dotnet restore
   COPY TaskFlow.Api/. .
   RUN dotnet publish -c Release -o /app/publish --no-restore

   FROM mcr.microsoft.com/dotnet/aspnet:10.0
   WORKDIR /app
   COPY --from=build /app/publish .

   # Soporte para UID arbitrario asignado por OpenShift: el grupo 0 sí es
   # predecible, aunque el UID no lo sea.
   RUN chgrp -R 0 /app && chmod -R g=u /app

   # Correr explícitamente como un UID non-root fijo (1654, el mismo que
   # trae predefinido la imagen base con el usuario "app" desde .NET 8):
   # válido en Kubernetes fuera de OpenShift, y no rompe nada cuando
   # OpenShift lo sobreescribe con su propio UID arbitrario.
   USER 1654

   EXPOSE 8080
   ENTRYPOINT ["dotnet", "TaskFlow.Api.dll"]
   ```

2. **Agregar un volumen efímero para `/tmp`.** Con rootfs de solo lectura,
   .NET necesita un directorio escribible para archivos temporales
   (extracción de paquetes, data protection keys, etc.). En
   `manifests/deployment.yaml` (ya tiene esto resuelto como referencia,
   revisar el bloque `volumes`/`volumeMounts`) se monta un `emptyDir` en
   `/tmp` y se fija `readOnlyRootFilesystem: true`.

3. **Completar el resto de `securityContext`** en
   `manifests/deployment.yaml` (tiene `# TODO`):
   `runAsNonRoot: true`, `allowPrivilegeEscalation: false`,
   `capabilities.drop: ["ALL"]`, `seccompProfile.type: RuntimeDefault`.

4. **Agregar `resources.requests/limits`** (tiene `# TODO`): valores
   livianos, acordes a un lab (ej. `requests: 100m/128Mi`,
   `limits: 500m/256Mi`).

5. **Reconstruir y publicar la imagen, y volver a desplegar** (mismos
   pasos de `build`/`push` del lab01, con el tag `:lab03`; el login de
   `podman` ya lo tienes de antes, no hace falta repetirlo):
   ```bash
   REGISTRY=default-route-openshift-image-registry.apps.workshop.bg.daytwodemo.com
   NS=$(oc project -q)          # tu namespace actual (dev-<tu-usuario>)

   podman build -t $REGISTRY/$NS/taskflow-api:lab03 \
     -f app/Dockerfile app
   podman push $REGISTRY/$NS/taskflow-api:lab03

   oc apply -f labs/lab03-imagen/manifests/
   oc rollout status deployment/taskflow-api
   ```

## Criterios de "hecho"

- [ ] La imagen no requiere la SCC `anyuid`, corre bien bajo
      `restricted-v2` (`oc get pod <pod> -o jsonpath='{.spec.securityContext}'`).
- [ ] `readOnlyRootFilesystem: true` sin que el pod falle al arrancar.
- [ ] El Deployment tiene `resources.requests` y `resources.limits`.
- [ ] `oc get events` no muestra errores de permisos (`Permission denied`)
      en el contenedor.
- [ ] Se entiende la diferencia entre SCC (OpenShift) y Pod Security
      Admission (Kubernetes), y por qué esta imagen pasaría ambas.

## Pistas

- Si el pod falla con `Permission denied` escribiendo en algún directorio,
  identificar cuál con `oc logs` y montarle un `emptyDir` ahí (igual que
  `/tmp`) en vez de aflojar `readOnlyRootFilesystem`.
- `oc get scc restricted-v2 -o yaml` muestra exactamente qué exige la SCC
  por defecto: es más rápido leerla que adivinar.
- Nunca es necesario (ni deseable) pedir la SCC `anyuid` para una app .NET
  bien empaquetada: si sientes que la necesitas, es señal de que falta un
  `chgrp`/`chmod` en el Dockerfile, no de que falte el permiso.
