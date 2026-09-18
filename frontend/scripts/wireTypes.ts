import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { compile, type JSONSchema } from 'json-schema-to-typescript';

/** The schemas the Api commits (`42`, `D-46` step 2), by the name each is exported under. */
export const schemaFiles = {
  ModelContract: 'model-contract',
  CompileResponse: 'compile-response',
  Metadata: 'metadata',
} as const;

const schemaDir = new URL('../../src/FluidScript.Api/Contracts/Schemas/', import.meta.url);

type SchemaNode = { [key: string]: unknown };

/**
 * The exporter inlines every record where it occurs, so a `Quantity` appears once per quantity-typed
 * member and a naive generator would emit `Quantity1`, `Quantity2`, ... Every titled object node is
 * hoisted into `$defs` under its title and replaced by a `$ref`; a nullable occurrence becomes
 * `anyOf: [ref, null]`. Two nodes with one title and different shapes keep the first and inline the
 * second under a suffixed title, so a real divergence is visible in the output rather than hidden.
 */
export function hoistTitledNodes(root: SchemaNode): SchemaNode {
  const defs: Record<string, SchemaNode> = {};
  const shapes = new Map<string, string>();
  const expanded = expandExporterRefs(root);

  const visit = (node: unknown, isRoot: boolean): unknown => {
    if (Array.isArray(node)) {
      return node.map((item) => visit(item, false));
    }
    if (typeof node !== 'object' || node === null) {
      return node;
    }

    const object = node as SchemaNode;
    if (typeof object['$ref'] === 'string' && object['$ref'].startsWith('#/')) {
      // A cycle the exporter left as a pointer: the target is an ancestor, hoisted under its title.
      const target = follow(expanded, object['$ref']);
      const title = typeof target?.['title'] === 'string' ? target['title'] : undefined;
      if (title !== undefined) {
        return { ...object, $ref: `#/$defs/${title}` };
      }
      return object;
    }
    const rebuilt: SchemaNode = {};
    for (const [key, value] of Object.entries(object)) {
      rebuilt[key] =
        key === 'properties' ? visitProperties(value as SchemaNode) : visit(value, false);
    }

    if (isRoot || typeof rebuilt['title'] !== 'string' || !('properties' in rebuilt)) {
      return rebuilt;
    }

    const title = rebuilt['title'];
    const nullable =
      Array.isArray(rebuilt['type']) && (rebuilt['type'] as string[]).includes('null');
    const { description, ...body } = rebuilt;
    body['type'] = 'object';
    const shape = JSON.stringify(body);

    let key = title;
    const known = shapes.get(title);
    if (known !== undefined && known !== shape) {
      let n = 2;
      while (shapes.has(`${title}${n}`) && shapes.get(`${title}${n}`) !== shape) {
        n++;
      }
      key = `${title}${n}`;
    }
    if (!shapes.has(key)) {
      shapes.set(key, shape);
      defs[key] = { ...body, title: key };
    }

    // A `$ref` with a sibling `description` is a new anonymous type to the generator, which is where
    // `Quantity1` comes from; the description goes on a wrapping `allOf`/`anyOf` instead.
    const pointer = { $ref: `#/$defs/${key}` };
    const documented: SchemaNode = typeof description === 'string' ? { description } : {};
    return nullable
      ? { ...documented, anyOf: [pointer, { type: 'null' }] }
      : { ...documented, allOf: [pointer] };
  };

  const visitProperties = (properties: SchemaNode): SchemaNode => {
    const out: SchemaNode = {};
    for (const [name, value] of Object.entries(properties)) {
      out[name] = visit(value, false);
    }
    return out;
  };

  const rebuiltRoot = visit(expanded, true) as SchemaNode;
  return { ...rebuiltRoot, $defs: defs };
}

/** Follows a JSON pointer such as `#/properties/layout/items` into `root`. */
function follow(root: SchemaNode, pointer: string): SchemaNode | undefined {
  let node: unknown = root;
  for (const token of pointer
    .slice(2)
    .split('/')
    .filter((t) => t.length > 0)) {
    if (typeof node !== 'object' || node === null) {
      return undefined;
    }
    node = (node as SchemaNode)[token.replaceAll('~1', '/').replaceAll('~0', '~')];
  }
  return typeof node === 'object' && node !== null ? (node as SchemaNode) : undefined;
}

/**
 * The exporter writes a repeated record as a `$ref` to the path of its first occurrence. Hoisting
 * moves those nodes, so every such pointer is expanded into a copy first -- except one that points
 * at its own ancestor, a genuine cycle, which stays a pointer for `hoistTitledNodes` to redirect.
 */
function expandExporterRefs(root: SchemaNode): SchemaNode {
  const visit = (node: unknown, path: string): unknown => {
    if (Array.isArray(node)) {
      return node.map((item, index) => visit(item, `${path}/${index}`));
    }
    if (typeof node !== 'object' || node === null) {
      return node;
    }
    const object = node as SchemaNode;
    const ref = object['$ref'];
    if (typeof ref === 'string' && ref.startsWith('#/')) {
      const targetPath = ref.slice(1);
      if (path === targetPath || path.startsWith(targetPath + '/')) {
        return object;
      }
      const target = follow(root, ref);
      if (target === undefined) {
        return object;
      }
      const { $ref: _ignored, ...rest } = object;
      return visit({ ...target, ...rest }, targetPath);
    }
    const out: SchemaNode = {};
    for (const [key, value] of Object.entries(object)) {
      out[key] = visit(value, `${path}/${key}`);
    }
    return out;
  };
  return visit(root, '') as SchemaNode;
}

/**
 * Renders the TypeScript wire types from the three committed schemas. Every object node carries a
 * `title`, so nested records come out as named interfaces rather than inline shapes, and every
 * documented member carries its `description` as a doc comment. Node-only: it reads the repository.
 */
export async function renderWireTypes(): Promise<string> {
  const parts: string[] = [
    '/* Generated from src/FluidScript.Api/Contracts/Schemas/*.schema.json by npm run types. Do not edit. */',
    '/* eslint-disable */',
    '',
  ];
  const seen = new Set<string>();

  for (const [name, file] of Object.entries(schemaFiles)) {
    const path = fileURLToPath(new URL(`${file}.schema.json`, schemaDir));
    const schema = hoistTitledNodes(JSON.parse(readFileSync(path, 'utf8')) as SchemaNode);
    const rendered = await compile(schema as JSONSchema, name, {
      bannerComment: '',
      additionalProperties: false,
      strictIndexSignatures: true,
      declareExternallyReferenced: true,
      style: { singleQuote: true, semi: true, printWidth: 100, trailingComma: 'all' },
    });

    // The three schemas share records (a Diagnostic appears in all three); one declaration each.
    for (const block of rendered.split(/\n(?=export )/)) {
      const match = /^export (?:interface|type) (\w+)/.exec(block);
      const key = match?.[1] ?? block;
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      parts.push(block.trimEnd(), '');
    }
  }

  return parts.join('\n');
}
