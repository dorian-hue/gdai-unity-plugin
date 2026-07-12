## v0.1.0-alpha.8.5 (2026-07-12)

- Fail-closed GDAI project binding: exported projects carry
  `ProjectSettings/GDAIProjectBinding.json`; the connector window locks to the
  bound project (read-only identity, no catalog fallback, mismatch blocks all
  fetch/import before network).
- User-triggered `Complete GDAI Export / Sync Project` CTA orchestrating the
  existing import/asset/role-map/background pipeline plus Layer B scene
  elements/spawn/arena/obstacle/edge blockers and Layer C minimal playable
  scene prep, with a PASS-only completion receipt under `.gdai/receipts/`.
- New Editor test assembly `GDAI.Editor.Tests` (19 EditMode tests).

# Changelog

## [0.1.0-alpha.8.4] - 2026-07-11

### Fixed
- **Release packaging**: 33 Unity `.meta` files (Layer A entire folder, 9 Layer B files,
  Layer C, Runtime `GdaiEntitySpriteBinding`, package root docs) existed only on the
  development machine and were never committed. Git-URL installs of `v0.1.0-alpha.8.3`
  therefore imported with the whole Layer A ignored ("has no meta file … will be ignored"),
  producing 33 `error CS` in any fresh project. Local `file:` installs masked the defect.
  All original (donor-verified) GUIDs are preserved; no GUID was regenerated.
- **Version telemetry**: pairing telemetry constant `PluginVersion` was stuck at
  `0.1.0-alpha.8.0`; now aligned with the package version (`0.1.0-alpha.8.4`).

### Added
- `scripts/validate-unity-package-integrity.py`: release guard validating the
  distributable git face (asset/meta parity, orphan metas, GUID uniqueness,
  no UserSettings, no local `file:` deps, package-vs-telemetry version match,
  Layer completeness). Run before every tag.

### Fixed (P4A)
- Preserve the `Resources/GDAI/UI` package directory used by UI metadata import and
  runtime lookup: ship `.gitkeep` inside it so the tracked folder (and its existing
  `UI.meta` GUID) survives git packaging; clean installs no longer emit the
  empty-folder orphan-meta warning.

### Known
- `Assets/GDAI/Resources/GDAI/UI.meta` tracks an empty folder (guid
  `ceeda8d9ce00d4c348879c25425c3917`); Unity recreates the folder on import.
  Kept pending reference audit; watched by the release guard as a warning.

## [0.1.0-alpha.8.3] - earlier
- Superseded: incomplete release package (missing metas). Do not install via git URL.
