# Security policy

Please report security problems privately to the repository maintainer before
opening a public issue. Include the smallest source-only reproduction possible.

Never attach game clients, PIN/RHO files, packet captures, crash dumps, access
tokens, usernames, persistent profile databases, or server logs containing
personal information. Redact local paths, IP addresses, and account identifiers.

The connector edits files in a user-selected client directory. Inspecting a
process started at a higher integrity level can require the user to restart the
connector with matching elevation. Its validation and transactional backup
checks are security boundaries; changes that weaken them require an explicit
threat-model explanation and tests.

The server launcher's item-probability editor writes a user-selected
`item.rho`/`aaa.pk` pair. Close every client that shares the selected `Data`
directory before applying a configuration. Archive identity, table membership,
JSON bounds, exclusive file access and the two-file recovery transaction are
security boundaries; do not bypass them to accept an unsupported client or a
partially written archive.

The compatibility server intentionally defaults to loopback. The legacy game
protocol does not provide modern authentication, confidentiality, or robust
identity binding. Do not expose the server directly to the public Internet;
use it only on the local machine or a trusted isolated network.
