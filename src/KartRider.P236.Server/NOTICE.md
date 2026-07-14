# Server source provenance

The P236 server in this directory combines new P236 compatibility behavior
with modified and reorganized protocol, packet, and networking implementation
based in part on `yanygm/Launcher_V2`. It does not contain the original game
client, extracted game assets, or raw internal artifacts such as packet
captures, dumps, decompiler/disassembler output, or debugger project files.

The small protocol primitives under `Protocol/` are cleaned-up adaptations of
networking algorithms published by
[`yanygm/Launcher_V2`](https://github.com/yanygm/Launcher_V2), licensed under
the Academic Free License 3.0. They have been rewritten for .NET 8, bounded
packet parsing, async shutdown, and testability. This is a prominent notice
that those portions were modified; the repository-level license and notices
apply.
