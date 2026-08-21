# Lab 07: Seguridad

**Día:** jueves · **Branch de referencia:** `lab07-start` → `lab07-solution`

## Objetivo

Cerrar las capas de seguridad que quedaban pendientes: confirmar que
non-root/SCC siguen aplicándose correctamente, verificar que la imagen
del lab03 también pasa Pod Security Admission (el equivalente portable de
la SCC), revisar el manejo de Secrets, aplicar RBAC de mínimo privilegio
para la ServiceAccount del pipeline (lab04), y agregar `NetworkPolicy` de
segmentación real entre los componentes de TaskFlow.

## Contexto

Tu namespace (`dev-<tu-usuario>`) ya tiene, desde el aprovisionamiento
inicial del workshop, una `NetworkPolicy` base que niega todo el tráfico
por defecto. Hasta ahora la app funcionó
igual porque esa política solo bloquea tráfico *entrante desde otros
namespaces*: el tráfico entre pods de tu propio namespace y desde el
router de OpenShift no estaba restringido. Este lab agrega las reglas
explícitas para que, aun con una política de aislamiento estricta, solo
pase el tráfico que TaskFlow necesita, ni más, ni menos.

## Pasos

> **Antes de empezar:** confirma que ya hiciste `oc login` como tu usuario
> (Paso 0 de `labs/README.md`) en esta terminal, `oc whoami` tiene que
> devolver tu usuario, no un `system:serviceaccount:...`. La sesión no
> persiste entre reinicios del workspace.

1. **Verificar SCC y non-root (repaso del lab03).**
   ```bash
   oc get pod -l app=taskflow-api \
     -o jsonpath='{.items[0].metadata.annotations.openshift\.io/scc}'
   ```
   Debe imprimir `restricted-v2`. Si en algún momento del workshop tu pod
   quedó con `anyuid` o `privileged`, es señal de que algo en el
   Dockerfile o el manifiesto se resolvió mal en el lab03: corregirlo ahí,
   no acá.

2. **Confirmar Pod Security Admission (equivalente portable de la SCC).**
   ```bash
   oc label namespace $(oc project -q) pod-security.kubernetes.io/enforce=restricted --overwrite
   oc rollout restart deployment/taskflow-api
   oc get pods -w
   ```
   El pod sigue arrancando sin problema: la imagen del lab03 ya cumple el
   perfil `restricted` de Pod Security Admission, no solo la SCC
   `restricted-v2`. Si algún ítem del lab03 hubiera quedado mal resuelto
   (por ejemplo, sin `runAsNonRoot`), este es el punto donde también
   fallaría fuera de OpenShift.

3. **Revisar el manejo de Secrets.** Confirmar que:
   - `taskflow-db-credentials` nunca existió como archivo con un valor
     real en el repo:
     ```bash
     git log --all --format=%H -- 'labs/*/manifests/*.yaml' \
       | xargs -I{} git show {} -- 'labs/*/manifests/*.yaml' 2>/dev/null \
       | grep -i password
     ```
     Solo deberían aparecer nombres de clave/variable (`key: password`,
     `POSTGRESQL_PASSWORD`, `Db__Password`), nunca un valor literal.
   - Nadie en el equipo necesita leer el valor del Secret a mano para que
     la app funcione: llega a los pods vía `secretKeyRef`, nunca como
     variable de entorno en texto plano en un manifiesto commiteado.

4. **RBAC de mínimo privilegio para el pipeline.** Completar
   `manifests/pipeline-role.yaml` y `manifests/pipeline-rolebinding.yaml`
   (tienen `# TODO`): un `Role` que le da a la ServiceAccount `pipeline`
   (usada en el lab04) **solo** los verbos que necesita:
   `get`/`list`/`watch`/`patch` sobre `deployments` (el `list`+`watch` los
   pide `oc rollout status`, no solo `oc set image`), nada de `delete` ni
   acceso a `secrets`. Este Role/RoleBinding ya existe desde que se
   aprovisionó tu namespace (por eso el pipeline del lab04 ya pudo
   desplegar el miércoles), acá lo completas de memoria y lo vuelves a
   aplicar, sin que cambie nada; el punto es entender por qué el clúster está
   configurado para que la ServiceAccount `pipeline` **no** tenga el
   `edit` amplio que el operador de OpenShift Pipelines da por defecto.

5. **NetworkPolicy de segmentación.** Completar
   `manifests/networkpolicy.yaml` (tiene `# TODO`) con cuatro reglas:
   - Permitir ingreso al pod de `taskflow-api` **solo** desde el router de
     OpenShift, identificado por la label estándar `policy-group.network.openshift.io/ingress: ""`
     en su namespace (no por nombre, es portable entre SDN y OVN-Kubernetes),
     en el puerto 8080.
   - Permitir ingreso al pod de `taskflow-api` también desde los
     namespaces de monitoreo (`network.openshift.io/policy-group:
     monitoring`), mismo puerto 8080: si no, el `ServiceMonitor` del
     lab06 deja de scrapear en cuanto se aplica esta política (el target
     pasa a `down` con `context deadline exceeded`).
   - Permitir ingreso al pod de `taskflow-db` **solo** desde pods con label
     `app: taskflow-api`, puerto 5432.
   - Todo lo demás queda denegado por la política base ya existente.

6. **Aplicar y validar:**
   ```bash
   oc apply -f labs/lab07-seguridad/manifests/
   curl "https://$(oc get route taskflow-api -o jsonpath='{.spec.host}')/api/tasks"
   ```
   Si la Route sigue respondiendo pero un `oc exec` a un pod cualquiera de
   otro namespace ya no puede alcanzar directamente el `Service` de
   `taskflow-api` por su IP interna, la segmentación está bien aplicada.

## Criterios de "hecho"

- [ ] El pod sigue corriendo bajo `restricted-v2`, sin SCC elevada.
- [ ] El namespace pasa a `enforce=restricted` (Pod Security Admission)
      sin que el pod deje de arrancar.
- [ ] Ningún valor de Secret aparece en texto plano en el historial de Git.
- [ ] La ServiceAccount `pipeline` puede hacer `get`/`list`/`watch`/`patch`
      sobre `deployments` en tu namespace, pero no `delete`: verificar con
      `oc auth can-i delete deployments --as=system:serviceaccount:<ns>:pipeline`
      (debe responder `no`).
- [ ] La Route sigue funcionando después de aplicar las `NetworkPolicy`.
- [ ] El tráfico directo pod-a-pod desde otro namespace queda bloqueado.
- [ ] El `ServiceMonitor` del lab06 sigue con el target en `up` (**Observe
      → Targets**) después de aplicar la `NetworkPolicy`.

## Pistas

- `oc auth can-i --list --as=system:serviceaccount:<ns>:pipeline` es la
  forma más rápida de auditar qué le quedó habilitado a la ServiceAccount.
- Si la Route deja de responder después de aplicar la `NetworkPolicy`, el
  namespace del router casi siempre es `openshift-ingress`: confirmarlo
  con `oc get pods -n openshift-ingress` antes de asumir el nombre.
- El valor de la label `policy-group.network.openshift.io/ingress` es un
  string vacío (`""`), no un booleano ni "true": es una label, no una
  anotación con valor semántico.
- La label de los namespaces de monitoreo NO sigue el mismo formato que
  la del router: es `network.openshift.io/policy-group: monitoring`
  (prefijo y valor distintos): confirmar con `oc get ns
  openshift-user-workload-monitoring -o jsonpath='{.metadata.labels}'`
  antes de asumir que es la misma convención.
- Tu namespace ya trae, desde el aprovisionamiento inicial, una
  `NetworkPolicy` base más permisiva (todo el tráfico intra-namespace +
  el router). Este lab la reemplaza por reglas más finas, no es la
  primera vez que una `NetworkPolicy` toca este namespace.
