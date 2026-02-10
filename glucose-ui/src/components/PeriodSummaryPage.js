import React, { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import {
  ResponsiveContainer,
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine,
  ReferenceArea,
} from 'recharts';
import { format, parseISO, subDays, subHours } from 'date-fns';
import EventDetailModal from './EventDetailModal';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

const PRESETS = [
  { label: 'Last 6 hours', hours: 6 },
  { label: 'Last 12 hours', hours: 12 },
  { label: 'Last 24 hours', hours: 24 },
  { label: 'Last 48 hours', hours: 48 },
  { label: 'Last 3 days', days: 3 },
  { label: 'Last 7 days', days: 7 },
  { label: 'Last 14 days', days: 14 },
  { label: 'Last 30 days', days: 30 },
];

function formatDuration(start, end) {
  const ms = new Date(end) - new Date(start);
  const hours = ms / (1000 * 60 * 60);
  if (hours < 24) return `${hours.toFixed(1)}h`;
  return `${(hours / 24).toFixed(1)}d`;
}

function formatDateShort(dateStr) {
  try { return format(parseISO(dateStr), 'MMM d, HH:mm'); } catch { return dateStr; }
}

function formatDateFull(dateStr) {
  try { return format(parseISO(dateStr), 'MMM d yyyy, HH:mm'); } catch { return dateStr; }
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

export default function PeriodSummaryPage() {
  const [summaries, setSummaries] = useState([]);
  const [loading, setLoading] = useState(true);
  const [selectedId, setSelectedId] = useState(null);
  const [detail, setDetail] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);

  // Event modal state
  const [selectedEventId, setSelectedEventId] = useState(null);

  // Form state
  const [formName, setFormName] = useState('');
  const [formStart, setFormStart] = useState('');
  const [formEnd, setFormEnd] = useState('');
  const [creating, setCreating] = useState(false);
  const [formError, setFormError] = useState(null);

  const fetchSummaries = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/periodsummary`);
      if (res.ok) {
        const data = await res.json();
        setSummaries(data);
      }
    } catch (err) {
      console.error('Failed to fetch period summaries:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchSummaries();

    const handler = () => fetchSummaries();
    window.addEventListener('periodSummariesUpdated', handler);
    return () => window.removeEventListener('periodSummariesUpdated', handler);
  }, [fetchSummaries]);

  // Fetch detail when selected
  useEffect(() => {
    if (!selectedId) { setDetail(null); return; }
    let cancelled = false;
    setDetailLoading(true);

    (async () => {
      try {
        const res = await fetch(`${API_BASE}/periodsummary/${selectedId}`);
        if (res.ok && !cancelled) {
          const data = await res.json();
          setDetail(data);
        }
      } catch (err) {
        console.error('Failed to fetch period summary detail:', err);
      } finally {
        if (!cancelled) setDetailLoading(false);
      }
    })();

    return () => { cancelled = true; };
  }, [selectedId]);

  // Re-fetch detail when SignalR updates arrive (for pending items)
  useEffect(() => {
    if (!selectedId) return;
    const handler = async () => {
      try {
        const res = await fetch(`${API_BASE}/periodsummary/${selectedId}`);
        if (res.ok) {
          const data = await res.json();
          setDetail(data);
        }
      } catch {}
    };
    window.addEventListener('periodSummariesUpdated', handler);
    return () => window.removeEventListener('periodSummariesUpdated', handler);
  }, [selectedId]);

  const handlePreset = async (preset) => {
    const now = new Date();
    let start;
    if (preset.hours) {
      start = subHours(now, preset.hours);
    } else {
      start = subDays(now, preset.days);
    }

    setCreating(true);
    setFormError(null);
    try {
      const res = await fetch(`${API_BASE}/periodsummary`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: preset.label,
          periodStart: start.toISOString(),
          periodEnd: now.toISOString(),
        }),
      });
      const data = await res.json();
      if (res.ok && data.success) {
        setSelectedId(data.id);
        fetchSummaries();
      } else {
        setFormError(data.message || 'Failed to create summary');
      }
    } catch (err) {
      setFormError('Request failed: ' + err.message);
    } finally {
      setCreating(false);
    }
  };

  const handleCustomSubmit = async (e) => {
    e.preventDefault();
    if (!formStart || !formEnd) {
      setFormError('Please provide both start and end dates.');
      return;
    }

    const startUtc = new Date(formStart).toISOString();
    const endUtc = new Date(formEnd).toISOString();

    if (new Date(startUtc) >= new Date(endUtc)) {
      setFormError('Start must be before end.');
      return;
    }

    setCreating(true);
    setFormError(null);
    try {
      const res = await fetch(`${API_BASE}/periodsummary`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: formName || null,
          periodStart: startUtc,
          periodEnd: endUtc,
        }),
      });
      const data = await res.json();
      if (res.ok && data.success) {
        setSelectedId(data.id);
        setFormName('');
        setFormStart('');
        setFormEnd('');
        fetchSummaries();
      } else {
        setFormError(data.message || 'Failed to create summary');
      }
    } catch (err) {
      setFormError('Request failed: ' + err.message);
    } finally {
      setCreating(false);
    }
  };

  const handleDelete = async (id, e) => {
    e.stopPropagation();
    if (!window.confirm('Delete this period summary?')) return;
    try {
      await fetch(`${API_BASE}/periodsummary/${id}`, { method: 'DELETE' });
      if (selectedId === id) setSelectedId(null);
      fetchSummaries();
    } catch (err) {
      console.error('Failed to delete:', err);
    }
  };

  return (
    <div className="period-summary-page">
      <div className="period-summary-header">
        <h2>Period Summaries</h2>
        <p className="period-summary-subtitle">
          Generate AI-powered summaries for any time period of your glucose data.
        </p>
      </div>

      {/* Quick Presets */}
      <div className="period-summary-card">
        <h3>Quick Summary</h3>
        <div className="period-summary-presets">
          {PRESETS.map((preset, i) => (
            <button
              key={i}
              className="period-preset-btn"
              onClick={() => handlePreset(preset)}
              disabled={creating}
            >
              {preset.label}
            </button>
          ))}
        </div>
      </div>

      {/* Custom Period Form */}
      <div className="period-summary-card">
        <h3>Custom Period</h3>
        <form onSubmit={handleCustomSubmit} className="period-summary-form">
          <div className="period-form-row">
            <label>
              Label (optional)
              <input
                type="text"
                value={formName}
                onChange={(e) => setFormName(e.target.value)}
                placeholder="e.g. Weekend trip, Fasting experiment"
              />
            </label>
          </div>
          <div className="period-form-row period-form-dates">
            <label>
              Start
              <input
                type="datetime-local"
                value={formStart}
                onChange={(e) => setFormStart(e.target.value)}
                required
              />
            </label>
            <label>
              End
              <input
                type="datetime-local"
                value={formEnd}
                onChange={(e) => setFormEnd(e.target.value)}
                required
              />
            </label>
          </div>
          <button type="submit" className="period-submit-btn" disabled={creating}>
            {creating ? 'Creating...' : 'Generate Summary'}
          </button>
          {formError && <div className="period-form-error">{formError}</div>}
        </form>
      </div>

      {/* Summaries List + Detail */}
      <div className="period-summary-content">
        {/* List */}
        <div className="period-summary-list">
          <h3>Summaries ({summaries.length})</h3>
          {loading && <div className="loading"><div className="spinner" /><p>Loading...</p></div>}
          {!loading && summaries.length === 0 && (
            <p className="period-empty-msg">No period summaries yet. Use the presets or custom form above to create one.</p>
          )}
          {summaries.map((s) => (
            <div
              key={s.id}
              className={`period-summary-item${selectedId === s.id ? ' selected' : ''}`}
              onClick={() => setSelectedId(s.id)}
            >
              <div className="period-item-header">
                <span className="period-item-emoji">{classEmoji(s.aiClassification)}</span>
                <span className="period-item-name">{s.name || 'Untitled'}</span>
                <button
                  className="period-item-delete"
                  onClick={(e) => handleDelete(s.id, e)}
                  title="Delete"
                >×</button>
              </div>
              <div className="period-item-dates">
                {formatDateShort(s.periodStart)} → {formatDateShort(s.periodEnd)}
                <span className="period-item-duration">({formatDuration(s.periodStart, s.periodEnd)})</span>
              </div>
              <div className="period-item-stats">
                {s.status === 'completed' ? (
                  <>
                    <span>Avg: {s.glucoseAvg ?? '—'}</span>
                    <span>TIR: {s.timeInRange != null ? `${s.timeInRange}%` : '—'}</span>
                    <span>{s.readingCount} readings</span>
                    <span>{s.eventCount} events</span>
                  </>
                ) : (
                  <span className={`period-status ${s.status}`}>
                    {s.status === 'pending' ? '⏳ Pending' : s.status === 'processing' ? '⏳ Processing...' : '❌ Failed'}
                  </span>
                )}
              </div>
            </div>
          ))}
        </div>

        {/* Detail */}
        <div className="period-summary-detail">
          {!selectedId && (
            <div className="period-detail-placeholder">
              <p>Select a period summary to view details, or create a new one above.</p>
            </div>
          )}
          {selectedId && detailLoading && (
            <div className="loading"><div className="spinner" /><p>Loading detail...</p></div>
          )}
          {selectedId && !detailLoading && detail && (
            <PeriodSummaryDetail detail={detail} onEventClick={(id) => setSelectedEventId(id)} />
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

// ── Detail sub-component ────────────────────────────────────

function PeriodSummaryDetail({ detail, onEventClick }) {
  const d = detail;

  // ── Chart data ──
  const chartData = useMemo(() => {
    if (!d.readings || d.readings.length === 0) return [];
    return d.readings.map((r) => {
      const localDate = new Date(r.timestamp);
      return {
        ts: localDate.getTime(),
        value: r.value,
        localLabel: format(localDate, 'MMM d, HH:mm'),
      };
    });
  }, [d.readings]);

  // ── Zoom state ──
  const [refAreaLeft, setRefAreaLeft] = useState(null);
  const [refAreaRight, setRefAreaRight] = useState(null);
  const [isDragging, setIsDragging] = useState(false);
  const [zoomLeft, setZoomLeft] = useState(null);
  const [zoomRight, setZoomRight] = useState(null);

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

    if (refAreaLeft != null && refAreaRight != null) {
      const left = Math.min(refAreaLeft, refAreaRight);
      const right = Math.max(refAreaLeft, refAreaRight);
      if (right - left > 60000) { // At least 1 minute span
        setZoomLeft(left);
        setZoomRight(right);
      }
    }
    setRefAreaLeft(null);
    setRefAreaRight(null);
  }, [isDragging, refAreaLeft, refAreaRight]);

  const handleResetZoom = useCallback(() => {
    setZoomLeft(null);
    setZoomRight(null);
  }, []);

  const isZoomed = zoomLeft != null && zoomRight != null;

  const displayData = useMemo(() => {
    if (!isZoomed) return chartData;
    return chartData.filter((d) => d.ts >= zoomLeft && d.ts <= zoomRight);
  }, [chartData, isZoomed, zoomLeft, zoomRight]);

  // ── Event markers ──
  const eventMarkers = useMemo(() => {
    if (!d.events || d.events.length === 0) return [];
    return d.events
      .map((evt) => {
        const ts = new Date(evt.eventTimestamp).getTime();
        return { ...evt, ts };
      })
      .filter((evt) => {
        if (!isZoomed) return true;
        return evt.ts >= zoomLeft && evt.ts <= zoomRight;
      });
  }, [d.events, isZoomed, zoomLeft, zoomRight]);

  const tsRange = useMemo(() => {
    if (chartData.length === 0) return [0, 0];
    return [chartData[0].ts, chartData[chartData.length - 1].ts];
  }, [chartData]);

  const xDomain = isZoomed ? [zoomLeft, zoomRight] : tsRange;

  const CustomTooltip = ({ active, payload }) => {
    if (!active || !payload || payload.length === 0) return null;
    const entry = payload[0]?.payload;
    if (!entry) return null;
    return (
      <div className="period-chart-tooltip">
        <div className="period-tooltip-time">{entry.localLabel}</div>
        <div style={{ color: '#2196F3' }}>Glucose: {entry.value} mg/dL</div>
      </div>
    );
  };

  return (
    <div className="period-detail-content">
      {/* Header */}
      <div className="period-detail-header">
        <div className="period-detail-title">
          <span className="period-detail-emoji">{classEmoji(d.aiClassification)}</span>
          <h3>{d.name || 'Untitled Period'}</h3>
        </div>
        <div className="period-detail-meta">
          <span>{formatDateFull(d.periodStart)} → {formatDateFull(d.periodEnd)}</span>
          <span className="period-detail-duration">({formatDuration(d.periodStart, d.periodEnd)})</span>
        </div>
        {d.status !== 'completed' && (
          <div className={`period-status-badge ${d.status}`}>
            {d.status === 'pending' ? '⏳ Pending...' : d.status === 'processing' ? '⏳ Processing...' : `❌ Failed: ${d.errorMessage || ''}`}
          </div>
        )}
      </div>

      {/* Chart */}
      {chartData.length > 0 && (
        <div className="period-detail-chart-section">
          <div className="period-chart-title-row">
            <h4>Glucose Trend</h4>
            {isZoomed && (
              <button className="period-zoom-reset" onClick={handleResetZoom}>Reset Zoom</button>
            )}
            {!isZoomed && chartData.length > 0 && (
              <span className="period-zoom-hint">Drag on chart to zoom</span>
            )}
          </div>
          <ResponsiveContainer width="100%" height={320}>
            <LineChart
              data={displayData}
              onMouseDown={handleMouseDown}
              onMouseMove={handleMouseMove}
              onMouseUp={handleMouseUp}
              margin={{ top: 10, right: 30, bottom: 10, left: 10 }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.1)" />
              <XAxis
                dataKey="ts"
                type="number"
                domain={xDomain}
                tickFormatter={(ts) => {
                  try { return format(new Date(ts), 'MMM d HH:mm'); } catch { return ''; }
                }}
                stroke="var(--text-muted)"
                tick={{ fontSize: 11 }}
              />
              <YAxis
                stroke="var(--text-muted)"
                tick={{ fontSize: 11 }}
              />
              <Tooltip content={<CustomTooltip />} />
              <ReferenceArea y1={70} y2={180} fill="rgba(76,175,80,0.08)" />

              {/* Event markers */}
              {eventMarkers.map((evt, i) => (
                <ReferenceLine
                  key={`evt-${i}`}
                  x={evt.ts}
                  stroke="var(--yellow)"
                  strokeDasharray="4 4"
                  strokeWidth={1.5}
                  label={{
                    value: evt.noteTitle.length > 15 ? evt.noteTitle.substring(0, 15) + '…' : evt.noteTitle,
                    position: 'top',
                    fill: 'var(--yellow)',
                    fontSize: 10,
                  }}
                />
              ))}

              <Line
                type="monotone"
                dataKey="value"
                stroke="#2196F3"
                strokeWidth={2}
                dot={false}
                connectNulls
                isAnimationActive={false}
              />

              {/* Drag selection area */}
              {isDragging && refAreaLeft != null && refAreaRight != null && (
                <ReferenceArea
                  x1={refAreaLeft}
                  x2={refAreaRight}
                  fill="rgba(33,150,243,0.2)"
                  strokeOpacity={0.3}
                />
              )}
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}

      {/* Stats */}
      {d.status === 'completed' && d.readingCount > 0 && (
        <div className="period-detail-stats-section">
          <h4>Statistics</h4>
          <div className="period-stats-grid">
            <div className="period-stat">
              <span className="period-stat-label">Average</span>
              <span className="period-stat-value">{d.glucoseAvg ?? '—'} mg/dL</span>
            </div>
            <div className="period-stat">
              <span className="period-stat-label">Min / Max</span>
              <span className="period-stat-value">{d.glucoseMin ?? '—'} / {d.glucoseMax ?? '—'} mg/dL</span>
            </div>
            <div className="period-stat">
              <span className="period-stat-label">Std Dev</span>
              <span className="period-stat-value">{d.glucoseStdDev ?? '—'} mg/dL</span>
            </div>
            <div className="period-stat">
              <span className="period-stat-label">Time in Range</span>
              <span className="period-stat-value" style={{ color: 'var(--green)' }}>
                {d.timeInRange != null ? `${d.timeInRange}%` : '—'}
              </span>
            </div>
            <div className="period-stat">
              <span className="period-stat-label">Above Range</span>
              <span className="period-stat-value" style={{ color: 'var(--red)' }}>
                {d.timeAboveRange != null ? `${d.timeAboveRange}%` : '—'}
              </span>
            </div>
            <div className="period-stat">
              <span className="period-stat-label">Below Range</span>
              <span className="period-stat-value" style={{ color: 'var(--yellow)' }}>
                {d.timeBelowRange != null ? `${d.timeBelowRange}%` : '—'}
              </span>
            </div>
            <div className="period-stat">
              <span className="period-stat-label">Readings</span>
              <span className="period-stat-value">{d.readingCount}</span>
            </div>
            <div className="period-stat">
              <span className="period-stat-label">Events</span>
              <span className="period-stat-value">{d.eventCount}</span>
            </div>
          </div>
        </div>
      )}

      {/* Events */}
      {d.events && d.events.length > 0 && (
        <div className="period-detail-events-section">
          <h4>Events ({d.events.length})</h4>
          <div className="period-events-list">
            {d.events.map((evt) => (
              <div key={evt.id} className="period-event-item period-event-clickable" onClick={() => onEventClick && onEventClick(evt.id)}>
                <div className="period-event-row">
                  <span className="period-event-emoji">{classEmoji(evt.aiClassification)}</span>
                  <span className="period-event-title">{evt.noteTitle}</span>
                  <span className="period-event-time">
                    {formatDateShort(evt.eventTimestamp)}
                  </span>
                </div>
                {evt.glucoseAtEvent != null && (
                  <div className="period-event-glucose">
                    Glucose: {evt.glucoseAtEvent} mg/dL
                    {evt.glucoseSpike != null && (
                      <span className={evt.glucoseSpike > 30 ? 'spike-high' : evt.glucoseSpike > 0 ? 'spike-mod' : 'spike-ok'}>
                        {' '}({evt.glucoseSpike > 0 ? '+' : ''}{evt.glucoseSpike})
                      </span>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* AI Analysis */}
      {d.aiAnalysis && (
        <div className="period-detail-analysis-section">
          <h4>AI Analysis</h4>
          <div
            className="period-analysis-text"
            dangerouslySetInnerHTML={{ __html: renderMarkdown(d.aiAnalysis) }}
          />
        </div>
      )}
    </div>
  );
}

// ── Simple markdown renderer ──
function renderMarkdown(text) {
  if (!text) return '';
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/\*(.+?)\*/g, '<em>$1</em>')
    .replace(/^### (.+)$/gm, '<h5>$1</h5>')
    .replace(/^## (.+)$/gm, '<h4>$1</h4>')
    .replace(/^# (.+)$/gm, '<h3>$1</h3>')
    .replace(/^- (.+)$/gm, '<li>$1</li>')
    .replace(/(<li>.*<\/li>)/gs, '<ul>$1</ul>')
    .replace(/<\/ul>\s*<ul>/g, '')
    .replace(/\n\n/g, '<br/><br/>')
    .replace(/\n/g, '<br/>');
}
