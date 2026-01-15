Design Note – Trade Editing in Blotter (OptionSuite v2)
1. Scope and Goal
This note defines how users edit trades from the Blotter in OptionSuite v2.
In scope:
•	Inline editing of two operational fields directly in the grid.
•	Full edit flow via double-click/context menu → “Edit trade…” in a dedicated window.
•	Duplicate trade flow via context menu → “Duplicate…”.
•	Required UX and technical guards so editing remains stable under polling/refresh and with multiple UI clients.
Out of scope:
•	Booking/export/import and any booking status chain.
•	Routing engine design beyond what is required to store edited values.
•	Shell/module integration.

2. Two Complementary Edit Modes
2.1 Inline Edit in the Blotter (Restricted, Operational)
Intent
Provide fast edits for two high-value fields without forcing a full deal window.
Allowed inline fields (v2)
•	Portfolio MX3: determines booking destination for MX3.
•	Book Calypso: determines booking destination / inclusion for Calypso in the same sense (i.e., “should this trade be routed/booked to Calypso”), not a cosmetic flag.
Everything else is read-only in the grid.
Eligibility rules
Inline edit is enabled only when:
•	Trade is in an editable lifecycle state (your existing CanEdit logic).
•	Trade and relevant system link are not soft-deleted.
•	Row is not currently in a conflicting state (e.g., an edit already in progress, or row locked by another process if you add such a concept later).
UX
•	Edits happen in-place in the cell (TextBox / Checkbox).
•	Commit on:
o	Enter / LostFocus for text
o	click toggle for checkbox
•	Escape cancels and reverts.
•	Clear, immediate feedback:
o	“Saving…” indicator per row or cell
o	failure message and revert on conflict/validation error
Write discipline
Inline edits update only the two specific values. No other side effects.
•	If the values are stored on TradeSystemLink, update the corresponding link row(s).
•	If you have a trade-level fallback field (e.g. Trade.PortfolioMx3) used prior to link creation, update that only if link is absent by design.
2.2 Full Edit via Dedicated Deal Window (“Edit trade…”)
Trigger
•	Double-click a row, or
•	context menu → Edit trade…
Intent
Full editing requires:
•	more fields than the grid shows
•	validation across multiple fields
•	a controlled Save/Cancel lifecycle
•	avoiding instability with polling
Characteristics
•	Opens a separate “deal window” bound to a dedicated Edit ViewModel.
•	Loads the full trade record (and relevant system link data).
•	Save is explicit; Cancel discards changes.
When to require the deal window
•	Any field other than the two inline fields.
•	Any edit requiring multi-field validation or domain logic.

3. Duplicate Trade Flow
Trigger
•	context menu → Duplicate…
Intent
Create a new trade based on a historical one while:
•	ensuring new identity (new StpTradeId, new audit trail)
•	defaulting lifecycle to editable state (typically “New”)
•	keeping the blotter grid stable
UX options
•	Option A (recommended): Duplicate opens the deal window in “Create from template” mode.
•	Option B: Duplicate immediately creates a new trade and opens it for edit.
Recommendation: Option A (window first) to avoid creating garbage trades when user cancels.
4. Polling/Refresh Interaction with Editing (Non-Negotiable Contracts)

The Blotter is polling-based. Editing must not degrade stability.
4.1 Selection must be stable
•	Refresh must preserve selection (by StpTradeId) whenever possible.
•	If selected trade disappears, selection clears and detail panel is reset.
4.2 Refresh must not overwrite an active inline edit
Minimum requirement:
•	While user is editing a cell, refresh must not replace the row object or clear editor state.
Implementation options:
•	Gate refresh with IsUserInteracting (you already implemented the interaction timer).
•	OR: Keep the row objects stable and update only properties (harder but best long-term).
Given your current architecture: use IsUserInteracting as the first-line gate.
4.3 Refresh must not race Save
•	While a save is in progress for a row (inline edit or deal window), refresh should either:
o	skip, or
o	allow refresh but not replace the row being saved
Keep it simple: skip refresh when IsBusy/saving.

5. Concurrency and Multi-Client Safety (Edit Path)
Even if the UI prevents double-click spam, two different UI clients can edit the same trade.
5.1 DB concurrency contract
All edit writes should be concurrency-checked using one of these:
•	WHERE LastUpdatedUtc = @expected (optimistic concurrency)
•	or RowVersion equivalent if you introduce it
If update affects 0 rows:
•	treat as conflict
•	reload from DB
•	show a clear conflict message: “Trade updated by another user; your changes were not applied.”
5.2 UI double-submit guard
For inline edit:
•	Disable editor / commit button while save is running.
•	If the same value is re-entered, no write.
For deal window:
•	Disable Save while saving
•	prevent multiple Save calls concurrently

6. Audit Requirements for Editing
Edits must be auditable, but not noisy.
Minimum audit events for edit flows:
•	TradeEdited (for deal window save)
•	TradeInlineEdited (for inline edit; include which field(s) changed)
•	TradeDuplicated (when a duplicate is created)
Details should include:
•	user id
•	fields changed (old → new, at least for the two inline fields)
•	timestamp
(Implementation can be via TradeWorkflowEvent if that’s your canonical audit table.)

7. Recommended Implementation Plan (No Spaghetti)
Step E1 – Inline edit of Portfolio MX3 + Book Calypso
•	Make those two columns editable in the DataGrid.
•	Implement commit → async save → reload row state.
•	Respect polling gates (IsUserInteracting, IsBusy) so editors don’t get destroyed.
Step E2 – Context menu + double-click routing
•	Add “Edit trade…” and “Duplicate…” actions.
•	Hook to placeholder window first (no domain edits yet) to validate wiring.
Step E3 – Deal window skeleton
•	Load full trade + links
•	Present fields read-only initially
•	Save/Cancel scaffolding with concurrency guard
Step E4 – Enable real edits + audit
•	Enable saving, validate, and write audit events.

