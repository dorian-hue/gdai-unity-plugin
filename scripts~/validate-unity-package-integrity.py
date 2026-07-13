#!/usr/bin/env python3
"""GDAI Unity package integrity release guard.

Validates the DISTRIBUTABLE face of this repo at a git revision (default HEAD),
i.e. exactly what a UPM git-URL install will see — not the working tree.

Checks (every scan set must be non-empty; empty scans fail, no vacuous green):
  1. tracked distributable asset without tracked .meta        == 0
  2. tracked .meta without corresponding tracked asset        == 0 (true orphans)
     tracked folder-.meta whose folder has no tracked content -> EMPTY_FOLDER_META
     (warning w/ GUID; final gate is the Unity clean-install log: orphan warnings == 0)
  3. duplicate GUID among tracked metas                       == 0
  4. UserSettings/** tracked                                  == 0
  5. absolute/local 'file:' dependency in package.json        == 0
  6. package.json version == pairing telemetry PluginVersion constant
  7. LayerA / LayerB / LayerC / Runtime tracked .cs counts    each > 0

Unity asset-pipeline ignore rules honored (these need no .meta by spec):
  names starting with '.', names ending with '~'.

Usage: python3 scripts/validate-unity-package-integrity.py [--rev REV] [--repo PATH]
Exit 0 = PASS, 1 = FAIL."""
import argparse, json, re, subprocess, sys

FAIL = 0
WARNINGS = []

def check(ok, name, detail=""):
    global FAIL
    print(f"{'PASS' if ok else 'FAIL'}  {name}" + (f" · {detail}" if detail else ""))
    if not ok:
        FAIL += 1

def warn(name, detail):
    WARNINGS.append((name, detail))
    print(f"WARN  {name} · {detail}")

def git(repo, *args):
    return subprocess.run(["git", "-C", repo, *args], check=True,
                          capture_output=True, text=True).stdout

def unity_ignores(path):
    parts = path.split("/")
    return any(p.startswith(".") or p.endswith("~") for p in parts)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rev", default="HEAD")
    ap.add_argument("--repo", default=".")
    a = ap.parse_args()

    tracked = git(a.repo, "ls-tree", "-r", a.rev, "--name-only").splitlines()
    check(len(tracked) > 0, "scan.tracked.nonEmpty", f"{len(tracked)} tracked files @ {a.rev}")

    metas = [p for p in tracked if p.endswith(".meta")]
    assets = [p for p in tracked if not p.endswith(".meta")]
    check(len(metas) > 0, "scan.metas.nonEmpty", f"{len(metas)} metas")
    check(len(assets) > 0, "scan.assets.nonEmpty", f"{len(assets)} assets")

    # 1. every distributable asset has a tracked meta
    meta_set = set(metas)
    importable = [p for p in assets if not unity_ignores(p)]
    check(len(importable) > 0, "scan.importable.nonEmpty", f"{len(importable)} importable assets")
    missing = [p for p in importable if p + ".meta" not in meta_set]
    check(not missing, "asset.allHaveMeta", ",".join(missing[:5]))

    # 2. every meta has a counterpart: file, or folder with tracked content
    asset_set = set(assets)
    dirs_with_content = set()
    for p in tracked:
        segs = p.split("/")
        for i in range(1, len(segs)):
            dirs_with_content.add("/".join(segs[:i]))
    true_orphans, empty_folder_metas = [], []
    for m in metas:
        base = m[:-5]
        if base in asset_set or base in dirs_with_content:
            continue
        blob = git(a.repo, "show", f"{a.rev}:{m}")
        guid = (re.search(r"guid: ([0-9a-f]{32})", blob) or [None, "?"])[1]
        if "folderAsset: yes" in blob:
            empty_folder_metas.append(f"{m}(guid={guid})")
        else:
            true_orphans.append(f"{m}(guid={guid})")
    check(not true_orphans, "meta.noTrueOrphans", ",".join(true_orphans[:5]))
    for e in empty_folder_metas:
        warn("meta.emptyFolder", e + " — requires Unity clean-install log evidence (orphan warnings == 0); do not exempt silently")

    # 3. duplicate GUIDs
    guids = {}
    dups = []
    for m in metas:
        g = re.search(r"guid: ([0-9a-f]{32})", git(a.repo, "show", f"{a.rev}:{m}"))
        if not g:
            continue
        if g.group(1) in guids:
            dups.append(f"{g.group(1)}({guids[g.group(1)]}|{m})")
        guids[g.group(1)] = m
    check(len(guids) > 0, "scan.guids.nonEmpty", f"{len(guids)} guids")
    check(not dups, "guid.unique", ",".join(dups[:3]))

    # 4. UserSettings must not ship
    us = [p for p in tracked if p == "UserSettings.meta" or p.startswith("UserSettings/")]
    check(not us, "package.noUserSettings", ",".join(us[:3]))

    # 5. no local file: deps
    pkg = json.loads(git(a.repo, "show", f"{a.rev}:package.json"))
    file_deps = [f"{k}={v}" for k, v in pkg.get("dependencies", {}).items() if str(v).startswith("file:")]
    check(not file_deps, "deps.noLocalFile", ",".join(file_deps))

    # 6. version alignment: package.json vs pairing telemetry constant
    win = git(a.repo, "show", f"{a.rev}:Assets/GDAI/Editor/LayerA/GdaiCoherentBundleWindow.cs")
    m = re.search(r'PluginVersion\s*=\s*"([^"]+)"', win)
    check(m is not None, "telemetry.constantFound")
    if m:
        check(pkg.get("version") == m.group(1), "version.packageEqualsTelemetry",
              f"package={pkg.get('version')} telemetry={m.group(1)}")

    # 7b. AUTO-0N required export/binding files (8.5 release contract)
    for req in ("Assets/GDAI/Editor/LayerA/GdaiProjectBinding.cs",
                "Assets/GDAI/Editor/LayerA/GdaiExportReceipt.cs",
                "Assets/GDAI/Tests/Editor/GDAI.Editor.Tests.asmdef",
                "Assets/GDAI/Tests/Editor/GdaiProjectBindingTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiBoundStateTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiExportReceiptTests.cs"):
        check(req in tracked, f"required.{req.rsplit('/',1)[1]}")

    # 7c. AUTO-0Q required zero-manual-composer files (8.6 release contract).
    # PREVENTS_REGRESSION: shipping 8.6 with a missing composer/receipt/CTA source
    # or its test (the 8.3->8.4 incident class: files present locally, absent from
    # the distributable git face -> fresh installs break).
    for req in ("Assets/GDAI/Editor/LayerA/GdaiPlayableContract.cs",
                "Assets/GDAI/Editor/LayerA/GdaiPlayableOperation.cs",
                "Assets/GDAI/Editor/LayerA/GdaiPlayableResume.cs",
                "Assets/GDAI/Editor/LayerB/GdaiInputAssetBuilder.cs",
                "Assets/GDAI/Editor/LayerC/GdaiAudioListenerEnsurer.cs",
                "Assets/GDAI/Editor/LayerC/GdaiCameraConfigurer.cs",
                "Assets/GDAI/Editor/LayerC/GdaiCanonicalScene.cs",
                "Assets/GDAI/Editor/LayerC/GdaiEnemyPrefabBuilder.cs",
                "Assets/GDAI/Editor/LayerC/GdaiPlayableBindingApplier.cs",
                "Assets/GDAI/Editor/LayerC/GdaiPlayableComposerCta.cs",
                "Assets/GDAI/Editor/LayerC/GdaiPlayableOwnershipManifest.cs",
                "Assets/GDAI/Editor/LayerC/GdaiPlayableReceipt.cs",
                "Assets/GDAI/Editor/LayerC/GdaiSceneObjectComposer.cs",
                "Assets/GDAI/Runtime/GdaiGeneratedPlayableMarker.cs",
                "Assets/GDAI/Tests/Editor/GdaiPlayableContractTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiPlayableOperationTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiPlayableResumeTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiInputAssetBuilderTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiCameraConfigurerTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiSceneComposerTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiEnemyPrefabBuilderTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiPlayableBindingApplierTests.cs",
                "Assets/GDAI/Tests/Editor/GdaiPlayableCtaReceiptTests.cs",
                "Assets/GDAI/Tests/Editor/Fixtures/PlayableContract.rev4.projectslash-2d874a40.json"):
        check(req in tracked, f"required.{req.rsplit('/',1)[1]}")

    # 7. layer completeness
    for name, prefix in (("LayerA", "Assets/GDAI/Editor/LayerA/"),
                         ("LayerB", "Assets/GDAI/Editor/LayerB/"),
                         ("LayerC", "Assets/GDAI/Editor/LayerC/"),
                         ("Runtime", "Assets/GDAI/Runtime/")):
        n = sum(1 for p in tracked if p.startswith(prefix) and p.endswith(".cs"))
        check(n > 0, f"layer.{name}.csTracked", f"{n} file(s)")

    print(f"== RESULT: {'PASS' if FAIL == 0 else 'FAIL'} · failures={FAIL} · warnings={len(WARNINGS)} ==")
    return 1 if FAIL else 0

if __name__ == "__main__":
    sys.exit(main())
