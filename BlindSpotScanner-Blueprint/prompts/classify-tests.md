# Task: classify ambiguous test files — v1.0.0

**Input:** `{ profileMarkdown, files: [{ path, content }] }`. Only the files that the
deterministic profile rules could not classify are sent.

For each file, decide its kind:
- `integration`: crosses a real process or dependency boundary as defined by the profile. Typical
  signs are a hosted test server, a real DB or container, a real broker, or real HTTP.
- `unit`: one unit in isolation, with collaborators mocked.
- `component`: a module with close collaborators, in-process, external dependencies faked.
- `e2e`: a browser or deployed-system flow.
- `other`: perf, contract, snapshot, helpers, fixtures (not tests).

**Output schema:**
```json
{ "files": [ { "path": "string", "kind": "integration|unit|component|e2e|other",
  "confidence": "High|Med|Low", "reason": "≤30 words citing the observed signal",
  "evidenceKind": "observed|inferred" } ] }
```
