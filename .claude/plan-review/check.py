#!/usr/bin/env python3
"""Mechanical structure checks for the FluidScript plan tree.

Step 1 of the plan-review skill. These catch the errors an LLM reviewer is worst at — broken
links, drifted counts, up-tier dependencies, duplicate ownership — cheaply and exactly, so no
reviewer agent is spent on them. Exit code 0 means clean.

Run from the repository root:  python3 .claude/plan-review/check.py
"""
import re, sys, pathlib, collections

ROOT = pathlib.Path(__file__).resolve().parents[2]
PLAN = ROOT / 'plan'
REQUIRED_KEYS = ['id', 'title', 'tier', 'status', 'owns', 'depends_on',
                 'traces_to', 'open_questions', 'last_review_pass']
REQUIRED_SECTIONS = ['## Purpose', '## Responsibilities', '## Invariants',
                     '## Acceptance criteria', '## Open questions']

problems = []


def strip_fences(text):
    """Remove fenced code blocks. Links and headings inside an example are not the plan's own."""
    return re.sub(r'^```.*?^```', '', text, flags=re.M | re.S)


def parse_frontmatter(text):
    if not text.startswith('---\n'):
        return None
    end = text.find('\n---\n', 3)
    if end == -1:
        return None
    fm = {}
    for line in text[4:end].split('\n'):
        m = re.match(r'^(\w+):\s*(.*)$', line)
        if m:
            fm[m.group(1)] = m.group(2).strip()
    return fm


def parse_list(value):
    value = (value or '').strip()
    if value.startswith('[') and value.endswith(']'):
        inner = value[1:-1].strip()
        return [x.strip() for x in inner.split(',') if x.strip()] if inner else []
    return []


docs = sorted(p for p in PLAN.rglob('*.md') if p.name not in ('README.md', '_template.md'))
meta = {}

for path in docs:
    text = path.read_text(encoding='utf-8')
    fm = parse_frontmatter(text)
    if fm is None:
        problems.append(f'no frontmatter: {path.relative_to(ROOT)}')
        continue
    for key in REQUIRED_KEYS:
        if key not in fm:
            problems.append(f'missing frontmatter key {key!r}: {path.stem}')
    meta[path] = (fm, text)
    if fm.get('id') != path.stem:
        problems.append(f'id mismatch: {path.stem} declares id={fm.get("id")!r}')
    if fm.get('tier') != path.parent.name:
        problems.append(f'tier mismatch: {path.stem} declares tier={fm.get("tier")!r}')

# index rows match the documents on disk, one to one
readme = (PLAN / 'README.md').read_text(encoding='utf-8')
rows = re.findall(r'^\| \[([^\]]+)\]\(([^)]+)\)', readme, re.M)
if len(rows) != len(docs):
    problems.append(f'index drift: {len(docs)} documents, {len(rows)} index rows')
indexed = set()
for _, target in rows:
    resolved = (PLAN / target).resolve()
    indexed.add(resolved)
    if not resolved.exists():
        problems.append(f'index points at a missing file: {target}')
for path in docs:
    if path.resolve() not in indexed:
        problems.append(f'not in the index: {path.relative_to(ROOT)}')

# dependencies resolve and never point up-tier
ids = {fm['id']: p for p, (fm, _) in meta.items() if 'id' in fm}
tier_of = lambda p: int(p.parent.name.split('-')[0])
for path, (fm, _) in meta.items():
    for dep in parse_list(fm.get('depends_on')):
        if dep not in ids:
            problems.append(f'depends_on names an unknown document: {path.stem} -> {dep}')
        elif tier_of(ids[dep]) > tier_of(path):
            problems.append(f'UP-TIER dependency: {path.stem} ({path.parent.name}) '
                            f'-> {dep} ({ids[dep].parent.name})')

# exactly one owner per concept
owners = collections.defaultdict(list)
for path, (fm, _) in meta.items():
    for concept in parse_list(fm.get('owns')):
        owners[concept.lower()].append(path.stem)
for concept, holders in owners.items():
    if len(holders) > 1:
        problems.append(f'two documents own {concept!r}: {", ".join(holders)}')

# declared open-question count matches the entries
for path, (fm, text) in meta.items():
    declared = int(fm.get('open_questions', '0') or 0)
    section = re.search(r'^## Open questions\s*\n(.*)$', text, re.M | re.S)
    actual = len(re.findall(r'^\d+\. \*\*', section.group(1), re.M)) if section else 0
    if declared != actual:
        problems.append(f'open_questions mismatch: {path.stem} declares {declared}, has {actual}')

# every internal link resolves (outside code fences)
for path, (_, text) in meta.items():
    for link in re.findall(r'\]\((\.{0,2}[^)#\s]*\.md)\)', strip_fences(text)):
        if not (path.parent / link).resolve().exists():
            problems.append(f'broken link: {path.stem} -> {link}')

# every template section present
for path, (_, text) in meta.items():
    for section in REQUIRED_SECTIONS:
        if section not in text:
            problems.append(f'missing section {section!r}: {path.stem}')

# traceability, both directions
vision = (PLAN / '00-foundation' / '01-vision-and-scope.md').read_text(encoding='utf-8')
requirements = set(re.findall(r'`(R-\d+)`\s*\|', vision))
claimed = set()
for path, (fm, _) in meta.items():
    traces = parse_list(fm.get('traces_to'))
    if not traces and path.stem != '01-vision-and-scope':
        problems.append(f'empty traces_to: {path.stem}')
    for req in traces:
        claimed.add(req)
        if req not in requirements:
            problems.append(f'traces_to names an unknown requirement: {path.stem} -> {req}')
for req in sorted(requirements - claimed, key=lambda r: int(r[2:])):
    problems.append(f'requirement claimed by nobody: {req}')

# every requirement id mentioned anywhere is declared at the traceability root
for path in [*docs, PLAN / 'README.md']:
    text = strip_fences(path.read_text(encoding='utf-8'))
    for req in sorted(set(re.findall(r'\bR-\d+\b', text)) - requirements,
                      key=lambda r: int(r[2:])):
        problems.append(f'unknown requirement reference: {path.relative_to(ROOT)} -> {req}')

# every active decision's linked plan targets cite that decision at the constrained clause
decision_path = PLAN / '00-foundation' / '06-decision-log.md'
decision_text = decision_path.read_text(encoding='utf-8')
decision_parts = re.split(r'^## (D-\d+) ·', decision_text, flags=re.M)
for index in range(1, len(decision_parts), 2):
    decision_id = decision_parts[index]
    body = decision_parts[index + 1]
    status_prefix = '\n'.join(body.splitlines()[:5])
    if 'Superseded by' in status_prefix:
        continue
    constrains = re.search(r'\*\*Constrains\.\*\*(.*?)(?:\n---|\Z)', body, re.S)
    if not constrains:
        continue
    for link in re.findall(r'\]\(([^)#]+\.md)\)', constrains.group(1)):
        target = (decision_path.parent / link).resolve()
        if target not in {path.resolve() for path in docs}:
            continue
        if decision_id not in target.read_text(encoding='utf-8'):
            problems.append(
                f'decision target does not cite {decision_id}: {target.relative_to(ROOT)}'
            )

# closed questions belong in the decision log/history, not the active question count
for path, (_, text) in meta.items():
    section = re.search(r'^## Open questions\s*\n(.*)$', text, re.M | re.S)
    if not section:
        continue
    for match in re.finditer(r'^(\d+)\.\s+~~\*\*(.*?)\*\*~~\s+\*\*Closed',
                             section.group(1), re.M):
        problems.append(f'closed question retained as active: {path.stem} question {match.group(1)}')

print(f'documents {len(docs)} · index rows {len(rows)} · '
      f'requirements {len(requirements)} ({len(claimed & requirements)} claimed) · '
      f'open questions {sum(int(fm.get("open_questions", 0) or 0) for fm, _ in meta.values())}')
print()
if problems:
    print(f'{len(problems)} problems')
    for p in problems:
        print(f'  {p}')
    sys.exit(1)
print('all structural checks pass')
