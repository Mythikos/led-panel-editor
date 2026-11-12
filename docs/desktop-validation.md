# Validation

The solution targets .NET 10 and C# 14. Tests run on Windows and use synthetic
messages, fonts, simulated panel responses, and local loopback sockets. They do
not require or contact the physical panel.

## Automated coverage

- Framing, checksums, malformed/truncated data, request matching, and read-only retries.
- Credential capture exclusion and bounded file transfers.
- Staging failures, uncertain writes, concurrent playlist changes, first-file
  verification, original-backup recovery, reconnects, and malformed font caches.
- 192 message configurations and 1,344 rendered frame comparisons from synthetic
  reference data included in the test project.
- Selected-message previews, queue editing, command gating, draft retention,
  reload confirmation, cancellation, and adapter cleanup on window closure.
- Native WPF bindings and rendering at 1080 x 980 and 760 x 650.
- Profile lookup beside the executable, moving application data with the app,
  and inclusion of the example profile and required adapter helpers in output.

Compiler/analyzer warnings are errors. Formatting is checked with `dotnet format
--verify-no-changes`. The README contains the test commands.

## Hardware acceptance

The physical panel has been used to verify temporary previews, permanent queue
uploads, first-message playback, colors, fonts, alignment, flashing, movement,
pauses, and original-message restoration. Permanent playback was confirmed after
a power cycle. Adapter cleanup was observed to restore DHCP, remove the session's
temporary address, and leave other networking unchanged.

These observations establish behavior on the documented TFI-16X128-50RG. They do
not establish support for other controllers or exact simulation of every effect.
Source layout and deployment changes are checked locally; they do not imply a new
physical upload or power-cycle test. No private message content or capture files
are included as test evidence.

## Source-only build and portable output

The source layout was checked using an independent copy containing only the
files eligible for Git at the time of that check. Build, publish, and formatting
checks passed with no warnings, along with 421 Core tests and 10 Desktop tests. No investigation tools,
private profiles, captures, or compiled files were needed in that source copy.

The clean published app contained the setup template and runtime adapter helpers,
with no actual profile or runtime data. After copying it outside the checkout and
supplying a synthetic profile, it opened from an unrelated working directory,
created `data/` beside itself, and closed normally. No physical panel commands
were sent during this portability check.
