#!/usr/bin/env bash
# Pre-merge verification for REL-1..REL-5 patches.
# Run from the repo root AFTER `git am`'ing both patches.
set -euo pipefail

echo "==> 1/4 Verifying workflow YAML parses"
for f in .github/workflows/ci.yml \
         .github/workflows/deploy-staging.yml \
         .github/workflows/deploy-prod.yml \
         .github/workflows/release.yml; do
  [ -f "$f" ] || { echo "MISSING: $f"; exit 1; }
  python3 -c "import yaml,sys; yaml.safe_load(open('$f'))" \
    && echo "  ok: $f" \
    || { echo "  BAD YAML: $f"; exit 1; }
done

echo "==> 2/4 Verifying required artifacts present"
for f in .github/CODEOWNERS \
         .github/pull_request_template.md \
         release-please-config.json \
         .release-please-manifest.json \
         version.txt \
         docs/release-process.md; do
  [ -f "$f" ] && echo "  ok: $f" || { echo "  MISSING: $f"; exit 1; }
done

echo "==> 3/4 Verifying version.txt bootstrap"
grep -qE '^v?0\.1\.0$' version.txt && echo "  ok: v0.1.0 bootstrap" \
  || { echo "  version.txt should be v0.1.0"; exit 1; }

echo "==> 4/4 Verifying build (this is the real gate)"
dotnet restore TravelAssistant.slnx
dotnet build   TravelAssistant.slnx -c Release --no-restore
dotnet test    TravelAssistant.slnx -c Release --no-build --verbosity minimal

echo
echo "✅ All checks passed. Safe to push and open PR."
