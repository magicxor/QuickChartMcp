# QuickChartMcp

Local **stdio MCP server** that renders charts via a configured self-hosted
[QuickChart](https://github.com/magicxor/quickchart) instance (the modernized Chart.js-4-only
fork) and writes the resulting file (PNG/SVG/PDF) to a directory the calling agent specifies,
returning only a compact summary (file path, size, metadata) instead of an inline blob.

## Tools

- `create_chart` — mirrors QuickChart's POST `/chart` endpoint: `chart` (Chart.js 4 config as
  JSON or JavaScript-syntax string), `width`, `height`, `devicePixelRatio`,
  `backgroundColor`, `format` (`png`/`svg`/`pdf`), plus a required `outputDirectory` and an
  optional `fileName`. Supports all chart types of the target fork, including boxplot/violin,
  error bars, funnel, geo (choropleth/bubbleMap with built-in named maps — `world`,
  `us-states`, ISO alpha-3 country codes, … — or inline GeoJSON for custom shapes),
  graph/tree, parallel coordinates, venn/euler and word clouds. QuickChart's 400 responses
  (invalid config) are surfaced with a fix-your-config hint; error images are never saved as
  successful charts.
- `list_maps` — proxies QuickChart's `GET /maps` discovery endpoint: without arguments lists
  the built-in geo maps (`{ name, source }`); with `mapName` returns that map's matchable
  features (`{ name, id }` pairs) for choropleth data rows. Returns JSON inline, writes no
  files.

## Configuration

Set the target instance via the `QuickChart` config section (env vars or `appsettings.json`):

- `QuickChart__BaseUrl` — default `http://localhost:3400`
- `QuickChart__TimeoutSeconds` — default `60`
- `QuickChart__AllowedOutputPatterns__0`, `__1`, … — regex allow-list for output directories

### Output path safety

Every write is validated before touching disk: `outputDirectory` must be absolute/rooted with
no `.`/`..`/all-dots/empty/invalid segments (paths are validated as-is, never normalized), any
`fileName` must be a bare leaf name, and the directory must match at least one regex in
`AllowedOutputPatterns`. **An empty allow-list denies all writes** — configure at least one
pattern. Existing files are never overwritten (a numeric suffix is appended on collision).
Invalid regexes fail fast at startup. On rejection the tool returns
`{ "success": false, "error": "..." }` and writes nothing.

## Local development

```bash
dotnet run --project QuickChartMcp
```

Register with an MCP client over stdio, e.g. Claude Code:

```bash
claude mcp add quickchart-local \
  -e QuickChart__BaseUrl=http://localhost:3400 \
  -e "QuickChart__AllowedOutputPatterns__0=^C:\\\\Users\\\\me\\\\charts(\\\\|$)" \
  -- dotnet run --project /abs/path/to/QuickChartMcp
```

## Packaging

This project is set up as an MCP-server NuGet tool (`PackAsTool`, `PackageType=McpServer`).
Build a package with `dotnet pack -c Release`; the `.mcp/server.json` manifest describes the
stdio transport and the `QuickChart__*` environment variables.
