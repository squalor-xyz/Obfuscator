# Contributing

Work is planned and tracked with `slicer` in `.slicer/`. Each roadmap item is implemented in its own git worktree, committed on its own branch, and handed off for review before it is merged.

## Prerequisites

- .NET 10 SDK (pinned in `global.json`)
- `slicer` on `PATH` (the tracking files are currently written by 1.4.0)
- optional: GnuPG (`gpg`) for the GPG manifest tests

## One-time setup per clone

Turn on slicer's merge drivers so branches that change tracking state merge cleanly. `slicer setup-git` prints these commands:

```bash
git config merge.slicer-generated.name "keep the current branch's generated files"
git config merge.slicer-generated.driver true
git config merge.slicer-index.name "keep the larger next_id when merging the index"
git config merge.slicer-index.driver "slicer merge-index %O %A %B"
```

## Tracking rules

- Read and change roadmap state only through `slicer` commands. Do not hand-edit `.slicer/*.json`, `.slicer/log.jsonl`, or `.slicer/render/`.
- When generated render files conflict, run `slicer render` and `slicer check` instead of merging them by hand.
- `slicer check` must pass before every commit that touches `.slicer/`. CI-equivalent code checks are listed under [Build and test](#build-and-test).
- Never put real passphrases, keys, or customer data in tests, fixtures, output, or commit messages.

## Slice workflow

The examples use `ob-NN` for the item id and `<topic>` for a short branch suffix.

### 1. Pick an item

From the main checkout:

```bash
slicer status
slicer list
slicer show ob-NN
```

Read the slice's scope, dependencies, and checks. If the specification is missing or ambiguous, resolve it with the owner first. A row without a slice needs `slicer promote` before anyone implements it.

### 2. Create the worktree

```bash
git worktree add .worktrees/ob-NN -b ob-NN-<topic> main
```

`.worktrees/` is ignored by git. All work for the item happens in this worktree. Leave the main checkout clean.

### 3. Claim the item in the worktree

Run every slicer command for this item against the worktree, either from inside it or with `--root`:

```bash
slicer --root .worktrees/ob-NN start ob-NN --render --strict
```

The claim and all later tracking changes are committed on the item's branch. The main checkout shows the item as open until the branch is merged.

If the slice needs a correction (for example, a file missing from **Files**), edit it:

```bash
slicer --root .worktrees/ob-NN edit ob-NN --section Files --text "..." --render --strict
```

### 4. Implement and verify

Implement the agreed scope and update the documentation it names. Then run the slice's **Check** section and the project checks under [Build and test](#build-and-test), and finally:

```bash
slicer --root .worktrees/ob-NN check
```

### 5. Record what you found

Work discovered along the way goes on the roadmap, scored, and linked to the item that found it:

```bash
slicer --root .worktrees/ob-NN add "Title" --discovered-from ob-NN \
  --size S --tree cli --findings "file:line and what is wrong" \
  --importance 2 --urgency 2 --effort 1 --render --strict
```

Friction with slicer itself or with this process goes in slicer's local feedback log:

```bash
slicer --root .worktrees/ob-NN feedback --kind friction --item ob-NN --text "..."
```

`.slicer/feedback.md` is gitignored and lives in the worktree. Copy it out before removing the worktree (step 8).

### 6. Commit in the worktree

```bash
git -C .worktrees/ob-NN add -A
git -C .worktrees/ob-NN commit -m "ob-NN: <summary>"
```

The commit includes the code, docs, tests, and the `.slicer/` changes.

### 7. Hand off for review

```bash
slicer --root .worktrees/ob-NN note ob-NN --text "Ready for review: branch, commits, behaviour change, checks run and results, follow-up items filed"
slicer --root .worktrees/ob-NN handoff ob-NN --render --check
git -C .worktrees/ob-NN add -A
git -C .worktrees/ob-NN commit -m "ob-NN: hand off for review"
```

The project sets `implement_finish` to `handoff`, so implementers never run `slicer done`. Do not merge or push your own branch.

### 8. Review, merge, and clean up

The reviewer works in the item's worktree:

```bash
slicer --root .worktrees/ob-NN next --status review --start --ready --section Check
slicer --root .worktrees/ob-NN render
git log main..ob-NN-<topic>
git diff main...ob-NN-<topic>
```

This claims the item, moves it to `reviewing`, and re-renders the roadmap, which the claim alone leaves stale. The reviewer re-runs the **Check** section and the project checks, then records the verdict on the branch.

- **Fail:** record the verdict, which returns the item to open, and commit it on the branch:

  ```bash
  slicer --root .worktrees/ob-NN reject ob-NN --note "VERDICT: FAIL - reason" --render
  git -C .worktrees/ob-NN add -A
  git -C .worktrees/ob-NN commit -m "ob-NN: review failed"
  ```

- **Pass:** commit the review claim on the branch, merge into `main` from the main checkout, then mark the item done there:

  ```bash
  git -C .worktrees/ob-NN add -A
  git -C .worktrees/ob-NN commit -m "ob-NN: review passed"
  git merge --no-ff ob-NN-<topic>
  slicer render && slicer check
  slicer done ob-NN --note "Reviewed and merged" --render --check
  git add .slicer
  git commit -m "ob-NN: done"
  ```

Then remove the worktree and branch:

```bash
git worktree remove .worktrees/ob-NN
git branch -d ob-NN-<topic>
```

## Build and test

```bash
dotnet test Obfuscator.slnx
./scripts/test-local.sh
```

`scripts/test-local.sh` packs both projects as `0.0.0-local`, installs the CLI into `artifacts/local-test/`, and runs generate, obfuscate, and deobfuscate end to end. CI runs the tests on Linux, macOS, and Windows, and runs the script on pushes to `main`.

## Agents

Coding agents follow this document plus `slicer ai`. In addition:

- Use `slicer --root` and `git -C` with explicit paths rather than changing directory.
- Ask the owner before merging, pushing, tagging, or changing git or slicer configuration.
