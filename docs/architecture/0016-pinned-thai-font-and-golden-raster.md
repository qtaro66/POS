# ADR 0016: Pinned Thai font and golden raster

- Status: Accepted
- Date: 2026-10-16
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13.4 and D21.2
- Extends: ADR 0014 and ADR 0015

## Decision

The repository vendors the unmodified Regular and Bold static TTF files from the official `NotoSansThaiLooped-v2.000` release. The font files retain the SIL Open Font License 1.1. The upstream release, source commit, locally computed source-archive hash and individual file hashes are recorded in the font manifest and third-party notice.

The Branch API copies the pinned font directory into build and publish output under `Assets/Fonts/NotoSansThaiLooped/2.000`. Deployment configuration continues to supply absolute Regular and Bold font paths so service configuration never depends on a drive letter, current working directory or an operating-system-installed font.

`SkiaThaiTextRasterizer` uses the pinned Regular typeface for normal lines and the pinned Bold typeface for bold lines. The legacy one-file constructor remains available and preserves synthesized-bold behavior for callers that have not yet migrated.

The safe test suite renders a fixed synthetic Thai fixture at 576 dots and threshold 180. It verifies font and license hashes, raster dimensions, black-pixel count and the packed one-bit raster SHA-256. The same test runs on Windows and Linux CI; a mismatch fails before hardware access. Changing the font bytes, shaping/raster dependencies, layout algorithm, fixture or threshold requires an intentional golden-hash review.

Local Windows and Linux-container verification found that SkiaSharp 3.119.4 produces the same 576-by-244 raster dimensions but not byte-identical one-bit output: the recorded Windows hash is `73D9AE75D84C881EFA862985D5E96EF223142F0FFA01251286F25FC136B6B068` with 11,102 black pixels, while the recorded Linux hash is `9E2942624F5AECC94D803DD4C02B75DEC4CBF5CC9A6FE59F8AB047B3A97CBDF9` with 10,935 black pixels. Separate platform baselines therefore detect regressions without falsely claiming payload equality.

## Safety and scope

The golden test performs no network operation and does not contact a printer or cash drawer. Passing it proves deterministic software output for the pinned environment; it does not prove paper output, physical density, cutter behavior, printer status or job-specific completion.

Byte-identical Windows/Linux payload equivalence remains unmet and is a Production-certification blocker. Physical comparison of both platform outputs on the TM-T82X remains a separate commissioning step; only that review can determine whether the observed 1.5% black-pixel difference is visually acceptable.

The font manifest is integrity and provenance metadata, not a complete release SBOM. The release pipeline must include the bundled font and its OFL license in the generated SBOM before public production release.
