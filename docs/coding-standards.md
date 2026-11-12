# C# conventions

These conventions apply to all four projects in this repository.

- UTF-8, LF, tabs at four columns, Allman braces, and a 200-column C# maximum.
  Use braces for control-flow bodies. Keep declarations and calls on one line
  when they fit; break longer expressions at logical boundaries.
- File-scoped namespaces match the project-relative folder path. Organize by
  feature/responsibility; interfaces and enums stay with their owning feature.
  System usings first, then separate project imports.
  Remove unused directives. The configured formatter owns whitespace.
- PascalCase types, members, and constants; camelCase parameters and locals;
  `_camelCase` private instance fields and `s_camelCase` private static fields.
  Use one top-level type per C# file, with its filename matching the type. Private
  nested helper types may stay with their sole owning type. XAML and code-behind
  stay together; application entry points may remain at the project root.
  Async methods use an Async suffix.
- Use explicit accessibility and descriptive names. Prefer explicit types when
  the initializer does not make the type apparent. Enable nullable references;
  do not use `dynamic` or null-forgiving operators to bypass invariants.
- Keep mutable state owned, pass dependencies through constructors, and separate
  transport, protocol, transactions, presentation models, and WPF rendering.
  Shared limits have one owner. Do not add speculative infrastructure.
- Async I/O must not block the dispatcher. Every task has an owner, cancellation,
  and observed failures. Reserve async void for framework event signatures.
  Window closure cancels and awaits active work before adapter cleanup.
- Document public authored APIs with XML comments. Explain nonobvious invariants
  and ownership. Generated code and public xUnit entry points alone are exempt
  from CS1591. Compiler and analyzer warnings are errors; do not weaken checks.
- Validate external inputs. Catch at boundaries that can recover, translate, or
  report failures. Keep credentials out of logs and packet captures.
- Tests use pinned xUnit v3 packages, descriptive test names, one `Tier` trait
  per test and at least one `Area` trait. Core checks are Fast; desktop checks
  exercise WPF rendering and presentation. Use per-test temporary directories,
  controlled clocks, and independent cases. Test fixtures stay in test projects.
- Preserve dependency lock files. Do not modify generated build output or depend
  on another project checkout at runtime. Report compilation, automated parity,
  rendered UI, protocol readback, and physical panel acceptance separately.

Run the test and formatting checks in the root `README.md`.
Build output belongs under `builds/` and is excluded from Git. The application
keeps its private profile and data beside its executable.
Runtime adapter scripts belong in `src/LedPanel.Desktop/Networking/Scripts/`. The SDK can be newer than .NET 10; the
application and core target .NET 10 with C# 14.
