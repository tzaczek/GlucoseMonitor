import React, { useState, useEffect, useCallback, useMemo } from 'react';
import {
  ResponsiveContainer,
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceArea,
  ReferenceLine,
  Legend,
} from 'recharts';
import { format, parseISO, subDays, subHours } from 'date-fns';
import EventDetailModal from './EventDetailModal';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

const PRESETS = [
  { label: 'Last 6h vs prev 6h', hours: 6 },
  { label: 'Last 12h vs prev 12h', hours: 12 },
  { label: 'Last 24h vs prev 24h', hours: 24 },
  { label: 'Last 48h vs prev 48h', hours: 48 },
  { label: 'Last 7d vs prev 7d', days: 7 },
  { label: 'Last 14d vs prev 14d', days: 14 },
  { label: 'Last 30d vs prev 30d', days: 30 },
];

function formatDuration(start, end) {
  const ms = new Date(end) - new Date(start);
  const hours = ms / (1000 * 60 * 60);
  if (hours < 24) return `${hours.toFixed(1)}h`;
  return `${(hours / 24).toFixed(1)}d`;
}

function formatDateShort(dateStr) {
  try {
    return format(parseISO(dateStr), 'MMM d, HH:mm');
  } catch { return dateStr; }
}

function classColor(cls) {
  if (!cls) return 'var(--text-muted)';
  if (cls === 'green') return 'var(--green)';
  if (cls === 'yellow') return 'var(--yellow)';
  if (cls === 'red') return 'var(--red)';
  return 'var(--text-muted)';
}

function classEmoji(cls) {
  if (cls === 'green') return '🟢';
  if (cls === 'yellow') return '🟡';
  if (cls === 'red') return '🔴';
  return '⏳';
}

export default function ComparePage() {
  const [comparisons, setComparisons] = useState([]);
  const [loading, setLoading] = useState(true);
  const [selectedId, setSelectedId] = useState(null);
  const [detail, setDetail] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);

  // Form state
  const [showForm, setShowForm] = useState(false);
  const [formName, setFormName] = useState('');
  const [formMode, setFormMode] = useState('preset'); // 'preset' or 'custom'
  const [periodAStart, setPeriodAStart] = useState('');
  const [periodAEnd, setPeriodAEnd] = useState('');
  const [periodALabel, setPeriodALabel] = useState('Period A');
  const [periodBStart, setPeriodBStart] = useState('');
  const [periodBEnd, setPeriodBEnd] = useState('');
  const [periodBLabel, setPeriodBLabel] = useState('Period B');
  const [creating, setCreating] = useState(false);
  const [formError, setFormError] = useState(null);

  // Event modal state
  const [selectedEventId, setSelectedEventId] = useState(null);

  const fetchComparisons = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/comparison`);
      if (res.ok) {
        const data = await res.json();
        setComparisons(data);
      }
    } catch (err) {
      console.error('Failed to fetch comparisons:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchComparisons();

    const handler = () => fetchComparisons();
    window.addEventListener('comparisonsUpdated', handler);
    return () => window.removeEventListener('comparisonsUpdated', handler);
  }, [fetchComparisons]);

  // Fetch detail when selected
  useEffect(() => {
    if (!selectedId) { setDetail(null); return; }
    let cancelled = false;
    setDetailLoading(true);
    (async () => {
      try {
        const res = await fetch(`${API_BASE}/comparison/${selectedId}`);
        if (res.ok && !cancelled) {
          const data = await res.json();
          setDetail(data);
        }
      } catch (err) {
        console.error('Failed to fetch comparison detail:', err);
      } finally {
        if (!cancelled) setDetailLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [selectedId]);

  // Re-fetch detail when comparison is updated (e.g., AI analysis completed)
  useEffect(() => {
    const handler = () => {
      if (selectedId) {
        (async () => {
          try {
            const res = await fetch(`${API_BASE}/comparison/${selectedId}`);
            if (res.ok) {
              const data = await res.json();
              setDetail(data);
            }
          } catch (err) { /* ignore */ }
        })();
      }
    };
    window.addEventListener('comparisonsUpdated', handler);
    return () => window.removeEventListener('comparisonsUpdated', handler);
  }, [selectedId]);

  const handlePreset = async (preset) => {
    setCreating(true);
    setFormError(null);
    const now = new Date();
    let bEnd = now;
    let bStart, aEnd, aStart;

    if (preset.hours) {
      bStart = subHours(now, preset.hours);
      aEnd = bStart;
      aStart = subHours(aEnd, preset.hours);
    } else {
      bStart = subDays(now, preset.days);
      aEnd = bStart;
      aStart = subDays(aEnd, preset.days);
    }

    try {
      const res = await fetch(`${API_BASE}/comparison`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: preset.label,
          periodAStart: aStart.toISOString(),
          periodAEnd: aEnd.toISOString(),
          periodALabel: 'Previous',
          periodBStart: bStart.toISOString(),
          periodBEnd: bEnd.toISOString(),
          periodBLabel: 'Recent',
        }),
      });
      if (res.ok) {
        const data = await res.json();
        await fetchComparisons();
        setSelectedId(data.id);
      } else {
        const err = await res.json();
        setFormError(err.message || 'Failed to create comparison.');
      }
    } catch (err) {
      setFormError('Failed to create comparison: ' + err.message);
    } finally {
      setCreating(false);
    }
  };

  const handleCustomSubmit = async (e) => {
    e.preventDefault();
    if (!periodAStart || !periodAEnd || !periodBStart || !periodBEnd) {
      setFormError('All date/time fields are required.');
      return;
    }
    setCreating(true);
    setFormError(null);

    try {
      const res = await fetch(`${API_BASE}/comparison`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: formName || null,
          periodAStart: new Date(periodAStart).toISOString(),
          periodAEnd: new Date(periodAEnd).toISOString(),
          periodALabel: periodALabel || 'Period A',
          periodBStart: new Date(periodBStart).toISOString(),
          periodBEnd: new Date(periodBEnd).toISOString(),
          periodBLabel: periodBLabel || 'Period B',
        }),
      });
      if (res.ok) {
        const data = await res.json();
        await fetchComparisons();
        setSelectedId(data.id);
        setShowForm(false);
        setFormName('');
        setPeriodAStart(''); setPeriodAEnd('');
        setPeriodBStart(''); setPeriodBEnd('');
      } else {
        const err = await res.json();
        setFormError(err.message || 'Failed to create comparison.');
      }
    } catch (err) {
      setFormError('Failed: ' + err.message);
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (id, e) => {
    e.stopPropagation();
    if (!window.confirm('Delete this comparison?')) return;
    try {
      await fetch(`${API_BASE}/comparison/${id}`, { method: 'DELETE' });
      if (selectedId === id) { setSelectedId(null); setDetail(null); }
      fetchComparisons();
    } catch (err) {
      console.error('Delete failed:', err);
    }
  };

  return (
    <div className="compare-page">
      <div className="page-header">
        <h2>Period Comparison</h2>
        <p className="page-subtitle">Compare glucose data between two time periods</p>
      </div>

      {/* Quick presets */}
      <div className="compare-presets">
        <h3>Quick Compare</h3>
        <div className="compare-preset-grid">
          {PRESETS.map((p, i) => (
            <button
              key={i}
              className="compare-preset-btn"
              onClick={() => handlePreset(p)}
              disabled={creating}
            >
              {p.label}
            </button>
          ))}
          <button
            className={`compare-preset-btn custom ${showForm ? 'active' : ''}`}
            onClick={() => { setShowForm(!showForm); setFormMode('custom'); }}
          >
            Custom...
          </button>
        </div>
        {formError && <div className="compare-error">{formError}</div>}
      </div>

      {/* Custom form */}
      {showForm && (
        <div className="compare-form-card">
          <h3>Custom Comparison</h3>
          <form onSubmit={handleCustomSubmit} className="compare-form">
            <div className="compare-form-row">
              <label>Comparison Name (optional)</label>
              <input
                type="text"
                value={formName}
                onChange={(e) => setFormName(e.target.value)}
                placeholder="e.g., Weekday vs Weekend"
              />
            </div>

            <div className="compare-form-periods">
              <div className="compare-form-period">
                <h4>Period A</h4>
                <label>Label</label>
                <input type="text" value={periodALabel} onChange={(e) => setPeriodALabel(e.target.value)} />
                <label>Start</label>
                <input type="datetime-local" value={periodAStart} onChange={(e) => setPeriodAStart(e.target.value)} required />
                <label>End</label>
                <input type="datetime-local" value={periodAEnd} onChange={(e) => setPeriodAEnd(e.target.value)} required />
              </div>
              <div className="compare-form-period">
                <h4>Period B</h4>
                <label>Label</label>
                <input type="text" value={periodBLabel} onChange={(e) => setPeriodBLabel(e.target.value)} />
                <label>Start</label>
                <input type="datetime-local" value={periodBStart} onChange={(e) => setPeriodBStart(e.target.value)} required />
                <label>End</label>
                <input type="datetime-local" value={periodBEnd} onChange={(e) => setPeriodBEnd(e.target.value)} required />
              </div>
            </div>

            <div className="compare-form-actions">
              <button type="submit" className="btn-primary" disabled={creating}>
                {creating ? 'Creating...' : 'Compare'}
              </button>
              <button type="button" className="btn-secondary" onClick={() => setShowForm(false)}>Cancel</button>
            </div>
          </form>
        </div>
      )}

      {/* Comparisons list */}
      <div className="compare-content">
        <div className="compare-list">
          <h3>Past Comparisons</h3>
          {loading && <div className="loading"><div className="spinner" /><p>Loading...</p></div>}
          {!loading && comparisons.length === 0 && (
            <p className="compare-empty">No comparisons yet. Use the presets above or create a custom comparison.</p>
          )}
          {comparisons.map(c => (
            <div
              key={c.id}
              className={`compare-card ${selectedId === c.id ? 'selected' : ''} ${c.status}`}
              onClick={() => setSelectedId(c.id)}
            >
              <div className="compare-card-header">
                <span className="compare-card-class" style={{ color: classColor(c.aiClassification) }}>
                  {classEmoji(c.status === 'completed' ? c.aiClassification : null)}
                </span>
                <span className="compare-card-name">{c.name || `Comparison #${c.id}`}</span>
                <button className="compare-delete-btn" onClick={(e) => handleDelete(c.id, e)} title="Delete">×</button>
              </div>
              <div className="compare-card-periods">
                <div className="compare-card-period">
                  <span className="period-label-a">{c.periodALabel || 'A'}</span>
                  <span className="period-dates">{formatDateShort(c.periodAStart)} – {formatDateShort(c.periodAEnd)}</span>
                  {c.periodAGlucoseAvg != null && <span className="period-avg">avg {Math.round(c.periodAGlucoseAvg)}</span>}
                </div>
                <div className="compare-card-period">
                  <span className="period-label-b">{c.periodBLabel || 'B'}</span>
                  <span className="period-dates">{formatDateShort(c.periodBStart)} – {formatDateShort(c.periodBEnd)}</span>
                  {c.periodBGlucoseAvg != null && <span className="period-avg">avg {Math.round(c.periodBGlucoseAvg)}</span>}
                </div>
              </div>
              <div className="compare-card-status">
                {c.status === 'pending' && <span className="status-badge pending">Pending</span>}
                {c.status === 'processing' && <span className="status-badge processing">Processing...</span>}
                {c.status === 'completed' && <span className="status-badge completed">Completed</span>}
                {c.status === 'failed' && <span className="status-badge failed">Failed</span>}
                <span className="compare-card-date">{format(parseISO(c.createdAt), 'MMM d, HH:mm')}</span>
              </div>
            </div>
          ))}
        </div>

        {/* Detail panel */}
        <div className="compare-detail">
          {!selectedId && (
            <div className="compare-detail-empty">
              <p>Select a comparison to view details</p>
            </div>
          )}
          {selectedId && detailLoading && (
            <div className="loading"><div className="spinner" /><p>Loading comparison...</p></div>
          )}
          {selectedId && !detailLoading && detail && (
            <ComparisonDetail detail={detail} onEventClick={(id) => setSelectedEventId(id)} />
          )}
        </div>
      </div>

      {/* Event detail modal */}
      {selectedEventId && (
        <EventDetailModal
          eventId={selectedEventId}
          onClose={() => setSelectedEventId(null)}
        />
      )}
    </div>
  );
}

function ComparisonDetail({ detail, onEventClick }) {
  const labelA = detail.periodALabel || 'Period A';
  const labelB = detail.periodBLabel || 'Period B';

  // Zoom state: drag-to-select range on the chart
  const [refAreaLeft, setRefAreaLeft] = useState(null);
  const [refAreaRight, setRefAreaRight] = useState(null);
  const [isDragging, setIsDragging] = useState(false);
  const [zoomLeft, setZoomLeft] = useState(null);
  const [zoomRight, setZoomRight] = useState(null);

  // Reset zoom when switching comparisons
  useEffect(() => {
    setZoomLeft(null);
    setZoomRight(null);
  }, [detail.id]);

  const handleMouseDown = useCallback((e) => {
    if (e && e.activeLabel != null) {
      setRefAreaLeft(e.activeLabel);
      setRefAreaRight(e.activeLabel);
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

    if (refAreaLeft == null || refAreaRight == null) {
      setRefAreaLeft(null);
      setRefAreaRight(null);
      return;
    }

    const left = Math.min(refAreaLeft, refAreaRight);
    const right = Math.max(refAreaLeft, refAreaRight);

    // Need at least some range
    if (right - left < 0.05) {
      setRefAreaLeft(null);
      setRefAreaRight(null);
      return;
    }

    setZoomLeft(left);
    setZoomRight(right);
    setRefAreaLeft(null);
    setRefAreaRight(null);
  }, [isDragging, refAreaLeft, refAreaRight]);

  const handleResetZoom = useCallback(() => {
    setZoomLeft(null);
    setZoomRight(null);
    setRefAreaLeft(null);
    setRefAreaRight(null);
  }, []);

  // Merge both periods into one sorted array.
  // Each entry has `offset` plus `valueA` and/or `valueB`, and
  // the actual local timestamps for the tooltip.
  const chartData = useMemo(() => {
    if (!detail.periodAReadings?.length && !detail.periodBReadings?.length) return [];

    const points = [];

    for (const r of (detail.periodAReadings || [])) {
      points.push({
        offset: r.offsetHours,
        valueA: r.value,
        timeA: format(new Date(r.timestamp), 'MMM d, HH:mm'),
      });
    }
    for (const r of (detail.periodBReadings || [])) {
      points.push({
        offset: r.offsetHours,
        valueB: r.value,
        timeB: format(new Date(r.timestamp), 'MMM d, HH:mm'),
      });
    }

    // Sort by offset so the chart axis is monotonic
    points.sort((a, b) => a.offset - b.offset);
    return points;
  }, [detail]);

  const maxOffset = useMemo(() => {
    if (chartData.length === 0) return 24;
    return Math.ceil(chartData[chartData.length - 1].offset) || 24;
  }, [chartData]);

  // Filter chart data to zoom range
  const displayData = useMemo(() => {
    if (zoomLeft == null || zoomRight == null) return chartData;
    return chartData.filter(d => d.offset >= zoomLeft && d.offset <= zoomRight);
  }, [chartData, zoomLeft, zoomRight]);

  const isZoomed = zoomLeft != null && zoomRight != null;

  const eventMarkersA = useMemo(() =>
    (detail.periodAEvents || []).map(e => ({ offset: e.offsetHours, title: e.noteTitle, cls: e.aiClassification })),
    [detail.periodAEvents]
  );

  const eventMarkersB = useMemo(() =>
    (detail.periodBEvents || []).map(e => ({ offset: e.offsetHours, title: e.noteTitle, cls: e.aiClassification })),
    [detail.periodBEvents]
  );

  const formatOffset = (h) => {
    if (h < 24) return `${h}h`;
    return `${(h / 24).toFixed(1)}d`;
  };

  const CustomTooltip = ({ active, payload, label }) => {
    if (!active || !payload?.length) return null;
    // Extract actual local timestamps from the data point
    const dataPoint = payload[0]?.payload;
    return (
      <div className="compare-tooltip">
        <div className="compare-tooltip-label">+{formatOffset(label)} from start</div>
        {payload.map((p, i) => {
          const localTime = p.dataKey === 'valueA' ? dataPoint?.timeA : dataPoint?.timeB;
          return (
            <div key={i} style={{ color: p.color }}>
              {p.name}: {Math.round(p.value)} mg/dL
              {localTime && <span className="compare-tooltip-time"> ({localTime})</span>}
            </div>
          );
        })}
      </div>
    );
  };

  const renderDelta = (valA, valB, unit = '', invert = false) => {
    if (valA == null || valB == null) return null;
    const delta = valB - valA;
    const better = invert ? delta < 0 : delta > 0;
    const worse = invert ? delta > 0 : delta < 0;
    return (
      <span className={`delta ${better ? 'better' : worse ? 'worse' : ''}`}>
        {delta >= 0 ? '+' : ''}{delta.toFixed(1)}{unit}
      </span>
    );
  };

  return (
    <div className="compare-detail-content">
      <h3>
        <span className="compare-detail-class" style={{ color: classColor(detail.aiClassification) }}>
          {classEmoji(detail.aiClassification)}
        </span>
        {detail.name || `Comparison #${detail.id}`}
      </h3>

      {detail.status === 'processing' && (
        <div className="compare-processing">
          <div className="spinner" />
          <p>AI analysis in progress... This will update automatically when complete.</p>
        </div>
      )}
      {detail.status === 'pending' && (
        <div className="compare-processing">
          <p>Queued for processing...</p>
        </div>
      )}
      {detail.status === 'failed' && (
        <div className="compare-error-detail">
          <p>Processing failed: {detail.errorMessage}</p>
        </div>
      )}

      {/* Overlay chart */}
      {chartData.length > 0 && (
        <div className="compare-chart-section">
          <div className="compare-chart-title-row">
            <h4>Glucose Overlay</h4>
            {isZoomed && (
              <button className="compare-zoom-reset" onClick={handleResetZoom}>
                Reset Zoom
              </button>
            )}
            {!isZoomed && <span className="compare-zoom-hint">Drag on chart to zoom</span>}
          </div>
          <div className="compare-chart-periods-header">
            <span className="period-label-a">{labelA}</span>
            <span className="period-header-dates">{format(new Date(detail.periodAStart), 'MMM d, HH:mm')} – {format(new Date(detail.periodAEnd), 'MMM d, HH:mm')}</span>
            <span style={{ margin: '0 12px', color: 'rgba(255,255,255,0.3)' }}>|</span>
            <span className="period-label-b">{labelB}</span>
            <span className="period-header-dates">{format(new Date(detail.periodBStart), 'MMM d, HH:mm')} – {format(new Date(detail.periodBEnd), 'MMM d, HH:mm')}</span>
          </div>
          <ResponsiveContainer width="100%" height={350}>
            <LineChart
              data={displayData}
              margin={{ top: 10, right: 20, left: 0, bottom: 10 }}
              onMouseDown={handleMouseDown}
              onMouseMove={handleMouseMove}
              onMouseUp={handleMouseUp}
              onMouseLeave={handleMouseUp}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.1)" />
              <XAxis
                dataKey="offset"
                type="number"
                domain={isZoomed ? [zoomLeft, zoomRight] : [0, maxOffset]}
                tickFormatter={formatOffset}
                stroke="rgba(255,255,255,0.5)"
                tick={{ fill: 'rgba(255,255,255,0.7)', fontSize: 11 }}
                allowDataOverflow={true}
              />
              <YAxis
                domain={['auto', 'auto']}
                stroke="rgba(255,255,255,0.5)"
                tick={{ fill: 'rgba(255,255,255,0.7)', fontSize: 11 }}
                label={{ value: 'mg/dL', angle: -90, position: 'insideLeft', fill: 'rgba(255,255,255,0.5)', fontSize: 11 }}
              />
              <Tooltip content={<CustomTooltip />} />
              <ReferenceArea y1={70} y2={180} fill="rgba(0,200,100,0.06)" />
              <ReferenceLine y={70} stroke="rgba(255,100,100,0.3)" strokeDasharray="5 5" />
              <ReferenceLine y={180} stroke="rgba(255,100,100,0.3)" strokeDasharray="5 5" />

              {/* Event markers for Period A (only if in view) */}
              {eventMarkersA
                .filter(m => !isZoomed || (m.offset >= zoomLeft && m.offset <= zoomRight))
                .map((m, i) => (
                  <ReferenceLine key={`ea-${i}`} x={m.offset} stroke="rgba(0,180,255,0.4)" strokeDasharray="4 4" label="" />
              ))}
              {/* Event markers for Period B (only if in view) */}
              {eventMarkersB
                .filter(m => !isZoomed || (m.offset >= zoomLeft && m.offset <= zoomRight))
                .map((m, i) => (
                  <ReferenceLine key={`eb-${i}`} x={m.offset} stroke="rgba(255,160,0,0.4)" strokeDasharray="4 4" label="" />
              ))}

              <Line
                type="monotone"
                dataKey="valueA"
                name={labelA}
                stroke="#00b4ff"
                strokeWidth={2}
                dot={false}
                connectNulls={true}
                isAnimationActive={false}
              />
              <Line
                type="monotone"
                dataKey="valueB"
                name={labelB}
                stroke="#ffa000"
                strokeWidth={2}
                dot={false}
                connectNulls={true}
                isAnimationActive={false}
              />

              {/* Drag selection highlight */}
              {isDragging && refAreaLeft != null && refAreaRight != null && (
                <ReferenceArea
                  x1={Math.min(refAreaLeft, refAreaRight)}
                  x2={Math.max(refAreaLeft, refAreaRight)}
                  strokeOpacity={0.3}
                  fill="rgba(0,180,255,0.2)"
                />
              )}

              <Legend />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}

      {/* Stats comparison table */}
      <div className="compare-stats-section">
        <h4>Statistics Comparison</h4>
        <table className="compare-stats-table">
          <thead>
            <tr>
              <th>Metric</th>
              <th className="col-a">{labelA}</th>
              <th className="col-b">{labelB}</th>
              <th>Change</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td>Period</td>
              <td>{format(new Date(detail.periodAStart), 'MMM d, HH:mm')} – {format(new Date(detail.periodAEnd), 'MMM d, HH:mm')}</td>
              <td>{format(new Date(detail.periodBStart), 'MMM d, HH:mm')} – {format(new Date(detail.periodBEnd), 'MMM d, HH:mm')}</td>
              <td></td>
            </tr>
            <tr>
              <td>Duration</td>
              <td>{formatDuration(detail.periodAStart, detail.periodAEnd)}</td>
              <td>{formatDuration(detail.periodBStart, detail.periodBEnd)}</td>
              <td></td>
            </tr>
            <tr>
              <td>Readings</td>
              <td>{detail.periodAStats.readingCount}</td>
              <td>{detail.periodBStats.readingCount}</td>
              <td></td>
            </tr>
            <tr>
              <td>Average</td>
              <td>{detail.periodAStats.glucoseAvg != null ? Math.round(detail.periodAStats.glucoseAvg) : '–'}</td>
              <td>{detail.periodBStats.glucoseAvg != null ? Math.round(detail.periodBStats.glucoseAvg) : '–'}</td>
              <td>{renderDelta(detail.periodAStats.glucoseAvg, detail.periodBStats.glucoseAvg, '', true)}</td>
            </tr>
            <tr>
              <td>Min</td>
              <td>{detail.periodAStats.glucoseMin != null ? Math.round(detail.periodAStats.glucoseMin) : '–'}</td>
              <td>{detail.periodBStats.glucoseMin != null ? Math.round(detail.periodBStats.glucoseMin) : '–'}</td>
              <td></td>
            </tr>
            <tr>
              <td>Max</td>
              <td>{detail.periodAStats.glucoseMax != null ? Math.round(detail.periodAStats.glucoseMax) : '–'}</td>
              <td>{detail.periodBStats.glucoseMax != null ? Math.round(detail.periodBStats.glucoseMax) : '–'}</td>
              <td></td>
            </tr>
            <tr>
              <td>Std Dev</td>
              <td>{detail.periodAStats.glucoseStdDev != null ? detail.periodAStats.glucoseStdDev.toFixed(1) : '–'}</td>
              <td>{detail.periodBStats.glucoseStdDev != null ? detail.periodBStats.glucoseStdDev.toFixed(1) : '–'}</td>
              <td>{renderDelta(detail.periodAStats.glucoseStdDev, detail.periodBStats.glucoseStdDev, '', true)}</td>
            </tr>
            <tr>
              <td>Time in Range</td>
              <td>{detail.periodAStats.timeInRange != null ? `${detail.periodAStats.timeInRange.toFixed(1)}%` : '–'}</td>
              <td>{detail.periodBStats.timeInRange != null ? `${detail.periodBStats.timeInRange.toFixed(1)}%` : '–'}</td>
              <td>{renderDelta(detail.periodAStats.timeInRange, detail.periodBStats.timeInRange, '%')}</td>
            </tr>
            <tr>
              <td>Time Above</td>
              <td>{detail.periodAStats.timeAboveRange != null ? `${detail.periodAStats.timeAboveRange.toFixed(1)}%` : '–'}</td>
              <td>{detail.periodBStats.timeAboveRange != null ? `${detail.periodBStats.timeAboveRange.toFixed(1)}%` : '–'}</td>
              <td>{renderDelta(detail.periodAStats.timeAboveRange, detail.periodBStats.timeAboveRange, '%', true)}</td>
            </tr>
            <tr>
              <td>Time Below</td>
              <td>{detail.periodAStats.timeBelowRange != null ? `${detail.periodAStats.timeBelowRange.toFixed(1)}%` : '–'}</td>
              <td>{detail.periodBStats.timeBelowRange != null ? `${detail.periodBStats.timeBelowRange.toFixed(1)}%` : '–'}</td>
              <td>{renderDelta(detail.periodAStats.timeBelowRange, detail.periodBStats.timeBelowRange, '%', true)}</td>
            </tr>
            <tr>
              <td>Events</td>
              <td>{detail.periodAStats.eventCount}</td>
              <td>{detail.periodBStats.eventCount}</td>
              <td></td>
            </tr>
          </tbody>
        </table>
      </div>

      {/* Events in each period */}
      {(detail.periodAEvents?.length > 0 || detail.periodBEvents?.length > 0) && (
        <div className="compare-events-section">
          <h4>Events</h4>
          <div className="compare-events-columns">
            <div className="compare-events-col">
              <h5 className="period-label-a">{labelA}</h5>
              {(!detail.periodAEvents || detail.periodAEvents.length === 0) && <p className="compare-empty-small">No events</p>}
              {detail.periodAEvents?.map(e => (
                <div key={e.id} className="compare-event-item compare-event-clickable" onClick={() => onEventClick && onEventClick(e.id)}>
                  <span className="compare-event-class" style={{ color: classColor(e.aiClassification) }}>
                    {classEmoji(e.aiClassification)}
                  </span>
                  <div>
                    <div className="compare-event-title">{e.noteTitle}</div>
                    <div className="compare-event-time">{format(new Date(e.eventTimestamp), 'MMM d, HH:mm')} • {e.glucoseAtEvent != null ? `${Math.round(e.glucoseAtEvent)} mg/dL` : ''}</div>
                  </div>
                </div>
              ))}
            </div>
            <div className="compare-events-col">
              <h5 className="period-label-b">{labelB}</h5>
              {(!detail.periodBEvents || detail.periodBEvents.length === 0) && <p className="compare-empty-small">No events</p>}
              {detail.periodBEvents?.map(e => (
                <div key={e.id} className="compare-event-item compare-event-clickable" onClick={() => onEventClick && onEventClick(e.id)}>
                  <span className="compare-event-class" style={{ color: classColor(e.aiClassification) }}>
                    {classEmoji(e.aiClassification)}
                  </span>
                  <div>
                    <div className="compare-event-title">{e.noteTitle}</div>
                    <div className="compare-event-time">{format(new Date(e.eventTimestamp), 'MMM d, HH:mm')} • {e.glucoseAtEvent != null ? `${Math.round(e.glucoseAtEvent)} mg/dL` : ''}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* AI Analysis */}
      {detail.aiAnalysis && (
        <div className="compare-analysis-section">
          <h4>AI Analysis</h4>
          <div className="compare-analysis-text" dangerouslySetInnerHTML={{ __html: renderMarkdown(detail.aiAnalysis) }} />
        </div>
      )}
    </div>
  );
}

// Simple markdown renderer (bold, bullets, headings)
function renderMarkdown(text) {
  if (!text) return '';
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
    .replace(/\*(.*?)\*/g, '<em>$1</em>')
    .replace(/^### (.+)$/gm, '<h5>$1</h5>')
    .replace(/^## (.+)$/gm, '<h4>$1</h4>')
    .replace(/^# (.+)$/gm, '<h3>$1</h3>')
    .replace(/^- (.+)$/gm, '<li>$1</li>')
    .replace(/(<li>.*<\/li>)/gs, '<ul>$1</ul>')
    .replace(/<\/ul>\s*<ul>/g, '')
    .replace(/\n{2,}/g, '</p><p>')
    .replace(/\n/g, '<br/>')
    .replace(/^/, '<p>')
    .replace(/$/, '</p>');
}
