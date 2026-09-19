# Verification notes

## v0.1.1

Three focused layout checks passed: a small screenshot expands to show the whole translation; a paragraph expands vertically; the panel remains within the monitor working area at 150% scaling with a negative monitor origin. These are WPF layout measurements, not a claim that every multi-monitor setup has been exercised.

## v0.1.0 baseline

30 automated checks passed for paragraph grouping, protocol request/response handling, malformed translation detection, cancellation, DPAPI round trips, local Chinese/English OCR, 20 consecutive OCR requests, worker release, idle exit and restart.

Windows UI checks covered the global capture hotkey, dragging a screenshot region, translated display, original/translated text comparison, translation-image export and the API test button. Translation responses for these checks were supplied by a local mock service; no claim is made about the quality or connectivity of any commercial model.

The local OCR fixture contains 13 lines on light and dark backgrounds, including Chinese, English, buttons, numbers and small text. A blank image returns no text.

Observed OCR timings on the development workstation: about 1 second for a cold request and about 0.6 seconds for a warm request. Idle private working set was about 85 MiB; shared-runtime-inclusive working set was about 247 MiB. These are example measurements, not hardware-independent limits. OCR adds temporary memory while loaded and is unloaded after inactivity.

## Reproduce

Build first using `powershell -ExecutionPolicy Bypass -File ./build.ps1 -Publish`.

Run `dotnet run --project src/ScreenLingo.Tests -c Release -- <project-root>` for the baseline checks, or append `--reader-layout` for the focused layout checks. Temporary fixtures and reports are generated under the ignored `work/qa` directory.

Real API providers, mixed-DPI multi-monitor desktop capture, protected video, exclusive full-screen applications and ARM devices require further testing. No private screenshots or user settings are included in this repository.
