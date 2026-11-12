# JetFile II protocol notes

Verified against a TFI-16X128-50RG with a 128 x 16 display, CPU `0x970A`, and
protocol version `0x2505`. These notes describe the supported protocol behavior;
they do not include a panel's saved message content or private packet captures.
Behavior on other controllers has not been established.

## Transport and framing

The application uses UDP port 3001, bound to the configured adapter address.
Responses must match the endpoint, request serial, and command, with valid lengths
and checksum. Only idempotent reads retry, with fresh serials and bounded waits.
A missing write acknowledgment leaves completion uncertain; it does not establish
that the operation failed.

| Offset | Bytes | Meaning |
| --- | --- | --- |
| 0 | 2 | Request `55 a7`, reply `55 a8` |
| 2 | 2 | Little-endian sum of every byte from offset 4 to end, modulo 65536 |
| 4 | 2 | Data length |
| 6 | 2 | Source address |
| 8 | 2 | Destination group/unit |
| 10 | 2 | Request serial, echoed in replies |
| 12 | 1 | Main command |
| 13 | 1 | Subcommand |
| 14 | 1 | Argument length in four-byte words |
| 15 | 1 | Legacy request 0=echo, 1=suppress status; response 0=data, 1=status |
| 16 | variable | Arguments, followed by data |

Replies on this board can echo the request address fields. The client accepts
exact echoed or reversed addresses, never arbitrary addresses. The request flag
must be 0 for acknowledgments on this firmware; newer documentation describes a
different convention. Successful status is `0x9000`.

## Supported operations

| Command | Purpose |
| --- | --- |
| `010a` | Hardware dimensions, CPU/protocol versions, and group/unit. |
| `010c` | Device identity, including MAC verification. |
| `010b` | Playback status: scheduled mode 0 or temporary mode 1. |
| `0102` | System file reads. |
| `0103` | Font directory and font file reads. |
| `0108` | Named message file reads. |
| `060d` | Currently playing file. |
| `0209` | Temporary message preview. |
| `0208` | Staged message file writes. |
| `0202` | Playlist write, restricted to `SEQUENT.SYS`. |

One documented default login is attempted when this known controller requires it.
Credentials are excluded from request capture files. Authentication does not replace
panel identity verification, and no credential changes are implemented.

## File transfers and playlists

System-file reads use a 12-byte NUL-padded filename, uint16 chunk size, and uint16
chunk index starting at 1. Named-file reads use chunk size/index followed by a
NUL-terminated path, padded to four bytes. Responses carry uint16 short size,
uint16 chunk index, uint32 large size, and the content. Transfers are bounded at
1 MiB with 512-byte chunks; inconsistent or incomplete reads are rejected.

Named-file write arguments contain uint32 total length, uint16 packet size, uint16
packet count, uint16 current packet, a NUL-terminated path, and four-byte padding.
Playlist writes use a 12-byte filename, uint32 total length, uint16 packet size,
count, and current index, followed by a reserved uint16. The packet data length
reflects the actual chunk size.

`SEQUENT.SYS` has an `SQ` signature, type 4, an eight-byte header, and 36-byte
entries. New message files use names such as `ED12345.Nmg` under `D:\T\`, leaving
room for a NUL in each entry's 12-byte filename field. The editor updates the
entry size/checksum, count, and always-play scheduling flag. Full byte readback
and SHA-256 are used for verification; a stored playlist checksum alone is not
accepted as proof of file integrity.

Every upload backs up the current queue, stages and verifies all new files,
checks that another writer has not changed the playlist, then switches it once.
The playlist write starts playback. Sending the documented `0601` replay command
afterwards was observed to advance to another message, so the editor omits it.
Completion verifies that the first uploaded file is playing.

Current-file replies have four leading zero bytes followed by the path, with
unreliable trailing bytes. Validation compares the expected path at offset 4
and requires scheduled mode. The schedule uses the documented always-play bit
`0x80`; inherited date/weekday restrictions can otherwise leave valid files idle.

## Messages and display controls

Supported NMG messages use the `01 5a 30 30 02 41 0f` wrapper, style controls,
a single printable ASCII text run, `0d 04`, and a NoteNmg metadata trailer.
Unsupported rich or multiline text formats are rejected explicitly.

| Control | Meaning |
| --- | --- |
| `1b 30 61` | Line mode, no automatic wrapping. |
| `1b 30 62` | Pages mode, wrapping and automatic typesetting. |
| `1a "N"` | Bold English font, 14 pixels high. |
| `1c "1"` | Red text. |
| `0a "I1"`, `0a "O1"` | Move Left entry and exit. |
| `0e "20000"` | Zero-second pause. |
| `07 31` | Flashing enabled. |

Overflowing Line text forces continuous horizontal movement and overrides
alignment, flashing, pause, and exit effects. Pages mode preserves those controls
on wrapped text. Short text can use alignment and hold effects in either mode.
The editor disables controls the firmware would ignore for an overflowing line.

Temporary command `0209` has four argument bytes: uint16 lifetime in seconds,
a sound byte, and a reserved zero. The file is supplied as packet data. The editor
requests 30 seconds with sound disabled. On expiry, the firmware restores a cached
playlist, so permanent writes wait for scheduled mode before changing files.

The startup beep does not establish sound support over the protocol. Sound requests
were not audibly confirmed, and `060f` returned unsupported-subcommand status on
this controller. Sound is not an editor feature.

## Fonts and simulation

`0103` reads `FONTLIST.LST` and the font files it names. The directory begins with
`FL`, a little-endian uint16 count, and 28-byte entries: filename[12], control code
u32, file size u32, width/height u16, and width/height/stride/reserved u8. Duplicate,
inconsistent, and oversized entries are rejected.

Glyphs are indexed by ASCII code with row-major bytes, most significant bit first.
A 64-byte trailer begins with `04 FONT`; width, height, and stride are u32 values
at trailer offsets 16, 20, and 24. The decoder validates dimensions and retains
printable ASCII glyphs. Font writes are not implemented.

The local cache stores the panel MAC, raw font bytes, and hashes. Missing or corrupt
caches are refreshed from the verified panel. Font acquisition failures disable
the simulation while leaving message editing and recovery available.

The preview uses fixed glyph cells and centered vertical placement. Movement timing
was calibrated against physical observations, but some timing, transitions, and
page wrapping remain approximations. Synthetic fixtures verify rasterized pages,
transition durations, and rendered frame hashes independently of live hardware.

## References

- [JetFile II v2.8.7 manufacturer specification](https://github.com/johnoneil/LEDSign/blob/master/doc/JetFileIIv2.8.7(EN).pdf): framing, reads, message controls, and playlist layout.
- [JetFile II v2.3 protocol](https://www.london-electronics.com/pdf/jetfile_protocol.pdf).

The protocol implementation is independent. Third-party application code and
private investigation captures are not distributed with this repository.
