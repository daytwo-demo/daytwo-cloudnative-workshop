# Laboratorios

Ocho laboratorios que transforman **TaskFlow** (ver `app/`) de una API
básica a un despliegue Cloud Native completo sobre OpenShift. Cada lab
construye sobre el resultado del anterior: no son ejercicios
independientes.

## Agenda de la semana

| Día | Labs |
|---|---|
| Lunes | [lab01-contenerizar](lab01-contenerizar/README.md) |
| Martes | [lab02-config](lab02-config/README.md), [lab03-imagen](lab03-imagen/README.md) |
| Miércoles | [lab04-pipeline](lab04-pipeline/README.md), [lab05-health](lab05-health/README.md) |
| Jueves | [lab06-observabilidad](lab06-observabilidad/README.md), [lab07-seguridad](lab07-seguridad/README.md) |
| Viernes | [lab08-capstone](lab08-capstone/README.md) |

## Esquema de branches y tags

- **`main`**: estado inicial de `app/` (sin cloud-native-izar) + los
  README de cada lab con los `# TODO` que el alumno debe resolver. Es lo
  que clona cada participante.
- **`solution`**: el mismo repo con las ocho capas ya aplicadas: la
  referencia completa del instructor.
- **Tags `labNN-start` / `labNN-solution`**: marcan, dentro de la
  historia del branch `solution`, el commit justo antes y justo después de
  aplicar el lab `NN`. `lab01-start` coincide con `main`.

Esto permite dos formas de consultar una solución sin spoilearse el resto
del workshop:

```bash
# Ver el estado completo de un lab puntual, sin ver los labs posteriores
git checkout labNN-solution

# Ver únicamente el diff que introdujo ese lab
git diff labNN-start labNN-solution
```

Un participante que se traba en el lab03, por ejemplo, puede hacer
`git diff lab03-start lab03-solution` y ver exactamente qué cambió, sin
tocar `main` ni adelantarse al lab04.

## Paso 0: crear tu workspace de Dev Spaces

El entorno de trabajo es OpenShift Dev Spaces (Eclipse Che), no tu
máquina local:

1. Entrar a la URL de Dev Spaces que dio el instructor, loguearse con tu
   usuario del workshop.
2. **Import from Git** → pegar la URL de este repo
   (`https://github.com/daytwo-demo/daytwo-cloudnative-workshop.git`) →
   **Create & Open**.
3. Esperar a que provisione (unos minutos la primera vez). El repo queda
   clonado en `/projects/daytwo-cloudnative-workshop`, con `git`, `oc` y
   el SDK de .NET 10 ya instalados en la terminal, no hace falta
   instalar nada a mano.
4. **Loguearse con `oc` como tú mismo.** La terminal del workspace
   arranca autenticada como una cuenta de servicio interna del propio
   Dev Spaces, sin permisos sobre tu namespace (`<tu-usuario>`): hace
   falta un login explícito:
   ```bash
   oc login https://api.workshop.bg.daytwodemo.com:6443 -u <tu-usuario> -p <tu-password>
   ```
   El API server usa un certificado autofirmado: cuando pregunte
   `Use insecure connections? (y/n)`, responder `y`. Confirmar con
   `oc whoami` que devuelve tu usuario, no un `system:serviceaccount:...`.
5. **Pararte en tu namespace de aplicación, explícitamente.** Tu usuario
   tiene acceso a dos namespaces (`<tu-usuario>`, donde van los labs,
   y `<tu-usuario>-devspaces`, el del propio workspace de Dev Spaces): el
   login no elige el correcto automáticamente.
   ```bash
   oc project <tu-usuario>
   ```
   Confirma con `oc project -q` antes de construir o publicar ninguna
   imagen: si el `NS` que usan los comandos de cada lab termina en
   `-devspaces`, la imagen queda etiquetada en el `ImageStream`
   equivocado, y el `Deployment` del lab no la va a encontrar.
6. Un solo workspace alcanza para toda la semana, no crear uno nuevo por
   lab.
7. **Activar el preview de Markdown** para leer estos README cómodos:
   che-code trae el editor de VS Code, que ya incluye el preview nativo,
   no hace falta instalar ninguna extensión. Con el archivo abierto,
   `Ctrl+Shift+V` (o el ícono de la lupa con hoja arriba a la derecha del
   editor) lo abre renderizado, al lado del original.

### Si `git pull` da error de "divergent branches"

Si en algún momento de la semana `git pull` responde con algo parecido a
`You have divergent branches and need to specify how to reconcile them`,
no es un problema de tu copia: el instructor actualizó el contenido del
repo del lado del servidor. La forma segura de resolverlo, sin arriesgar
ningún trabajo tuyo (no deberías tener cambios propios sin aplicar, ya
que los labs se validan con `oc apply`, no con `git commit`):

```bash
git fetch origin
git reset --hard origin/$(git branch --show-current)
```

Esto descarta tu copia local del branch y la deja idéntica a la del
repositorio remoto. No uses `git config pull.rebase true` para este caso
puntual: intenta combinar tu historia local con la nueva, lo cual puede
dejar commits duplicados o confusos si ambas historias no comparten un
punto de partida común.

### Sobre los comandos de este workshop

Vas a ver bastante `$(algún-comando)` en los pasos de cada lab, por
ejemplo `curl "https://$(oc get route taskflow-api -o jsonpath=...)"`.
Esa sintaxis de la terminal (no es específica de este workshop, es de
`bash`) significa "correr lo de adentro de `$(...)` primero, y usar su
resultado como si lo hubieras escrito ahí a mano". Así, en vez de copiar
la URL de la Route a mano cada vez, el comando la busca solo. No hace
falta escribirlo distinto ni entender `bash` a fondo, alcanza con saber
que esa parte se resuelve sola.

## Cómo usar cada lab

1. Leer el `README.md` de la carpeta del lab: objetivo, contexto, pasos y
   checklist de "hecho".
2. Trabajar sobre el código en `app/` (o los manifiestos que el lab
   indique) resolviendo los `# TODO`.
3. Validar contra el checklist antes de pasar al siguiente lab.
4. Si hace falta, comparar contra la solución con los comandos de arriba,
   sin copiarla antes de intentarlo.

## Convención de marcadores

- `# TODO`: el alumno debe completar o decidir algo acá.
- `# SOLUCIÓN`: presente solo en el branch `solution`; código ya resuelto,
  de referencia para el instructor.
