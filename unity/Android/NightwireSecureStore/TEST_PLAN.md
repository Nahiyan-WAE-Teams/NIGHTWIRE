# StrongBox sequence-store verification plan

These are required device tests; they have not been executed.

1. First load with no record returns sequence 0.
2. Save sequence 1, recreate the provider, and load 1.
3. Save sequence 2; confirm sequence 1's key alias no longer exists.
4. Restore a captured copy of the old SharedPreferences record after saving sequence 2; loading must fail because the old StrongBox key was deleted.
5. Corrupt the IV, ciphertext, alias, and authenticated namespace independently; each must fail closed.
6. Attempt to save a lower sequence; it must be rejected.
7. Simulate process termination after old-key deletion and before the new preference commit; next load must fail closed, not return the older sequence.
8. Run on a device without StrongBox; key creation must fail and must not create a software-backed fallback.
9. Run the C# bridge under Unity Mono and Android IL2CPP; verify Java method names/signatures and exception propagation.
10. Invoke concurrent load/save operations from multiple provider instances; updates must serialize and the highest accepted sequence must never decrease.
11. Verify backup/restore and app update behavior. No failed load may be treated as an empty store or silently reset to zero.
