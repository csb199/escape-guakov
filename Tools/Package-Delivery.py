"""Package source, the verified player and selected evidence; omit caches and keys."""
from pathlib import Path
import hashlib
import json
import zipfile

project = Path(__file__).resolve().parent.parent
target = project.parent / "unity-game-framework-delivery.zip"
results = sorted((project / "TestResults").glob("HybridCLR-*/suite-result.json"), reverse=True)
if not results:
    raise RuntimeError("Run Tools/Test-Demo.ps1 -Backend HybridCLR before packaging.")
suite = json.loads(results[0].read_text(encoding="utf-8-sig"))
assert suite["passed"] and suite["playerStarts"] == 16 and suite["demoChecks"] >= 62
core = json.loads((project / "TestResults/core-tests.json").read_text(encoding="utf-8-sig"))
assert core["passed"] >= 177 and core["failed"] == 0
editor_report = max((project / "TestResults").glob("Editor*/demo-report.json"), key=lambda p: p.stat().st_mtime)
editor = json.loads(editor_report.read_text(encoding="utf-8-sig"))
assert editor["passed"] and len(editor["checks"]) >= 62
publish_report = max((project / "TestResults").glob("Publish-*/suite-result.json"), key=lambda p: p.stat().st_mtime)
publish = json.loads(publish_report.read_text(encoding="utf-8-sig"))
assert publish["passed"] and publish["playerStarts"] == 3 and publish["demoChecks"] >= 62 and publish["incompatibleBaselineRejected"]
config = json.loads((project / "Assets/Resources/game-config.json").read_text(encoding="utf-8-sig"))
assert publish.get("sceneBundlesChanged") and "/HotScenes/V3/" in publish["sceneAsset"]
assert publish["clientBuild"] == config["clientBuild"], "Published release uses an old client baseline"
source_check = json.loads((project / "TestResults/source-consistency.json").read_text(encoding="utf-8"))
assert source_check["passed"]
for item in source_check["files"]:
    assert hashlib.sha256((project / "Assets/Game" / item["file"]).read_bytes()).hexdigest() == item["sha256"], "Source changed after build verification"
for name, key in [("EnergyCollector.exe", "playerSha256"), ("GameAssembly.dll", "gameAssemblySha256")]:
    with (project / "Builds/HybridCLR" / name).open("rb") as stream:
        assert hashlib.file_digest(stream, "sha256").hexdigest().upper() == suite[key], "Player changed since validation"
    assert suite[key] == publish[key], "Published release was tested against a different player"
selected = ["Assets", "Packages", "ProjectSettings", "Tools", "Tests"]
files = []
for folder in selected:
    for path in (project / folder).rglob("*"):
        relative = path.relative_to(project)
        if path.is_file() and not any(p in {"bin", "obj", "__pycache__", ".git"} for p in relative.parts):
            files.append(path)
for path in (project / "Builds/HybridCLR").rglob("*"):
    if path.is_file() and not any("BackUpThisFolder" in p for p in path.parts):
        files.append(path)
baseline = project / "Builds/ClientBaselines" / config["clientBuild"]
assert (baseline / "baseline.json").is_file()
files.extend(p for p in baseline.rglob("*") if p.is_file())
files.extend(p for p in (publish_report.parent / "Release").rglob("*") if p.is_file())
for name in ["README.md", "CODEMAP.md", "AGENTS.md", "ARCHITECTURE.md", "ENTITY.md", "EXPANSION.md", "FRAMEWORK-AUDIT.md", "THIRD-PARTY.md", "Launch-Demo.cmd", ".gitignore", "BuildHybrid.log",
             "WORLD.md", "TestResults/WORLD-VALIDATION.md", "TestResults/WORLD-REVIEW.md", "TestResults/scene-entity-core.log", "TestResults/scene-entity-reentry-before.log", "TestResults/world-changes.txt", "TestResults/VALIDATION.md", "TestResults/FIVE-FIXES.md", "TestResults/EXPANSION-REVIEW.md", "TestResults/EXPANSION-VALIDATION.md",
             "TestResults/core-tests.json", "TestResults/five-fixes-core.log", "TestResults/expansion-core.log", "TestResults/expansion-changes.txt", "TestResults/source-consistency.json"]:
    files.append(project / name)
for folder in [editor_report.parent, results[0].parent, publish_report.parent]:
    for path in folder.rglob("*"):
        if path.is_file() and path.suffix in {".json", ".jsonl", ".png", ".txt", ".log"} and not any(p in {"Updates", "RejectedBaseline", "Release"} for p in path.parts):
            files.append(path)
assert files and all(p.is_file() for p in files)
assert not any("signing-key" in p.name or "LocalOnly" in p.relative_to(project).parts for p in files)
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for path in sorted(set(files)):
        archive.write(path, Path(project.name) / path.relative_to(project))
with zipfile.ZipFile(target) as archive:
    assert archive.testzip() is None
    assert "unity-game-framework/Builds/HybridCLR/EnergyCollector.exe" in archive.namelist()
print(f"Created {target} ({target.stat().st_size / 1024 / 1024:.1f} MiB)")
print(f"Player evidence: {results[0].parent.name}")
print(f"Independent release evidence: {publish_report.parent.name}")
