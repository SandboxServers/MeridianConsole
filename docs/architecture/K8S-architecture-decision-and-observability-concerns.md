# Kubernetes Architecture Decision: Talos Linux

**Date:** December 26, 2024
**Status:** Proposed
**Author:** Derek (with Claude Code assistance)
**Reviewers:** [Team to review]

---

## Executive Summary

After evaluating multiple Kubernetes distributions for Meridian Console's production infrastructure, **Talos Linux + Cilium** is the recommended choice. This document captures the requirements, evaluation criteria, comparison of options, and rationale for the recommendation.

---

## Table of Contents

1. [Requirements](#requirements)
2. [Distro Comparison](#distro-comparison)
3. [Recommendation: Talos Linux](#recommendation-talos-linux)
4. [Observability on Talos](#observability-on-talos)
5. [Upgrade Process](#upgrade-process)
6. [Open Questions](#open-questions)
7. [Next Steps](#next-steps)

---

## Requirements

### Deployment Target
- Separate production environment (not homelab)
- Multi-node single cluster
- Must support both dev/testing and production workloads

### Scale & HA
- High availability required (multiple replicas per service)
- Planning for hyperscale usage (new app, no customers yet)
- 13+ microservices to deploy

### Security
- **Hard requirement: mTLS everywhere** (inter-service communication)
- Security-first SaaS platform handling customer infrastructure

### Team Experience
- 1 team member: SRE with 1+ years K8s experience (uses EKS daily)
- Other team members: Beginners ("drool on face" level)
- Preference for featured distro over minimal

### Infrastructure
- In-cluster databases initially (PostgreSQL, RabbitMQ, Redis)
- May migrate to managed services if operational burden increases
- YARP Gateway is single public entry point
- TLS termination at Gateway only (Cloudflare in front)
- SaaS focus (KiP self-host edition is future consideration)

---

## Distro Comparison

### Evaluated Options

| Distro | Description |
|--------|-------------|
| **Talos Linux** | Immutable, API-driven Kubernetes OS |
| **RKE2** | Rancher's CIS-hardened K8s distribution |
| **k3s** | Lightweight CNCF-certified K8s |
| **kubeadm** | Vanilla Kubernetes bootstrapping |
| **MicroK8s** | Canonical's snap-based K8s |
| **k0s** | Mirantis's zero-friction K8s |

### Comparison Matrix

| Distro | Security | mTLS Native | Beginner Friendly | Hyperscale Ready | Operational Overhead |
|--------|----------|-------------|-------------------|------------------|---------------------|
| **Talos** | ★★★★★ | ★★★★ (with Cilium) | ★★ | ★★★★★ | ★★★★ (low once learned) |
| **RKE2** | ★★★★ | ★★★ (add mesh) | ★★★★ | ★★★★ | ★★★ |
| **k3s** | ★★★ | ★★ (add mesh) | ★★★★★ | ★★★ | ★★★★ |
| **kubeadm** | ★★★ | ★★ (add mesh) | ★★ | ★★★★★ | ★★ |
| **MicroK8s** | ★★★ | ★★★ (addon) | ★★★★ | ★★★ | ★★★ |

### Service Mesh Requirement

Since mTLS is a hard requirement, any distro needs a service mesh. Options:

| Mesh | Notes |
|------|-------|
| **Cilium** | eBPF-based, mTLS without sidecars, best performance. Native Talos integration. |
| **Linkerd** | Lightweight Rust sidecars, easiest to operate. Good for mesh beginners. |
| **Istio** | Most features, heaviest footprint, steepest learning curve. |

**Recommendation:** Cilium with Talos (native integration), Linkerd as fallback.

---

## Recommendation: Talos Linux

### What is Talos?

Talos Linux is an immutable, API-driven operating system purpose-built for Kubernetes. Key characteristics:

- **No SSH**: No shell, no SSH daemon, no package manager
- **Immutable**: Root filesystem is read-only
- **API-driven**: All management via `talosctl` and declarative YAML
- **Minimal attack surface**: Only what's needed to run Kubernetes
- **Atomic upgrades**: A/B partition scheme with instant rollback

### Why Talos for Meridian Console

#### 1. Security-First Aligns with Our Requirements

| Security Aspect | Talos Approach |
|-----------------|----------------|
| Remote access exploits | No SSH = no SSH vulnerabilities |
| Supply chain attacks | No package manager = no OS package attacks |
| Persistent malware | Immutable rootfs = malware can't persist |
| Configuration drift | Declarative config = nodes are identical |
| Audit trail | API-only changes = all changes logged |
| etcd security | Encrypted at rest by default |
| Component communication | Mutual TLS between all Talos components |

#### 2. Fits EKS Mental Model

Team members using EKS already think this way:
- Nodes are cattle, not pets
- Infrastructure is code (Terraform → Talos YAML)
- SSH is a last resort
- Declarative > imperative

Talos enforces these patterns rather than just encouraging them.

#### 3. Better Than Bottlerocket (AWS's Equivalent)

| Aspect | AWS Bottlerocket | Talos |
|--------|------------------|-------|
| Vendor lock-in | AWS only | Multi-cloud + bare metal |
| SSH | Disabled by default, can enable | Doesn't exist |
| Immutability | Yes | Yes |
| K8s integration | Runs K8s | IS the K8s node |

#### 4. Operational Benefits

- **Atomic upgrades**: Entire OS replaced in one operation
- **Instant rollback**: `talosctl rollback` swaps partitions (~30 seconds)
- **GitOps native**: Cluster config lives in Git
- **No AMI management**: No baking images, no launch template versioning
- **Visible control plane**: Unlike EKS, you can inspect etcd, API server logs, etc.

#### 5. Cost Efficiency

No control plane tax like EKS ($73/month/cluster). Only pay for compute.

### Talos Downsides (Honest Assessment)

| Challenge | Mitigation |
|-----------|------------|
| Steep learning curve | SRE can lead, good documentation exists |
| No "SSH in and fix it" | Forces proper debugging practices |
| Different debugging model | `talosctl logs`, `talosctl dmesg` replace SSH |
| More responsibility than EKS | We own control plane (but it's immutable/declarative) |
| Beginners need training | Investment pays off in operational discipline |

### Fallback Option: RKE2 + Linkerd

If Talos proves too aggressive for the team:
- RKE2 is CIS-hardened but allows SSH access
- Rancher UI helps beginners visualize the cluster
- Linkerd is simpler to operate than Istio
- Still security-focused, just less opinionated

---

## Observability on Talos

### Key Insight

Observability agents run as **Kubernetes workloads** (DaemonSets), not OS packages. This works identically on Talos, EKS, or any K8s cluster.

```
┌─────────────────────────────────────────────────────┐
│ Talos Node (immutable, no SSH)                      │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Kubernetes (kubelet, containerd)                │ │
│ │ ┌───────────────┐ ┌───────────────┐             │ │
│ │ │ Your App Pod  │ │ Monitoring    │ ← DaemonSet │ │
│ │ │               │ │ Agent Pod     │             │ │
│ │ └───────────────┘ └───────────────┘             │ │
│ └─────────────────────────────────────────────────┘ │
│     Agents access /proc, /sys, kubelet API          │
└─────────────────────────────────────────────────────┘
```

### New Relic Installation

Standard Helm installation:

```bash
helm repo add newrelic https://helm-charts.newrelic.com
helm install newrelic-bundle newrelic/nri-bundle \
  --namespace newrelic --create-namespace \
  --set global.licenseKey=YOUR_LICENSE_KEY \
  --set global.cluster=meridian-production \
  --set newrelic-infrastructure.privileged=true \
  --set ksm.enabled=true \
  --set kubeEvents.enabled=true \
  --set logging.enabled=true
```

### Sentry Installation

Sentry is application-level (SDK in code). For self-hosted Sentry Relay:

```bash
helm repo add sentry https://sentry-kubernetes.github.io/charts
helm install sentry sentry/sentry \
  --namespace sentry --create-namespace \
  --values sentry-values.yaml
```

### Talos-Specific Logging

Talos exposes logs via API (no SSH needed):

```bash
# Kernel logs
talosctl dmesg --nodes 10.0.0.5 -f

# Service logs (kubelet, containerd, etc.)
talosctl logs kubelet --nodes 10.0.0.5 -f
```

For centralized logging, configure Talos to forward logs:

```yaml
# In Talos machine config
machine:
  logging:
    destinations:
      - endpoint: "udp://logs.example.com:514"
        format: json_lines
```

### Prometheus Metrics

Standard Prometheus scraping works. Talos exposes:
- Kubelet metrics: `https://<node>:10250/metrics`
- Talos system metrics via `talosctl`

---

## Upgrade Process

### How Talos Upgrades Work

Talos uses A/B partitioning for atomic upgrades:

```
Before upgrade:
┌─────────────────────────┬─────────────────────────┐
│ Partition A (Active)    │ Partition B (Staged)    │
│ Talos v1.5.0 [RUNNING]  │ (empty)                 │
└─────────────────────────┴─────────────────────────┘

After `talosctl upgrade --image v1.6.0`:
┌─────────────────────────┬─────────────────────────┐
│ Partition A (Rollback)  │ Partition B (Active)    │
│ Talos v1.5.0            │ Talos v1.6.0 [RUNNING]  │
└─────────────────────────┴─────────────────────────┘

If issues, `talosctl rollback` (~30 seconds):
┌─────────────────────────┬─────────────────────────┐
│ Partition A (Active)    │ Partition B (Inactive)  │
│ Talos v1.5.0 [RUNNING]  │ Talos v1.6.0            │
└─────────────────────────┴─────────────────────────┘
```

### Important: Talos Does NOT Auto-Drain

Unlike EKS managed node groups, Talos does not automatically drain nodes before upgrade. You must handle this.

### Manual Upgrade Process

```bash
# 1. Cordon node (prevent new pods)
kubectl cordon node-1

# 2. Drain node (evict existing pods)
kubectl drain node-1 --ignore-daemonsets --delete-emptydir-data

# 3. Upgrade Talos
talosctl upgrade --nodes 10.0.0.5 \
  --image ghcr.io/siderolabs/installer:v1.6.0

# 4. Wait for node (1-3 minutes)
talosctl health --nodes 10.0.0.5

# 5. Uncordon
kubectl uncordon node-1

# 6. Repeat for next node
```

### Automated Upgrades (Production)

**Option 1: System Upgrade Controller (Rancher)**

```yaml
apiVersion: upgrade.cattle.io/v1
kind: Plan
metadata:
  name: talos-upgrade
  namespace: system-upgrade
spec:
  concurrency: 1  # One node at a time
  drain:
    force: true
  version: v1.6.0
  upgrade:
    image: ghcr.io/siderolabs/installer
```

**Option 2: Sidero Omni**

Talos's native management platform with:
- Automatic drain/cordon
- Health checks before proceeding
- Automatic rollback on failure
- GitOps-driven upgrade policies

### Control Plane Upgrades

Extra care required for etcd quorum:

```bash
# Check etcd health first
talosctl etcd members --nodes 10.0.0.1

# Upgrade ONE control plane node at a time
talosctl upgrade --nodes 10.0.0.1 --image ghcr.io/siderolabs/installer:v1.6.0

# Verify etcd health after
talosctl etcd status --nodes 10.0.0.1

# Wait for stability, then proceed to next CP node
```

### Comparison to EKS Upgrades

| Aspect | EKS Managed Node Groups | Talos |
|--------|------------------------|-------|
| Drain/Cordon | Automatic | Manual or via controller |
| Rollback | Delete node, launch old AMI (5-10 min) | `talosctl rollback` (30 sec) |
| Granularity | Node group level | Per-node control |
| Visibility | CloudWatch | `talosctl dmesg`, `talosctl health` |

---

## Open Questions

### For Team Discussion

1. **Cilium vs Linkerd**: Cilium has better Talos integration and no sidecars, but Linkerd is simpler. Which fits our operational model better?

2. **Sidero Omni**: Should we use Talos's native management platform, or stick with System Upgrade Controller + GitOps?

3. **Cluster topology**: How many control plane nodes? (Recommend 3 for HA)

4. **Disaster recovery**: Backup strategy for etcd? Cluster rebuild procedures?

5. **Local development**: Should devs run Talos locally (via Docker/QEMU) or use a different local setup?

6. **Timeline**: When do we want to stand up the first Talos cluster for testing?

### Technical Questions to Research

- [ ] Cilium configuration for mTLS policy enforcement
- [ ] Talos + Terraform provider for infrastructure-as-code
- [ ] GitOps workflow for Talos machine configs
- [ ] Integration with existing Cloudflare DNS/CDN setup
- [ ] Storage provisioner options (Longhorn, Rook-Ceph, cloud volumes)

---

## Next Steps

1. **Team review** of this document
2. **POC cluster**: Stand up a 3-node Talos cluster (1 CP + 2 workers) for testing
3. **Cilium evaluation**: Deploy Cilium, test mTLS between sample services
4. **Observability POC**: Deploy New Relic/Prometheus stack, verify metrics collection
5. **Document learnings**: Update this ADR with findings

---

## References

- [Talos Linux Documentation](https://www.talos.dev/docs/)
- [Cilium Documentation](https://docs.cilium.io/)
- [Sidero Omni](https://www.siderolabs.com/platform/saas-for-kubernetes/)
- [System Upgrade Controller](https://github.com/rancher/system-upgrade-controller)
- [Talos Factory](https://factory.talos.dev/) - Custom Talos image builder

---

## Appendix: Talos Machine Config Example

```yaml
# controlplane.yaml
version: v1alpha1
machine:
  type: controlplane
  token: <generated>
  ca:
    crt: <generated>
    key: <generated>
  network:
    hostname: cp-1
    interfaces:
      - interface: eth0
        dhcp: true
  install:
    disk: /dev/sda
    image: ghcr.io/siderolabs/installer:v1.6.0
cluster:
  controlPlane:
    endpoint: https://k8s.meridian.example.com:6443
  clusterName: meridian-production
  network:
    cni:
      name: cilium
    podSubnets:
      - 10.244.0.0/16
    serviceSubnets:
      - 10.96.0.0/12
```

---

*This document will be updated as decisions are finalized and implementation progresses.*
