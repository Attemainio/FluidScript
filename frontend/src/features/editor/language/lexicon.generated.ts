/* Generated from src/FluidScript.Api/Contracts/Schemas/language.json by npm run types. Do not edit. */

/** The words that open a statement at a line's start, in `19`'s order; the lexer reserves none. */
export const statementWords: readonly string[] = [
  'fluidscript',
  'project',
  'let',
  'curve',
  'circuit',
  'run',
];

/** The words that open an event, inside a run only. */
export const eventWords: readonly string[] = [
  'at',
  'over',
];

/** Every unit spelling a script may write after a number, longest first, as the lexer probes them (maximal munch). */
export const unitSymbols: readonly string[] = [
  'kJ/(kg*K)',
  'kW/(m2*K)',
  'J/(kg*K)',
  'W/(m2*K)',
  'm2*K/W',
  'kJ/kg',
  'kg/m3',
  'l/min',
  'mbara',
  'mmH2O',
  'J/kg',
  'MPaa',
  'bara',
  'barg',
  'dbar',
  'dkPa',
  'kPaa',
  'kPag',
  'kg/h',
  'kg/s',
  'km/h',
  'm/s2',
  'm3/h',
  'm3/s',
  'mH2O',
  'mbar',
  'psia',
  'MPa',
  'MWh',
  'Paa',
  'bar',
  'cm2',
  'dPa',
  'dm3',
  'kPa',
  'kWh',
  'l/h',
  'l/s',
  'm/s',
  'min',
  'mm2',
  'psi',
  't/h',
  'MJ',
  'MW',
  'Pa',
  'Wh',
  'cm',
  'dC',
  'dK',
  'dm',
  'ft',
  'hp',
  'kJ',
  'kW',
  'kg',
  'km',
  'm2',
  'm3',
  'ml',
  'mm',
  'ms',
  'px',
  '°C',
  '°F',
  '%',
  'C',
  'F',
  'J',
  'K',
  'W',
  'd',
  'g',
  'h',
  'l',
  'm',
  's',
];

/** The similarity at which a misspelled kind or parameter is offered as the fix (`D-15`; since `D-170` it never binds). */
export const resolveThreshold = 0.7;

/** How far clear of the runner-up a match must be, or both are reported. */
export const ambiguityMargin = 0.05;

/** The score below which a failed match carries no suggestion. */
export const suggestionFloor = 0.6;
