#!/usr/bin/env python3
"""NN training tools for the Racetrack project (macOS, Windows, Linux).

Run through the launcher, which uses the ML-Agents virtual environment:
    Tools/ml/nntools <command> ...            (macOS / Linux)
    Tools\\ml\\nntools.cmd <command> ...        (Windows)

Commands:
    build    Build the training game (the four NN scenes) with the Unity Editor in batch mode.
             The project must not be open in the Editor.
    record   Record demonstrations of your FuzzyVehicle on all tracks at once (headless, as fast as
             possible), keeping only good driving, and print a report per track.
    train    Train with mlagents-learn on the training build (headless or in small windows) or in the Editor.
    test     Run the graded races (Editor tests in RacingTest) with your FuzzyVehicle, or with a trained NN
             model (--nn [--model FILE]), and print the estimated scores.

Run `nntools <command> --help` for options. Standard library only.
"""

import argparse
import glob
import json
import os
import platform
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
SYSTEM = platform.system()  # Darwin, Windows, Linux
PLATFORM_FOLDER = {"Darwin": "macOS", "Windows": "Windows", "Linux": "Linux"}.get(SYSTEM, "Linux")
BUILD_EXTENSION = {"Darwin": ".app", "Windows": ".exe", "Linux": ".x86_64"}.get(SYSTEM, ".x86_64")
DEFAULT_BUILD = PROJECT / "Builds" / "NNTraining" / PLATFORM_FOLDER / ("RacetrackNN" + BUILD_EXTENSION)
SCENES = ["RaceTrackNN", "WindingRaceTrackNN", "FastSweepersRaceTrackNN", "DragRaceTrackNN"]
STUDENT_BUILD_METHOD = "GameAI.EditorTools.NNTrainingBuild.BuildFromCommandLine"

# Local editor state that batch mode rewrites (last opened scenes, user settings, ...): saved and restored
PRESERVE = [
    "Library/LastSceneManagerSetup.txt",
    "Library/EditorUserBuildSettings.asset",
    "Library/SceneVisibilityState.asset",
    "Library/expandedItems",
    "UserSettings",
    "ProjectSettings/ProjectSettings.asset",
]


def fail(message):
    print(f"error: {message}", file=sys.stderr)
    sys.exit(1)


def rel(path):
    try:
        return str(Path(path).resolve().relative_to(PROJECT))
    except ValueError:
        return str(path)


# ---------------------------------------------------------------------------------------------- Unity Editor

def unity_version():
    text = (PROJECT / "ProjectSettings" / "ProjectVersion.txt").read_text()
    m = re.search(r"^m_EditorVersion:\s*(\S+)", text, re.M)
    if not m:
        fail("Could not read the Unity version from ProjectSettings/ProjectVersion.txt")
    return m.group(1)


def unity_hub_install_dirs():
    """Editor install folders: Unity Hub's default plus a custom location set in the Hub, if any."""
    home = Path.home()
    if SYSTEM == "Darwin":
        dirs = [Path("/Applications/Unity/Hub/Editor")]
        config = home / "Library" / "Application Support" / "UnityHub"
    elif SYSTEM == "Windows":
        dirs = [Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity" / "Hub" / "Editor"]
        config = Path(os.environ.get("APPDATA", home / "AppData" / "Roaming")) / "UnityHub"
    else:
        dirs = [home / "Unity" / "Hub" / "Editor"]
        config = home / ".config" / "UnityHub"
    try:
        custom = json.loads((config / "secondaryInstallPath.json").read_text())
        if custom:
            dirs.insert(0, Path(custom))
    except (OSError, ValueError):
        pass
    return dirs


def find_unity():
    if os.environ.get("UNITY"):
        return Path(os.environ["UNITY"])
    version = unity_version()
    relative = {"Darwin": Path("Unity.app/Contents/MacOS/Unity"),
                "Windows": Path("Editor/Unity.exe")}.get(SYSTEM, Path("Editor/Unity"))
    candidates = [d / version / relative for d in unity_hub_install_dirs()]
    for c in candidates:
        if c.exists():
            return c
    fail(f"Unity {version} not found (looked in: {', '.join(str(c) for c in candidates)}). "
         f"Install it with Unity Hub or set UNITY=/path/to/the/Unity/executable")


def project_open_in_editor():
    # The Editor holds Temp/UnityLockfile while the project is open
    return (PROJECT / "Temp" / "UnityLockfile").exists()


def run_unity_batch(args, log_file, env=None):
    if project_open_in_editor():
        fail("The project looks open in the Unity Editor (Temp/UnityLockfile exists). Close it first "
             "(if the Editor crashed, delete Temp/UnityLockfile).")
    unity = find_unity()
    snapshot = Path(tempfile.mkdtemp(prefix="nntools-state-"))
    for p in PRESERVE:
        src = PROJECT / p
        if src.is_dir():
            shutil.copytree(src, snapshot / p)
        elif src.exists():
            (snapshot / p).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, snapshot / p)
    try:
        cmd = [str(unity), "-batchmode", "-projectPath", str(PROJECT), "-logFile", str(log_file)] + args
        print(f"Running Unity ({unity_version()}) in batch mode; log: {rel(log_file)}")
        return subprocess.call(cmd, env=env)
    finally:
        for p in PRESERVE:
            saved, target = snapshot / p, PROJECT / p
            if saved.exists():
                if target.is_dir():
                    shutil.rmtree(target)
                elif target.exists():
                    target.unlink()
                if saved.is_dir():
                    shutil.copytree(saved, target)
                else:
                    shutil.copy2(saved, target)
        shutil.rmtree(snapshot, ignore_errors=True)


# ---------------------------------------------------------------------------------------------- build

def cmd_build(a, unity_args):
    output = Path(a.output).resolve() if a.output else DEFAULT_BUILD
    log = PROJECT / "Builds" / "build-training-env.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    args = ["-quit", "-executeMethod", a.execute_method, "-buildOutput", str(output)] + unity_args
    code = run_unity_batch(args, log)
    tag = "[" + a.execute_method.split(".")[-2] + "]"  # the build class logs with its own name, e.g. [NNTrainingBuild]
    if log.exists():
        for line in log.read_text(errors="replace").splitlines():
            if tag in line:
                print(line.strip())
    creating_scenes = "CreateScenes" in a.execute_method
    if code != 0 or (not creating_scenes and not output.exists()):
        tail = log.read_text(errors="replace").splitlines()[-30:] if log.exists() else []
        print("\n".join(tail))
        fail(f"Build failed (exit code {code}); see {rel(log)}"
             + ("" if SYSTEM != "Linux" else ". On Linux, the Unity Linux Build Support module must be installed."))
    print("Done." if creating_scenes else f"Built {rel(output)}")


# ---------------------------------------------------------------------------------------------- the game

def resolve_build(env):
    path = Path(env).resolve() if env else DEFAULT_BUILD
    if not path.exists() and not path.suffix:
        with_ext = path.with_name(path.name + BUILD_EXTENSION)
        if with_ext.exists():
            path = with_ext
    if not path.exists():
        fail(f"Training build not found: {rel(path)}. Build it first: nntools build")
    return path


def player_executable(build):
    """The executable inside a build (on macOS the binary inside the .app)."""
    if SYSTEM == "Darwin" and build.suffix == ".app":
        binaries = sorted((build / "Contents" / "MacOS").iterdir())
        if not binaries:
            fail(f"No executable in {build}/Contents/MacOS")
        return binaries[0]
    return build


def running_instances(build):
    """PIDs of game instances started from this build (e.g. left over from a crashed run)."""
    exe = str(player_executable(build))
    pids = []
    try:
        if SYSTEM == "Windows":
            out = subprocess.run(
                ["powershell", "-NoProfile", "-Command",
                 "Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq '%s' } | "
                 "ForEach-Object { $_.ProcessId }" % exe.replace("'", "''")],
                capture_output=True, text=True).stdout
        else:
            out = subprocess.run(["pgrep", "-f", re.escape(exe)], capture_output=True, text=True).stdout
        pids = [int(x) for x in out.split() if x.strip().isdigit() and int(x) != os.getpid()]
    except OSError:
        pass
    return pids


def kill(pids):
    for pid in pids:
        if SYSTEM == "Windows":
            subprocess.call(["taskkill", "/F", "/PID", str(pid)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        else:
            try:
                os.kill(pid, 15)
            except OSError:
                pass


def ports_in_use(base, count):
    busy = []
    for port in range(base, base + count):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            try:
                s.bind(("127.0.0.1", port))
            except OSError:
                busy.append(port)
    return busy


def check_stale(build, kill_stale):
    stale = running_instances(build)
    if not stale:
        return
    if kill_stale:
        print(f"Stopping game instances left running from {rel(build)}: {stale}")
        kill(stale)
        time.sleep(2)
    else:
        fail(f"Game instances of {rel(build)} are already running (PIDs {stale}). If they are left over from "
             f"a crashed run, rerun with --kill-stale. To train two runs at once, use separate builds and --base-port.")


# ---------------------------------------------------------------------------------------------- record

def cmd_record(a):
    build = resolve_build(a.env)
    check_stale(build, a.kill_stale)
    full_name = re.sub(r"[^A-Za-z0-9]", "", a.name) or "Racecar"
    name = full_name[:9]   # + one digit per track = the 10-character file prefix DemonstrationSession allows
    out = Path(a.out).resolve() if a.out else PROJECT / "Demonstrations" / full_name
    scenes = SCENES if a.scenes == "all" else [s.strip() for s in a.scenes.split(",") if s.strip()]

    out.mkdir(parents=True, exist_ok=True)
    old = glob.glob(str(out / f"{name}[0-9]*.demo"))
    if old and not a.append:
        fail(f"{rel(out)} already has {len(old)} {name}*.demo files. Use --append to add to them, "
             f"another --name, or delete them first.")

    exe = player_executable(build)
    procs = []
    for scene in scenes:
        index = scenes.index(scene)
        prefix = f"{name}{index}"   # one prefix per track: files share one directory
        args = [str(exe), "-raceScene", scene, "-recordDemos", str(out), "-recordSeconds", str(a.seconds),
                "-demoPrefix", prefix, "-logFile", str(out / f"{prefix}.log")]
        if not a.window:
            args += ["-batchmode", "-nographics"]
        else:
            args += ["-screen-width", "320", "-screen-height", "180", "-screen-fullscreen", "0"]
        if a.teacher_max_speed:
            args += ["-teacherMaxSpeed", str(a.teacher_max_speed)]
        if a.seed:
            args += ["-recordSeed", str(a.seed + index)]
        procs.append((scene, prefix, subprocess.Popen(args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)))
    print(f"Recording {a.seconds:g} simulated seconds on {len(scenes)} track(s) into {rel(out)} ...")

    for scene, prefix, p in procs:
        p.wait()
    print()
    warnings = 0
    for scene, prefix, p in procs:
        report = out / f"{prefix}_report.txt"
        print(f"== {scene}")
        if report.exists():
            text = report.read_text()
            warnings += "TEACHER WARNING" in text
            print(text.rstrip())
        else:
            print(f"No report (exit code {p.returncode}); see {rel(out / (prefix + '.log'))}")
        print()
    files = glob.glob(str(out / "*.demo"))
    print(f"{len(files)} .demo files in {rel(out)}. Point demo_path in your trainer config at that directory.")
    if warnings:
        print("Some tracks had teacher warnings (see above).")


# ---------------------------------------------------------------------------------------------- train

def mlagents_learn():
    exe = Path(sys.executable).parent / ("mlagents-learn.exe" if SYSTEM == "Windows" else "mlagents-learn")
    if not exe.exists():
        fail("mlagents-learn not found next to this Python. Set up the environment first "
             "(Tools/ml/setup_mlagents_env.sh, or setup_mlagents_env.cmd on Windows) and run nntools through its launcher.")
    return exe


def cmd_train(a, extra):
    args = [str(mlagents_learn()), a.config, "--run-id", a.run_id, "--results-dir", "results",
            "--time-scale", str(a.time_scale)]
    if a.resume:
        args.append("--resume")
    if a.force:
        args.append("--force")

    if a.mode != "editor":
        build = resolve_build(a.env)
        check_stale(build, a.kill_stale)
        num_envs = a.num_envs or (4 if a.mode == "headless" else 1)
        args += ["--env", str(build), "--num-envs", str(num_envs)]
        if a.mode == "headless":
            args.append("--no-graphics")
        else:
            args += ["--width", str(a.width), "--height", str(a.height), "--quality-level", "0"]

        base_port = 5005
        if "--base-port" in extra:
            base_port = int(extra[extra.index("--base-port") + 1])
        # A previous run that just finished can hold its ports for a minute or so while the system releases them
        for i in range(120):
            busy = ports_in_use(base_port, num_envs)
            if not busy:
                break
            if i % 15 == 0:
                print(f"Waiting for ports {busy} to be released (a previous run just ended?) ...")
            time.sleep(1)
        else:
            fail(f"Ports {busy} are in use. Another training run? Use --base-port N (after --).")
    else:
        print("Press Play in the Unity Editor when mlagents-learn says it is listening.")

    args += extra
    if a.scene and a.mode != "editor":
        args += ["--env-args", "-raceScene", a.scene]   # must come last: passed through to the game

    print(" ".join(args))
    os.chdir(PROJECT)   # demo_path etc. in configs are relative to the project
    sys.exit(subprocess.call(args))


# ---------------------------------------------------------------------------------------------- test

def run_graded_races(nn, model):
    """Run the graded races once; returns {race: (kph, wipeouts, score%, weight)}."""
    env = dict(os.environ)
    if model:
        env["NN_MODEL"] = str(model)
    results = PROJECT / "Builds" / "racing-test-results.xml"
    log = PROJECT / "Builds" / "racing-test.log"
    results.parent.mkdir(parents=True, exist_ok=True)
    if results.exists():
        results.unlink()
    print(f"Running the graded races ({'NN ' + rel(model) if nn and model else 'NN' if nn else 'FuzzyVehicle'}) ...")
    run_unity_batch(["-nographics", "-runTests", "-testPlatform", "PlayMode", "-testCategory", "NN" if nn else "FL",
                     "-testResults", str(results)], log, env)

    text = log.read_text(errors="replace") if log.exists() else ""
    if "error CS" in text:
        print("\n".join(l for l in text.splitlines() if "error CS" in l)[:3000])
        fail("Compile errors; fix them in the Editor first")
    races = {}
    for line in text.splitlines():
        m = re.match(r"\[RacingTest\] (?:NN|FL) (\S+): ([\d.]+) kph .*wipeouts (\d+) .*estimated score ([-\d.]+)% \(weight ([\d.]+)\)", line)
        if m:
            races[m.group(1)] = (float(m.group(2)), int(m.group(3)), float(m.group(4)), float(m.group(5)))
            print("  " + line[len("[RacingTest] "):].strip())
    xml = results.read_text(errors="replace") if results.exists() else ""
    failed = re.search(r'<test-run [^>]*failed="(\d+)"', xml)
    if failed and int(failed.group(1)) > 0:
        messages = re.findall(r"<message><!\[CDATA\[(.*?)\]\]>", xml, re.S)
        print("  Failed: " + "\n".join(m.strip() for m in messages[:4]))
    if not races:
        fail(f"No race results; see {rel(log)}")
    print(f"  Estimated weighted total: {sum(s * w for _, _, s, w in races.values()):.1f}%")
    return races


def checkpoints_near(run_id, steps):
    """The run's checkpoint .onnx nearest each step count (checkpoint names carry approximate step numbers)."""
    files = glob.glob(str(PROJECT / "results" / run_id / "*" / "*-*.onnx"))
    found = {}
    for f in files:
        m = re.search(r"-(\d+)\.onnx$", f)
        if m:
            found[int(m.group(1))] = f
    if not found:
        fail(f"No checkpoints in results/{run_id}")
    if not steps:
        top = max(found)
        steps = list(range(500000, top + 1, 500000)) or [top]
    picked = []
    for target in steps:
        best = min(found, key=lambda n: abs(n - target))
        if found[best] not in picked:
            picked.append(found[best])
    return picked


def cmd_test(a):
    if (a.model or a.run) and not a.nn:
        fail("--model and --run are for --nn")
    models = []
    if a.run:
        a.model = (a.model or []) + checkpoints_near(a.run, a.steps)
    for m in a.model or []:
        path = Path(m).resolve()
        if not path.exists():
            fail(f"Model not found: {m}")
        models.append(path)
    if not models:
        run_graded_races(a.nn, None)
        return

    table = [(m, run_graded_races(True, m)) for m in models]
    if len(table) > 1:
        races = sorted({r for _, rs in table for r in rs})
        print("\nSummary (kph / wipeouts; estimated weighted total)")
        for m, rs in table:
            cells = "  ".join(f"{r.replace('Race_', '')} {rs[r][0]:.1f}/{rs[r][1]}" for r in races if r in rs)
            total = sum(s * w for _, _, s, w in rs.values())
            print(f"  {rel(m)}: {cells}  -> {total:.1f}%")


# ---------------------------------------------------------------------------------------------- main

def main():
    parser = argparse.ArgumentParser(prog="nntools", description=__doc__.split("\n\n")[0],
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    b = sub.add_parser("build", help="build the training game (project must be closed in the Editor)")
    b.add_argument("--output", help=f"build path (default {rel(DEFAULT_BUILD)})")
    b.add_argument("--execute-method", default=STUDENT_BUILD_METHOD, help=argparse.SUPPRESS)

    r = sub.add_parser("record", help="record FuzzyVehicle demonstrations on all tracks (headless)")
    r.add_argument("--seconds", type=float, default=600, help="simulated seconds per track (default 600)")
    r.add_argument("--name", default="Racecar", help="file name prefix, letters/digits (default Racecar)")
    r.add_argument("--out", help="output directory (default Demonstrations/<name>)")
    r.add_argument("--scenes", default="all", help=f"'all' ({', '.join(SCENES)}) or a comma-separated list of scenes in the build")
    r.add_argument("--teacher-max-speed", type=float, default=0,
                   help="cap your controller's speed while recording, in kph (recommended if the report warns)")
    r.add_argument("--seed", type=int, default=0, help="random seed for resets and noise (0 = random)")
    r.add_argument("--append", action="store_true", help="add to existing files with the same name")
    r.add_argument("--window", action="store_true", help="show small windows instead of running headless")
    r.add_argument("--env", help=f"training build (default {rel(DEFAULT_BUILD)})")
    r.add_argument("--kill-stale", action="store_true", help="stop game instances left over from a crashed run")

    t = sub.add_parser("train", help="train with mlagents-learn",
                       epilog="Arguments after -- go to mlagents-learn, e.g. -- --initialize-from RUN --base-port 5105")
    t.add_argument("--config", default="Tools/ml/config/Racecar.yaml", help="trainer config")
    t.add_argument("--run-id", default="racecar", help="results go to results/<run-id>")
    t.add_argument("--mode", choices=["windowed", "headless", "editor"], default="windowed",
                   help="windowed: small windows you can watch; headless: fastest; editor: press Play in Unity")
    t.add_argument("--env", help=f"training build (default {rel(DEFAULT_BUILD)})")
    t.add_argument("--scene", help=f"track: {', '.join(SCENES)}, or 'auto' (parallel instances cover different tracks)")
    t.add_argument("--num-envs", type=int, help="parallel game instances (default 1 windowed, 4 headless)")
    t.add_argument("--time-scale", type=float, default=20)
    t.add_argument("--width", type=int, default=320)
    t.add_argument("--height", type=int, default=180)
    t.add_argument("--resume", action="store_true", help="continue an existing run-id")
    t.add_argument("--force", action="store_true", help="overwrite an existing run-id")
    t.add_argument("--kill-stale", action="store_true", help="stop game instances left over from a crashed run")

    e = sub.add_parser("test", help="run the graded races with your FuzzyVehicle or a trained NN (project closed in the Editor)")
    e.add_argument("--nn", action="store_true", help="test the NN (RacecarNNAgent prefab) instead of the FuzzyVehicle")
    e.add_argument("--model", nargs="+", help="NN model .onnx file(s) to test "
                   "(default: the model assigned to the RacecarNNAgent prefab)")
    e.add_argument("--run", help="test checkpoints of this training run (results/<run-id>): by default the ones "
                   "nearest every 500k steps")
    e.add_argument("--steps", type=int, nargs="+", help="with --run: test the checkpoints nearest these step counts")

    argv = sys.argv[1:]
    extra = []
    if "--" in argv:
        i = argv.index("--")
        argv, extra = argv[:i], argv[i + 1:]
    a = parser.parse_args(argv)
    os.chdir(PROJECT)
    if a.command == "build":
        cmd_build(a, extra)
    elif a.command == "record":
        cmd_record(a)
    elif a.command == "test":
        cmd_test(a)
    else:
        cmd_train(a, extra)


if __name__ == "__main__":
    main()
