# ADR 0015: Branch ESC/POS raster renderer composition

- Status: Accepted
- Date: 2026-10-16
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13 and D17
- Extends: ADR 0013 and ADR 0014

## Decision

The Branch API owns the runtime composition for the Unicode ESC/POS raster renderer. Configuration section `EscPosRasterRendering` supplies an absolute Thai-capable `.ttf` or `.otf` font path and monochrome threshold. The threshold defaults to the provisional TM-T82X value of 180 and must remain between 1 and 254.

Print spool polling remains disabled by default. While it is disabled, the Branch API starts without a configured font and does not construct the native rasterizer. When polling is enabled, startup validation requires the configured font file to exist and be readable before any hosted print lane can run. Resolving the renderer then loads the font through SkiaSharp; an invalid font still fails before a transport attempt.

The rasterizer and `IPrintPayloadRenderer` are singleton services so the immutable font configuration and native typeface are shared for the process lifetime and disposed with the service provider.

## Safety

This composition does not register a RAW TCP adapter, printer-condition probe, persisted spool-cycle processor, or any other network transport. Automated tests validate configuration and DI descriptors only. They do not open a socket, print paper, or operate a cash drawer.

## Deliberately outside this slice

- choosing and licensing the production Thai font asset
- registering printer condition/status probes and RAW TCP transport
- enabling `PrintSpoolPolling` in deployment configuration
- physical printer verification or automatic `Completed` status
