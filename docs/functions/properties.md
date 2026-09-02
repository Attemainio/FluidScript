# Properties

Every value you can read back off a component, written `Name.property` — in an expression, in a
[`let`](let.md) binding, or as the measurement point of a [`control`](control.md) line.

A property is not a parameter. `power` is both something you set on a heat exchanger and something you
read off it; `dp` is only ever read. Setting a property is an error that names the parameter you
probably meant.

**When it exists matters.** A property marked *after the solve* has no value while you are still
typing the circuit, and a reference to one from somewhere that is needed earlier — a sizing input, for
instance — is reported rather than silently zero.

<!-- BEGIN GENERATED: component-properties -->
| Kind | Property | Unit | Available |
|---|---|---|---|
| `node` | `flow` | `kg/s` | after the solve |
| `node` | `h` | `J/kg` | after the solve |
| `node` | `p` | `kPa` | after the solve |
| `node` | `rho` | `kg/m3` | after the solve |
| `node` | `t` | `°C` | after the solve |
| `pipe` | `diameter` | `m` | after sizing |
| `pipe` | `dn` | — | after sizing |
| `pipe` | `dp` | `kPa` | after the solve |
| `pipe` | `flow` | `kg/s` | after the solve |
| `pipe` | `re` | — | after the solve |
| `pipe` | `velocity` | `m/s` | after the solve |
| `pipe` | `volume` | `dm3` | after sizing |
| `heat_exchanger` | `approach` | `dK` | after the solve |
| `heat_exchanger` | `area` | `m2` | after sizing |
| `heat_exchanger` | `dp` | `kPa` | after the solve |
| `heat_exchanger` | `dp2` | `kPa` | after the solve |
| `heat_exchanger` | `dt` | `dK` | after the solve |
| `heat_exchanger` | `dt2` | `dK` | after the solve |
| `heat_exchanger` | `effectiveness` | — | after the solve |
| `heat_exchanger` | `flow` | `kg/s` | after the solve |
| `heat_exchanger` | `flow2` | `kg/s` | after the solve |
| `heat_exchanger` | `lmtd` | `dK` | after the solve |
| `heat_exchanger` | `ntu` | — | after sizing |
| `heat_exchanger` | `plates` | — | after sizing |
| `heat_exchanger` | `power` | `kW` | after sizing |
| `heat_exchanger` | `t_in` | `°C` | after the solve |
| `heat_exchanger` | `t_in2` | `°C` | after the solve |
| `heat_exchanger` | `t_out` | `°C` | after the solve |
| `heat_exchanger` | `t_out2` | `°C` | after the solve |
| `heat_exchanger` | `u` | — | after sizing |
| `heat_exchanger` | `ua` | — | after sizing |
| `valve` | `authority` | — | after sizing |
| `valve` | `dp` | `kPa` | after the solve |
| `valve` | `flow` | `kg/s` | after the solve |
| `valve` | `kv` | `m3/h` | after sizing |
| `valve` | `position` | — | as written |
| `three_way_valve` | `authority` | — | after sizing |
| `three_way_valve` | `dp` | `kPa` | after the solve |
| `three_way_valve` | `flow` | `kg/s` | after the solve |
| `three_way_valve` | `kv` | `m3/h` | after sizing |
| `three_way_valve` | `position` | — | as written |
| `pump` | `dp` | `kPa` | after the solve |
| `pump` | `efficiency` | — | after sizing |
| `pump` | `flow` | `kg/s` | after the solve |
| `pump` | `head` | `m` | after sizing |
| `pump` | `power` | `kW` | after the solve |
| `pump` | `speed` | — | after the solve |
| `tank` | `layers` | — | as written |
| `tank` | `stored_energy` | `J` | after the solve |
| `tank` | `volume` | `dm3` | as written |
| `t_sensor` | `t` | `°C` | after the solve |
| `p_sensor` | `p` | `kPa` | after the solve |
| `flow_sensor` | `flow` | `kg/s` | after the solve |
<!-- END GENERATED: component-properties -->

## See also

[Units](units.md) · [`let`](let.md) · [`control`](control.md)
