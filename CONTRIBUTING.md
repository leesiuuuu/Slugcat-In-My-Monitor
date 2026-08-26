# Contributing guide

<p align="center">
  <strong>English</strong> | <a href="CONTRIBUTING.ko.md">한국어</a>
</p>

Thank you for your interest in SlugcatInMyMonitor. This guide covers the basic
workflow from proposing an issue to merging a pull request (PR).

## Before you contribute

- Search existing issues and PRs before reporting a bug or proposing a feature.
- Discuss large features, architectural changes, and new dependencies in an issue before implementation.
- Do not publish reproduction details for a security vulnerability or another issue that could be exploited. Report it privately to the repository owner. If no private contact channel is available, open an issue containing no sensitive details and ask the owner how to continue privately.
- Follow the [Code of Conduct](CODE_OF_CONDUCT.md) in every project space.

## Development environment and verification

Development requires Windows, PowerShell 5.1 or later, Visual Studio 2022 C++
desktop build tools (v143), and the Windows 10/11 SDK. Run the complete Release
build and test suite with:

```powershell
.\build.ps1 -Configuration Release
```

Run the relevant checks even when changing only documentation or JavaScript tools.

```powershell
npm test
node --check tools\validate-dms-template.mjs
```

If a check cannot be run, record the command, reason, and expected risk in the PR.
For behavior or rendering changes, include screenshots or a short recording when
possible.

## Branches and commits

This repository uses a lightweight Git Flow.

- `main`: releasable code
- `develop`: integration branch for the next release
- `feature/<name>`, `fix/<name>`: branch from `develop` and open a PR back to `develop`
- `release/<version>`: branch from `develop` only when separate release stabilization is needed
- `hotfix/<name>`: branch from `main`, merge into `main`, then merge back into `develop`

Target normal changes at `develop`. Only release PRs should merge `develop` into
`main`. Use Conventional Commit syntax for commits and PR titles when practical.

```text
feat: add a new user-facing capability
fix: prevent food from leaving the monitor
docs: clarify local asset requirements
```

Common prefixes are `feat`, `fix`, `docs`, `build`, `ci`, `refactor`, `test`, and
`chore`. Keep each PR focused on one purpose, and separate unrelated large-scale
formatting changes from functional work.

## Code and test standards

- Follow the existing structure and naming conventions. Keep public APIs and new coupling points as narrow as practical.
- For a bug fix, add a regression test that fails before the fix and passes afterward when possible.
- For a feature, test boundary values, failure paths, cleanup, and lifecycle behavior as well as the happy path.
- Update `README.md` or the relevant `docs/` pages when user behavior, settings, installation, or compatibility changes.
- Do not commit debug output, local paths, credentials, personal information, build products, or temporary files.
- Explain the need, source, version, license, and distribution impact of every new external dependency.

## Compatibility research and public-source boundary

Compatibility work with Rain World should describe **observable behavior and this
project's own implementation** in the public repository. See
[Behavior compatibility and source boundary](docs/BehaviorCompatibility.md) for the
full policy.

- Do not include decompiled source, reconstructed third-party method bodies, IL/ILDASM output, method tokens, RVAs, binary offsets, or decompiler dumps in public PRs or documentation.
- Do not add Rain World DLLs, executables, extracted texture/audio payloads, or other proprietary game files to the repository.
- Reduce research findings to user-visible behavior requirements, project regression tests, or independently written implementation specifications that fit this project's architecture.
- Keep local analysis output and detailed reverse-engineering notes in ignored/private working directories.
- Do not publish source-shaped pseudocode or comments that preserve the expression of a third-party implementation.

These rules are repository-hygiene and provenance guidance, not a legal opinion
about any particular piece of code.

## AI-assisted tools

AI-assisted tools are allowed, but contributors remain responsible for reviewing
the generated code and documentation and for its behavior, security, and licensing.

- If AI produced or designed a meaningful part of the change, disclose the tool and scope in the PR.
- Do not submit generated work you do not understand or could not verify.
- Do not send private code, personal information, credentials, or non-redistributable assets to an external AI service.
- Disclosure helps reviewers calibrate their review; it does not replace tests or an explanation of the change.

## Assets, copyright, and licensing

Images, audio, and binaries from Rain World, Dress My Slugcat (DMS), Workshop
mods, and community skins must not be added without explicit redistribution
permission from their rights holders. Test materials must follow
[THIRD_PARTY_TEST_ASSETS.md](THIRD_PARTY_TEST_ASSETS.md).

By submitting a PR, you confirm that:

- you have the right to provide the submitted code and materials;
- you agree that the contribution may be distributed under this repository's [MIT License](LICENSE); and
- any third-party code identifies its original source, copyright notice, license, and your modifications.

Material with unclear provenance or a license incompatible with this project will
not be merged.

## Preparing a pull request

Complete every relevant part of the PR template. In particular, give reviewers
enough detail to reproduce and verify:

1. the problem and the user-visible outcome;
2. the scope and anything intentionally excluded;
3. related issues, such as `Closes #123`;
4. commands and results for completed checks, plus checks not run;
5. before-and-after evidence for visual changes;
6. new dependencies, third-party materials, and meaningful AI assistance; and
7. known limitations and follow-up work.

Use a draft PR to share direction early. Before requesting review, incorporate the
latest `develop`, resolve conflicts, ensure CI passes, self-review the diff, and
finish the checklist. When responding to review, point to the relevant commit or
explain your reasoning. Do not dismiss unresolved conversations without addressing
them.

## Review and merge criteria

Maintainers review correctness, regression risk, tests, documentation, security,
performance, accessibility, licensing, and project scope. Passing CI is required
but does not guarantee a merge. Maintainers may request changes or close a PR when:

- its explanation is insufficient to reproduce or verify the work;
- requested changes remain unresolved or the scope keeps expanding;
- it contains unauthorized third-party material, secrets, or malicious code;
- it does not fit the project's direction, or its maintenance cost outweighs its benefit; or
- it has been inactive long enough to become difficult to integrate with the current branch.

Maintainers decide the final merge method and timing. Feature PRs normally merge
into `develop`. Merging a release PR from `develop` to `main` runs the Release
Drafter and distribution workflows.
