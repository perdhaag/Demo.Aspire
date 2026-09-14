# GitOps to the Raspberry Pi

How a commit on `main` becomes running pods on the Pi, with nobody typing `kubectl apply`.

```
  git push main
      │
      ▼
  GitHub Actions (.github/workflows/images.yml)
      │  cross-builds 5 services for linux/arm64
      ▼
  ghcr.io/perdhaag/demo-aspire/<service>:main-<sha>-<timestamp>     (private)
      │
      │  polled every 5m by image-reflector-controller
      ▼
  ImagePolicy picks the highest timestamp
      │
      │  image-automation-controller rewrites the $imagepolicy setters in
      │  deploy/apps/demo-kino/release.yaml and commits back to main
      ▼
  helm-controller sees the new HelmRelease values and upgrades the release
      │
      ▼
  pods rolling on the Pi
```

The cluster pulls; nothing pushes to it. CI has no cluster credentials, and the repository
is the only record of what is deployed — including the image tags, which land there as
commits by `fluxcdbot`.

## Layout

| Path | What it is |
|---|---|
| `deploy/chart/` | Helm chart, generated from `AppHost.cs`. Regenerate with `deploy/publish-chart.sh`; don't hand-edit. |
| `deploy/apps/demo-kino/` | What runs. Flux reconciles this with SOPS decryption. |
| `deploy/apps/demo-kino/release.yaml` | The HelmRelease. Image tags live here and Flux rewrites them. |
| `deploy/cluster/pi/` | Flux's own configuration. `flux bootstrap` writes `flux-system/` alongside it. |
| `deploy/make-secrets.sh` | Generates the infrastructure passwords, encrypted. |

Chart values are split on purpose. `deploy/chart/values.yaml` is regenerated wholesale and
keeps its meaningless `:latest` image defaults; the real image refs sit in the HelmRelease.
Without that split, every chart regeneration would clobber the tags image automation just
wrote, and every automation commit would show up as a diff against generated output.

## One-time setup

### 1. The Pi

Needs the **64-bit** Raspberry Pi OS — the .NET images are `linux-arm64` and will not run on
a 32-bit userland. 8 GB of RAM is realistic for this stack: Postgres, Redis, RabbitMQ, the
Aspire dashboard, Mailpit and five .NET services.

k3s needs the memory cgroup, and on a Pi 5 it is actively disabled: the firmware puts
`cgroup_disable=memory` on the kernel command line. Note that it is *not* in
`/boot/firmware/cmdline.txt` — grepping that file finds nothing, and `/proc/cmdline` is the
only place it shows up. It cannot be removed, only overridden, which works because the
contents of `cmdline.txt` are appended after the firmware's own parameters and the kernel
lets the last one win. Append to the single line in `/boot/firmware/cmdline.txt`:

```
cgroup_enable=memory cgroup_memory=1
```

That file must stay exactly one line, so append to it deliberately rather than with a
careless `sed` — a wrong edit here does not boot. Reboot, then confirm the controller
appeared before going further:

```bash
cat /sys/fs/cgroup/cgroup.controllers     # must now include 'memory'
```

Without it k3s starts, reaches the datastore, and exits with
`failed to find memory cgroup (v2)`, leaving the service stuck in `activating`.

```bash
curl -sfL https://get.k3s.io | sh -
sudo k3s kubectl get nodes        # should read Ready
```

k3s brings its own Traefik ingress controller and local-path storage class.

### Reaching the API from your workstation

The Pi firewalls 6443, so k3s is not reachable across the LAN even though it listens on
`*:6443`. Rather than opening a port on a machine that also runs `cloudflared`, tunnel over
SSH and point the kubeconfig at the tunnel — k3s's certificate already covers `127.0.0.1`,
so TLS verifies with no `--insecure-skip-tls-verify`:

```bash
ssh -fN -L 6443:127.0.0.1:6443 per@pi
```

Copy `/etc/rancher/k3s/k3s.yaml` off the Pi, leave the server as `https://127.0.0.1:6443`,
rename its cluster/user/context away from `default`, and merge it into `~/.kube/config`
alongside whatever is already there. The tunnel is per-session: if `kubectl` starts timing
out, it has died and wants restarting. Opening 6443 on the Pi's firewall is the alternative
if you would rather not depend on the tunnel.

### 2. GHCR credentials

The packages are private, so the cluster needs a pull credential in two places: `demo-kino`
to pull images, and `flux-system` so image-reflector-controller can list tags.

Both namespaces are created by Flux, so this step comes *after* bootstrap. Create a GitHub
PAT (classic) with **`read:packages`**, then:

```bash
for ns in demo-kino flux-system; do
  kubectl -n "$ns" create secret docker-registry ghcr-creds \
    --docker-server=ghcr.io \
    --docker-username=perdhaag \
    --docker-password="$GHCR_PAT"
done
```

Both are needed and they fail differently: without the `demo-kino` copy the pods sit in
`ImagePullBackOff`; without the `flux-system` copy the ImageRepositories report
`failed to configure authentication options` and no new image is ever detected.

These two secrets and the SOPS key below are the only things created by hand — the
deliberate exception, since they are the credentials that let the cluster read everything
else. Whatever token you use is stored in the cluster permanently, so scope it to
`read:packages` and nothing more.

### 3. SOPS key

```bash
age-keygen -o ~/.config/sops/age/demo-kino.agekey        # keep this; it is the only copy
kubectl -n flux-system create secret generic sops-age \
  --from-file=age.agekey=$HOME/.config/sops/age/demo-kino.agekey
```

Put the **public** key (the `age1...` line `age-keygen` prints) into `.sops.yaml`, replacing
`REPLACE_WITH_YOUR_AGE_PUBLIC_KEY`, then generate the infrastructure passwords:

```bash
deploy/make-secrets.sh
```

That writes `deploy/apps/demo-kino/secrets.enc.yaml`, encrypted and safe to commit. The
plaintext is piped into `sops` and never written to disk.

### 4. Bootstrap Flux

Image automation is not part of a default install, so it has to be asked for:

```bash
export GITHUB_TOKEN=<PAT with repo scope>
flux bootstrap github \
  --owner=perdhaag \
  --repository=Demo.Aspire \
  --branch=main \
  --path=deploy/cluster/pi \
  --personal \
  --read-write-key \
  --components-extra=image-reflector-controller,image-automation-controller
```

Flux commits its own manifests to `deploy/cluster/pi/flux-system/` and starts reconciling.

`--read-write-key` is not optional here. Bootstrap authenticates with the PAT once and then
leaves a *deploy key* behind as the cluster's lasting credential; that key is read-only
unless this flag is given, and image automation has to push its tag commits back to main.
Without it everything reconciles correctly and images simply never update — a failure that
looks like nothing happening at all.

### 5. First rollout

Push a commit to `main` so the workflow produces a first set of images. Until it does, the
HelmRelease points at the placeholder tag `main-0000000-0` and pods will sit in
`ImagePullBackOff`; the first automation run replaces it. Watch:

```bash
flux get images all
flux get helmrelease -n demo-kino
kubectl -n demo-kino get pods -w
```

## Day to day

Change code, push to `main`, and that is the whole procedure. Within roughly ten minutes
the Pi is running it.

Changing the *topology* — a new service, a new dependency in `AppHost.cs` — additionally
needs the chart regenerated and committed:

```bash
deploy/publish-chart.sh
git add deploy/chart && git commit -m "chore: regenerate chart"
```

Useful when it isn't behaving:

```bash
flux reconcile source git flux-system        # pull the repo now
flux reconcile helmrelease demo-kino -n demo-kino --with-source
flux get images policy                       # what tag does Flux think is newest?
flux logs --follow --level=error
```

## Current state

The cluster is up and the loop is closed. `raspberrypi` runs k3s v1.36.4 alongside the
existing Home Assistant / Mosquitto / cloudflared workload, Flux 2.9.5 is bootstrapped with
both image controllers, and all ten pods are healthy. Automation has already made its first
five commits to `main`, rolling the placeholder tags to `main-5cc9d27-1789399580`.

Verified end to end on the Pi: `POST /api/bookings` returned 202, the booking reached
`Confirmed` in about ten seconds, and the ticket arrived in Mailpit — so all five services,
Postgres, Redis and RabbitMQ are working on arm64.

The UI is at **http://pi/** (also `http://pi.lan/` and `http://raspberrypi.local/`). Traefik
holds the Pi's LAN address and ports 80/443 are open; only 6443 is firewalled.

One loose end, plus a caveat:

* Nothing has a volume, so each image rollout resets the databases (see below).
* `ghcr-creds` currently holds a `gh` CLI token carrying `repo`, `write:packages`, `gist`
  and `read:org`. It works, but it is far broader than a pull credential needs; replacing it
  with a `read:packages` PAT is a one-command swap.

## Things worth knowing

**Nothing has a volume.** The generated chart gives Postgres and RabbitMQ no
`volumeClaimTemplates`, so every pod restart — including every image rollout — starts from
an empty database. The app reseeds itself, so the demo still works, but bookings made
before a deploy are gone afterwards. If that matters, add a PVC patch via the HelmRelease's
`postRenderers`; k3s's `local-path` storage class will satisfy it.

**Image pull credentials come from the ServiceAccount**, not the Deployments. The
`WithGhcrPullSecret` callback in `AppHost.cs` does not reach the generated chart — the
templates have no `imagePullSecrets` at all — so `deploy/apps/demo-kino/serviceaccount.yaml`
attaches `ghcr-creds` to the namespace's default ServiceAccount and Kubernetes injects it
into every pod. That also means it survives chart regeneration.

**The Aspire dashboard is deployed but not exposed.** It is in the chart on port 18888 and
collects OTLP from all five services. Reach it with
`kubectl -n demo-kino port-forward svc/k8s-dashboard-service 18888:18888`.

**The `web` resource is not deployed.** It is the bun dev server, which is dev-time only —
the gateway serves the bundled React UI from its own `wwwroot`. `publish-chart.sh` strips it
from the generated output so nothing tries to pull an image that CI never builds.
