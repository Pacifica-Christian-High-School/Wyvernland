"""Check browser export preparation and reusable per-chunk names."""
import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path


def main():
    tool = Path(__file__).with_name("prepare_web.py")
    with tempfile.TemporaryDirectory(prefix="wyvernland-download-") as directory:
        root = Path(directory)
        build = root / "Build"
        build.mkdir()
        config = '<script>window.wyvernlandBuild = { dataUrl: "Build/test.data.unityweb", loaderUrl: "Build/test.loader.js", frameworkUrl: "Build/test.framework.js.unityweb", codeUrl: "Build/test.wasm.unityweb", productName: "Exported settings" };</script>'
        for filename in ["test.loader.js", "test.framework.js.unityweb", "test.wasm.unityweb"]:
            (build / filename).write_bytes(filename.encode())
        (root / "index.html").write_text(config + '<p>Old page</p>', encoding="utf-8")
        raw = build / "test.data.unityweb"

        def prepare(content=None):
            if content is not None:
                raw.write_bytes(content)
            subprocess.run([sys.executable, str(tool), str(root)], check=True, capture_output=True)
            manifest = json.loads((root / "game-data.json").read_text())
            joined = b"".join((root / chunk).read_bytes() for chunk in manifest["chunks"])
            assert len(joined) == manifest["size"]
            assert hashlib.sha256(joined).hexdigest() == manifest["sha256"]
            assert not raw.exists(), "Only manifest-addressed world files should remain"
            assert (root / "download.js").is_file()
            page = (root / "index.html").read_text(encoding="utf-8")
            assert 'dataUrl: "Build/test.data.unityweb"' in page and 'productName: "Exported settings"' in page, "Template refresh must preserve exported settings"
            for filename in ["test.loader.js", "test.framework.js.unityweb", "test.wasm.unityweb"]:
                digest = hashlib.sha256((build / filename).read_bytes()).hexdigest()[:12]
                assert f'Build/{filename}?v={digest}' in page, "Compiled gameplay assets must not use a stale browser cache"
            assert 'id="share-world"' in page and "{{{" not in page
            assert 'src="background-loading.js?v=' in page and 'href="style.css?v=' in page
            assert (root / "fullscreen.js").is_file() and 'src="fullscreen.js?v=' in page, "The hold-to-exit controller must ship with the page"
            return manifest

        small = prepare(b"small export")
        assert len(small["chunks"]) == 1 and small["chunks"][0].endswith("-00.bin")
        first_piece = b"a" * (16 * 1024 * 1024)
        previous = prepare(first_piece + b"old ending")
        current = prepare(first_piece + b"new ending")
        assert current["chunks"][0] == previous["chunks"][0], "Unchanged chunks should retain their cache key"
        assert current["chunks"][1] != previous["chunks"][1]
        assert not (root / previous["chunks"][1]).exists(), "Obsolete generated chunks should be removed"
        assert prepare() == current, "Preparation should support already-split exports"
        (build / "test.wasm.unityweb").write_bytes(b"updated gameplay")
        assert prepare() == current, "A code update preserves cached world data while refreshing its code URL"
    print("PASS: small export, whole-data integrity, reusable unchanged chunks, obsolete cleanup, repeated preparation")


if __name__ == "__main__":
    main()
