# LP-001 — Apply

**Owner:** experience-design-squad  
**Target repo:** tamirdresher/travel-assistant  
**Target branch:** `feature/last-viewed-page`

## Files to transplant

Copy the following into the target repo at the indicated paths (preserve directory structure):

| Source (this folder)                                              | Destination (travel-assistant)                              |
|-------------------------------------------------------------------|-------------------------------------------------------------|
| `docs/wireframes/last-page/restore-flow.md`                       | `docs/wireframes/last-page/restore-flow.md`                 |
| `apps/web/src/navigation/lastPage.denylist.ts`                    | `apps/web/src/navigation/lastPage.denylist.ts`              |

## Why two files in an XD-owned LP-001

The deny-list module ships with the UX contract intentionally:

- semgrep rules (LP-005), the setter (LP-002), and the restore hook (LP-003) all need a single canonical import path
- duplicating the deny-list regexes across squads in code review is how drift starts
- shipping the regexes as code (not as a markdown table) lets QT property-test them against the spec immediately

App-dev does NOT re-author this file in LP-002 — they import it. Any change to the deny-list goes through XD per §4 D2 + §11 of the spec.

## Commit message

```
feat(last-page): LP-001 restore-flow UX contract + deny-list module

Locks D1..D6 for "remember last viewed page". First-paint = / skeleton,
restore is a post-hydration router.replace. Deny-list ships as code at
apps/web/src/navigation/lastPage.denylist.ts so setter, hook, semgrep,
and tests share one source of truth.

Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
```

## Sequencing

1. Land this PR first (no runtime code paths touched — the .ts file is unreferenced until LP-002 imports it).
2. LP-002 + LP-005 may proceed in parallel against this deny-list.
3. LP-003 waits on LP-002.
4. LP-004 (settings UI) waits on this doc's §5.3 + D4 (now locked).
5. LP-006 may scaffold against the regexes in `lastPage.denylist.ts` immediately.

## EMU push note for rev-deploy

`tamirdresher_microsoft` cannot push to tamirdresher/travel-assistant. Maintainer transplants from this folder using the `tamirdresher` keyring, same pattern as DM-001 PR #28 and RM-002.
