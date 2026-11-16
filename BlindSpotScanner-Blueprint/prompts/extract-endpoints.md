# Task: extract HTTP endpoints (fallback for frameworks without a native extractor) — v1.0.0

**Input:** `{ framework, files: [{ path, content }] }`

Return every HTTP endpoint declared in these **production** files. Include the full route with
its prefixes. Do not invent endpoints that are only implied by convention. If a route is built
dynamically and cannot be resolved, return it with `routeResolved:false`.

**Output schema:**
```json
{ "endpoints": [ { "sourcePath": "…", "controller": "…", "method": "GET|POST|PUT|PATCH|DELETE",
  "route": "…", "routeResolved": true, "evidenceKind": "observed" } ] }
```
