"""Writes THIRD-PARTY-NOTICES.txt: every package the app ships, its license and
copyright, the full license texts, and the notices the packages carry themselves.

Run after adding or updating a package (a unit test fails until you do):
    python tools/notices/make_notices.py
Reads the restored NuGet cache (~/.nuget/packages) and the license texts in
tools/notices/licenses/ (from the SPDX license list).
"""
import glob
import hashlib
import os
import re
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
APP = os.path.join(ROOT, "src", "Ascolotl.App", "Ascolotl.App.csproj")
CACHE = os.path.join(os.path.expanduser("~"), ".nuget", "packages")
TEXTS = os.path.join(ROOT, "tools", "notices", "licenses")

# Where the package metadata has no copyright line, or one that is not the notice to show.
COPYRIGHT = {
    "AdocNet": "Copyright (c) 2026 Sylvain Fasel",
    "ExCSS": "Copyright (c) Tyler Brinks",
    "Markdig": "Copyright (c) Alexandre Mutel",
    "PdfPig": "Copyright (c) UglyToad",
    "Tmds.DBus.Protocol": "Copyright (c) Tom Deseyn",
}

# Shipped inside a package, under a license of its own.
EXTRA = [
    ("Inter typeface 3.019, in Avalonia.Fonts.Inter", "OFL-1.1",
     "Copyright 2020 The Inter Project Authors (https://github.com/rsms/inter). Inter is a trademark of rsms.",
     "https://github.com/rsms/inter"),
    ("JetBrains Mono 2.305, the editor font", "OFL-1.1",
     "Copyright 2020 The JetBrains Mono Project Authors (https://github.com/JetBrains/JetBrainsMono). "
     "JetBrains Mono is a trademark of JetBrains s.r.o.",
     "https://github.com/JetBrains/JetBrainsMono"),
]

TITLES = {
    "MIT": "MIT License",
    "BSD-2-Clause": "BSD 2-Clause License",
    "Apache-2.0": "Apache License 2.0",
    "MS-PL": "Microsoft Public License",
    "OFL-1.1": "SIL Open Font License 1.1",
}


def packages():
    out = subprocess.run(["dotnet", "list", APP, "package", "--include-transitive"],
                         capture_output=True, text=True, check=True).stdout
    found = {}
    for line in out.splitlines():
        m = re.match(r"\s+>\s+(\S+)\s+.*?(\S+)\s*$", line)
        if m:
            found[m.group(1)] = m.group(2)
    return sorted(found.items(), key=lambda p: p[0].lower())


def nuspec(pid, ver):
    folder = os.path.join(CACHE, pid.lower(), ver)
    text = open(glob.glob(os.path.join(folder, "*.nuspec"))[0], encoding="utf-8").read()

    def tag(name):
        m = re.search(rf"<{name}[^>]*>(.*?)</{name}>", text, re.S)
        return m.group(1).strip() if m else ""

    lic = re.search(r'<license type="(\w+)">(.*?)</license>', text)
    license = lic.group(2) if lic and lic.group(1) == "expression" else "see its notice below"
    copyright = next((c for k, c in COPYRIGHT.items() if pid == k or pid.startswith(k + ".")), None) \
        or tag("copyright") or f"Copyright (c) {tag('authors')}"
    return folder, license, copyright, tag("projectUrl")


def runtime_notices():
    """The .NET runtime is published into the app (self-contained)."""
    packs = glob.glob(os.path.join(CACHE, "microsoft.netcore.app.runtime.win-x64", "10.*", "THIRD-PARTY-NOTICES.TXT"))
    newest = max(packs, key=lambda p: [int(x) for x in re.findall(r"\d+", os.path.basename(os.path.dirname(p)))])
    return os.path.basename(os.path.dirname(newest)), newest


def main():
    rows, by_license, notices = [], {}, {}

    def notice(owner, path):
        body = open(path, encoding="utf-8", errors="replace").read().strip()
        key = hashlib.sha256(body.encode()).hexdigest()
        notices.setdefault(key, ([], os.path.basename(path), body))[0].append(owner)

    for pid, ver in packages():
        folder, license, copyright, url = nuspec(pid, ver)
        rows.append((f"{pid} {ver}", license, copyright, url))
        by_license.setdefault(license, []).append(pid)
        for f in sorted(os.listdir(folder)):
            if re.match(r"(?i)(third.?party.?notices|notices\.md)", f) or (license.startswith("see") and re.match(r"(?i)licen[cs]e", f)):
                notice(pid, os.path.join(folder, f))

    for name, license, copyright, url in EXTRA:
        rows.append((name, license, copyright, url))
        by_license.setdefault(license, []).append(name)

    runtime_version, runtime_path = runtime_notices()
    rows.append((f".NET runtime {runtime_version}", "MIT", "Copyright (c) .NET Foundation and Contributors",
                 "https://github.com/dotnet/runtime"))
    by_license["MIT"].append(".NET runtime")
    notice(f".NET runtime {runtime_version}", runtime_path)

    unknown = set(by_license) - set(TITLES) - {"see its notice below"}
    if unknown:
        raise SystemExit(f"No license text for {sorted(unknown)}: add it to tools/notices/licenses/ and TITLES.")

    rule = "=" * 78
    out = [
        "Ascolotl: third-party notices",
        "",
        "Ascolotl is free software under the GNU General Public License, version 3",
        "(see LICENSE). It is built with, and ships, the components below, each under",
        "its own license. The full license texts follow the list, then the notices the",
        "components carry themselves.",
        "",
        rule, "Components", rule, "",
    ]
    for name, license, copyright, url in rows:
        out += [name, f"  License: {license}", f"  {copyright}"] + ([f"  {url}"] if url else []) + [""]

    for spdx, title in TITLES.items():
        users = by_license.get(spdx)
        if not users:
            continue
        text = open(os.path.join(TEXTS, f"{spdx}.txt"), encoding="utf-8").read().strip()
        text = re.sub(r"Copyright \(c\) <year> <(copyright holders|owner)>",
                      "Copyright: as listed above for each component.", text)
        out += [rule, title, f"Applies to: {', '.join(users)}", rule, "", text, ""]

    for owners, file, body in notices.values():
        out += [rule, f"Notice: {file}, from {', '.join(owners)}", rule, "", body, ""]

    with open(os.path.join(ROOT, "THIRD-PARTY-NOTICES.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out))
    print(f"{len(rows)} components, {len(notices)} bundled notices")


if __name__ == "__main__":
    main()
