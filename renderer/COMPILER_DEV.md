# Compiler development

Base: upstream development 0d0b266 (PR54 merged). Branch compiler_dev.

Patch 0030 changes only Keep going (GPU.compile_mode=2). It removes the short pipeline wait and legacy/menu/tessellation overrides, preserves pending preloads, and queues worker builds even when read-ahead helpers are disabled. Workers still build pipelines and subsequent draws install completed results. Required storage-buffer/image-writing draws remain ordered.

Shader translation and compute compilation can still wait. This change reduces compilation stalls; it does not eliminate every compilation wait. Startup precompilation is unchanged. Menus that draw a texture only once can show incomplete content until redrawn.

The local renderer build also retains the prior controller integration fix and stdout-only logging change. Patch 0030 is independent of those changes. Validation: full eight-library build passed, patch reverse application passed, worker queue concurrency/completion tests passed. Runtime validation: the tester reported substantially improved execution during compilation and confirmed the restored overlay works.

Patch 0031 restores the compile notice while background shader translation or driver graphics-pipeline compilation is active. It reuses the existing 150 ms delay, 300 ms grace period and moving bar; it does not hold guest clocks or change frame-generation policy. Translation activity uses a scoped atomic counter, including recoverable-failure exits.
