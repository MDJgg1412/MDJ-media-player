#!/usr/bin/env python3
"""MDJ AI stem separation runner using Demucs Python API."""

from __future__ import annotations

import argparse
import importlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import traceback
from typing import Any, Tuple


def run_pip_install(package_name: str) -> None:
    command = [sys.executable, "-m", "pip", "install", "--upgrade", package_name]
    result = subprocess.run(command, capture_output=True, text=True)
    if result.returncode == 0:
        return

    details = (result.stderr or result.stdout or "").strip()
    raise RuntimeError(f"Failed to install dependency '{package_name}': {details}")


def ensure_module(module_name: str, package_name: str, auto_install: bool) -> Any:
    try:
        return importlib.import_module(module_name)
    except ModuleNotFoundError:
        if not auto_install:
            raise

        run_pip_install(package_name)
        return importlib.import_module(module_name)


def load_audio(input_path: str, soundfile_module: Any) -> Tuple[Any, int]:
    try:
        wav, sample_rate = soundfile_module.read(input_path, dtype="float32", always_2d=True)
        return wav.T, int(sample_rate)
    except Exception as first_error:
        ffmpeg = shutil.which("ffmpeg")
        if not ffmpeg:
            raise RuntimeError(
                "Could not decode input with soundfile and ffmpeg was not found in PATH."
            ) from first_error

        temp_directory = tempfile.mkdtemp(prefix="mdj-demucs-ffmpeg-")
        temp_wav = os.path.join(temp_directory, "input.wav")

        try:
            command = [ffmpeg, "-y", "-i", input_path, "-vn", "-ac", "2", "-ar", "44100", temp_wav]
            completed = subprocess.run(command, capture_output=True, text=True)
            if completed.returncode != 0:
                details = (completed.stderr or completed.stdout or "").strip()
                raise RuntimeError(f"ffmpeg conversion failed: {details}")

            wav, sample_rate = soundfile_module.read(temp_wav, dtype="float32", always_2d=True)
            return wav.T, int(sample_rate)
        finally:
            shutil.rmtree(temp_directory, ignore_errors=True)


def separate_stems(input_path: str, output_directory: str, model_name: str, auto_install: bool) -> dict[str, Any]:
    torch = ensure_module("torch", "torch", auto_install)
    soundfile = ensure_module("soundfile", "soundfile", auto_install)
    demucs_pretrained = ensure_module("demucs.pretrained", "demucs", auto_install)
    demucs_apply = ensure_module("demucs.apply", "demucs", auto_install)

    wav_np, sample_rate = load_audio(input_path, soundfile)
    wav = torch.from_numpy(wav_np).contiguous()

    get_model = getattr(demucs_pretrained, "get_model")
    apply_model = getattr(demucs_apply, "apply_model")

    model = get_model(model_name)
    model.cpu()

    if sample_rate != model.samplerate:
        torchaudio = ensure_module("torchaudio", "torchaudio", auto_install)
        wav = torchaudio.functional.resample(wav, sample_rate, model.samplerate)

    sources = apply_model(model, wav[None], device="cpu", progress=False, split=True)[0]

    mapping = {
        "vocals": "vocal",
        "other": "intrumental",
        "bass": "bass",
        "drums": "drums",
    }

    os.makedirs(output_directory, exist_ok=True)

    for index, source_name in enumerate(model.sources):
        output_name = mapping.get(source_name)
        if output_name is None:
            continue

        output_path = os.path.join(output_directory, f"{output_name}.wav")
        source_wav = sources[index].detach().cpu().T.numpy()
        soundfile.write(output_path, source_wav, model.samplerate, subtype="PCM_16")

    required = ("vocal.wav", "intrumental.wav", "bass.wav", "drums.wav")
    missing = [name for name in required if not os.path.exists(os.path.join(output_directory, name))]
    if missing:
        raise RuntimeError("Missing stem outputs: " + ", ".join(missing))

    return {
        "ok": True,
        "model": model_name,
        "output_dir": output_directory,
        "sources": list(model.sources),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Run Demucs source separation for MDJ.")
    parser.add_argument("--input", required=True, help="Input media file path.")
    parser.add_argument("--output", required=True, help="Output folder for generated stems.")
    parser.add_argument("--model", default="htdemucs", help="Demucs model name.")
    parser.add_argument(
        "--auto-install",
        action="store_true",
        help="Attempt pip install for missing python dependencies.",
    )
    args = parser.parse_args()

    try:
        result = separate_stems(args.input, args.output, args.model, args.auto_install)
        print(json.dumps(result, ensure_ascii=True))
        return 0
    except Exception as exc:
        payload = {
            "ok": False,
            "error": str(exc),
            "trace": traceback.format_exc(limit=4),
        }
        print(json.dumps(payload, ensure_ascii=True), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
