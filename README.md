# QuickChartMcp

A local **stdio MCP server** (C# / .NET 10) that renders charts via a configured self-hosted
[QuickChart](https://quickchart.io/) instance and **writes the resulting file to a directory
the AI agent supplies** in a mandatory argument.

The single `create_chart` tool mirrors the arguments of QuickChart's
[POST `/chart` endpoint](https://quickchart.io/documentation/usage/post-endpoint/): the agent
passes a Chart.js configuration plus rendering options, the server POSTs them to the
configured instance over the internal network, and saves the binary result (PNG/SVG/WebP/
JPG/PDF) to disk, returning a small summary — the file path, size and metadata — instead of
an inline blob.

## Tool: `create_chart`

| Argument | Required | Default | Notes |
|----------|----------|---------|-------|
| `chart` | yes | — | Chart.js configuration as a string. Plain JSON is forwarded as an object; JavaScript object syntax (callback functions, unquoted keys) is forwarded as a string for QuickChart to evaluate |
| `outputDirectory` | yes | — | absolute directory the file is written to (created if missing) |
| `width` | no | `500` | pixel width |
| `height` | no | `300` | pixel height |
| `devicePixelRatio` | no | `2.0` | output dimensions are multiplied by this |
| `backgroundColor` | no | `transparent` | color name, hex, `rgb()` or `hsl()` |
| `format` | no | `png` | `png`, `svg`, `webp`, `jpg` or `pdf` (`base64` is deliberately not supported — the result is a file) |
| `version` | no | *(instance default)* | Chart.js version: `2`, `3` or `4` |
| `fileName` | no | *(derived)* | bare leaf name; derived from the chart title (fallback `chart`) when omitted; a numeric suffix is appended on collision |

On success the tool returns `{ "success": true, "filePath": "...", "bytes": N, ... }`; on any
error it returns `{ "success": false, "error": "..." }` (plus `statusCode` for QuickChart API
errors) and writes nothing. A rendering failure reported via the `X-quickchart-error` response
header is treated as an error even when the HTTP status is 200, so an "error image" is never
saved as a successful chart.

## Configuration

Configure the target QuickChart instance via the `QuickChart` config section — either
`appsettings.json` or environment variables (the standard MCP-client mechanism):

| Setting | Env var | Default | Notes |
|---------|---------|---------|-------|
| `BaseUrl` | `QuickChart__BaseUrl` | `http://localhost:3400` | QuickChart base URL |
| `ApiKey` | `QuickChart__ApiKey` | *(empty)* | sent as the `key` request-body property; not needed for self-hosted instances |
| `TimeoutSeconds` | `QuickChart__TimeoutSeconds` | `60` | per-request HTTP timeout |
| `AllowedOutputPatterns` | `QuickChart__AllowedOutputPatterns__0`, `__1`, … | *(empty ⇒ deny all)* | regex allow-list for output directories (see below) |

### Output path safety

Every file write is guarded at a single choke point before anything touches the filesystem:

1. **Absolute paths only** — `outputDirectory` must be fully qualified/rooted (e.g. `C:\out`,
   `\\server\share\out`). Relative or drive-relative paths are rejected.
2. **No traversal / trash segments** — any `.`, `..`, all-dots, empty (double-separator), or
   invalid-character path segment is rejected outright. The path is validated as-is (never
   normalized), so `..` is refused rather than silently collapsed. An optional `fileName` must
   be a bare leaf name (no separators, not just dots).
3. **Allow-list** — the directory must match at least one regex in `AllowedOutputPatterns`
   (matched case-insensitively). **An empty list denies everything** — you must configure at
   least one pattern before the tool can write. Invalid regexes fail fast at startup.
4. **No overwrites** — existing files are never overwritten; a numeric suffix (`-1`, `-2`, …)
   is appended until a free name is found.

Configure the allow-list in `appsettings.json`:

```json
{
  "QuickChart": {
    "AllowedOutputPatterns": [
      "^C:\\\\Users\\\\me\\\\charts(\\\\|$)",
      "^\\\\\\\\nas\\\\charts(\\\\|$)"
    ]
  }
}
```

or via environment variables (indexed): `QuickChart__AllowedOutputPatterns__0=^C:\\out(\\|$)`.
On any rejection the tool returns `{ "success": false, "error": "..." }` and writes nothing.

## Running it

You need a running QuickChart instance. For example:

```bash
docker run -d -p 3400:3400 ianw/quickchart
```

Build and run this MCP server:

```bash
dotnet build
dotnet run --project QuickChartMcp
```

### Registering with an MCP client

Claude Code (stdio), pointing at your QuickChart instance:

```bash
claude mcp add quickchart-local \
  -e QuickChart__BaseUrl=http://localhost:3400 \
  -e "QuickChart__AllowedOutputPatterns__0=^C:\\\\Users\\\\me\\\\charts(\\\\|$)" \
  -- dotnet run --project /abs/path/to/QuickChartMcp
```

## Notes

- QuickChart evaluates a string `chart` config as JavaScript; that is the documented way to
  use configs containing functions (e.g. tick/label formatters). This server forwards such
  strings verbatim — sandboxing is the QuickChart instance's responsibility.
- On any QuickChart error (bad config, network, server), the tool returns
  `{ "success": false, "error": "...", "statusCode": <code> }` and writes no files.
