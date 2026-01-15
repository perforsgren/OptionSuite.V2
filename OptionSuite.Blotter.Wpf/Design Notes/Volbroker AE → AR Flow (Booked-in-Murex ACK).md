Volbroker AE → AR Flow (Booked-in-Murex ACK)
Status
Approved / Locked
________________________________________
1. Purpose
This document defines the structural design and invariants for handling inbound AE (TradeCaptureReport) messages from Volbroker and outbound AR (TradeCaptureReportAck) messages sent back to Volbroker.
The design ensures that:
•	AR is only sent once the trade is booked internally (e.g. in Murex).
•	AR always contains a stable internal booking identifier.
•	The flow is deterministic, restart-safe, and operationally transparent.
•	Responsibility between core logic and FIX gateway is clearly separated.
This document intentionally avoids FIX field-level details and focuses on state, ownership, and flow control.
________________________________________
2. High-Level Flow Overview
1.	Volbroker sends an AE.
2.	AE is imported and normalized into the hub.
3.	Trade is booked internally (e.g. Murex).
4.	Once booking is complete, the system signals readiness to send AR.
5.	The FIX gateway sends AR using polling.
6.	The final outcome is persisted and visible for operations.
AR semantics are explicitly “booked internally and accepted”, not “received” or “preliminary”.
________________________________________
3. Core Design Principle
Outbound AR is driven by state, not by code paths or direct calls.
All outbound behavior is derived from persisted state.
The database is the single source of truth.
________________________________________
4. VOLBROKER_STP State Model
The following state machine is the authoritative contract between core logic and FxFixGateway:
New
  → ReadyToAck
       → AckSent
       → AckFailed
       → Rejected
State Semantics
New
•	AE has been received and imported.
•	Trade exists in the hub.
•	Trade is not yet booked internally.
•	AR must not be sent.
This is an inbox/pre-booking state.
________________________________________
ReadyToAck
•	Trade is fully booked internally (e.g. in Murex).
•	Internal booking identifier exists.
•	All prerequisites for constructing AR are satisfied.
•	AR is allowed and expected to be sent.
This state is the hard outbound gate.
________________________________________
AckSent
•	AR has been successfully sent to Volbroker.
•	This is a terminal state for the VOLBROKER_STP flow.
________________________________________
AckFailed
•	AR could not be sent due to transport or gateway failure.
•	The trade remains eligible for retry.
•	No business or data validation errors are represented by this state.
________________________________________
Rejected
•	The trade has been rejected as a business decision.
•	AR reject has been sent.
•	This is a terminal state.
________________________________________
5. State Transition Rules (Invariants)
I1. ReadyToAck is the only send-trigger
FxFixGateway may only send AR for trades in:
•	ReadyToAck
•	AckFailed (retry)
It must never send from New.
________________________________________
I2. ReadyToAck implies full AR constructability
If a trade is in ReadyToAck, AR must be constructable without:
•	additional data fetching,
•	conditional logic,
•	or fallback behavior.
Failures due to missing data indicate a design violation.
________________________________________
I3. AckFailed represents transport failure only
AckFailed must only be used for:
•	FIX session issues,
•	connectivity problems,
•	send-level errors.
It must never be used for validation or business logic failures.
________________________________________
I4. Terminal states
•	AckSent and Rejected are terminal.
•	No transitions are allowed out of these states.
________________________________________
6. Ownership and Responsibility
Core / Orchestrator
•	Owns inbound AE processing.
•	Creates and updates Trade.
•	Sets VOLBROKER_STP to:
o	New when AE is imported,
o	ReadyToAck when internal booking is complete.
•	Must not set AckSent or AckFailed.
________________________________________
FxFixGateway
•	Owns outbound AR transmission.
•	Polls for trades in ReadyToAck or AckFailed.
•	Sends AR.
•	Sets:
o	AckSent on success,
o	AckFailed on transport failure.
•	Must not set New or ReadyToAck.
________________________________________
7. Outbound Trigger Mechanism
Outbound AR transmission is driven by polling.
FxFixGateway periodically queries the database for trades with:
•	VOLBROKER_STP status = ReadyToAck or AckFailed.
This approach is chosen to ensure:
•	restart safety,
•	decoupling between core and gateway,
•	deterministic behavior,
•	operational simplicity.
No direct API calls from core to gateway are required for correctness.
________________________________________
8. Rationale for Booked-in-Murex AR
AR is sent only after internal booking because:
•	Volbroker requires the internal booking identifier in AR.
•	AR represents final acceptance, not preliminary receipt.
•	This avoids acknowledging trades that later fail internal booking.
This design favors correctness and consistency over minimal latency.
________________________________________
9. Non-Goals
This design note explicitly does not cover:
•	FIX tag mapping or message layouts,
•	specific database schemas or column names,
•	UI behavior,
•	retry timing or backoff strategies.
Those concerns are implementation details governed by this structure.
________________________________________
10. Summary
This design establishes a clear, enforceable contract for AE → AR processing:
•	State-driven.
•	Booking-first.
•	Polling-based.
•	Restart-safe.
•	Operationally transparent.
As long as the invariants defined in this document hold, the system will behave deterministically even under failure, replay, or restart conditions.

