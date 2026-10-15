# ADR 0014: ESC/POS Unicode raster renderer

- Status: Accepted
- Date: 2026-10-15
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13
- Extends: ADR 0010, ADR 0012, and ADR 0013

## Decision

RAW TCP ESC/POS printers receive an immutable monochrome bitmap payload. Bill Preview and Kitchen Order JSON schema version 1 are parsed with case-sensitive property names and unknown properties are rejected. The renderer verifies the source SHA-256 before layout, shapes Unicode text with HarfBuzz, rasterizes with SkiaSharp, and encodes the result with ESC/POS `GS v 0` commands.

Thai is never sent as a printer code page. Deployment must provide an explicit, licensed Thai-capable TrueType or OpenType font file. Startup composition will pass that path to `SkiaThaiTextRasterizer`; missing files or missing glyphs fail before any transport attempt. The font file and its version therefore form part of the operational printer-rendering configuration even though the rendered bytes remain the durable retry artifact defined by ADR 0012.

The current renderer uses a monochrome threshold of 180, matching the provisional TM-T82X commissioning result. The threshold remains renderer configuration and must be revalidated when the printer, paper, font, or density setting changes.

## Document rules

- Bill Preview is labelled `ใบตรวจสอบรายการ` and `ยังไม่ใช่ใบเสร็จ`; it has no receipt number and includes fulfillment, options, comments, discounts, charges, rounding, net total, and VAT information from the immutable snapshot.
- Kitchen Order prints the original table, production station, order batch, fulfillment, quantity, options, and comments from the immutable Production Order snapshot.
- The payload initializes the printer, emits one or more bounded raster chunks, feeds paper, and optionally applies the cutter command from the assigned printer profile.
- The renderer never emits a cash-drawer pulse. Drawer operations remain a separate authorized command path.
- Native Thai code pages and native-text rendering remain experimental and are rejected by this renderer.

## Safety and determinism

Rendering does not open a network socket and does not access SQLite. Automated tests use an in-memory rasterizer to verify semantic layout and exact ESC/POS framing; a native Skia smoke test only writes an in-memory bitmap. No automated test prints paper or opens a cash drawer.

The same source JSON, printer profile, font file, threshold, and renderer version must produce the same persisted byte payload. Retries reuse those persisted bytes and do not render again.

## Deliberately outside this slice

- committing a font asset and its third-party license to the repository
- Branch API dependency-injection and font-path configuration
- RAW TCP transport, condition probing, and physical-printer verification
- receipt, cancellation, transfer, reprint, and cash-drawer document renderers
- visual golden-image approval for the final production font and paper
