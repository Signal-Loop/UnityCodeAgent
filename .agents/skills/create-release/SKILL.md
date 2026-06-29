---
name: create-release
description: Create repository releases from the Unity package version with concise user-focused release notes, Git tags, and GitHub releases. Use this skill whenever the user asks to cut, prepare, publish, create, or draft a GitHub release, generate release notes from changes since the last release, tag a version like vX.X.X, or run a release workflow.
---

# Create Release

## Overview

Use this workflow to prepare and publish a GitHub release from the current repository. Keep release notes brief, professional, and written for users: explain what changed, how it affects their experience, and any actions they should take.

## Workflow

1. Check the current branch before doing anything else:

   ```bash
   git branch --show-current
   ```

   If the branch is not exactly `main`, stop immediately and tell the user releases should be made from `main`. Do not create notes, tags, commits, or GitHub releases from another branch.

2. Determine the release version from the Unity package manifest.

   Read `Packages/com.signal-loop.unitycodeagent/package.json` and use its `version` field as the only release version source. Do not use an explicit version from the user unless they first update this package manifest. The release tag is `v` plus the package version, for example package version `1.2.3` becomes tag `v1.2.3`.

   ```powershell
   $packageVersion = (Get-Content -Raw Packages\com.signal-loop.unitycodeagent\package.json | ConvertFrom-Json).version
   $releaseTag = "v$packageVersion"
   ```

   If the manifest version is missing or is not an `X.X.X` version, stop and tell the user to update `Packages/com.signal-loop.unitycodeagent/package.json` before releasing.

3. Find the latest release tag and verify the package version advances it.

   Fetch tags, identify the latest `vX.X.X` tag, and compare it with the package version:

   ```powershell
   git fetch --tags --quiet
   $lastTag = git tag --list 'v[0-9]*' --sort=-v:refname | Select-Object -First 1
   if ($lastTag) {
     $lastVersion = [version]($lastTag.TrimStart('v'))
     $nextVersion = [version]$packageVersion
     if ($nextVersion -le $lastVersion) {
       throw "Package version $packageVersion must be greater than latest tag $lastTag before releasing."
     }
   }
   ```

   If the package version is lower than or equal to the latest tag, stop immediately and report that `Packages/com.signal-loop.unitycodeagent/package.json` must be bumped before release work can continue.

4. Find the previous release for change analysis.

   Prefer GitHub release data:

   ```bash
   gh release list --limit 1 --json tagName,publishedAt,isDraft,isPrerelease
   ```

   If there is no GitHub release, fall back to the latest local or remote tag:

   ```bash
   git describe --tags --abbrev=0
   ```

5. Inspect repository changes since the previous release.

   Use a mix of commit history and changed files so the notes reflect behavior, not just commit subjects:

   ```bash
   git log --oneline <previous-tag>..HEAD
   git diff --stat <previous-tag>..HEAD
   git diff --name-only <previous-tag>..HEAD
   ```

   If there is no previous release, inspect the whole reachable history and summarize the initial release.

6. Create release notes in `docs/release_notes`.

   Create `docs/release_notes/<releaseTag>.md`. Keep the notes concise and structured:

   ```markdown
   # vX.X.X

   ## Highlights
   - User-facing change and why it matters.

   ## Improvements
   - Smaller behavior, workflow, reliability, or UI improvements.

   ## Fixes
   - Bugs fixed and their user-visible effect.

   ## Upgrade Notes
   - Required user action, migration note, compatibility caveat, or `None`.
   ```

   Omit empty sections except `Upgrade Notes` when it helps reassure users that no action is required. Avoid internal implementation detail unless it directly affects users.

7. Review and verify before publishing.

   Check for an existing tag or release with the same version:

   ```bash
   git rev-parse -q --verify refs/tags/<releaseTag>
   gh release view <releaseTag>
   ```

   If either already exists, stop and report the conflict. If release notes were created or edited, include them in the current working tree but do not commit unless the user explicitly asked for a release-note commit.

8. Create the Git tag and GitHub release.

   After the notes are ready and the tag/release do not already exist:

   ```bash
   git tag -a <releaseTag> -m "<releaseTag>"
   git push origin <releaseTag>
   gh release create <releaseTag> --title "<releaseTag>" --notes-file docs/release_notes/<releaseTag>.md --target main
   ```

9. Final response.

   Report the release tag, the release notes path, and the GitHub release URL from `gh release view <releaseTag> --json url --jq .url`. Mention if notes were left uncommitted.
