# FluidScript documentation

Three categories, and every user-visible feature has a page in exactly one of them:

| Category | Holds |
|---|---|
| `tutorial/` | The path from an empty file to a solved circuit, in order, with no forward references |
| `advanced/` | Workflows that assume the tutorial — sizing overrides, transients, export |
| `functions/` | One page per component kind, statement and diagnostic code |

The structure and page template are owned by
[`plan/60-docs-and-devex/61-documentation-plan.md`](../plan/60-docs-and-devex/61-documentation-plan.md).

**These directories are empty because no user-visible feature has shipped yet**, and that is the only
reason they may be empty. `DocumentationGateTests` fails the build the moment a component kind, a
statement-introducing reserved word or a reachable diagnostic code exists without its page.
