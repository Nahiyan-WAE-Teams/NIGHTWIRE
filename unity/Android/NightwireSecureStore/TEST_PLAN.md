# StrongBox sequence-store verification plan

These are required device tests; they have not been executed.

1. First load with no record and no NIGHTWIRE sequence key returns sequence 0.
2. After saving a sequence, clear or delete the SharedPreferences record while leaving AndroidKeyStore intact; load must fail closed because an orphaned NIGHTWIRE key remains.
3. A missing record with a sequence key created under a different configured namespace must also fail closed rather than treating the namespace as a fresh installation.
4. Save sequence 1, recreate the provider, and load 1.
5. Save sequence 2; confirm sequence 1's key alias no longer exists.
6. Restore a captured copy of the old SharedPreferences record after saving sequence 2; loading must fail because the old StrongBox key was deleted.
7. Corrupt the IV, ciphertext, alias, and authenticated namespace independently; each must fail closed.
8. Attempt to save a lower sequence; it must be rejected.
9. Simulate process termination after old-key deletion and before the new preference commit; next load must fail closed, not return the older sequence.
10. Run on a device without StrongBox; key creation must fail and must not create a software-backed fallback.
11. Run the C# bridge under Unity Mono and Android IL2CPP; verify Java method names/signatures and exception propagation.
12. Invoke concurrent load/save operations from multiple provider instances; updates must serialize and the highest accepted sequence must never decrease.
13. Verify backup/restore and app update behavior. No failed load may be treated as an empty store or silently reset to zero.
