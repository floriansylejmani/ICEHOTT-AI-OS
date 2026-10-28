# ADR 0001 — Greenfield V2 restart

**Decision:** Start a new, clean monorepo and local git history. Do not reset, force-push, purge or overwrite the current `floriansylejmani/ICEHOTT-AI-Workspace` repository.

**Reasons:** The prior repository has accumulated feature branches/PRs and partially implemented billing. The owner wants a full top-down build of the originally planned product, not incremental billing patches. Greenfield Phase 0 enables security and module boundaries to be validated in order before reintroducing complex features.

**Alternatives rejected:** Rebase or force-push the existing main branch (destructive); delete old working tree (data loss); fork existing code as V2 (not a fresh architecture).

**Consequences:** The old repo and its data are preserved. V2 does not automatically carry over prior features or migrations. A separate V2 GitHub repository should be established after the owner selects its name and CI credentials/permissions. Nothing is deployed. Billing remains inactive.
