# AGENTS.md

## Scope
This file applies to the `com.say.grid` package.

The package provides grid math and grid storage primitives. Treat it as a low-level
infrastructure package. Keep it small, deterministic, and reusable by gameplay code.

## Primary Responsibility

- Own grid coordinate math.
- Own world/local/grid conversion helpers.
- Own typed rectangular storage (`RectGridMap<T>`).
- Provide clear, stable APIs for consumers in gameplay and view code.

## Canonical Coordinate Convention

**All grid coordinates use a bottom-left, Y-up system:**

- `(0, 0)` = bottom-left cell.
- `X` grows to the right (columns).
- `Y` grows upward (rows).
- `Width` = number of cells along X = column count.
- `Height` = number of cells along Y = row count.

This applies to `RectGrid`, `RectGridMap<T>`, and any future grid types. Do not
introduce Y-down or top-left conventions anywhere in the package.

## Key Classes

### `RectGrid`
Pure coordinate math — no data storage. Converts between `RectGridCoordinate`,
local positions, and world positions. Supports multiple `RectGridOrigin` anchors
(`Center`, `BottomLeft`, `TopLeft`, etc.) that control where local (0,0) lands.
All origins follow the Y-up convention: `Y=0` is the **bottom** row regardless of
which origin anchor is used.

### `RectGridMap<T>`
Generic `T[,]` storage indexed as `_cells[x, y]` (column first, row second).
- `Width` = columns (X dimension), `Height` = rows (Y dimension).
- Provides `Get`, `Set`, `TryGet`, `IsInside`, and both `int`/`RectGridCoordinate` indexers.
- Is independent of `RectGrid` — it owns storage only; callers pair it with a `RectGrid` for world-space math.

## Core Rules

1. Keep coordinate conversion inside the grid package

- Grid-to-world and world-to-grid math should live in grid classes or small helpers
  inside this package.
- Do not duplicate conversion logic in views, controllers, or gameplay states.
- Consumers should ask the grid for positions or coordinates; they should not
  reconstruct the math themselves.

2. Make coordinate spaces explicit

- Always state which space a value is in: `grid`, `local`, or `world`.
- Use `RectGridCoordinate` for logical cell indices; use `Vector3` or `RectGridPoint`
  for spatial values.
- Column = X dimension; Row = Y dimension — be consistent in naming and docs.
- If a grid uses an origin anchor or cell size, make that explicit in the API.

3. Prefer pure and deterministic helpers

- Conversion helpers should be side-effect free.
- The same input must always produce the same output.
- Avoid hidden dependence on scene state, random values, or mutable global context.

4. Preserve round-trip correctness

- `cell -> world -> cell` should round-trip as closely as possible.
- `world -> cell -> world` should land on the same cell center when the input is
  inside the cell bounds.
- Add tests whenever you change conversion math, cell size handling, or origin rules.

5. Encapsulate storage shape behind the API

- Keep storage layout details inside the grid implementation.
- Consumers should not care whether the implementation uses dense arrays, offset
  indexing, a hash table, or another backing structure.
- `RectGridMap<T>` uses `T[width, height]` internally — do not leak this shape to callers.

6. Keep map shape separate from tile behavior

- The grid package should describe coordinates and storage, not gameplay rules.
- Do not mix movement logic, match rules, scoring, or animation into the grid layer.
- A grid can be used by multiple systems, so avoid assumptions that only fit one game mode.

7. Make bounds checks explicit

- Validate indices before reading or writing cells.
- Prefer `Try...` APIs for lookups that can fail.
- Return clear failures for out-of-range coordinates, missing cells, or invalid world points.

8. Support non-uniform layouts carefully

- If a grid supports custom cell sizes, offsets, or varying row lengths, keep those
  rules documented and tested.
- Do not assume square cells unless the API says so.
- Do not assume the visual center is the same as the logical cell origin.

## Best Practices

- Use a single source of truth for cell size, origin, and spacing.
- Expose helpers for cell centers, not only cell corners, when gameplay objects must
  be placed visually in the middle of a cell.
- Keep world-space transforms separate from grid math. The grid should know how to
  convert coordinates; the caller should decide which `Transform` or camera is used.
- Cache reusable grid metadata when it is expensive to recompute.
- Avoid allocations in hot paths such as raycasts, hover checks, drag handling,
  and per-frame hover updates.
- Prefer `TryGet...` / `Get...` pairs over throwing on expected user input paths.
- Pair `RectGrid` (math) with `RectGridMap<T>` (storage) rather than building ad-hoc
  2D arrays in calling code.
- Add editor or unit tests for:
  - cell center placement (Y-up — row 0 is bottom)
  - world-to-grid hit testing
  - bounds rejection
  - custom cell size handling
  - origin/offset changes
  - `RectGridMap<T>` get/set round-trips and out-of-bounds guards

## API Design Guidance

- Keep public APIs small and obvious.
- Prefer clear method names over clever abstractions.
- If a method returns a grid coordinate, document whether it is logical cell data,
  visual layout data, or both.
- Preserve backward compatibility for existing consumers when possible.
- If a breaking change is necessary, update all call sites in the same change set.

## Testing Expectations

- Add or update tests whenever coordinate math changes.
- Verify Y-up semantics: increasing Y must produce a higher world-space Y value
  for every origin mode.
- Verify that rectangular layouts stay stable when the grid is resized.
- Verify that world-space hit tests still map to the expected cell after
  transform, origin, or cell size changes.
- Verify that decorative or non-playable cells are excluded from gameplay lookups
  when that is part of the layout contract.
- `RectGridMap<T>` tests must confirm that `[x, y]` and `[RectGridCoordinate]`
  indexers address the same cell as `Get`/`Set`.

## Practical References

- Red Blob Games on grid coordinate systems and map storage.
- Red Blob Games on converting between pixel/world positions and grid coordinates.
- Red Blob Games on treating grids as graphs for traversal and pathfinding.

## What Not To Do

- Do not spread grid math across gameplay views and controllers.
- Do not hide coordinate-space conversions in random helper classes.
- Do not add gameplay rules to the grid package.
- Do not assume one layout shape will fit every future use case.
- Do not break existing serialized data or public APIs without a migration plan.
- Do not use Y-down conventions or store coordinates with row 0 at the top.
  The canonical system is always Y-up, bottom-left origin.
- Do not replicate `T[,]` storage in calling code — use `RectGridMap<T>` instead.
