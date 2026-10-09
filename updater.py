#!/usr/bin/env python3
"""
CAS Unity Plugin release automation tool.

Commands:
  new      Pull native dependency versions from sibling CAS-Kotlin/CAS-Swift repos,
           copy Unity bridge files and sample assets, update package.json and MobileAds.cs.
  dll      Build CleverAdsSolutions.Common.dll using the Unity-bundled msbuild.
  release  Build .unitypackage and UPM .tgz, then create a draft GitHub release
           from the latest CHANGELOG.md entry.

Usage:
  1. python3 updater.py new
  2. python3 updater.py dll
  3. Do changes and update CHANGELOG.md
  4. Commit changes
  5. python3 updater.py release
  6. Check and publish release on Github

Requirements:
  - gh CLI installed:  brew install gh && gh auth login     (release command)
  - Unity Editor installed                                  (dll and release commands)
  - msbuild available in PATH                               (dll and release commands)
  - Sibling repos at the same directory level as this repo:
      CAS-Kotlin   (Android Internal SDK)
      CAS-Swift    (iOS Internal SDK)
  - .env file with UPM credentials:                         (release command)
      UPM_ORG_ID=
      UPM_SERVICE_ACCOUNT_KEY_ID=
      UPM_SERVICE_ACCOUNT_KEY_SECRET=

Optional .env overrides:
  UNITY_EXE=   Full path to Unity binary (e.g. Unity.app/Contents/MacOS/Unity or Editor\\Unity.exe).
               Auto-discovered from ProjectSettings/ProjectVersion.txt if not set.
  UPM_CLI=     Full path to the upm CLI binary.
               Defaults to ~/.upm/bin/upm and installed automatically if missing.
"""

import argparse
import os
import platform
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from datetime import date
from tempfile import TemporaryDirectory
from urllib.request import urlretrieve

PROJECT_DIR = os.path.dirname(os.path.abspath(__file__))
PACKAGE_DIR = os.path.join(PROJECT_DIR, "Assets", "CleverAdsSolutions")
DIST_DIR = os.path.join(PROJECT_DIR, "ExportedPackages")
CHANGELOG_PATH = os.path.join(PACKAGE_DIR, "CHANGELOG.md")

ROOT_REPOS_DIR = os.path.dirname(PROJECT_DIR)
TARGET_IOS_VERSION = "15.0"
UPM_INSTALL_URL = "https://cdn.packages.unity.com/upm-cli/install.sh"
UPM_BIN = os.path.join(os.path.expanduser("~"), ".upm", "bin")
GITHUB_REPOS = 'https://github.com/cleveradssolutions/CAS-'

# Assets exported into .unitypackage
UNITY_PACKAGE_ASSETS = [
    "Assets/CleverAdsSolutions/Editor",
    "Assets/CleverAdsSolutions/LICENSE.md",
    "Assets/CleverAdsSolutions/Plugins",
    "Assets/CleverAdsSolutions/Runtime",
]

# MARK: Unity executable discovery


def find_unity_exe() -> str:
    """Return path to Unity binary. Checks UNITY_EXE env var first, then
    derives the path from ProjectSettings/ProjectVersion.txt."""
    exe = os.environ.get("UNITY_EXE", "")
    if exe:
        if os.path.isfile(exe):
            return exe
        print(f"WARNING: UNITY_EXE is set but file not found: {exe}\n"
              "  Falling back to auto-detection.", file=sys.stderr)

    version_file = os.path.join(
        PROJECT_DIR, "ProjectSettings", "ProjectVersion.txt")
    version = ""
    if os.path.isfile(version_file):
        match = re.search(r"m_EditorVersion:\s*(\S+)",
                          open(version_file).read())
        if match:
            version = match.group(1)

    candidates = []
    if platform.system() == "Darwin":
        if version:
            candidates.append(
                f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"
            )
            candidates.append(
                os.path.expanduser(
                    f"~/Documents/Environment/UnityEditors/{version}/Unity.app/Contents/MacOS/Unity")
            )
    elif platform.system() == "Linux":
        if version:
            candidates += [
                f"/opt/unity/Editor/{version}/Unity",
                f"/home/runner/Unity/Editor/{version}/Unity",
            ]
    elif platform.system() == "Windows":
        if version:
            candidates.append(
                rf"C:\Program Files\Unity\Hub\Editor\{version}\Editor\Unity.exe"
            )

    for path in candidates:
        if os.path.isfile(path):
            return path

    print(
        "ERROR: Unity executable not found.\n"
        "  Set the UNITY_EXE environment variable to the full path of the Unity binary.",
        file=sys.stderr,
    )
    sys.exit(1)


def build_common_dll(unity_exe):
    # Unity.app/Contents/MacOS/Unity -> Unity.app/Contents/Managed
    managed_dir = os.path.join(
        os.path.dirname(os.path.dirname(unity_exe)), "Managed")
    csproj = os.path.join(
        PACKAGE_DIR, "Source~", "CleverAdsSolutions.Common", "CleverAdsSolutions.Common.csproj"
    )
    # csproj compiles Source\**\*.cs, so new files need no csproj changes
    subprocess.run(
        ["msbuild", csproj, f"/p:ReferencePath={managed_dir}"], check=True)

    # Force-add sources from Source~ (ignored by the global '*~' rule).
    # Git pathspec '*' also matches '/', so subfolders are included.
    subprocess.run(
        ["git", "-C", PACKAGE_DIR, "add", "-f", "--",
            "Source~/CleverAdsSolutions.Common/Source/*.cs"],
        check=True
    )


# MARK: .unitypackage via Unity -batchmode -exportPackage

def pack_unity_package(unity_exe: str, version: str) -> str:
    os.makedirs(DIST_DIR, exist_ok=True)
    output_path = os.path.join(
        DIST_DIR, f"CleverAdsSolutions-{version}.unitypackage")
    log_path = os.path.join(PROJECT_DIR, "Logs", "unity_export.log")

    cmd = [
        unity_exe,
        "-batchmode",
        "-quit",
        "-projectPath", PROJECT_DIR,
        "-logFile", log_path,
        "-exportPackage", *UNITY_PACKAGE_ASSETS, output_path,
    ]

    print(f"Running: {' '.join(cmd)}")
    result = subprocess.run(cmd, capture_output=False)
    if result.returncode != 0:
        print(
            f"ERROR: Unity export failed (exit {result.returncode}). See {log_path}", file=sys.stderr)
        sys.exit(result.returncode)

    print(f"Done: {output_path}")
    return output_path

# MARK: UPM .tgz via upm pack


def load_dotenv():
    """Load variables from .env file into os.environ (does not override existing vars)."""
    env_file = os.path.join(PROJECT_DIR, ".env")
    if not os.path.isfile(env_file):
        return
    with open(env_file) as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, _, value = line.partition("=")
            os.environ.setdefault(key.strip(), value.strip())


def require_upm_cli():
    upm_direct = os.environ.get("UPM_CLI", os.path.join(UPM_BIN, "upm"))
    if shutil.which("upm") or os.path.isfile(upm_direct):
        os.environ["PATH"] = UPM_BIN + os.pathsep + os.environ.get("PATH", "")
        return

    print("Installing upm CLI...")
    with TemporaryDirectory() as tmp:
        install_script = os.path.join(tmp, "upm-install.sh")
        urlretrieve(UPM_INSTALL_URL, install_script)
        subprocess.run(["bash", install_script], check=True)

    os.environ["PATH"] = UPM_BIN + os.pathsep + os.environ.get("PATH", "")
    print(f"upm installed to {UPM_BIN}")

    if not shutil.which("upm"):
        print("ERROR: upm installation failed or not found in PATH.", file=sys.stderr)
        sys.exit(1)


def pack_upm_archive(version: str) -> str:
    require_upm_cli()

    org_id = os.environ.get("UPM_ORG_ID", "")
    if not org_id:
        print("ERROR: not found required UPM_ORG_ID environment variable",
              file=sys.stderr)
        sys.exit(1)

    os.makedirs(DIST_DIR, exist_ok=True)

    cmd = ["upm", "pack", ".", "--destination",
           DIST_DIR, "--organization-id", org_id]

    print(f"Running: {' '.join(cmd)}")
    print(f"  Working dir: {PACKAGE_DIR}")

    result = subprocess.run(cmd, cwd=PACKAGE_DIR, capture_output=False)
    if result.returncode != 0:
        print(
            f"ERROR: upm pack failed (exit {result.returncode})", file=sys.stderr)
        sys.exit(result.returncode)

    output_path = os.path.join(
        DIST_DIR, f"com.cleversolutions.ads.unity-{version}.tgz")
    if not os.path.isfile(output_path):
        print("ERROR: No .tgz file found after packing.", file=sys.stderr)
        sys.exit(1)

    print(f"Done: {output_path}")
    return output_path

# MARK: Update Dependencies


def _prepare_mediation_and_get_version(platform: str) -> str:
    template = os.path.join(PACKAGE_DIR, "Editor",
                            "BuildConfig", f"CAS{platform}Mediation.list")
    source = os.path.join(get_native_repo_dir(platform), "CASMediation.list")
    if not os.path.isfile(source):
        print(f"ERROR: Invalid path: {source}", file=sys.stderr)
        return "Invalid path"
    shutil.copy2(source, template)
    print(f"Updated: {template}")
    match = re.search(r'"version":\s*"(.*?)"', open(template).read())
    return match.group(1) if match else ""


def _write_android_dep(parent: ET.Element, dependency: str, version: str):
    pkg = ET.SubElement(parent, "androidPackage")
    pkg.set("spec", dependency + version)
    pkg.set("version", version)


def _write_ios_dep(parent: ET.Element, dependency: str, version: str):
    pod = ET.SubElement(parent, "iosPod")
    pod.set("name", dependency)
    pod.set("version", version)
    pod.set("minTargetSdk", f"{TARGET_IOS_VERSION}")


def _parse_semver(v: str) -> tuple:
    m = re.match(r"(\d+)\.(\d+)\.(\d+)(.*)", v)
    if not m:
        return (0, 0, 0, "")
    major, minor, patch, pre = int(m.group(1)), int(
        m.group(2)), int(m.group(3)), m.group(4)
    # pre-release (e.g. "-rc1", "-beta1") sorts lower than a full release
    pre_order = 0 if not pre else -1
    return (major, minor, patch, pre_order)


def _max_version(v1: str, v2: str) -> str:
    return v1 if _parse_semver(v1) >= _parse_semver(v2) else v2


def _set_plugin_version(version: str):
    package_json = os.path.join(PACKAGE_DIR, "package.json")
    content = open(package_json).read()
    content = re.sub(
        r'("version":\s*")[^"]+(")', rf'\g<1>{version}\2', content)
    open(package_json, "w").write(content)
    print(f"package.json version → {version}")

    mobile_ads = os.path.join(PACKAGE_DIR, "Runtime", "Common", "MobileAds.cs")
    content = open(mobile_ads).read()
    content = re.sub(
        r'(wrapperVersion\s*=\s*")[^"]+(")', rf'\g<1>{version}\2', content)
    open(mobile_ads, "w").write(content)
    print(f"MobileAds.wrapperVersion → {version}")


def update_dependencies():
    android_version = _prepare_mediation_and_get_version("Android")
    ios_version = _prepare_mediation_and_get_version("iOS")

    new_version = android_version
    if android_version != ios_version:
        print(
            f"WARNING: Plugin version mismatch:\n"
            f"  Android: {android_version}\n"
            f"  iOS:     {ios_version}"
        )
        if input("Continue? [y/N] ").lower() != "y":
            sys.exit("Canceled")
        new_version = _max_version(android_version, ios_version)

    add_changelog_entry(new_version)
    _set_plugin_version(new_version)

    base_dep_path = os.path.join(
        PACKAGE_DIR, "Editor", "CASBaseDependencies.xml")

    root_el = ET.Element("dependencies")

    android_pkgs = ET.SubElement(root_el, "androidPackages")
    if "-" in android_version:
        repos = ET.SubElement(android_pkgs, "repositories")
        ET.SubElement(
            repos, "repository").text = "https://repo.repsy.io/mvn/cleveradssolutions/beta"
    _write_android_dep(
        android_pkgs, "com.cleveradssolutions:cas-sdk:", android_version)
    _write_android_dep(
        android_pkgs, "androidx.lifecycle:lifecycle-process:", "2.6.2")

    ios_pods = ET.SubElement(root_el, "iosPods")
    sources = ET.SubElement(ios_pods, "sources")
    ET.SubElement(
        sources, "source").text = "https://github.com/cleveradssolutions/CAS-Specs.git"
    _write_ios_dep(ios_pods, "CleverAdsSolutions-Base", ios_version)

    ET.indent(root_el, space="  ")
    ET.ElementTree(root_el).write(
        base_dep_path, encoding="unicode")
    print(f"Updated: {base_dep_path}")


# MARK: Copy Native Unity bridge

def get_native_repo_dir(platform: str) -> str:
    if platform == "Android":
        return os.path.join(ROOT_REPOS_DIR, "CAS-Kotlin")
    return os.path.join(ROOT_REPOS_DIR, "CAS-Swift")


def copy_sk_adnetwork_list():
    ad_net_dir = os.path.join(get_native_repo_dir(
        "iOS"), "PublicSamplesRepo", "AdNetworkIdentifiers")
    mappings = [
        ("SKAdNetworkCompact.txt", "CASSKAdNetworks.txt"),
        ("AdNetworkCompact.txt",   "CASAttAdNetworks.txt"),
    ]
    for src_name, dest_name in mappings:
        src = os.path.join(ad_net_dir, src_name)
        dest = os.path.join(PACKAGE_DIR, "Editor", "BuildConfig", dest_name)
        if os.path.isfile(src):
            shutil.copy2(src, dest)
            print(f"Copy file: {src}")
        else:
            print(f"ERROR: File not found: {src}", file=sys.stderr)


def _copy_sample_assets_dir(dir_name: str):
    src_dir = os.path.join(PROJECT_DIR, "Assets", dir_name)
    dest_dir = os.path.join(PACKAGE_DIR, "Samples~", dir_name)
    os.makedirs(dest_dir, exist_ok=True)
    for name in os.listdir(src_dir):
        src = os.path.join(src_dir, name)
        if os.path.isfile(src):
            shutil.copy2(src, os.path.join(dest_dir, name))


def copy_sample_assets():
    _copy_sample_assets_dir("SampleComponents")
    _copy_sample_assets_dir("SampleScripts")


def copy_unity_bridge():
    android_lib = os.path.join(get_native_repo_dir(
        "Android"), "buildCAS", "CASUnityBridge.aar")
    if os.path.isfile(android_lib):
        dest = os.path.join(PACKAGE_DIR, "Plugins",
                            "Android", "CASUnityBridge.aar")
        if os.path.isfile(dest):
            os.remove(dest)
        shutil.move(android_lib, dest)
        print(f"Updated: {dest}")

    ios_bridge_dir = os.path.join(get_native_repo_dir(
        "iOS"), "CASUnityBridge", "CASUnityBridge")
    if os.path.isdir(ios_bridge_dir):
        dest_dir = os.path.join(PACKAGE_DIR, "Plugins", "iOS")
        for name in os.listdir(ios_bridge_dir):
            if name == ".DS_Store":
                continue
            src = os.path.join(ios_bridge_dir, name)
            if not os.path.isfile(src):
                continue
            shutil.copy2(src, os.path.join(dest_dir, name))
            print(f"Updated: {os.path.join(dest_dir, name)}")

# MARK: Changelog


def _read_latest_changelog() -> tuple[str, str]:
    content = open(CHANGELOG_PATH).read()
    match = re.search(
        r"# \[(\d[^\]]+)\][^\n]*\n(.*?)(?=\n# \[|\Z)",
        content, re.DOTALL
    )
    version = ""
    notes = ""
    if match:
        version = match.group(1).strip()
        notes = match.group(2).strip()

    if not version or not notes:
        print("ERROR: Could not read latest version from CHANGELOG.md", file=sys.stderr)
        sys.exit(1)

    print(f"\nVersion: {version}")
    print(f"\n{notes}\n")
    confirm = input("Create release? [y/N]: ").strip().lower()
    if confirm != "y":
        sys.exit("Canceled")

    return version, notes


def add_changelog_entry(version: str):
    content = open(CHANGELOG_PATH).read()
    if re.search(rf"#\s*\[{re.escape(version)}\]", content):
        print(f"Version {version} already exists in CHANGELOG.md")
        return
    today = date.today().strftime("%Y-%m-%d")
    entry = (
        f"# [{version}] - {today}\n"
        f"- Updated the [Android SDK to {version}]({GITHUB_REPOS}Android/releases)"
        f" and [iOS SDK to {version}]({GITHUB_REPOS}iOS/releases).\n\n"
    )
    header_end = content.index("\n") + 1
    open(CHANGELOG_PATH, "w").write(
        content[:header_end] + "\n" + entry + content[header_end:])
    print(f"Added changelog entry for {version}")

def check_submodule_clean():
    result = subprocess.run(
        ["git", "-C", PACKAGE_DIR, "status", "--porcelain"],
        capture_output=True, text=True, check=True
    )
    if result.stdout.strip():
        print(
            f"ERROR: Uncommitted changes in submodule '{PACKAGE_DIR}'.\n"
            "Commit or stash them before creating a release.",
            file=sys.stderr
        )
        sys.exit(1)


def create_github_release(version: str, notes: str, assets: list):
    if not assets:
        print(f"ERROR: No assets found in {DIST_DIR}", file=sys.stderr)
        sys.exit(1)

    cmd = ["gh", "release", "create", version,
           "--title", f"Version {version}", "--notes", notes, *assets, "--draft"]

    print(f"Creating GitHub release {version} with {len(assets)} asset(s)...")
    subprocess.run(cmd, check=True, cwd=PACKAGE_DIR)


# MARK: Entry point

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command")

    new_cmd = subparsers.add_parser(
        "new", help="Add a new plugin version entry")

    dll_cmd = subparsers.add_parser(
        "dll", help="Add a new plugin version entry")

    release_cmd = subparsers.add_parser(
        "release", help="Create a GitHub release from latest CHANGELOG entry")

    args = parser.parse_args()
    load_dotenv()
    if args.command == "new":
        update_dependencies()
        copy_sk_adnetwork_list()
        copy_unity_bridge()
        copy_sample_assets()
    elif args.command == "dll":
        unity_exe = find_unity_exe()
        build_common_dll(unity_exe)
    elif args.command == "release":
        version, notes = _read_latest_changelog()
        unity_exe = find_unity_exe()

        check_submodule_clean()
        create_github_release(version, notes, [
            pack_unity_package(unity_exe, version),
            pack_upm_archive(version)
        ])
