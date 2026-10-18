# Logging

The launcher list box selects the minimum severity written by the renderer to
`stdout.log`: Trace, Debug, Info, Warning, Error, or Critical. Warning is the
default; it includes Warning, Error, and Critical. A missing or invalid setting
also uses Warning. Changes take effect on the next game launch.

The selection is stored as `stdout_level=Warning` in `RyGT.ini`. The renderer
reads this file from the game's working directory, which the launcher sets to
the package directory. Other launchers should use that working directory too.

The shared renderer backend applies the threshold before writing to stdout.
Its existing internal log files keep their independent class filters. Raw
stdout messages without a severity are retained. `stderr.log` and `setup.log`
are never severity-filtered.

Active logs are in the package's `logs` directory:

- `stdout.log` and `stderr.log` belong to a game session.
- `setup.log` belongs to a setup operation.

Before opening a log for a new session or setup operation, the previous file is
archived with a UTC timestamp, for example
`stdout.log.20261018T223045123Z`. A numeric suffix prevents timestamp collisions
from replacing an archive. Legacy logs in the package root are migrated into
`logs` with a `legacy` marker. Archives are retained until manually removed.
The runtime preserves inherited Windows standard handles, including debugger
or terminal redirection, as before.

Rotation uses standard C++ filesystem/chrono APIs in `LogFiles.hpp`; the
launcher uses the equivalent managed `System.IO` APIs. No Windows API is added
to shared logging code. The existing detached Windows stream adapter remains
in `CrashReport.cpp`; non-Windows standard streams use the same portable file
helper.

## Building and checking

Rebuild the launcher and libc. The renderer is a separate build: apply
`renderer/shadGT-patches/0019-leveled-stdout-logging.patch` to shadGT `ryty-gpu`
at `afc9e36cdcf1601336776147893581650410569e` (which already contains patches
0001 through 0018), then rebuild `ryty_gpu`. An older prebuilt renderer does
not implement `stdout_level`.

With libc tests enabled, `ctest -R '^log_files$' --output-on-failure` checks
severity parsing, the threshold matrix, and lossless log rotation. The
standalone launcher test can be compiled with the .NET Framework C# compiler:

```bat
csc /target:exe /out:LoggingTests.exe loader\Logging.cs loader\LoggingTests.cs
LoggingTests.exe
```

Place the freshly built `RyGT.exe` beside `LoggingTests.exe`; the test also
checks persistence against the actual launcher's settings class.

`renderer/stdout_level.h` is the testable copy of the header carried by patch
0019. Keep both copies identical when changing the severity policy.
