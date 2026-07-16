# Legal and content policy

This is a source-code interoperability project. It is not affiliated with or
endorsed by Nexon or the upstream game publisher, and it does not grant rights
to any game client, artwork, music, data archive, trademark, or online service.

The repository intentionally contains none of the following:

- a KartRider client executable or modified client;
- PIN, RHO/RHO5, BML, model, texture, replay, or other game data;
- raw or internal analysis artifacts such as packet captures, memory dumps,
  disassembler databases, decompiler/disassembler output, or debugger projects;
- user profiles, server logs, credentials, or locally generated account data.

Users must supply only software and data they are legally entitled to use.
The connector, item-probability editor, and L1 data patcher operate only on
user-selected local installations and do not download or distribute a client.
The probability editor modifies `item.rho` and adjacent `aaa.pk` metadata only
after an explicit apply action and maintains recovery data for the two-file
update. The L1 patcher reads a user-supplied donor installation, creates the
P236-compatible archives locally, updates only the target metadata, and keeps a
transactional original-file backup. Before mutating the target, it verifies all
36 required donor files against SHA-256 fingerprints for the exact supported
donor revision; byte-different repacks or modified files are rejected. Its
executable contains only transformation rules, validation logic, and those
fingerprints—not donor RHO/BML files, extracted assets, binary deltas, or
generated archive or asset payloads. Do not publish client files, extracted
assets, generated probability tables, generated archives, or private user data
in issues or pull requests.

This document is a project policy, not legal advice. Laws and license terms vary
by jurisdiction; distributors remain responsible for reviewing their own use.
