# DevOps: LandmarkLocal releases (GitHub)

This is the “how the hell do we ship LandmarkLocal” note for the team.  
You do **not** need to be a GitHub expert, you just need to follow the steps.

Player-facing install docs stay in [landmark-local.md](landmark-local.md).

---

## Tiny glossary


| Word                  | Meaning                                                                                                          |
| --------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `main`                | The shared “official” branch of the repo - this is the release code. Merged work lands here.                     |
| **Branch**            | Your private line of work (e.g. `feature/landmark-local-install`). You open a PR from it.                        |
| **PR (pull request)** | A request to merge your branch into another branch (for us: into `main`). Review happens here.                   |
| **Merge**             | Accepting the PR so its commits become part of `main`.                                                           |
| **Actions**           | GitHub’s robots that build the project for you (CI). Menu: **Actions** tab on the repo.                          |
| **Workflow**          | One named robot job. Ours is called **LandmarkLocal**.                                                           |
| **Artifact**          | A zip the workflow built and attached to **that run only**. Good for testing a PR. Not the public download page. |
| **Release**           | A public version on the repo’s **Releases** page, with a downloadable zip for players.                           |
| **Tag**               | A named bookmark on a commit, like `landmark-local-v0.1.2`. Releases hang off tags.                              |
| **Semver**            | Version numbers `MAJOR.MINOR.PATCH` (e.g. `1.2.3`). We mostly auto-bump **PATCH**.                               |


---

## What ships

The LandmarkLocal zip is a **self-contained Windows x64** folder: LandmarkLocal + Auth + World + LaunchPad.  
Players do **not** need a separate .NET install. MariaDB and terrain still download on first **Start**.

Built by: `tools/publish-landmark-local.ps1`  
Version helper: `tools/landmark-local-next-version.ps1`  
Workflow file: `.github/workflows/landmark-local.yml`

---

## The normal path (what you usually want)

Goal: get a new zip onto **Releases** for players.

1. Create a **branch** off `main` and do your work there.
  Do **not** push release changes straight to `main` unless the team agrees.
2. Open a **PR** that targets `main` (base = `main`, compare = your branch).
3. Wait for the **LandmarkLocal** workflow on the PR (if you touched LandmarkLocal-related paths).
  When it finishes: open the run → **Artifacts** → download **LandmarkLocal** and try it.  
   That zip is for testers; it is **not** a public Release yet.
4. Get the PR reviewed and **merge** it into `main`.
5. After merge, Actions runs again on `main`. It will:
  - build the zip  
  - bump the **patch** version (e.g. `0.1.1` → `0.1.2`)  
  - create tag `landmark-local-v0.1.2`  
  - publish a **Release** with that zip
6. Players (and grandma) download from the repo **Releases** page.

If the PR did **not** touch LandmarkLocal-related files, this workflow may not run. That’s intentional (saves CI time).

**Relevant paths** (any of these can trigger the workflow):  
`LandmarkLocal/`, `LandmarkLocal.Core/`, Auth/World/LaunchPad/ClientLaunch/PatchPacks pieces listed in the workflow, `tools/publish-landmark-local.ps1`, `tools/landmark-local-next-version.ps1`, `.github/workflows/landmark-local.yml`.

---

## Important: PR vs merge


| When                                       | What you get                                           |
| ------------------------------------------ | ------------------------------------------------------ |
| PR **open / updated** against `main`       | Build + **artifact** only. No tag. No Release.         |
| PR **merged** into `main` (push to `main`) | Build + **tag** + **Release** (patch bump by default). |


We never publish a Release from a feature branch. Only `main` - because anything else would be a bit stupid and defeat the point.

---

## Bumping minor or major (not just patch)

Default on every merge to `main` is **patch** (`x.y.Z` → `x.y.Z+1`).

### Option A — label on the PR (before you merge)

On the PR page, add **one** label:


| Label                  | Result                    |
| ---------------------- | ------------------------- |
| *(none)*               | patch                     |
| `landmark-local-minor` | minor (`1.2.3` → `1.3.0`) |
| `landmark-local-major` | major (`1.2.3` → `2.0.0`) |


If both major and minor labels are present, **major** wins.

Someone with repo access may need to **create** those labels the first time (Issues → Labels → New label). Names must match exactly.

### Option B — run the workflow by hand on `main`

1. Repo → **Actions** → **LandmarkLocal** (left sidebar).
2. **Run workflow**.
3. Branch: `main`.
4. Choose bump: **patch** / **minor** / **major**.
5. Run. When it finishes, check **Releases**.

Use this for a deliberate bump that isn’t tied to a code PR, or if you forgot the label.

---

## Where to click (cheat sheet)


| I want…                              | Go here                                                      |
| ------------------------------------ | ------------------------------------------------------------ |
| See if the build is running / failed | **Actions** → **LandmarkLocal** → latest run                 |
| Download a PR test zip               | That run → **Artifacts** → LandmarkLocal                     |
| Public player zip                    | Repo → **Releases** → latest `LandmarkLocal x.y.z`           |
| Force a release / choose major-minor | **Actions** → **LandmarkLocal** → **Run workflow** on `main` |


---

## Org rules that already bit us

This GitHub org only allows certain Actions (`actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, plus things owned by the org / GitHub).  

We publish Releases with the `gh` **CLI** inside the workflow — **not** third-party actions like `softprops/action-gh-release`. Don’t reintroduce those; the job will be blocked.

---

## Failure / “it didn’t release” checklist

1. Was the PR aimed at `main`? (Other base branches do not publish.)
2. Did the PR change a **relevant path**? (Unrelated merges skip this workflow.)
3. Open **Actions** → failed run → read the red step.
4. Hand-run on `main` only if you intend a new tag; don’t spam releases.
5. If Windows blocked an old zip for a tester: Smart App Control / unsigned download — see [landmark-local.md](landmark-local.md).

---

## Local build (developers only)

From the repo root on Windows:

```
powershell -File tools/publish-landmark-local.ps1
```

Output: `artifacts/LandmarkLocal/`.  
Peek at the next tag without releasing:

```
powershell -File tools/landmark-local-next-version.ps1 -Bump patch
```

