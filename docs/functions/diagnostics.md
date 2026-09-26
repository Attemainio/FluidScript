# Diagnostics

Every message FluidScript shows you about a script carries a code, a severity, and — when it is about
something you wrote — the exact piece of text it is about. This page is the complete list of codes,
and what each one means.

## Reading a code

A code is `FS` followed by four digits, for example `FS1302`.

**A code never changes meaning.** Once a code has been given a meaning it keeps it, and it is never
renumbered. That is what makes a code safe to write into a note, a test, or a prompt: `FS1302` will
mean the same thing next year. When a rule changes so much that a code no longer applies, the code is
withdrawn rather than re-used, and it appears under [Withdrawn codes](#withdrawn-codes) below.

The first two digits say which part of FluidScript noticed the problem, which is usually the first
thing worth knowing. The **Reported by** column in the table below spells that out for each code.

## Severities

| Severity | What it means | What happens |
|---|---|---|
| Error | The thing it is about cannot be used | That one component, connection, or line is skipped |
| Warning | It was used, but it probably does not say what was meant | Nothing — the script runs as written |
| Info | Something was decided for you | Nothing |

**Nothing stops at the first problem.** An error is always about one element — one component, one
connection, one line — and everything else in the script still runs. A script with three errors in it
still sizes, still solves, and still draws the parts that are intact. This is deliberate: a script
being edited is incomplete most of the time, and a diagram that blanks on every keystroke is useless.

An error does mean the element it names is left out of the result, so a downstream number that depends
on it will be missing too. Fix errors from the top of the list down; later ones often disappear on
their own.

## Suggestions

Some messages come with a suggested edit — a replacement for an exact piece of your script, which the
editor can apply for you. A suggestion is offered only when there is one certainly correct answer. Two
plausible readings means no suggestion, and a message that explains the choice instead.

## Codes

<!-- BEGIN GENERATED: diagnostic-codes -->
| Code | Severity | About | Message |
|---|---|---|---|
| `FS1001` | Error | Lexer | Unterminated string; add a closing quote. |
| `FS1002` | Error | Lexer | '{ch}' is not valid here. |
| `FS1003` | Error | Lexer | '{name}' reads as a quantity ({value} {unit}), not a name. Try '{suggestion}'. |
| `FS1004` | Error | Lexer | '{word}' is reserved. Choose another name. |
| `FS1104` | Error | Parser | Cannot read this line. Expected a declaration such as 'PU1 pump', a connection such as 'A - B', or a setting such as 'name = value'. |
| `FS1105` | Error | Parser | '{token}' looks like a parameter but has no value. Write '{token} = …'. |
| `FS1108` | Error | Parser | '{text}' — a name cannot contain '-'. Write '{underscored}'. |
| `FS1114` | Error | Parser | '{extra}' is more than this line can hold. |
| `FS1115` | Error | Parser | Put this pair under a 'curve' line. |
| `FS1116` | Error | Parser | 'curve {name}' needs what it depends on after a colon, such as 'curve {name}: outdoor'. |
| `FS1117` | Error | Parser | A curve row is one x and one y, such as '-26 50'. |
| `FS1119` | Error | Parser | An index is a whole number in brackets right after the name, such as 'in[2]'. |
| `FS1121` | Error | Parser | A list is one value per case, separated by commas, such as '[30, 10]'. |
| `FS1202` | Warning | Style directive | '{a}' overrides the earlier '{b}'. |
| `FS1203` | Warning | Style directive | '#' starts a comment; the rest of this line was ignored. Write the colour as "{hex}". |
| `FS1210` | Warning | Style directive | Nothing to show called '{name}'. Available: {list}. |
| `FS1213` | Info | Style directive | '{name}' listed twice. |
| `FS1214` | Warning | Style directive | Only the first 'show' is used. |
| `FS1302` | Error | Units | Cannot add two {dimension}s. To offset by a difference, write '{example}'. |
| `FS1304` | Error | Units | '{parameter}' is a {expected}; '{value}' is a {actual}. |
| `FS1305` | Error | Units | Cannot {operation} a {left} and a {right}. |
| `FS1306` | Warning | Units | {parameter} = {value} is outside the usual range ({low}–{high}). Check the unit. |
| `FS1307` | Error | Units | {parameter} cannot be negative. |
| `FS1308` | Warning | Units | '{component}' is a {kind}, whose power is a capacity: {value} is read as {magnitude}. Write it positive, or use 'heat_exchanger' for a signed heat flow. |
| `FS1401` | Error | Expressions | '{name}' is already defined at line {line}. |
| `FS1402` | Error | Expressions | '{name}' depends on itself: {cycle}. |
| `FS1403` | Error | Expressions | Dividing by zero here. '{expression}' is zero. |
| `FS1404` | Error | Expressions | Nothing named '{name}'. |
| `FS1405` | Error | Expressions | '{expr}' did not settle: {v1} then {v2} then {v3}. Try stating a value directly. |
| `FS1406` | Error | Expressions | A {kind} has no '{property}'. It has: {available}. |
| `FS1408` | Error | Expressions | No function '{name}'. Available: {available}. |
| `FS1409` | Error | Expressions | '{function}' takes {expected} arguments. |
| `FS1410` | Warning | Expressions | '{target} = {expr}' was never evaluated: {waited} is not published by any pass, so the value was chosen as if the line were absent. State a value directly. |
| `FS1411` | Error | Expressions | '{name}' is reserved for {what}, {value}. Choose another name. |
| `FS1412` | Warning | Expressions | '{target} = {expr}' was still waiting on {waited} when pass {pass} failed, so the circuit was solved without it. State it directly, or from a value the seed can supply. |
| `FS1501` | Error | Binder | '{name}' is already declared at line {line}. Names are unique across the whole file; tags are what distinguish circuits. |
| `FS1502` | Error | Binder | There is no '{kind}'. |
| `FS1503` | Error | Binder | A {kind} has no '{parameter}'. It accepts: {available}. |
| `FS1504` | Error | Binder | '{name}' is a value, not a component. |
| `FS1505` | Error | Binder | A {kind} has no port '{port}'. Ports: {available}. |
| `FS1506` | Error | Binder | Port '{port}' of '{name}' is already connected at line {line}. |
| `FS1507` | Warning | Binder | '{name}' is not connected to anything. |
| `FS1510` | Info | Binder | Added {kind} '{name}' ({rule}). |
| `FS1511` | Warning | Binder | '{name}' and {count} others are not connected to the rest of the circuit. |
| `FS1513` | Error | Binder | '{written}' could be '{first}' or '{second}'. Write one of them. |
| `FS1514` | Error | Binder | '{parameter}' accepts {available}; '{written}' is none of them. |
| `FS1515` | Error | Binder | '{parameter}' names a component property, like 'N2.t'. |
| `FS1516` | Error | Binder | '{written}' is outside {kind}'s supported {min}…{max} range. |
| `FS1519` | Info | Binder | '{name}' is not a known circuit role, so it is placed neutrally. Known roles: {available}. |
| `FS1521` | Error | Binder | A controller needs {list}. Missing: {missing}. |
| `FS1522` | Error | Binder | '{param}' of '{component}' cannot be controlled. |
| `FS1523` | Error | Binder | '{name}' is a {kind}, not a controller. |
| `FS1524` | Error | Binder | Circuit {number} is already '{owner}'. Every circuit's number is its own. |
| `FS1525` | Error | Binder | '{name}' is already a circuit at line {line}. |
| `FS1528` | Error | Binder | '{curve}' follows '{driver}', which only a run has. Drive the curve by a let with one value per case, and have the run hand that let a curve of time. |
| `FS1529` | Info | Binder | '{curve}' has two rows at {x}; the later one is used. |
| `FS1530` | Error | Binder | '{curve}' needs at least two rows to interpolate between. |
| `FS1531` | Error | Binder | A {kind} has no single {role}. Write it out, such as '{example}'. |
| `FS1532` | Error | Binder | '{name}' is a {kind}, which is not placed with 'at'. Connect it with '-' instead. |
| `FS1533` | Warning | Binder | '{name}' observes nothing. Put it in a chain, such as 'A - {name} - B', or place it with 'at' and the name of a node. |
| `FS1534` | Error | Binder | '{curve}' has a format that cannot read a date: {reason}. Write a quoted .NET pattern with a day and a month, such as format="dd/MM/yyyy HH:mm". |
| `FS1535` | Error | Binder | '{curve}': {count} more rows could not be read; the first {shown} are marked. Check the columns and the format. |
| `FS1537` | Error | Binder | A {kind} has one state and no ports: write '{quantity} =' rather than '{written} ='. |
| `FS1538` | Error | Binder | A {kind}'s '{port}' has no '{quantity}'. It takes: {available}. |
| `FS1539` | Error | Binder | '{written}' states the pressure of '{node}', which '{other}' already states. State it once. |
| `FS1540` | Error | Binder | '{written}' states {given} {values} for {count} case{plural}: {names}. State one per case, or one value for all of them. |
| `FS1541` | Error | Binder | '{written}' states a list of values, but this file declares no cases. Add 'cases = [<name>, <name>]' to the project block. |
| `FS1542` | Error | Binder | '{name}' is not a case of this file. It declares: {names}. |
| `FS1544` | Error | Binder | '{name}' is declared twice. Each case needs its own name. |
| `FS1545` | Error | Binder | start = {value} is not a time. Write it as a date, such as start = 2026-01-15 06:00. |
| `FS1546` | Warning | Binder | This follows '{curve}', which runs on the clock, and the run does not say where it starts. Add 'start = …' to the run; until then it holds the curve at its design value. |
| `FS1548` | Error | Binder | '{name}' reads '{node}', where {count} pipes meet, and a junction has no single stream to measure. Put a node on the pipe you mean, next to '{node}', and read that one. |
| `FS1549` | Warning | Binder | '{component}' is sized at {point}, and none of its parameters read '{driver}', so it changes nothing. Read '{driver}' in a parameter, directly or through a curve, or remove the point. |
| `FS1701` | Info | Compatibility | This draft states no language version. Add 'fluidscript {major}' as its first line to save it. |
| `FS1702` | Error | Compatibility | This file is FluidScript {major}, which this version cannot read. It understands {supported}. |
| `FS1705` | Error | Compatibility | This file says it is FluidScript {first} and also {second}. Delete the line that is wrong. |
| `FS1801` | Error | Language2 | This line is indented unlike the rest of its block. Indent it as the line above it is. |
| `FS1802` | Error | Language2 | {statement} belongs {place}. |
| `FS1803` | Error | Language2 | A pipe's length and size describe one link, and this line has {links}. Put the pipe on a line of its own: '{first} - {second} {properties}'. |
| `FS1804` | Error | Language2 | '{component}' cannot take this connection: {reason}. Name the port, such as '{example}'. |
| `FS1805` | Error | Language2 | '{component}' is written as a {asserted} valve, and its connections make it {actual}: {inflows} in and {outflows} out. |
| `FS1806` | Error | Language2 | '{word}' is language 1. In language 2, {instead}. |
| `FS1807` | Error | Language2 | A ramp needs both ends of {half}, such as '{example}'. For a step, write 'at'. |
| `FS1808` | Error | Language2 | '{controller}' is a {type} controller, which has no '{parameter}'. A {type} controller takes: {available}. |
| `FS1809` | Error | Language2 | '{controller}' states both band and kp, and each says the other. State one. |
| `FS1810` | Warning | Language2 | '{controller}' is a {type} controller, which the solver does not run yet. |
| `FS1811` | Error | Language2 | '{curve}' is driven by '{driver}', which is not a let. Write 'let {driver} = [...]' with one value per case, or drive it by time. |
| `FS1812` | Error | Language2 | A {head} line opens a block and ends with ':'. |
| `FS1813` | Error | Language2 | '{text}' is not a pipe size. Write a DN designation such as DN25, or name the property: 'roughness = 0.05 mm'. |
| `FS1814` | Error | Language2 | '{sensor}' sits in a chain and is also placed at '{node}'. Keep one: in a chain it reads the point where it sits. |
| `FS1815` | Info | Language2 | '{component}' is wired as {wiring}. |
| `FS1816` | Error | Language2 | '{time}' is a clock time, and '{run}' states no start. Write 'start = 2026-01-15 06:00' in the run, or a duration such as '30 min'. |
| `FS2001` | Error | Substances | There is no fluid called '{name}'. Available: {list}. |
| `FS2002` | Error | Substances | Cannot fix a state from {a} and {b}; they are not independent here. |
| `FS2003` | Error | Substances | {name} data covers {lo} to {hi}; this state is at {value}. |
| `FS2004` | Error | Substances | Could not evaluate {property} for {name} at {state}. |
| `FS2006` | Error | Substances | Relative humidity must be between 0 and 100 %. |
| `FS2101` | Error | Components | '{name}': {parameters} cannot all be set. Any {count} of them fix the rest. |
| `FS2103` | Warning | Components | '{name}': using kv={kv}; dp is implied by it. |
| `FS2105` | Error | Components | '{name}': position must be between 0 and 1. |
| `FS2107` | Warning | Components | '{name}' is a dead end. Declare it 'inlet' or 'outlet' if fluid crosses there; a node's t or p only states a level and passes no mass. |
| `FS2108` | Error | Components | '{name}': efficiency must be between 0 and 1. |
| `FS2109` | Error | Components | '{name}': primary.in.t, primary.out.t, secondary.in.t, secondary.out.t and power already fix the thermal size. Remove {param}, or let a temperature be solved. |
| `FS2110` | Warning | Components | '{name}': '{param}' has no secondary side to rate. State secondary.in.t, secondary.out.t, secondary.in.dt or secondary.in.flow, connect both secondary ports, or remove it. |
| `FS2111` | Error | Components | {name} cannot transfer {power} kW: with {t_hot} and {t_cold} in, the most any exchanger could move is {qmax} kW. |
| `FS2112` | Error | Components | '{name}': a coupled exchanger needs both secondary.in and secondary.out connected; {port} is open. |
| `FS2113` | Error | Components | '{name}': state either t for every layer, or all of layer[1].t…layer[{layers}].t; do not mix them. |
| `FS2114` | Error | Components | '{name}': layers must be a whole number from 1 to 100. |
| `FS2115` | Error | Components | '{name}': {parameter} is a normalized level and must be between 0 (bottom) and 1 (top). |
| `FS2117` | Error | Components | '{name}': the {kind} must state {parameter}. |
| `FS2118` | Error | Components | '{name}': a {kind} must state {count} of {parameters}. |
| `FS2119` | Error | Components | '{name}': power = {power} means the {side} side {duty}, but {inlet} = {in} and {outlet} = {out} say the water {change}{cases}. Flip the sign, swap the temperatures, or use a role word such as load or heater. |
| `FS2201` | Warning | Topology | Using '{node}' as the pressure datum. Pressures are relative to it. |
| `FS2202` | Warning | Topology | '{component}' port '{port}' is not connected; treating it as closed. |
| `FS2203` | Error | Topology | '{circuit}' is closed and its heat does not balance: {power} with nowhere to go. Add a load, a source, or a boundary. |
| `FS2204` | Error | Topology | '{circuit}' has an {present} and no {missing}. Fluid must both enter and leave, or neither. |
| `FS2210` | Error | Topology | This circuit is over-specified by {n}. Remove one of: {list}{advice}. |
| `FS2211` | Error | Topology | This circuit is under-specified by {n}. Add one of: {list}. |
| `FS2212` | Error | Topology | '{a}' and '{b}' both set a pressure on the same closed loop, with no path between them for flow to take. Remove one, or connect them. |
| `FS2213` | Info | Topology | Nothing connects '{list}' to the rest of the plant, so that part is solved as a system of its own. |
| `FS2214` | Warning | Topology | Nothing drives flow around {loop}; it will carry none. Is a pump on the wrong leg? |
| `FS2215` | Error | Topology | {substance} cannot be at {state}. |
| `FS2216` | Info | Topology | '{component}' touches {a} and {b} with no clear heat direction; tagging it into {chosen}. |
| `FS2218` | Info | Topology | '{constraint}' is held by '{pump}', on another branch of its loop: the flow is set through the pressure the two branches share. If a pump on its own branch was meant to hold it, free that one. |
| `FS2219` | Error | Topology | '{second}' at {b} m is wired directly to '{first}' at {a} m. Put a pipe between them, or give them one height. |
| `FS2220` | Error | Topology | '{node}' is {rise} m above '{datum}', which puts it {short} kPa below the lowest pressure {substance} can be at. State a pressure on '{datum}' of at least {needed} kPa. |
| `FS2221` | Warning | Topology | '{node}' is {short} kPa below atmospheric pressure. State a pressure on '{datum}' of at least {needed} kPa. |
| `FS2301` | Warning | Sizing | Sizes did not settle for {list}. Showing the last values; state them directly to fix. |
| `FS2304` | Error | Sizing | Cannot size '{name}': no flow is determined anywhere in its branch. State a duty or a flow. |
| `FS2305` | Warning | Sizing | '{name}' needs more than DN{max}, the largest size in {catalog}. Using DN{max}. |
| `FS2307` | Info | Sizing | '{name}' stepped up to DN{n} for velocity. |
| `FS2310` | Info | Sizing | '{name}' sized to {plates} plates ({area} m²); {required} m² was needed, so it delivers {actual} kW against {stated} kW. |
| `FS2312` | Info | Sizing | '{name}' sized to zero head because its circuit contains no modelled resistance. Add a pipe, valve, exchanger drop, or other loss if resistance is intended. |
| `FS2314` | Warning | Sizing | '{name}' carries no duty and no flow in any of the {count} cases ({names}), so nothing sizes it. If it exists to serve two demands that peak in different cases, the case where both are on is not in the list. |
| `FS2401` | Info | Layout hints | The circuit closes on itself, so components are ordered by a depth-first walk from the pressure datum. |
| `FS2402` | Info | Layout hints | '{group}' has {count} members and will start collapsed; expand it on the canvas to see them. |
| `FS2403` | Info | Layout hints | '{circuit}' is named as a {role} circuit but its stated duties make it a {stage}; the duties decide where it is drawn. |
| `FS2501` | Error | Model contract | '{component}.{field}' is {value} and cannot be written in {unit}; the field is sent empty. |
| `FS2502` | Warning | Model contract | The model is {size} KiB with states, over the {cap} KiB cap; states are omitted and can be fetched per component. |
| `FS2603` | Error | Catalog | No catalogue '{name}'. Available: {list}. |
| `FS2604` | Error | Catalog | Catalogue '{name}' is invalid: {reason}. |
| `FS2605` | Error | Catalog | Catalogue '{name}' has {count} row(s) without two verified public sources, starting at '{first}'. An unverified dimension is a wrong design nobody can see. |
| `FS2606` | Info | Catalog | Using catalogue '{name}'. Write 'catalog = {name}' in the project block to pin it. |
| `FS3001` | Error | Solver | Could not solve in {steps} steps. Furthest off: {component} {equation} by {amount}. |
| `FS3002` | Error | Solver | The circuit has no unique solution around {component}. Check for a missing pressure datum, a closed circuit with no stated temperature, or a loop with no driver. |
| `FS3003` | Error | Solver | The solution is moving away from a balance: {residual} after {steps} steps, from {previous}. |
| `FS3004` | Error | Solver | Stuck at {residual}: {component} {equation} may have conflicting requirements. |
| `FS3005` | Error | Solver | {solver} cannot solve this: {reason}. |
| `FS3006` | Info | Solver | Solve cancelled after {steps} steps. |
| `FS3007` | Error | Solver | {component} produced an impossible value in {equation} after {steps} steps. |
| `FS3008` | Warning | Solver | {parameter} was held at {bound}, which is as far as it goes. The circuit is asking for more than this component can give: check the duty, the resistance, or a stated temperature it cannot reach. |
| `FS3009` | Error | Solver | Nothing in the circuit determines {combination}. These move together and no equation separates them, so a value stated for any one of them determines the rest. |
| `FS3010` | Error | Solver | {combination} are not independent: one of them is already implied by the others, so the circuit constrains one thing fewer than it appears to. Stating something elsewhere will not help — one of these has to change. |
| `FS3011` | Info | Solver | Taking a reduced step near {component}; the solution is hard to reach here. |
| `FS3012` | Info | Solver | Restarted from the initial estimate. |
| `FS3013` | Warning | Solver | {component} carries {flow} kg/s from '{outlet}' to '{inlet}', against its written direction{note}. |
| `FS3014` | Warning | Solver | {parameter} solved to {head} m: the header pushes forward through {component}'s stopped branch and a pump cannot resist that. Close the branch -- an isolation valve, or the mixing valve at its stop -- or the plant runs through it. |
| `FS3015` | Info | Solver | {parameter} runs dead-headed at {head} m: it holds {component}'s stopped branch still against the header pushing backwards through it. If that pump is off, nothing here stops the flow: add a check valve to the branch, or state the flow you expect through it. |
| `FS3016` | Warning | Solver | {parameters} move together along a valley of the circuit: one series path fixes only their combination, so the values shown are one answer among those the script allows. State all but one of them, or decouple the blocks so the path is no longer shared. |
| `FS3101` | Info | Transient | Step limited to {step} s by '{component}'. Fewer internal nodes would run faster. |
| `FS3102` | Error | Transient | The simulation cannot advance past {time} s. Something is changing faster than the model can follow. |
| `FS3103` | Error | Transient | Could not balance the circuit at t = {time} s: {inner}. |
| `FS3104` | Info | Transient | Still changing at {horizon} s. Extend the run to see it settle. |
| `FS3105` | Error | Transient | Cannot change '{target}' — {reason}. |
| `FS3106` | Warning | Transient | Energy balance drifted by {pct} % over the run. Results may be unreliable. |
| `FS3107` | Error | Transient | Simulation stopped at {time} s because {invariant} failed. The last verified frame is {sequence}. |
| `FS3108` | Error | Transient | Cannot initialize '{tank}' layer {layer} at {state}. |
| `FS3109` | Error | Transient | '{target}' is driven by {controller}; an event in a run cannot also move it. |
| `FS3210` | Info | Controllers | {controller} may start off its setpoint: {reason}, so the design solve did not hold {measurement} at {setpoint}. |
| `FS3211` | Info | Controllers | {controller} measures {measurement}, which the design solve cannot hold at a setpoint; only a node's temperature can be. The run starts wherever the design solve lands. |
| `FS4008` | Error | Design warning | '{name}': the approach is {approach} K, below the {minimum} K it must respect. Raise the duty's temperature difference, or accept a closer approach with approach = {approach}. |
| `FS4011` | Warning | Design warning | '{name}' throttles its {leg} leg by {drop} kPa at position {position}: that path is {imbalance} kPa easier than the {other} path, more than the {band} kPa the valve drops fully open. A balancing valve between {where} and {name}.{leg} dropping {imbalance} kPa at {flow} kg/s (Kv {kv}) would level the legs and leave the valve its travel. |
| `FS4012` | Warning | Design warning | '{name}' is written as a {declared} valve and the solve runs it {actual}: {detail}. A body built for one service must not be used for the other. Write it as three_way_valve if the arrangement is open, or wire the ports for {declared}. |
| `FS4013` | Warning | Design warning | '{name}' must pass {light} kg/s in {lightCase} and {heavy} kg/s in {heavyCase}, {ratio} % of its heaviest flow. {trim} valve at authority {authority} controls down to about {limit} % ({range}:1 × √{authority}), so in {lightCase} it will open and shut rather than modulate. Give the light case a smaller valve in parallel, or split the duty. |
| `FS4601` | Error | Request | The script has {count} {what}; the limit is {max}, so it is not solved. |
| `FS5002` | Warning | Rendering | The drawing breaks its own {rule} rule between '{first}' and '{second}' ({detail}); the picture is unreliable there. |
<!-- END GENERATED: diagnostic-codes -->

## Withdrawn codes

These codes were used once and never will be again. They are listed so that a code found in an old
note or an old script can still be looked up, and so that it is clear the number has not been quietly
given to something else.

<!-- BEGIN GENERATED: retired-diagnostic-codes -->
| Code | Why it is no longer reported |
|---|---|
| `FS1101` | A second 'connections' or 'schedule' section in one circuit. Language 1's sections; language 2 has none, and language 1 was removed (D-174). |
| `FS1102` | A connection above language 1's 'connections' line. Language 2 has no sections (D-174). |
| `FS1103` | A statement in the wrong language 1 section. Language 2 places a statement by its block, which is FS1802 (D-174). |
| `FS1106` | A step or ramp outside language 1's 'schedule' section. In language 2 an event outside a run is FS1802 (D-169, D-174). |
| `FS1107` | A language 1 schedule in a circuit with no time to run in. Language 2's events belong to a run, which has time by construction (D-169, D-174). |
| `FS1109` | 'in' or 'out' where language 1's 'inlet'/'outlet' attachment line was meant. Language 2 has no attachment lines (D-174). |
| `FS1110` | A malformed language 1 'inlet'/'outlet' attachment line. Language 2 has none (D-174). |
| `FS1111` | A malformed language 1 'control' line. Language 2 writes a loop as one controller declaration (D-168, D-174). |
| `FS1112` | Language 1's 'project' or 'spacing' line after the first circuit. Language 2 writes both in the project block (D-174). |
| `FS1113` | Language 1's 'spacing' line given a quantity. Language 2's spacing is a setting of the project block (D-174). |
| `FS1118` | Language 1's 'design' line with no values. Language 2 has no 'design' line: the first case is the operating one (D-174). |
| `FS1120` | Language 1's 'scenarios' line with no names. Language 2 writes 'cases = [...]' (D-174). |
| `FS1201` | A token of language 1's style line that was no style. Language 2 checks each style setting against its key, which is FS1514 (L-77, D-174). |
| `FS1204` | A named style used that language 1's 'style name = ...' never defined. Language 2 has no named styles and no component style (19, D-174). |
| `FS1205` | A named style defined twice in language 1. Language 2 has no named styles (19, D-174). |
| `FS1508` | Language 1 statements before any 'circuit' line, read into an implicit circuit. Language 2 declares a component only inside a circuit block, which is FS1802 (L-70, D-174). |
| `FS1509` | Meant 'more than one circuit header', which is now legal: a script may declare several numbered circuits. Two circuits claiming one number is a different condition and took a new code rather than inheriting this one. |
| `FS1512` | A name bound to the registered spelling it was near, with a note. Language 2 binds only exact spellings and offers the near one as the fix (D-170, D-174). |
| `FS1517` | Language 1's circuit mode ('fluid water dynamic') contradicting the project's. Language 2 states modes per run (D-169, D-174). |
| `FS1518` | A language 1 attachment line naming no component. Language 2 has no attachment lines (D-174). |
| `FS1520` | A language 1 circuit with an inlet attachment and no outlet, or the reverse. Language 2 has no attachment lines (D-174). |
| `FS1526` | A language 1 circuit attached to two parent circuits. Language 2 has no attachment lines (D-174). |
| `FS1527` | A language 1 curve driven by a name that was no role, curve or design value. Language 2's driver is a let or time, which is FS1811 (D-167, D-174). |
| `FS1536` | A port, parameter or property in the spelling D-120 replaced (in2, t3, HX1.t_in2), bound with a note for one language major. Language 2 is the next major and does not read them (18, L-79). |
| `FS1543` | Language 1's scenarios with no 'design' line. Language 2's first case is the operating one (D-174). |
| `FS1547` | Language 1's project 'start=' with no dynamic circuit to read it. Language 2's start is a run setting (D-169, D-174). |
| `FS2217` | A language 1 attachment to a component of the attaching circuit itself. Language 2 has no attachment lines (D-174). |
<!-- END GENERATED: retired-diagnostic-codes -->
