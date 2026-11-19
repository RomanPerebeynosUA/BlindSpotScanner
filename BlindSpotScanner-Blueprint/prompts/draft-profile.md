# Task: draft a repo profile — v1.1.0

**Input:** `{ rootTree: ["paths…"], buildFiles: [{path, content}], sampleTests: [{path, content}], zeroMatchPatterns?: ["field: pattern"] }`

Fill in `profiles/_template.md` (provided at runtime) for this repository. Base every glob,
attribute and base class on paths and content in the input. Mark uncertain lines with
`<!-- TODO confirm -->`. Choose `integrationMappedTo` from the evidence. Default to `real-seam`
and explain any other choice.

Also return the classification rules the scanner uses (they are not read from the Markdown):

- `testGlobs`: every test source file. `controllerGlobs`: MVC controller files.
  `minimalApiGlobs`: files that map minimal API routes. `integrationGlobs`: folders or names of
  integration tests. `notIntegrationGlobs`: helper files inside those folders that hold no tests.
- `integrationAttributes` (e.g. `Trait("Category","Integration")`) and `integrationBaseTypes`
  (e.g. `WebApplicationFactory`): only those the sample tests actually use.
- Each list entry is exactly one glob (`**` and `*` allowed), relative to the repository root, with
  no spaces, commas or explanations. Each glob must match at least one path in `rootTree`. A list
  may be empty, e.g. `controllerGlobs: []` when the repository has no controllers.
- If `zeroMatchPatterns` is present, those globs matched no file in your previous answer: replace or
  drop each one.

**Output:** `{ "markdown": "…", "openQuestions": ["…"], "integrationMappedTo": "real-seam", "testGlobs": ["…"], "controllerGlobs": [], "minimalApiGlobs": ["…"], "integrationGlobs": ["…"], "integrationAttributes": [], "integrationBaseTypes": ["…"], "notIntegrationGlobs": [] }`
