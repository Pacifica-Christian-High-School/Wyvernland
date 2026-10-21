"""Split large Unity world downloads for static hosting and copy the page assets."""
import argparse
import hashlib
import json
import re
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    root = args.directory.resolve()
    html = (root / "index.html").read_text(encoding="utf-8")
    match = re.search(r'dataUrl:\s*"([^"]+)"', html)
    if not match:
        parser.error("The Unity export has no dataUrl.")
    data = (root / match.group(1)).resolve()
    if not data.is_relative_to(root / "Build"):
        parser.error("The data file must be inside this export's Build folder.")
    manifest_path = root / "game-data.json"
    if data.exists():
        content = data.read_bytes()
        digest = hashlib.sha256(content).hexdigest()
        chunks = []
        for index, offset in enumerate(range(0, len(content), 16 * 1024 * 1024)):
            piece = content[offset:offset + 16 * 1024 * 1024]
            chunk_digest = hashlib.sha256(piece).hexdigest()
            chunk = root / "Build" / f"world-{chunk_digest[:16]}-{index:02}.bin"
            chunk.write_bytes(piece)
            chunks.append(chunk.relative_to(root).as_posix())
        joined = b"".join((root / chunk).read_bytes() for chunk in chunks)
        if hashlib.sha256(joined).hexdigest() != digest:
            raise ValueError("Split world data failed verification.")
        manifest_path.write_text(json.dumps({"size": len(content), "sha256": digest, "chunks": chunks}, indent=2) + "\n", encoding="utf-8")
        data.unlink()
    elif not manifest_path.exists():
        parser.error("The world data file is missing.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    active_chunks = {(root / chunk).resolve() for chunk in manifest["chunks"]}
    for stale in (root / "Build").glob("world-*.bin"):
        if re.fullmatch(r"world-[0-9a-f]{16}-[0-9]{2}\.bin", stale.name) and stale.resolve().is_relative_to(root / "Build") and stale.resolve() not in active_chunks:
            stale.unlink()
    template = Path(__file__).resolve().parents[1] / "Assets" / "WebGLTemplates" / "Wyvernland"
    browser_assets = ("game.js", "style.css", "online.js", "background-loading.js", "rooms.js", "world.js", "download.js", "fullscreen.js")
    for filename in browser_assets:
        (root / filename).write_bytes((template / filename).read_bytes())
    # Browser-only changes do not require recompiling Unity. Retain the exported
    # filenames/settings while refreshing the page around that build config.
    config_pattern = re.compile(r"<script>\s*window\.wyvernlandBuild\s*=\s*\{.*?</script>", re.S)
    config = config_pattern.search(html)
    if config:
        def version_native_asset(match):
            filename = match.group(2).split("?", 1)[0]
            asset = (root / filename).resolve()
            if not asset.is_relative_to(root / "Build") or not asset.is_file():
                raise ValueError(f"Missing native game asset: {filename}")
            digest = hashlib.sha256(asset.read_bytes()).hexdigest()[:12]
            return f'{match.group(1)}{filename}?v={digest}{match.group(3)}'

        # Unity uses stable export filenames. Version the compiled code too, so
        # returning players receive changed gameplay instead of cached binaries.
        build_config = re.sub(r'((?:loaderUrl|frameworkUrl|codeUrl):\s*")([^"]+)(")',
            version_native_asset, config.group(0))
        updated, count = config_pattern.subn(lambda _: build_config,
            (template / "index.html").read_text(encoding="utf-8"), count=1)
        if count != 1:
            raise ValueError("The browser template must contain one build configuration.")
        for filename in browser_assets:
            version = hashlib.sha256((root / filename).read_bytes()).hexdigest()[:12]
            attribute = "href" if filename.endswith(".css") else "src"
            updated = updated.replace(f'{attribute}="{filename}"', f'{attribute}="{filename}?v={version}"')
        (root / "index.html").write_text(updated, encoding="utf-8")
    print(f"Browser export prepared: {root}")


if __name__ == "__main__":
    main()
