
# Rule: Write Idempotency
- **Lock only when an INSERT might happen.** Case A (all fingerprints hit) takes NO advisory lock — only an atomic `UPDATE occurrence_count`.
- **Peek before embed.** Check anchor and fingerprint existence in DB before calling `embed()`. Known errors pay zero embedding cost.
- **Idempotency-gate every write.** Pair every count increment with a `ProcessedIngests` INSERT (`ON CONFLICT DO NOTHING RETURNING`) in the **same transaction**. If `RETURNING` yields nothing, skip the write — already processed.
- **Deterministic batch order.** Sort samples by `context_fingerprint ASC` before processing to guarantee deadlock-free ordering.
