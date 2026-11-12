# Engineering Principle

Always choose the solution that is best for the codebase. Do not default to a localized workaround when a more correct, more principled solution exists.

- Prefer the proper fix and validate it with targeted tests, builds, or headless checks.
- Treat risk as a reason to test carefully, not as a reason to choose a lesser fix.
- Mention simpler alternatives only for user awareness; implement the best-for-the-codebase approach unless the user explicitly chooses otherwise.
- **Do not factor in implementation time or effort.** Effort/time estimates are irrelevant to the decision.

## Project Objective

Maintain a Windows desktop editor for discovering the connected LED panel and editing its messages, effects, and playback queue.

- Verified model, transport, and protocol behavior are documented in `README.md` and `docs/protocol.md`. Never include a panel's previous message content or private captures in public documentation or fixtures.
- The supported panel uses JetFile II over Ethernet. Verify transport and protocol compatibility before extending support to other hardware.
- The application uses C# and WPF on .NET 10; protocol and device logic remain independent of WPF.

## Hardware Investigation and Evidence

- Identify unfamiliar hardware through read-only inspection. A friendly name, adapter VID/PID, or connector shape alone does not establish panel identity or transport.
- Distinguish observations, hypotheses, and verified behavior. Preserve captures privately with reproducible steps and SHA-256 hashes; keep machine-specific inventories and identifiers out of public commits.
- Do not send guessed commands to unidentified devices. Do not flash, erase, factory-reset, replace drivers, or change adapter/network configuration as part of identification.
- Before destructive device operations, establish a backup/recovery procedure and obtain explicit authorization for the concrete operation.
- Do not assume a connector pinout, electrical level, or power requirement without evidence.

## Application and Validation

- Separate device discovery, transport, protocol encoding/decoding, and UI concerns.
- Handle disconnected devices, reconnects, multiple matches, access failures, malformed responses, and timeouts explicitly. Never silently select an ambiguous target for writes.
- Validate message limits and encoding before transmission; bound retries and avoid duplicate writes when completion is uncertain.
- Use synthetic fixtures and targeted tests for protocol behavior, including malformed/truncated data. Keep hardware captures private. Keep hardware-dependent checks explicit and opt-in.
- Distinguish automated/local checks from actual panel acceptance. A successful transfer is not proof that the display changed; verify the visible message on hardware.
- Preserve unrelated workspace changes. Keep the UI concise and report actionable connection/upload status.

## Editor invariants

- `builds/desktop/LedPanel.Desktop.exe` is the WPF application (`net10.0-windows`). Source is
  in `src/LedPanel.Desktop/`; protocol and device logic are in `src/LedPanel.Core/`.
  Follow [C# conventions](docs/coding-standards.md). Test and formatting commands
  are in `README.md`.
- Never run concurrent hardware writers. Test fixtures stay in test projects and contain synthetic data only.
- Use request flag 0 for acknowledgments on this legacy board. See the physical
  evidence and protocol corrections in `docs/protocol.md`.
- Uploads wait for temporary-preview mode to finish before changing files: this
  firmware restores a cached playlist at preview expiry.
- Stage a new message, verify full readback, switch and verify the playlist, then
  confirm the intended file is playing. Never equate an ACK with visible success.
- For a queue, stage and verify every message before switching the playlist once.
  Preserve its order, enforce the 20-message bound, and verify playback of the
  first member. The playlist write starts playback on this board; do not append
  a 0601 replay command, which can skip the first message.
- Layout is a protocol setting: Line (`1b 30 61`) forces overflowing content into
  continuous horizontal playback; Pages (`1b 30 62`) preserves effects on wrapped
  content. Keep the preview and enabled controls consistent with these behaviors.
- Uploads and restoration use always-play scheduling. Restoration retains the
  backed-up text and effects; preserve the exact original playlist in the backup.
- Never overwrite `data/original/` beside the executable or the original panel message file. Keep
  interrupted transaction journals and allow recovery from a malformed playlist.
- Keep elevation in the separate adapter watchdog, not the editor. Cancel late
  helpers after a permission timeout and restore only settings this session owns.
- Panel writes remain explicit user actions. Connection
  monitoring only performs reads and session authentication. Only idempotent reads
  may retry automatically, with bounded attempts and fresh serials.

## Build, portability, and repository contents

- Build output belongs under root `builds/`, which is ignored by Git. This is a source-only repository; do not commit compiled binaries.
- The public repository contains the application source, assets, tests, documentation, example configuration, and required solution/build settings.
- Do not include agent scratch files, investigation tooling, raw captures, private profiles, original message content, or historical interface implementations.
- Review the actual Git index, history, and add candidates before sharing. The root and public media allowlists in `.gitignore` must only admit reviewed files.
- Do not publish original hardware label photos or embedded identifiers. Review image metadata and video/audio tracks, not just filenames or visible text. Keep demo media free of unnecessary audio and personal metadata.
- The published application resolves `panel.json` and `data/` beside its executable using `AppContext.BaseDirectory`. Never search parent folders or depend on a checkout path.
- Preserve local data across builds and moves. Do not overwrite `panel.json` or `data/original/`; only `panel.example.json` is included in build output.
- The Windows adapter PowerShell helpers are runtime dependencies. Keep them under Desktop's `Networking/Scripts/` and verify they are copied to output and publish folders.
- Verify a clean source checkout builds and publishes without local tooling. Run the test and formatting checks documented in `README.md`.

## Current Documentation (Context7)

Use Context7 MCP to fetch current documentation whenever working with a library, framework, SDK, API, CLI tool, or cloud service, including API syntax, configuration, migration, dependency-specific debugging, and setup. Prefer it over web search for library documentation, even when the API seems familiar.

Do not use it for general programming concepts, business-logic debugging, code review, refactoring, or scripts written from scratch unless dependency-specific documentation is needed.

1. Start with `resolve-library-id` using the library name and the task question, unless an exact `/org/project` library ID was supplied.
2. Select by exact name, relevance, source reputation, snippet coverage, and benchmark score. Retry alternate queries when results do not fit; use version-specific IDs when applicable.
3. Call `query-docs` with the selected ID and a complete question scoped to one concept. Query separate concepts separately unless their interaction is the subject.
4. Base implementation and explanations on the fetched documentation.
