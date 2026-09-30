import assert from "node:assert/strict";
import { structureTools } from "../build/tools/structure-tools.js";
import { withAnnotations } from "../build/tools/annotations.js";

const tool = structureTools.find(({ name }) => name === "calculate_concrete_quantity");

assert.ok(tool, "calculate_concrete_quantity must be registered in structureTools");
assert.deepEqual(
  tool.inputSchema.properties.categories.items.enum,
  ["column", "beam", "floor", "structuralWall", "foundation"],
  "the five structural categories are the public contract (domain/concrete-quantity-takeoff.md)",
);
assert.equal(tool.inputSchema.required, undefined, "every input is optional: default = whole model, five categories");
assert.equal(tool.inputSchema.properties.structuralOnly.default, true);
assert.equal(tool.inputSchema.properties.exportExcel.default, false);
assert.ok(tool.description.includes("domain/concrete-quantity-takeoff.md"), "description must cite the domain method file");

const annotated = withAnnotations(tool);
assert.equal(annotated.annotations?.readOnlyHint, true);
assert.equal(annotated.annotations?.destructiveHint, false);

console.log("concrete quantity MCP contract: PASS");
