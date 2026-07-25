# Release notes (JSON-driven)

Each release has one JSON file named after its version (no `v` prefix):

```
release-notes/1.0.0.json
release-notes/1.1.0.json
```

Format — a `title` plus `description` as a list of dot points:

```json
{
  "title": "Release abc",
  "description": [
    { "content": "content 1" },
    { "content": "content 2" },
    { "content": "content 3" }
  ]
}
```

## How it's used

When you push a tag `v1.1.0`, the GitHub Actions release workflow:

1. reads `release-notes/1.1.0.json`,
2. renders it to Markdown — `title` becomes the heading, each `description[].content` becomes a `* bullet`,
3. uses that as the GitHub Release body, and names the release after `title`.

If the file for the tagged version is missing, the workflow fails on purpose (so you never ship a release with empty notes).

## Adding a new release

1. Copy `TEMPLATE.json` to `release-notes/<version>.json`.
2. Fill in `title` and the `description` bullets.
3. Bump the app version if needed, commit, then `git tag vX.Y.Z && git push --tags`.
