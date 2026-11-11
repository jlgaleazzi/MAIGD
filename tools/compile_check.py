#!/usr/bin/env python3
"""Compile-check the Biplanes C# assemblies without starting Unity.

Use it when tools/unity.sh can't run (no UNITY_PATH, or the Unity editor already has the
project open, which blocks batch mode). It builds the runtime assembly, then the editor
assembly, with `dotnet build`, against the DLLs Unity already compiled into
Biplanes/Library/ScriptAssemblies. Nothing is written into the project: the temporary
.csproj files are deleted afterwards and the output goes to a temp folder.

Needs: the .NET SDK (`dotnet`) and Unity's generated Biplanes.csproj and
Biplanes.Editor.csproj (Unity writes these when it opens the project). New .cs files are
picked up even before Unity regenerates the .csproj.

Usage:  python tools/compile_check.py [--runtime-only]
Exit:   0 = both assemblies compile, 1 = compile errors (printed), 3 = can't run the check.
"""
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Biplanes")
ROOT = os.path.normpath(ROOT)

ASSEMBLIES = [
    # (generated csproj, temp csproj, source folder, excluded sub-folder)
    ("Biplanes.csproj", "zz_check_runtime.csproj", "Assets/Biplanes", "Assets/Biplanes/Editor"),
    ("Biplanes.Editor.csproj", "zz_check_editor.csproj", "Assets/Biplanes/Editor", None),
]


def norm(p):
    return p.replace("\\", "/")


def make_csproj(src, dst, src_dir, exclude, out_dir, runtime_dll):
    text = open(os.path.join(ROOT, src), encoding="utf-8-sig").read()
    # Project references pull in Unity packages that don't build outside Unity.
    # Reference their prebuilt DLLs instead.
    text = re.sub(r"<ProjectReference Include=.*?</ProjectReference>", "", text, flags=re.S)
    dlls = [
        norm(d)
        for d in glob.glob(os.path.join(ROOT, "Library", "ScriptAssemblies", "*.dll"))
        if not os.path.basename(d).startswith("Biplanes")
    ]
    if runtime_dll:
        dlls.append(norm(runtime_dll))
    refs = "".join(
        '<Reference Include="%s"><HintPath>%s</HintPath></Reference>'
        % (os.path.splitext(os.path.basename(d))[0], d)
        for d in dlls
    )
    # Add .cs files Unity hasn't listed yet (created since it last regenerated the csproj).
    listed = {norm(m).lower() for m in re.findall(r'<Compile Include="([^"]+)"', text)}
    extra = []
    for path in glob.glob(os.path.join(ROOT, src_dir, "**", "*.cs"), recursive=True):
        rel = norm(os.path.relpath(path, ROOT))
        if exclude and rel.startswith(exclude + "/"):
            continue
        if "/Tests/" in rel:
            continue
        if rel.lower() not in listed:
            extra.append(rel)
    compiles = "".join('<Compile Include="%s" />' % e for e in extra)
    text = text.replace("</Project>", "<ItemGroup>%s%s</ItemGroup></Project>" % (refs, compiles))
    open(os.path.join(ROOT, dst), "w", encoding="utf-8").write(text)
    return extra


def build(dst, out_dir, obj_dir):
    cmd = [
        "dotnet", "build", dst, "-nologo", "-v", "q",
        "-o", out_dir, "-p:BaseIntermediateOutputPath=%s/" % norm(obj_dir),
    ]
    proc = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)
    lines = (proc.stdout + proc.stderr).splitlines()
    errors = sorted({l.strip() for l in lines if ": error " in l})
    return proc.returncode == 0 and not errors, errors


def main():
    runtime_only = "--runtime-only" in sys.argv
    if shutil.which("dotnet") is None:
        print("compile_check: dotnet SDK not found. Install .NET SDK or use tools/unity.sh.")
        return 3
    for src, _, _, _ in ASSEMBLIES:
        if not os.path.exists(os.path.join(ROOT, src)):
            print("compile_check: %s missing. Open the project in Unity once so it writes it." % src)
            return 3
    if not glob.glob(os.path.join(ROOT, "Library", "ScriptAssemblies", "*.dll")):
        print("compile_check: Library/ScriptAssemblies is empty. Open the project in Unity once.")
        return 3

    work = tempfile.mkdtemp(prefix="biplanes_check_")
    ok_all = True
    runtime_dll = None
    try:
        for src, dst, src_dir, exclude in ASSEMBLIES:
            if runtime_only and dst != "zz_check_runtime.csproj":
                continue
            name = os.path.splitext(src)[0]
            out_dir = os.path.join(work, name, "bin")
            obj_dir = os.path.join(work, name, "obj")
            try:
                extra = make_csproj(src, dst, src_dir, exclude, out_dir, runtime_dll)
                ok, errors = build(dst, out_dir, obj_dir)
            finally:
                try:
                    os.remove(os.path.join(ROOT, dst))
                except OSError:
                    pass
            note = " (+%d new file(s) not yet in the csproj)" % len(extra) if extra else ""
            if ok:
                print("PASS %s%s" % (name, note))
                runtime_dll = os.path.join(out_dir, "Biplanes.dll")
            else:
                ok_all = False
                print("FAIL %s%s" % (name, note))
                for e in errors:
                    print("  " + e)
                break  # the editor assembly needs the runtime one
    finally:
        shutil.rmtree(work, ignore_errors=True)
    return 0 if ok_all else 1


if __name__ == "__main__":
    sys.exit(main())
