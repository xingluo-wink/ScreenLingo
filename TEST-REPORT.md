# Verification notes

## v0.2.0

25 reader checks and 21 core/protocol checks passed. The reader scenarios create real WPF windows with fixed OCR data and an injected local HTTP handler. They cover native caption/resize configuration, manual placement retention, selectable cross-paragraph text, selection and scroll retention, single/dual-language caching, duplicate clicks, cancellation with partial-result retry, stale-response rejection, close during translation, original-layout overflow fallback, long-document export, and responsive bilingual layout. Stacked, two-column and font-40 renders were inspected.

The reader windows in these checks are transparent and controlled by the test harness; these checks do not claim a new physical mouse-drag or mixed-monitor desktop interaction test. The negative-origin/mixed-DPI placement case is a geometry test. Commercial APIs were not contacted. The OCR engine and models are unchanged from the baseline below.

Run `dotnet run --project src/ScreenLingo.Tests -c Release -- <project-root> --reader-layout` for reader scenarios and `--core-only` for core/protocol checks. Test responses and fixtures contain no user screenshots or credentials.

## v0.1.2

13 focused checks passed for reader sizing, complete side-by-side Chinese/English content, font enlargement, scrollbar-free display of short text, full-height image export, bilingual copying, missing-language placeholders, settings migration and font bounds. A 32-size bilingual image was rendered and inspected. These checks used fixed sample text and no external API calls. The new controls and real-provider translation are left for user testing.

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
