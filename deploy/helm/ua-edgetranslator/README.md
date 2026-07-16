# UA Edge Translator Helm chart

Deploys [UA Edge Translator](https://github.com/OPCFoundation/UA-EdgeTranslator) — a
standards-based, containerized industrial connectivity edge application that
translates many proprietary protocols to OPC UA — into Kubernetes.

This chart renders the same resource set as the
[`UA-EdgeTranslator.yaml`](../../../UA-EdgeTranslator.yaml) manifest, but every
site-specific decision (namespace, images, credentials, persistence, service
exposure, security context, driver-pack rollout, RBAC and NetworkPolicy) is a
value. Two operational defaults intentionally differ from the manifest:

| Concern      | Manifest                   | This chart (default)                    |
| ------------ | -------------------------- | --------------------------------------- |
| Service type | `LoadBalancer`, all ports  | `ClusterIP`, only the OPC UA port       |
| Persistence  | `hostPath` (WSL/K3s paths) | `PersistentVolumeClaim`                 |

## TL;DR

```
# 1. Provision the OPC UA credentials Secret out-of-band (see below).
kubectl create namespace ua-edgetranslator
kubectl -n ua-edgetranslator create secret generic ua-edgetranslator-credentials \
	--from-literal=OPCUA_USERNAME='<username>' \
	--from-literal=OPCUA_PASSWORD='<password>'

# 2. Install the chart from the OCI registry.
helm install ua-edgetranslator \
	oci://ghcr.io/opcfoundation/charts/ua-edgetranslator \
	--namespace ua-edgetranslator \
	--set credentials.existingSecret=ua-edgetranslator-credentials
```

## Prerequisites

* Kubernetes 1.23+
* Helm 3.8+ (OCI support is on by default from 3.8.0)
* A default StorageClass (or set `persistence.*.storageClass`) when using the
  default PVC-backed persistence.

## Published versions

The chart is published to `oci://ghcr.io/opcfoundation/charts/ua-edgetranslator`
by [`helm-publish.yml`](../../../.github/workflows/helm-publish.yml):

* **Released tags** — pushing a `v*.*.*` git tag publishes the chart with
  version `<tag without v>` (e.g. `1.2.3`) and `appVersion: <tag>` (e.g.
  `v1.2.3`), so `image.tag` / `drivers.image.tag` default to the immutable
  released server and driver-pack images.
* **Prereleases from `main`** — every push to `main` publishes a SemVer
  prerelease `<Chart version>-main.<short-sha>` (e.g. `0.1.0-main.abc1234`) with
  `appVersion: main`, tracking the mutable `:main` images.

Pull the latest stable release (default), or opt into the newest prerelease:

```
# Latest stable release.
helm show chart oci://ghcr.io/opcfoundation/charts/ua-edgetranslator

# Latest prerelease from main.
helm show chart oci://ghcr.io/opcfoundation/charts/ua-edgetranslator \
    --version '>=0.0.0-0'
```

## Namespace

The chart does not template a `Namespace`; Helm installs resources into the
namespace you pass with `--namespace` (add `--create-namespace` to create it).
This lets you apply Pod Security Admission (PSA/PSS) labels to the namespace
yourself, for example:

```
kubectl create namespace ua-edgetranslator
kubectl label namespace ua-edgetranslator \
	pod-security.kubernetes.io/enforce=restricted \
	pod-security.kubernetes.io/enforce-version=latest
```

The default `podSecurityContext` / `containerSecurityContext` in this chart are
compatible with the `restricted` Pod Security Standard.

## Credentials (provisioning the Secret out-of-band)

By default the chart injects `OPCUA_USERNAME` / `OPCUA_PASSWORD` from a Secret
you create yourself and reference via `credentials.existingSecret`. The chart
**never** renders `REPLACE_ME` placeholder values.

```
kubectl -n ua-edgetranslator create secret generic ua-edgetranslator-credentials \
	--from-literal=OPCUA_USERNAME='<username>' \
	--from-literal=OPCUA_PASSWORD='<password>' \
	# optional additional keys consumed by the app:
	--from-literal=UACLURL='https://uacloudlibrary.opcfoundation.org' \
	--from-literal=UACLUsername='<uacl-user>' \
	--from-literal=UACLPassword='<uacl-pass>' \
	--from-literal=LICENSE_KEY='<license-key>'
```

To also wire the optional keys as environment variables, list their **names**
under `credentials.extraKeys` (values are ignored when using an existing
Secret):

```yaml
credentials:
  existingSecret: ua-edgetranslator-credentials
  extraKeys:
	UACLURL: ""
	UACLUsername: ""
	UACLPassword: ""
	LICENSE_KEY: ""
```

For **dev clusters only** you may let the chart create the Secret. This is
discouraged in production because the values live in your Helm release:

```yaml
credentials:
  create: true
  username: dev
  password: changeit
  extraKeys:
	LICENSE_KEY: dev-license
```

`credentials.create` and `credentials.existingSecret` are mutually exclusive.

## RBAC

* `serviceAccount.create=true` (default) creates a dedicated ServiceAccount with
  `automountServiceAccountToken: false`. UA Edge Translator does not call the
  Kubernetes API, so **no Role/RoleBinding is required**.
* To reuse an existing ServiceAccount, set `serviceAccount.create=false` and
  `serviceAccount.name=<existing>`.
* If your cluster mandates NetworkPolicies, enable `networkPolicy.enabled=true`
  and supply `networkPolicy.ingress` rules that permit the ports you expose.

## Persistence

Four volumes hold durable state, each independently configurable via
`persistence.<name>` where `<name>` is one of `settings`, `pki`, `logs`,
`nodesets` (mounted at `/app/<name>`):

| `kind`     | Behaviour                                                                 |
| ---------- | ------------------------------------------------------------------------- |
| `pvc`      | Creates a PersistentVolumeClaim (default), or reuses `existingClaim`.     |
| `hostPath` | Mounts `hostPath` from the node; triggers the `fix-hostpath-perms` init.  |
| `emptyDir` | Ephemeral scratch space (state is lost on pod restart).                   |

Because the PVCs default to `ReadWriteOnce`, keep `replicaCount: 1` unless you
provide `ReadWriteMany`-capable storage.

Set `persistence.<name>.storageClass` to a class name, or to `"-"` to force an
empty string (binds to a pre-provisioned PV without dynamic provisioning).

## Service exposure

`service.type` defaults to `ClusterIP` and only the OPC UA port (4840) is
published. Enable additional ports as needed:

```yaml
service:
  type: LoadBalancer
  ports:
	opcua:       { enabled: true,  port: 4840 }
	diagnostics: { enabled: true,  port: 8081 }   # unauthenticated dashboard — keep internal
	lorawan:     { enabled: true,  port: 5000 }
	lorawanTls:  { enabled: true,  port: 5001 }
	ocpp:        { enabled: true,  port: 19520 }
	ocppTls:     { enabled: true,  port: 19521 }
```

> The diagnostics dashboard on 8081 is unauthenticated HTTP. Do not expose it to
> untrusted networks; prefer `kubectl port-forward` or an internal-only Service.

## Driver pack and the allow-list

The driver-pack init container copies the signed protocol drivers into the
`drivers` volume before the server starts. It is gated by `drivers.enabled` and
its image is versioned independently from the server:

```yaml
drivers:
  enabled: true
  image:
	repository: ghcr.io/opcfoundation/ua-edgetranslator-drivers
	tag: ""          # defaults to .Chart.AppVersion
```

The published driver-pack image ships its own signed
`drivers.allowlist.json` + `drivers.allowlist.sigstore.json`. To load only a
curated subset, provision a ConfigMap and point `drivers.allowlist.configMap`
at it — the chart overlays the two files onto `/app/drivers` via `subPath`
mounts, overriding the image's copy:

```
kubectl -n ua-edgetranslator create configmap ua-edgetranslator-allowlist \
	--from-file=drivers.allowlist.json=./drivers.allowlist.json \
	--from-file=drivers.allowlist.sigstore.json=./drivers.allowlist.sigstore.json
```

```yaml
drivers:
  allowlist:
	configMap: ua-edgetranslator-allowlist
	offlineMode: ""            # "" = fail-closed (signature required)
```

For air-gapped clusters where the Sigstore bundle cannot be verified, set
`drivers.allowlist.offlineMode: allow-hash-only`. In that mode the chart mounts
only `drivers.allowlist.json` (the `.sigstore.json` subPath is skipped) and sets
`DRIVER_ALLOWLIST_OFFLINE_MODE=allow-hash-only` so the loader downgrades to
hash-only enforcement. See the repository README section *Protocol driver
allow-list (trust manifest)* for the full security model, including
`DRIVER_ALLOWLIST_OIDC_ISSUER` / `DRIVER_ALLOWLIST_OIDC_REPO` (supply them via
`extraEnv`).

To disable the driver pack entirely (e.g. drivers provisioned another way), set
`drivers.enabled=false`.

## Values

| Key                                   | Default                                             | Description                                                            |
| ------------------------------------- | --------------------------------------------------- | ---------------------------------------------------------------------- |
| `image.repository`                    | `ghcr.io/opcfoundation/ua-edgetranslator`           | Server image repository.                                               |
| `image.tag`                           | `""` (=> `.Chart.AppVersion`)                       | Server image tag.                                                      |
| `image.pullPolicy`                    | `IfNotPresent`                                      | Server image pull policy.                                              |
| `image.pullSecrets`                   | `[]`                                                | `imagePullSecrets` for private registries / mirrors.                  |
| `drivers.enabled`                     | `true`                                              | Roll out the driver pack via an init container.                        |
| `drivers.image.repository`            | `ghcr.io/opcfoundation/ua-edgetranslator-drivers`   | Driver-pack image repository.                                          |
| `drivers.image.tag`                   | `""` (=> `.Chart.AppVersion`)                       | Driver-pack image tag.                                                 |
| `drivers.image.pullPolicy`            | `IfNotPresent`                                      | Driver-pack image pull policy.                                         |
| `drivers.allowlist.configMap`         | `""`                                                | ConfigMap overlaying the allow-list onto `/app/drivers`.              |
| `drivers.allowlist.offlineMode`       | `""`                                                | `""` or `allow-hash-only` (air-gapped hash-only enforcement).         |
| `credentials.existingSecret`          | `""`                                                | Pre-provisioned Secret with OPCUA_USERNAME / OPCUA_PASSWORD.           |
| `credentials.create`                  | `false`                                             | Chart-managed Secret (dev only).                                       |
| `credentials.username` / `.password`  | `""`                                                | Values used when `create=true`.                                        |
| `credentials.extraKeys`               | `{}`                                                | Extra secret keys wired as env (UACL*, LICENSE_KEY, ...).             |
| `persistence.<name>.enabled`          | `true`                                              | Enable the `settings`/`pki`/`logs`/`nodesets` volume.                 |
| `persistence.<name>.kind`             | `pvc`                                               | `pvc` \| `hostPath` \| `emptyDir`.                                     |
| `persistence.<name>.storageClass`     | `""`                                                | StorageClass (`"-"` forces `""`).                                      |
| `persistence.<name>.size`             | `1Gi`                                               | PVC size.                                                              |
| `persistence.<name>.hostPath`         | `""`                                                | Node path when `kind=hostPath`.                                        |
| `persistence.<name>.existingClaim`    | `""`                                                | Reuse an existing PVC when `kind=pvc`.                                 |
| `service.type`                        | `ClusterIP`                                         | `ClusterIP` \| `LoadBalancer` \| `NodePort`.                           |
| `service.ports.<name>.enabled`        | `opcua` only                                        | Publish the port (`opcua`, `diagnostics`, `lorawan*`, `ocpp*`).       |
| `resources`                           | `200m`/`256Mi` .. `1000m`/`1Gi`                     | Requests / limits.                                                     |
| `replicaCount`                        | `1`                                                 | Replicas (keep at 1 for RWO / hostPath).                              |
| `podSecurityContext`                  | non-root 1000, RuntimeDefault                       | Pod-level security context.                                            |
| `containerSecurityContext`            | drop ALL, no priv-esc                               | Container-level security context.                                      |
| `fixHostPathPerms.enabled`            | `auto`                                              | `auto` \| `true` \| `false` for the chown init container.             |
| `fixHostPathPerms.image`              | `busybox:1.36`                                      | Image for the chown init container.                                    |
| `nodeSelector`                        | `kubernetes.io/os: linux`                           | Node selector.                                                         |
| `tolerations` / `affinity`            | `[]` / `{}`                                         | Scheduling.                                                            |
| `probes.liveness` / `.readiness`      | TCP 4840                                            | Probe definitions.                                                     |
| `networkPolicy.enabled`               | `false`                                             | Render a NetworkPolicy.                                                |
| `serviceAccount.create`               | `true`                                              | Create a dedicated ServiceAccount.                                     |
| `extraEnv` / `extraVolumes` / `extraVolumeMounts` | `[]`                                    | Passthrough extension points.                                          |

## Migrating from `UA-EdgeTranslator.yaml`

The manifest remains in the repository as a quick-start
reference. To move to the chart:

1. **Namespace** — the manifest's `ua-edgetranslator-namespace` becomes the Helm
   install namespace: `--namespace ua-edgetranslator-namespace --create-namespace`.
2. **Credentials** — instead of editing the inline `REPLACE_ME` Secret, provision
   `ua-edgetranslator-credentials` out-of-band and set
   `credentials.existingSecret`.
3. **Images** — the manifest pins `:main`. Pin a released tag by installing a
   chart version whose `appVersion` matches, or override `image.tag` /
   `drivers.image.tag`.
4. **Persistence** — to keep the exact WSL/K3s hostPath layout, install with
   [`ci/hostpath-values.yaml`](ci/hostpath-values.yaml). Otherwise the chart uses
   PVCs; migrate node-side data into the new PVCs first.
5. **Service** — the manifest exposes every port over `LoadBalancer`. Re-enable
   the ports you need under `service.ports` and set `service.type=LoadBalancer`,
   or use [`ci/hostpath-values.yaml`](ci/hostpath-values.yaml) which reproduces
   the exposure.

Render and diff before switching:

```
helm template ua-edgetranslator oci://ghcr.io/opcfoundation/charts/ua-edgetranslator \
	--namespace ua-edgetranslator-namespace \
	-f deploy/helm/ua-edgetranslator/ci/hostpath-values.yaml
```
