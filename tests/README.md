# NIGHTWIRE validator integration tests

These tests exercise the .NET episode validator against a copied sample package and intentionally corrupted variants.

## Requirements

- Python 3.10+
- .NET 10 SDK

## Run

From the repository root:

```bash
python -m unittest discover -s tests -v
```

The test suite builds the validator in Release configuration once, then checks:
- the sample package passes;
- a modified file fails SHA-256 integrity validation;
- a choice pointing to a missing message fails semantic validation;
- a path-traversal manifest entry is rejected.

The tests use only Python's standard library. They do not run GitHub Actions; CI remains deferred until the implementation and review milestone is reached.
