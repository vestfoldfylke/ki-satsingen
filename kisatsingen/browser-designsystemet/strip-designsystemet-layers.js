/**
 * strip-layers.js
 *
 * Removes @layer wrapping from a CSS file, while keeping all the rules
 * inside those layers intact. Useful as an IDE-only helper file for tools
 * (like JetBrains Rider) that don't traverse into @layer blocks when
 * building CSS class completion candidates.
 *
 * IMPORTANT: The output file is meant for editor/IDE indexing only.
 * Do NOT load it in your actual running app — @layer is there on purpose
 * to control cascade precedence (see the source file), and removing it
 * changes how the CSS competes with other, unlayered CSS in your project.
 *
 * Usage:
 *   node strip-layers.js index.min.css
 *   node strip-layers.js index.min.css custom-output-name.css
 */

const fs = require('node:fs');
const path = require('node:path');

function findMatchingBrace(str, openIdx) {
  let depth = 0;
  for (let i = openIdx; i < str.length; i++) {
    const c = str[i];
    if (c === '{') depth++;
    else if (c === '}') {
      depth--;
      if (depth === 0) return i;
    }
  }
  return -1;
}

function stripLayerStatements(css) {
  // Removes "@layer name, name2, ...;" (order-only declarations, no body)
  return css.replace(/@layer\s+[a-zA-Z0-9_.\-,\s]+;/g, '');
}

function stripLayerBlocks(css) {
  let changed = true;
  while (changed) {
    changed = false;
    let searchFrom = 0;

    while (true) {
      const idx = css.indexOf('@layer ', searchFrom);
      if (idx === -1) break;

      const braceIdx = css.indexOf('{', idx);
      const semiIdx = css.indexOf(';', idx);

      if (braceIdx === -1) {
        searchFrom = idx + 7;
        continue;
      }

      if (semiIdx !== -1 && semiIdx < braceIdx) {
        // Statement form (@layer name;), already handled separately
        searchFrom = semiIdx + 1;
        continue;
      }

      // Block form: @layer <names> { ... }
      const closeIdx = findMatchingBrace(css, braceIdx);
      if (closeIdx === -1) {
        searchFrom = idx + 7;
        continue;
      }

      const inner = css.slice(braceIdx + 1, closeIdx);
      // Splice out the "@layer <names>{" prefix and the trailing "}",
      // keeping only the inner rules.
      css = css.slice(0, idx) + inner + css.slice(closeIdx + 1);
      changed = true;
      break; // restart scanning since content shifted
    }
  }
  return css;
}

function main() {
  const [, , inputArg, outputArg] = process.argv;

  if (!inputArg) {
    console.error('Usage: node strip-layers.js <input.css> [output.css]');
    process.exit(1);
  }

  const inputPath = path.resolve(inputArg);
  if (!fs.existsSync(inputPath)) {
    console.error(`File not found: ${inputPath}`);
    process.exit(1);
  }

  const parsed = path.parse(inputPath);
  const outputPath = outputArg
    ? path.resolve(outputArg)
    : path.join(parsed.dir, `${parsed.name}-flat${parsed.ext}`);

  const css = fs.readFileSync(inputPath, 'utf8');

  let flat = stripLayerStatements(css);
  flat = stripLayerBlocks(flat);

  const openBraces = (flat.match(/{/g) || []).length;
  const closeBraces = (flat.match(/}/g) || []).length;
  const remainingLayers = (flat.match(/@layer/g) || []).length;

  fs.writeFileSync(outputPath, flat, 'utf8');

  console.log(`Wrote: ${outputPath}`);
  console.log(`Remaining @layer occurrences: ${remainingLayers}`);
  console.log(`Brace balance: ${openBraces} open / ${closeBraces} close ${openBraces === closeBraces ? '(OK)' : '(MISMATCH — check output!)'}`);
}

main();