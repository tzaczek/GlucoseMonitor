import React, { useState, useCallback, useMemo } from 'react';
import {
  ResponsiveContainer,
  ComposedChart,
  Line,
  Scatter,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine,
  ReferenceArea,
  Brush,
  Cell,
} from 'recharts';
import { format, parseISO } from 'date-fns';

function GlucoseChart({ data, events = [], onEventClick }) {
  const [refAreaLeft, setRefAreaLeft] = useState(null);
  const [refAreaRight, setRefAreaRight] = useState(null);
  const [zoomLeft, setZoomLeft] = useState(null);
  const [zoomRight, setZoomRight] = useState(null);
  const [isDragging, setIsDragging] = useState(false);
  const [activeEventId, setActiveEventId] = useState(null);

  // Sort data by timestamp ascending for the chart — use numeric time as X axis
  const chartData = useMemo(() =>
    [...data]
      .sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
      .map(d => ({
        ...d,
        time: new Date(d.timestamp).getTime(),
      })),
    [data]
  );

  // Apply zoom filter
  const displayData = useMemo(() => {
    if (zoomLeft === null || zoomRight === null) return chartData;
    return chartData.filter(d => d.time >= zoomLeft && d.time <= zoomRight);
  }, [chartData, zoomLeft, zoomRight]);

  const isZoomed = zoomLeft !== null && zoomRight !== null;

  // Dynamic Y-axis domain based on visible data
  const yDomain = useMemo(() => {
    if (displayData.length === 0) return [40, 300];
    const values = displayData.map(d => d.value);
    const min = Math.min(...values);
    const max = Math.max(...values);
    return [Math.max(30, Math.floor(min / 10) * 10 - 10), Math.min(400, Math.ceil(max / 10) * 10 + 10)];
  }, [displayData]);

  // Time range for formatting
  const rangeHours = useMemo(() => {
    if (displayData.length < 2) return 0;
    return (displayData[displayData.length - 1].time - displayData[0].time) / (1000 * 60 * 60);
  }, [displayData]);

  // X-axis tick formatter using numeric time
  const xTickFormatter = useCallback((timeMs) => {
    const d = new Date(timeMs);
    if (rangeHours > 48) {
      return format(d, 'MMM dd HH:mm');
    }
    return format(d, 'HH:mm');
  }, [rangeHours]);

  // Map ALL events within the full chart data range (for sidebar — always visible)
  const allChartEvents = useMemo(() => {
    if (!events.length || !chartData.length) return [];
    const minTime = chartData[0].time;
    const maxTime = chartData[chartData.length - 1].time;

    return events
      .map(evt => {
        const evtTime = new Date(evt.eventTimestamp).getTime();
        if (evtTime < minTime || evtTime > maxTime) return null;

        // Find the closest data point to get glucose value at event
        let closest = chartData[0];
        let closestDist = Math.abs(closest.time - evtTime);
        for (const dp of chartData) {
          const dist = Math.abs(dp.time - evtTime);
          if (dist < closestDist) {
            closest = dp;
            closestDist = dist;
          }
        }

        return {
          ...evt,
          evtTime,
          glucoseValue: closest.value,
          displayLabel: format(new Date(evt.eventTimestamp), 'HH:mm'),
        };
      })
      .filter(Boolean);
  }, [events, chartData]);

  // Events visible in the current zoom range (for chart markers)
  const visibleEvents = useMemo(() => {
    if (!allChartEvents.length || !displayData.length) return [];
    const minTime = displayData[0].time;
    const maxTime = displayData[displayData.length - 1].time;
    return allChartEvents.filter(evt => evt.evtTime >= minTime && evt.evtTime <= maxTime);
  }, [allChartEvents, displayData]);

  // Create scatter data for event marker dots on the glucose line
  const eventScatterData = useMemo(() => {
    return visibleEvents.map(evt => ({
      time: evt.evtTime,
      eventMarker: evt.glucoseValue,
      eventId: evt.id,
      eventTitle: evt.noteTitle,
      eventLabel: evt.displayLabel,
      hasAnalysis: evt.hasAnalysis,
    }));
  }, [visibleEvents]);

  // Zoom handlers
  const handleMouseDown = useCallback((e) => {
    if (e && e.activeLabel != null) {
      setRefAreaLeft(e.activeLabel);
      setIsDragging(true);
    }
  }, []);

  const handleMouseMove = useCallback((e) => {
    if (isDragging && e && e.activeLabel != null) {
      setRefAreaRight(e.activeLabel);
    }
  }, [isDragging]);

  const handleMouseUp = useCallback(() => {
    if (!isDragging) return;
    setIsDragging(false);

    if (refAreaLeft === null || refAreaRight === null) {
      setRefAreaLeft(null);
      setRefAreaRight(null);
      return;
    }

    const left = Math.min(refAreaLeft, refAreaRight);
    const right = Math.max(refAreaLeft, refAreaRight);

    // Minimum selection of 2 points
    const pointsInRange = chartData.filter(d => d.time >= left && d.time <= right);
    if (pointsInRange.length < 2) {
      setRefAreaLeft(null);
      setRefAreaRight(null);
      return;
    }

    setZoomLeft(left);
    setZoomRight(right);
    setRefAreaLeft(null);
    setRefAreaRight(null);
  }, [isDragging, refAreaLeft, refAreaRight, chartData]);

  const handleResetZoom = useCallback(() => {
    setZoomLeft(null);
    setZoomRight(null);
    setRefAreaLeft(null);
    setRefAreaRight(null);
    setActiveEventId(null);
  }, []);

  // Zoom chart to center on a specific event (±2 hours window)
  const zoomToEvent = useCallback((evtTime) => {
    if (!chartData.length) return;
    const windowMs = 2 * 60 * 60 * 1000; // 2 hours each side
    const dataMin = chartData[0].time;
    const dataMax = chartData[chartData.length - 1].time;

    let left = evtTime - windowMs;
    let right = evtTime + windowMs;

    // Clamp to data bounds
    if (left < dataMin) left = dataMin;
    if (right > dataMax) right = dataMax;

    // Ensure at least some range
    if (right - left < 30 * 60 * 1000) {
      left = Math.max(dataMin, evtTime - 30 * 60 * 1000);
      right = Math.min(dataMax, evtTime + 30 * 60 * 1000);
    }

    setZoomLeft(left);
    setZoomRight(right);
    setRefAreaLeft(null);
    setRefAreaRight(null);
  }, [chartData]);

  // Custom event dot renderer
  const renderEventDot = useCallback((props) => {
    const { cx, cy, payload } = props;
    if (!cx || !cy || !payload) return null;
    return (
      <g
        key={`event-dot-${payload.eventId}`}
        onClick={(e) => {
          e.stopPropagation();
          onEventClick?.(payload.eventId);
        }}
        style={{ cursor: 'pointer' }}
      >
        {/* Outer glow */}
        <circle cx={cx} cy={cy} r={10} fill="#ff6b6b" fillOpacity={0.2} />
        {/* Main dot */}
        <circle cx={cx} cy={cy} r={6} fill="#ff6b6b" stroke="#fff" strokeWidth={2} />
        {/* Bookmark icon above */}
        <g transform={`translate(${cx}, ${cy - 22})`}>
          <path
            d="M-7,-12 L7,-12 L7,2 L0,7 L-7,2 Z"
            fill="#ff6b6b"
            stroke="#ff3333"
            strokeWidth={0.8}
          />
          <text
            x={0}
            y={-2}
            textAnchor="middle"
            fontSize={8}
            fill="white"
            pointerEvents="none"
          >
            ♦
          </text>
        </g>
        <title>{`${payload.eventTitle || 'Event'} (${payload.eventLabel}) — Click for details`}</title>
      </g>
    );
  }, [onEventClick]);

  // Custom tooltip showing event info if near an event
  const CustomTooltip = ({ active, payload }) => {
    if (!active || !payload || !payload.length) return null;
    const d = payload[0]?.payload;
    if (!d) return null;

    // Check if there's a nearby event
    const nearbyEvent = visibleEvents.find(evt =>
      Math.abs(evt.evtTime - d.time) < 5 * 60 * 1000 // within 5 minutes
    );

    return (
      <div style={{
        background: '#1a1a2e',
        border: '1px solid #333',
        borderRadius: 8,
        padding: '10px 14px',
        fontSize: '0.85rem',
        maxWidth: 220,
      }}>
        <div style={{ color: '#888', marginBottom: 4 }}>
          {format(new Date(d.time), rangeHours > 48 ? 'MMM dd HH:mm' : 'HH:mm')}
        </div>
        <div style={{ fontWeight: 700, fontSize: '1.1rem', color: getColor(d.value) }}>
          {d.value} mg/dL
        </div>
        {nearbyEvent && (
          <div style={{
            marginTop: 6,
            paddingTop: 6,
            borderTop: '1px solid #333',
            color: '#ff6b6b',
            fontSize: '0.78rem',
          }}>
            🔖 {nearbyEvent.noteTitle || 'Event'} @ {nearbyEvent.displayLabel}
            <div style={{ color: '#888', fontSize: '0.72rem', marginTop: 2 }}>
              Click marker for analysis
            </div>
          </div>
        )}
      </div>
    );
  };

  // Merge display data with event scatter for ComposedChart
  const mergedData = useMemo(() => {
    // Add eventMarker field to the closest data point for each event
    const eventMap = new Map();
    for (const es of eventScatterData) {
      // Find the closest displayData point
      let closest = null;
      let closestDist = Infinity;
      for (const dp of displayData) {
        const dist = Math.abs(dp.time - es.time);
        if (dist < closestDist) {
          closest = dp;
          closestDist = dist;
        }
      }
      if (closest) {
        eventMap.set(closest.time, es);
      }
    }

    return displayData.map(dp => {
      const evt = eventMap.get(dp.time);
      return evt
        ? { ...dp, eventMarker: evt.eventMarker, eventId: evt.eventId, eventTitle: evt.eventTitle, eventLabel: evt.eventLabel, hasAnalysis: evt.hasAnalysis }
        : dp;
    });
  }, [displayData, eventScatterData]);

  // Sort ALL chart events newest first for the sidebar
  const sortedEvents = useMemo(() =>
    [...allChartEvents].sort((a, b) => b.evtTime - a.evtTime),
    [allChartEvents]
  );

  // Handle sidebar bookmark click: zoom + open popup
  const handleBookmarkClick = useCallback((evt) => {
    zoomToEvent(evt.evtTime);
    setActiveEventId(evt.id);
    onEventClick?.(evt.id);
  }, [zoomToEvent, onEventClick]);

  return (
    <div className="chart-with-bookmarks">
      <div className="chart-main">
        <div className="chart-toolbar">
          <span className="chart-hint">
            {isZoomed ? 'Zoomed — ' : 'Click & drag on chart to zoom'}
          </span>
          {isZoomed && (
            <button className="btn-reset-zoom" onClick={handleResetZoom}>
              ↩ Reset Zoom
            </button>
          )}
        </div>
        <ResponsiveContainer width="100%" height={380}>
          <ComposedChart
          data={mergedData}
          margin={{ top: 30, right: 20, left: 0, bottom: 0 }}
          onMouseDown={handleMouseDown}
          onMouseMove={handleMouseMove}
          onMouseUp={handleMouseUp}
          onMouseLeave={handleMouseUp}
        >
          <CartesianGrid strokeDasharray="3 3" stroke="#1f1f35" />
          <XAxis
            dataKey="time"
            type="number"
            scale="time"
            domain={['dataMin', 'dataMax']}
            stroke="#555"
            tick={{ fontSize: 11 }}
            tickFormatter={xTickFormatter}
            interval="preserveStartEnd"
          />
          <YAxis
            stroke="#555"
            tick={{ fontSize: 12 }}
            domain={yDomain}
          />
          <Tooltip content={<CustomTooltip />} />

          {/* Target range band (70–180 mg/dL) */}
          <ReferenceArea y1={70} y2={180} fill="#4ade80" fillOpacity={0.05} />
          <ReferenceLine y={70} stroke="#4ade80" strokeDasharray="4 4" strokeOpacity={0.4} />
          <ReferenceLine y={180} stroke="#fbbf24" strokeDasharray="4 4" strokeOpacity={0.4} />

          {/* Event vertical markers — ReferenceLine for each event */}
          {visibleEvents.map(evt => (
            <ReferenceLine
              key={`evt-line-${evt.id}`}
              x={evt.evtTime}
              stroke="#ff6b6b"
              strokeDasharray="6 3"
              strokeWidth={1.5}
              strokeOpacity={0.6}
              label={{
                value: `🔖 ${evt.noteTitle || ''}`,
                position: 'insideTopRight',
                fill: '#ff6b6b',
                fontSize: 10,
                fontWeight: 600,
                offset: 5,
              }}
            />
          ))}

          {/* Glucose line */}
          <Line
            type="monotone"
            dataKey="value"
            stroke="#4facfe"
            strokeWidth={2}
            dot={false}
            activeDot={{ r: 5, fill: '#4facfe' }}
            isAnimationActive={false}
          />

          {/* Event marker dots on the glucose line */}
          <Scatter
            dataKey="eventMarker"
            fill="#ff6b6b"
            shape={renderEventDot}
            isAnimationActive={false}
          />

          {/* Drag-to-zoom selection highlight */}
          {isDragging && refAreaLeft && refAreaRight && (
            <ReferenceArea
              x1={Math.min(refAreaLeft, refAreaRight)}
              x2={Math.max(refAreaLeft, refAreaRight)}
              strokeOpacity={0.3}
              fill="#4facfe"
              fillOpacity={0.15}
            />
          )}

          {/* Brush navigator at bottom */}
          {!isZoomed && chartData.length > 20 && (
            <Brush
              dataKey="time"
              height={30}
              stroke="#4facfe"
              fill="#12121f"
              travellerWidth={10}
              tickFormatter={(timeMs) => format(new Date(timeMs), 'HH:mm')}
            />
          )}
        </ComposedChart>
        </ResponsiveContainer>
      </div>

      {/* Event bookmarks sidebar — right side, scrollable, newest first */}
      {sortedEvents.length > 0 && (
        <div className="chart-bookmarks-sidebar">
          <div className="bookmarks-sidebar-title">🔖 Events</div>
          <div className="bookmarks-sidebar-list">
            {sortedEvents.map(evt => (
              <button
                key={evt.id}
                className={`sidebar-bookmark ${evt.hasAnalysis ? 'analyzed' : 'pending'} ${activeEventId === evt.id && isZoomed ? 'active' : ''} ${evt.aiClassification ? `bookmark-${evt.aiClassification}` : ''}`}
                onClick={() => handleBookmarkClick(evt)}
                title={`${evt.noteTitle} — ${evt.displayLabel}\nClick to zoom & view analysis`}
              >
                {evt.aiClassification && (
                  <span className={`sidebar-bookmark-dot classification-dot-${evt.aiClassification}`} />
                )}
                <span className="sidebar-bookmark-time">{evt.displayLabel}</span>
                <span className="sidebar-bookmark-title">{evt.noteTitle || 'Event'}</span>
                {evt.hasAnalysis && <span className="sidebar-bookmark-ai">🤖</span>}
                {evt.glucoseAtEvent != null && (
                  <span className="sidebar-bookmark-glucose">{Math.round(evt.glucoseAtEvent)}</span>
                )}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function getColor(value) {
  if (value < 70) return '#f87171';
  if (value <= 180) return '#4ade80';
  if (value <= 250) return '#fbbf24';
  return '#f87171';
}

export default GlucoseChart;
