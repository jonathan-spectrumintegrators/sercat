# Releasing sercat (and submitting to winget)

## 1. Build the release artifact

```powershell
pwsh ./pack.ps1 -Version 1.0.0
```

This publishes a framework-dependent `win-x64` build (no PDB), zips it to
`sercat-<version>-win-x64.zip`, and prints the **SHA256**.

> **Important — the hash is tied to the exact file.** `Compress-Archive` embeds file
> timestamps, so rebuilding produces a byte-different zip with a different hash. Whatever
> zip you upload to the GitHub Release must be the one whose hash is in the manifest. Either
> upload the exact file `pack.ps1` produced, or re-run the hash on the final file and update
> the manifest. (Using `wingetcreate` — step 4 — sidesteps this by hashing the uploaded URL.)

## 2. Create the GitHub Release

On https://github.com/jonathan-spectrumintegrators/sercat → **Releases → Draft a new
release**:

- Tag: `v1.0.0` (create on `main`).
- Attach `sercat-1.0.0-win-x64.zip` as a release asset.
- Publish.

The asset URL becomes:
`https://github.com/jonathan-spectrumintegrators/sercat/releases/download/v1.0.0/sercat-1.0.0-win-x64.zip`
(already referenced in the installer manifest).

## 3. Confirm the manifests

The manifests live in `winget/<version>/`:

- `SpectrumIntegrators.Sercat.yaml` (version)
- `SpectrumIntegrators.Sercat.installer.yaml` (installer — URL + SHA256 + .NET dependency)
- `SpectrumIntegrators.Sercat.locale.en-US.yaml` (metadata)

Keep `ArchiveBinariesDependOnPath: true` in the installer manifest. sercat.exe is a
framework-dependent .NET apphost that loads sercat.dll from its own folder; without this
flag winget creates a Links symlink and the alias fails with "The application to execute
does not exist: ...\Links\sercat.dll".

If you rebuilt the zip, update `InstallerSha256` in the installer manifest to match. Validate:

```powershell
winget validate --manifest winget/1.0.0
```

## 4. Submit to microsoft/winget-pkgs

Using wingetcreate (already installed). Either submit the reviewed local manifests:

```powershell
wingetcreate submit --token <github-token> winget/1.0.0
```

…or regenerate from the published release URL (auto-computes the hash, then opens the PR):

```powershell
wingetcreate new https://github.com/jonathan-spectrumintegrators/sercat/releases/download/v1.0.0/sercat-1.0.0-win-x64.zip
```

`wingetcreate` forks `microsoft/winget-pkgs`, commits under
`manifests/s/SpectrumIntegrators/Sercat/1.0.0/`, and opens the PR. A GitHub token with
`public_repo` scope is required (it will prompt/store one if not supplied).

## 5. After the PR

Automated validation + Windows Sandbox install tests run on the PR. Once merged:

```powershell
winget install SpectrumIntegrators.Sercat
```

## Local test before submitting (optional)

Install the portable zip straight from the local manifest to confirm the alias works:

```powershell
winget install --manifest winget/1.0.0
sercat --version
winget uninstall SpectrumIntegrators.Sercat
```
