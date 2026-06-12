# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Guard min-count invariants with an atomic check-and-mutate

- **Context**: src/Homdutio.Api/Households/HouseholdEndpoints.cs — the last-admin
  guard (`IsLastAdminAsync`) reads the admin count, then a *separate* statement
  demotes/removes the member.
- **Problem**: The count read and the mutation aren't atomic. Two concurrent
  requests acting on *different* admins in a 2-admin household both read count = 2,
  both pass the `<= 1` guard, and the household lands at zero admins — the exact
  invariant the guard exists to protect (a TOCTOU race).
- **Rule**: When an endpoint enforces a "must keep at least one X" (or any
  minimum-count) invariant, do the count check and the mutating write inside one
  serializable transaction — or re-check the count inside the transaction before
  committing. A guard that reads outside the write is a race.
- **Applies to**: Any endpoint enforcing a minimum-count / last-of-kind invariant
  under possible concurrency — role demotion/removal, deleting the last owner,
  decrementing a bounded resource.
