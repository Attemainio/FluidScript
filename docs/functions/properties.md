# Properties

Every value you can read back off a component, written `Name.property` — in an expression, in a
[`let`](let.md) binding, or as the measurement point of a [`control`](control.md) line.

A property is not a parameter. `power` is both something you set on a heat exchanger and something you
read off it; `dp` is only ever read. Setting a property is an error that names the parameter you
probably meant.

**When it exists matters.** A property marked *after the solve* has no value while you are still
typing the circuit, and a reference to one from somewhere that is needed earlier — a sizing input, for
instance — is reported rather than silently zero.

**A `d` in front of a quantity is its change across the component.** `HE1.dt` is the outlet
temperature less the inlet (negative on a cooler), `HE1.dh` the enthalpy change — the duty per
kilogram — and `PU1.dp` the pressure drop, inlet less outlet, so it is negative across a pump. The
declared `dt=20` on an exchanger is still a magnitude whose direction the role word supplies; read
back as `.dt` it is signed. `dn` is a pipe size, not "delta n".

**A port's state is a property of the port.** `HX1.in[2].t` is the temperature entering the
exchanger's second side, `T1.out.t` the temperature at a tank's first outlet, `T1.layer[3].t` its
third layer from the bottom; `in[1]` is `in`. The quantity may be spelled long — `HX1.in[2].temperature`
— and the old flat names (`HX1.t_in2`, `T1.t3`, `T1.in2_t`) still resolve with a notice pointing at
the new one ([`FS1536`](diagnostics.md)). The [syntax page](syntax.md#a-ports-state) has the rule.

<!-- BEGIN GENERATED: component-properties -->
| Kind | Property | Unit | Available |
|---|---|---|---|
| `node` | `flow` | `kg/s` | after the solve |
| `node` | `h` | `J/kg` | after the solve |
| `node` | `p` | `kPa` | after the solve |
| `node` | `rho` | `kg/m3` | after the solve |
| `node` | `t` | `°C` | after the solve |
| `inlet` | `flow` | `kg/s` | after the solve |
| `inlet` | `h` | `J/kg` | after the solve |
| `inlet` | `p` | `kPa` | after the solve |
| `inlet` | `rho` | `kg/m3` | after the solve |
| `inlet` | `t` | `°C` | after the solve |
| `outlet` | `flow` | `kg/s` | after the solve |
| `outlet` | `h` | `J/kg` | after the solve |
| `outlet` | `p` | `kPa` | after the solve |
| `outlet` | `rho` | `kg/m3` | after the solve |
| `outlet` | `t` | `°C` | after the solve |
| `pipe` | `diameter` | `m` | after sizing |
| `pipe` | `dn` | — | after sizing |
| `pipe` | `dp` | `kPa` | after the solve |
| `pipe` | `flow` | `kg/s` | after the solve |
| `pipe` | `in.p` | `kPa` | after the solve |
| `pipe` | `out.p` | `kPa` | after the solve |
| `pipe` | `re` | — | after the solve |
| `pipe` | `velocity` | `m/s` | after the solve |
| `pipe` | `volume` | `dm3` | after sizing |
| `heat_exchanger` | `approach` | `dK` | after the solve |
| `heat_exchanger` | `area` | `m2` | after sizing |
| `heat_exchanger` | `dp` | `kPa` | after the solve |
| `heat_exchanger` | `dt` | `dK` | after the solve |
| `heat_exchanger` | `effectiveness` | — | after the solve |
| `heat_exchanger` | `flow` | `kg/s` | after the solve |
| `heat_exchanger` | `in.p` | `kPa` | after the solve |
| `heat_exchanger` | `in.t` | `°C` | after the solve |
| `heat_exchanger` | `in[2].dp` | `kPa` | after the solve |
| `heat_exchanger` | `in[2].dt` | `dK` | after the solve |
| `heat_exchanger` | `in[2].flow` | `kg/s` | after the solve |
| `heat_exchanger` | `in[2].p` | `kPa` | after the solve |
| `heat_exchanger` | `in[2].t` | `°C` | after the solve |
| `heat_exchanger` | `lmtd` | `dK` | after the solve |
| `heat_exchanger` | `ntu` | — | after sizing |
| `heat_exchanger` | `out.p` | `kPa` | after the solve |
| `heat_exchanger` | `out.t` | `°C` | after the solve |
| `heat_exchanger` | `out[2].p` | `kPa` | after the solve |
| `heat_exchanger` | `out[2].t` | `°C` | after the solve |
| `heat_exchanger` | `plates` | — | after sizing |
| `heat_exchanger` | `power` | `kW` | after sizing |
| `heat_exchanger` | `u` | `W/(m2*K)` | after sizing |
| `heat_exchanger` | `ua` | `W/K` | after sizing |
| `heat_exchanger` | `volume` | `dm3` | after sizing |
| `heat_exchanger` | `volume[2]` | `dm3` | after sizing |
| `valve` | `authority` | — | after sizing |
| `valve` | `dp` | `kPa` | after the solve |
| `valve` | `flow` | `kg/s` | after the solve |
| `valve` | `in.p` | `kPa` | after the solve |
| `valve` | `kv` | `m3/h` | after sizing |
| `valve` | `out.p` | `kPa` | after the solve |
| `valve` | `position` | — | as written |
| `three_way_valve` | `a.p` | `kPa` | after the solve |
| `three_way_valve` | `ab.p` | `kPa` | after the solve |
| `three_way_valve` | `authority` | — | after sizing |
| `three_way_valve` | `b.p` | `kPa` | after the solve |
| `three_way_valve` | `dp` | `kPa` | after the solve |
| `three_way_valve` | `flow` | `kg/s` | after the solve |
| `three_way_valve` | `kv` | `m3/h` | after sizing |
| `three_way_valve` | `position` | — | as written |
| `pump` | `dp` | `kPa` | after the solve |
| `pump` | `efficiency` | — | after sizing |
| `pump` | `flow` | `kg/s` | after the solve |
| `pump` | `head` | `m` | after sizing |
| `pump` | `in.p` | `kPa` | after the solve |
| `pump` | `out.p` | `kPa` | after the solve |
| `pump` | `power` | `kW` | after the solve |
| `pump` | `speed` | — | after the solve |
| `tank` | `in.p` | `kPa` | after the solve |
| `tank` | `in.t` | `°C` | after the solve |
| `tank` | `layers` | — | as written |
| `tank` | `out.p` | `kPa` | after the solve |
| `tank` | `out.t` | `°C` | after the solve |
| `tank` | `stored_energy` | `J` | after the solve |
| `tank` | `volume` | `dm3` | as written |
| `tank` | `in[{index}].p`, 2 to 16 | `kPa` | after the solve |
| `tank` | `in[{index}].t`, 2 to 16 | `°C` | after the solve |
| `tank` | `layer[{index}].t`, 1 to `layers` | `°C` | after the solve |
| `tank` | `out[{index}].p`, 2 to 16 | `kPa` | after the solve |
| `tank` | `out[{index}].t`, 2 to 16 | `°C` | after the solve |
| `t_sensor` | `t` | `°C` | after the solve |
| `p_sensor` | `p` | `kPa` | after the solve |
| `flow_sensor` | `flow` | `kg/s` | after the solve |
<!-- END GENERATED: component-properties -->

## See also

[Units](units.md) · [`let`](let.md) · [`control`](control.md)
