## v0.1.0-alpha.8.9 (2026-07-14)

- Fix: the composed playable export now fully passes its own shipped structure
  standard (`validate_project_standard.py --stage playable`, 47/47). Two parts,
  both aligning the ownership manifest with the standard authored in
  `docs/GDAI/PROJECT-STRUCTURE.md`:
  1. The manifest is written to
     `Assets/GDAI_Project/Generated/Manifests/GDAIPlayableAssets.json` (the
     documented location, parallel to the `Input/` and `Prefabs/` subdirs) —
     8.8 wrote it to the `Generated/` root.
  2. The manifest now carries a top-level `profile_id` and a flat `assets[]`
     list of the owned files (input asset, enemy prefab, canonical scene) — the
     "exact files listed in the manifest" the docs describe. Purely additive;
     the richer typed records and per-object provenance are unchanged.
- Surfaced by the P7b real-product remote-tag TREE. No behavior change to the
  playable scene: receipt/PlayMode/idempotency identical; the receipt's
  independent readback and ownership cross-check follow the manifest constant.

## v0.1.0-alpha.8.8 (2026-07-14)

- Fix: the `Complete GDAI Export / Sync Project` CTA now composes the playable
  scene on a FRESH project. On a first sync the generated MonoBehaviours are
  imported in the same operation and only exist after the domain reload, so the
  synchronous compose could not resolve them. The CTA now defers when the code is
  compiling and the resume hook finishes the compose automatically after the
  reload (next editor tick) — still one click, zero manual steps. Re-syncs where
  the code is already compiled compose in the same tick.
- Proven on a real fresh production export (new snapshot): TREE-B one deferred CTA
  → auto-compose → hard receipt PASS + PlayMode (player/enemy visible & hittable);
  TREE-C second same-snapshot sync idempotent (0 GUID drift, 0 duplicates).

## v0.1.0-alpha.8.7 (2026-07-14)

- Fix: the `Complete GDAI Export / Sync Project` CTA now runs the zero-manual
  playable composer when the imported bundle carries the rev4 playable
  contract (contract-conditional; pre-rev4 snapshots keep the legacy minimal
  scene prep). In 8.6 the composer shipped fully tested but was not invoked by
  the window CTA, so a one-click sync never composed the playable scene.
- New window seam `GdaiPlayableComposerCta.RunFromImportedContract` (parse
  fail-closed, sha256-pinned identity, full composer + hard receipt; a
  compose/receipt failure fails the sync). EditMode suite: 89 tests.

## v0.1.0-alpha.8.6 (2026-07-14)

- Zero-manual playable composer consuming the frozen
  `gdai.unity.playable_contract.v1` contract_revision 4 (identity pinned as
  schema + revision + sha256): one CTA composes the five canonical owned scene
  objects, deterministic InputActionAsset, owned enemy prefab (sprite-fit
  non-zero collider), all 7 scene references, 3 action-matched input
  references, LayerMask value binding, camera fit_arena framing (solved
  orthographic size from arena world-bounds), and exactly one active
  AudioListener — `manual_assembly_steps = 0`.
- Resumable CTA operation state survives real domain reloads
  (`[InitializeOnLoad]` + afterAssemblyReload): atomic Library-scoped operation
  writes, stale (>24h) never auto-resumes, multiple-active and contract-drift
  (schema/revision/sha256) fail closed; the destructive generated-root replace
  is never repeated.
- Ownership discipline: every composed object/prefab carries
  `GdaiGeneratedPlayableMarker`; same-named or same-path human objects and
  prefabs without the marker are preserved verbatim and never adopted,
  stamped, renamed, or overwritten (active AND inactive).
- Hard receipt `GDAIPlayableReceipt.json` by independent readback of the saved
  scene (never builder return values) + ownership manifest
  `GDAIPlayableAssets.json` (atomic, identity-pinned, forward+reverse marker
  verification); the CTA completes only on a PASS receipt.
- Proven on a real production export copy: receipt 18/18 PASS, PlayMode with
  real semantic sprites (player visible, enemy auto-spawned and hittable),
  authenticated post-reload catalog read with the scoped plugin token.
- EditMode suite grown to 87 tests (68 new since 8.5).

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
