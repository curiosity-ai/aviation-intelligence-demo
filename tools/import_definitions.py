"""Imports src/AviationIntel.Definitions into a workspace.

Zips the definitions folder exactly as the workspace's own export lays it out (code/, config/,
nlp/ at the root), posts it to /api/graph/import-definitions and prints the streamed log. The
templates/ folder is source for tools/embed_templates.py, not a definitions folder, so it is left out.

    python3 tools/import_definitions.py --endpoint http://localhost:8080 --user admin --password admin
    python3 tools/import_definitions.py --token <bearer token>

Import after the first connector run: the AI tool, agent and endpoints compile against the N/E
constants the workspace generates from the schema the connector registered.
"""
import argparse
import io
import json
import os
import sys
import urllib.request
import uuid
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFINITIONS = os.path.join(ROOT, "src", "AviationIntel.Definitions")
INCLUDED_ROOTS = ("code", "config", "nlp")


def build_zip():
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as z:
        for root in INCLUDED_ROOTS:
            base = os.path.join(DEFINITIONS, root)
            for folder, _, files in os.walk(base):
                for name in sorted(files):
                    path = os.path.join(folder, name)
                    z.write(path, os.path.relpath(path, DEFINITIONS).replace(os.sep, "/"))
    return buffer.getvalue()


def login(endpoint, user, password):
    req = urllib.request.Request(endpoint + "/api/login/create", data=json.dumps({"UserName": user, "Password": password}).encode(),
                                 headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req) as r:
        return r.read().decode().strip('"')


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--endpoint", default=os.environ.get("CURIOSITY_ENDPOINT", "http://localhost:8080"))
    p.add_argument("--token")
    p.add_argument("--user", default="admin")
    p.add_argument("--password", default="admin")
    p.add_argument("--zip-out", help="also write the zip here")
    args = p.parse_args()

    endpoint = args.endpoint.rstrip("/")
    token = args.token or login(endpoint, args.user, args.password)
    payload = build_zip()

    if args.zip_out:
        with open(args.zip_out, "wb") as f:
            f.write(payload)

    boundary = uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"definitions.zip\"\r\n"
            f"Content-Type: application/zip\r\n\r\n").encode() + payload + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(endpoint + "/api/graph/import-definitions", data=body, method="POST",
                                 headers={"Authorization": "Bearer " + token, "Content-Type": f"multipart/form-data; boundary={boundary}"})

    ok = False
    event = None
    with urllib.request.urlopen(req, timeout=900) as r:
        for raw in r:
            line = raw.decode("utf-8").rstrip("\n")
            if line.startswith("event:"):
                event = line[6:].strip()
            elif line.startswith("data:") and event in ("log", "result"):
                data = json.loads(line[5:].strip() or "{}")
                if event == "log":
                    print(f"  {data.get('Level', ''):<7} {data.get('Message', '')}")
                else:
                    ok = bool(data.get("Success"))
                    print("Success:", ok)
                    for e in data.get("Errors") or []:
                        print("  ERROR:", e)
                    for w in data.get("Warnings") or []:
                        print("  WARN: ", w)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
