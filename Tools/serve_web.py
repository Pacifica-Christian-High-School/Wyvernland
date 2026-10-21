"""Serve the exported browser game locally without extra dependencies."""
import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parents[1] / "Sites" / "Wyvernland" / "public")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    if not (args.directory / "index.html").is_file():
        parser.error("Build the browser game first with Tools/Build-Web.ps1.")
    handler = partial(SimpleHTTPRequestHandler, directory=str(args.directory.resolve()))
    with ThreadingHTTPServer(("127.0.0.1", args.port), handler) as server:
        print(f"Play Wyvernland: http://127.0.0.1:{args.port}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
