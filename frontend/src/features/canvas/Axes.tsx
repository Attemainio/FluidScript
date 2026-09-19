/** The origin's rays (`R-22`): X red, Y green, one world unit long with a tick at each half. Drawn by the canvas and, on request, by the export. */
export function Axes(): React.ReactNode {
  return (
    <g className="canvas-axes">
      <g className="canvas-axes__x">
        <line x1={0} y1={0} x2={1} y2={0} vectorEffect="non-scaling-stroke" />
        <line x1={0.5} y1={-0.04} x2={0.5} y2={0.04} vectorEffect="non-scaling-stroke" />
        <line x1={1} y1={-0.06} x2={1} y2={0.06} vectorEffect="non-scaling-stroke" />
      </g>
      <g className="canvas-axes__y">
        <line x1={0} y1={0} x2={0} y2={1} vectorEffect="non-scaling-stroke" />
        <line x1={-0.04} y1={0.5} x2={0.04} y2={0.5} vectorEffect="non-scaling-stroke" />
        <line x1={-0.06} y1={1} x2={0.06} y2={1} vectorEffect="non-scaling-stroke" />
      </g>
    </g>
  );
}
