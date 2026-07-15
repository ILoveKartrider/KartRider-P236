# Provenance notice

This project is distributed under the repository's Academic Free License 3.0
(AFL-3.0).

The RHO 1.0/1.1, BML, KRData and `aaa.pk` compatibility code in the `Internal`
directory is adapted from the following parts of
[`yanygm/Launcher_V2`](https://github.com/yanygm/Launcher_V2), which are
licensed under the Academic Free License 3.0 (AFL-3.0):

- `KartriderLibrary/File/Rho/*`
- `KartriderLibrary/Encrypt/RhoKey.cs` and `RhoEncrypt.cs`
- `KartriderLibrary/IO/Adler.cs` and its binary string helpers
- `KartriderLibrary/Xml/BinaryXml*.cs`
- `KartriderLibrary/Data/DataProcessor.cs`

The code was substantially reduced, bounds-checked and reorganized for the
single purpose of importing and transactionally updating the five Korean P236
item-probability BML entries. No game data, extracted tables or client assets
are included.

The upstream copyright and AFL-3.0 license terms remain applicable to the
adapted portions. See the repository-level `LICENSE.md` and
`THIRD_PARTY_NOTICES.md` when this project is distributed as part of the full
repository.
