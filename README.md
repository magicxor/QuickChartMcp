# QuickChartMcp

A local **stdio MCP server** (C# / .NET 10) that renders charts via a configured self-hosted
[QuickChart](https://github.com/magicxor/quickchart) instance and **writes the resulting file
to a directory the AI agent supplies** in a mandatory argument.

The single `create_chart` tool mirrors the arguments of QuickChart's POST `/chart` endpoint:
the agent passes a Chart.js **4** configuration plus rendering options, the server POSTs them
to the configured instance over the internal network, and saves the binary result
(PNG/SVG/PDF) to disk, returning a small summary — the file path, size and metadata — instead
of an inline blob.

> Targets the modernized QuickChart fork (Chart.js 4 only). The legacy `version` parameter
> and the hosted-service `key` parameter don't exist there and are not exposed by this tool.

## Tool: `create_chart`

| Argument | Required | Default | Notes |
|----------|----------|---------|-------|
| `chart` | yes | — | Chart.js 4 configuration as a string. Plain JSON is forwarded as an object; JavaScript object syntax (callback functions, unquoted keys) is forwarded as a string for QuickChart to evaluate |
| `outputDirectory` | yes | — | absolute directory the file is written to (created if missing) |
| `width` | no | `500` | pixel width |
| `height` | no | `300` | pixel height |
| `devicePixelRatio` | no | `2.0` | output dimensions are multiplied by this |
| `backgroundColor` | no | `transparent` | color name, hex, `rgb()` or `hsl()` |
| `format` | no | `png` | `png`, `svg` or `pdf` (`base64` is deliberately not supported — the result is a file) |
| `fileName` | no | *(derived)* | bare leaf name; derived from the chart title (fallback `chart`) when omitted; a numeric suffix is appended on collision |

On success the tool returns `{ "success": true, "filePath": "...", "bytes": N, ... }`; on any
error it returns `{ "success": false, "error": "..." }` (plus `statusCode`, and a `hint` for
HTTP 400) and writes nothing. QuickChart's error contract: **400** = invalid request/config
(fix the config, don't retry unchanged), **500** = server failure; the error message always
arrives in the `X-quickchart-error` response header. A rendering failure reported via that
header is treated as an error even when the HTTP status is 200, so an "error image" is never
saved as a successful chart.

### Supported chart types

- Standard Chart.js 4: `bar`, `line`, `pie`, `doughnut`, `radar`, `polarArea`, `scatter`, `bubble`
- QuickChart custom: `sparkline`, `progressBar`, `donut` (alias)
- Box plots: `boxplot`, `horizontalBoxplot`, `violin`, `horizontalViolin`
- Error bars: `barWithErrorBars`, `lineWithErrorBars`, `scatterWithErrorBars`, `polarAreaWithErrorBars`
- `funnel`; geo: `choropleth`, `bubbleMap`; graphs/trees: `graph`, `forceDirectedGraph`, `dendrogram`, `tree`
- Parallel coordinates: `pcp`, `logarithmicPcp`; sets: `venn`, `euler`; `wordCloud`
- Plus: the `hierarchical` axis scale, `options.plugins.annotation`, `options.plugins.datalabels`,
  and time scales with moment.js format strings

Configs must use **Chart.js 4 syntax** (`options.scales.x`/`y`, `options.plugins.title`/`legend`);
Chart.js 2 syntax (`scales.xAxes`/`yAxes`, top-level `title`/`legend`, `type: 'horizontalBar'`)
is not translated — use `type: 'bar'` with `options.indexAxis: 'y'` for horizontal bars.

### Geo charts

The QuickChart instance bundles map data, so maps are referenced **by name** — no inlined
GeoJSON needed for standard maps:

| Map name | Contents |
|---|---|
| `world`, `world-50m` | all countries (110m / higher-detail 50m) |
| `world-land` | single land outline |
| `us`, `us-states`, `us-counties` | US nation outline / states / counties |
| ISO 3166-1 alpha-3 codes (`deu`, `fra`, `jpn`, …) | one country with its first-level subdivisions |

```jsonc
{
  "type": "choropleth",
  "data": {
    "datasets": [{
      "map": "world",
      "data": [
        { "feature": "Germany", "value": 83 },   // matched by name or id,
        { "feature": "France",  "value": 67 }    // case-insensitive
      ]
    }]
  }
}
```

`bubbleMap` is analogous with `"outline": "<map name>"` and
`data: [{ "longitude": ..., "latitude": ..., "value": ... }]`. When a named map is used, the
`color`/`size` scales, `showOutline`, and a hidden legend are defaulted automatically, and the
projection is **aimed at the map** — so a single country renders correctly framed without any
`options.scales.projection` of your own. Use the [`list_maps` tool](#tool-list_maps) to
discover available maps, their matchable features, and the projection spec the instance aims at
each one. Inline GeoJSON Features still work anywhere a named reference does — use them for
custom shapes, and mind the instance's request body limit (`EXPRESS_JSON_LIMIT`, default
100 KB) when doing so.

#### Showing part of a map

Set `options.scales.projection.fit` to frame the view on a region; everything outside is
clipped, and the automatic projection is aimed at the region rather than at the whole map:

```jsonc
"scales": {
  "projection": { "axis": "x", "fit": [-25, 34, 45, 72] },              // [W, S, E, N] degrees
  "projection": { "axis": "x", "fit": { "map": "rus",
                                        "features": ["Amur", "Sakhalin"] } },  // named features
  "projection": { "axis": "x", "fit": { "map": "deu" } }                // a whole map
}
```

West may exceed east for a region past the antimeridian (`[160, 62, -172, 72]` is Chukotka).
To aim a projection by hand instead, `projection` accepts an object —
`{ "type": "conicEqualArea", "rotate": [-100, 0], "center": [0, 65], "parallels": [50, 70] }`
— and the scale's `projectionScale`, `projectionOffset` and `padding` nudge the result in
pixel space. A hand-named projection is not aimed for you.

## Tool: `list_maps`

Proxies the instance's `GET /maps` discovery endpoint so the calling agent can self-serve
map names and feature spellings without HTTP access to the instance:

| Argument | Required | Default | Notes |
|----------|----------|---------|-------|
| `mapName` | no | *(none)* | omit to list all maps as `{ name, source }`; set (e.g. `world`, `us-states`, `deu`) to get that map's `features` as `{ name, id }` pairs, plus its `bbox` (`[W, S, E, N]`; west > east when the map crosses the antimeridian), `centroid`, and the `projection` spec the instance aims at it |

Returns JSON inline (`{ "success": true, "maps": [...] }` or `{ "success": true, "map": {...} }`)
and writes no files. Useful when a country map's ISO alpha-3 code is non-standard (e.g. `kos`
for Kosovo) or when subdivision features are only matchable by id (some have `name: null`,
ids like `DE.BE`). An unknown `mapName` returns `{ "success": false, ... }` with a hint.

## Configuration

Configure the target QuickChart instance via the `QuickChart` config section — either
`appsettings.json` or environment variables (the standard MCP-client mechanism):

| Setting | Env var | Default | Notes |
|---------|---------|---------|-------|
| `BaseUrl` | `QuickChart__BaseUrl` | `http://localhost:3400` | QuickChart base URL |
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

You need a running instance of the modernized QuickChart fork. Either build it from
[magicxor/quickchart](https://github.com/magicxor/quickchart):

```bash
docker build -t quickchart . && docker run -d -p 3400:3400 quickchart
```

or pull a released multi-arch image from GHCR (published on `vX.Y.Z` tags):

```bash
docker run -d -p 3400:3400 ghcr.io/magicxor/quickchart:latest
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
  strings verbatim — sandboxing is the QuickChart instance's responsibility, so only point
  this tool at an instance you trust and that is not exposed to untrusted parties.
- On any QuickChart error (bad config, network, server), the tool returns
  `{ "success": false, "error": "...", "statusCode": <code> }` and writes no files.
