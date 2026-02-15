import React, { useState, useEffect, useMemo, useCallback } from 'react';
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
  Brush,
} from 'recharts';
import { format, parseISO } from 'date-fns';

import MODEL_OPTIONS from './modelOptions';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

function EventDetailModal({ eventId, onClose, onReprocess }) {
  const [currentEventId, setCurrentEventId] = useState(eventId);
  const [event, setEvent] = useState(null);
  const [loading, setLoading] = useState(true);
  const [reprocessing, setReprocessing] = useState(false);
  const [reanalyzeModel, setReanalyzeModel] = useState('');

  // Sync internal ID when parent prop changes
  useEffect(() => {
    setCurrentEventId(eventId);
  }, [eventId]);

  // Zoom state for chart
  const [refAreaLeft, setRefAreaLeft] = useState(null);
  const [refAreaRight, setRefAreaRight] = useState(null);
  const [zoomLeft, setZoomLeft] = useState(null);
  const [zoomRight, setZoomRight] = useState(null);
  const [isDragging, setIsDragging] = useState(false);

  useEffect(() => {
    const loadEvent = async () => {
      setLoading(true);
      setInitialZoomApplied(false);
      setZoomLeft(null);
      setZoomRight(null);
      try {
        const res = await fetch(`${API_BASE}/events/${currentEventId}`);
        if (res.ok) {
          const data = await res.json();
          setEvent(data);
        }
      } catch (err) {
        console.error('Failed to fetch event detail:', err);
      } finally {
        setLoading(false);
      }
    };
    loadEvent();
  }, [currentEventId]);

  // Close on Escape
  useEffect(() => {
    const handleKeyDown = (e) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const reloadEvent = async () => {
    try {
      const res = await fetch(`${API_BASE}/events/${currentEventId}`);
      if (res.ok) {
        const data = await res.json();
        setEvent(data);
      }
    } catch (err) {
      console.error('Failed to fetch event detail:', err);
    }
  };

  const handleReprocess = async () => {
    setReprocessing(true);
    try {
      const body = reanalyzeModel ? { modelOverride: reanalyzeModel } : {};
      const res = await fetch(`${API_BASE}/events/${currentEventId}/reprocess`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (res.ok) {
        onReprocess?.();
        await reloadEvent();
      }
    } catch (err) {
      console.error('Failed to reprocess event:', err);
    } finally {
      setReprocessing(false);
    }
  };

  // Chart data preparation
  const chartData = useMemo(() => {
    if (!event?.readings) return [];
    return event.readings
      .sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
      .map((d) => ({
        ...d,
        time: new Date(d.timestamp).getTime(),
        displayTime: format(parseISO(d.timestamp), 'HH:mm'),
        displayDate: format(parseISO(d.timestamp), 'MMM dd HH:mm'),
      }));
  }, [event]);

  // Event time marker
  const eventTimeMs = event ? new Date(event.eventTimestamp).getTime() : null;
  const eventTimeLabel = event ? format(parseISO(event.eventTimestamp), 'HH:mm') : null;

  // Auto-center the chart on the event when data first loads
  const [initialZoomApplied, setInitialZoomApplied] = useState(false);
  useEffect(() => {
    if (initialZoomApplied || chartData.length < 3 || !eventTimeMs) return;

    const firstTime = chartData[0].time;
    const lastTime = chartData[chartData.length - 1].time;
    const totalRange = lastTime - firstTime;
    if (totalRange <= 0) return;

    // Check if the event is already roughly centered (within 35%-65%)
    const eventPosition = (eventTimeMs - firstTime) / totalRange;
    if (eventPosition >= 0.35 && eventPosition <= 0.65) {
      setInitialZoomApplied(true);
      return;
    }

    // Center the event by using equal time on both sides
    const timeBefore = eventTimeMs - firstTime;
    const timeAfter = lastTime - eventTimeMs;
    const halfWindow = Math.min(timeBefore, timeAfter);
    // Add 10% padding so the edge readings aren't clipped
    const padded = halfWindow * 1.1;

    setZoomLeft(Math.max(eventTimeMs - padded, firstTime));
    setZoomRight(Math.min(eventTimeMs + padded, lastTime));
    setInitialZoomApplied(true);
  }, [chartData, eventTimeMs, initialZoomApplied]);

  // Apply zoom filter
  const displayData = useMemo(() => {
    if (zoomLeft === null || zoomRight === null) return chartData;
    return chartData.filter((d) => d.time >= zoomLeft && d.time <= zoomRight);
  }, [chartData, zoomLeft, zoomRight]);

  const isZoomed = zoomLeft !== null && zoomRight !== null;

  const yDomain = useMemo(() => {
    if (displayData.length === 0) return [40, 300];
    const values = displayData.map((d) => d.value);
    const min = Math.min(...values);
    const max = Math.max(...values);
    return [
      Math.max(30, Math.floor(min / 10) * 10 - 10),
      Math.min(400, Math.ceil(max / 10) * 10 + 10),
    ];
  }, [displayData]);

  const xDataKey = useMemo(() => {
    if (!displayData.length) return 'displayTime';
    const rangeMs = displayData[displayData.length - 1].time - displayData[0].time;
    const rangeHours = rangeMs / (1000 * 60 * 60);
    return rangeHours > 48 ? 'displayDate' : 'displayTime';
  }, [displayData]);

  // Zoom handlers
  const handleMouseDown = useCallback(
    (e) => {
      if (e && e.activeLabel) {
        const point = displayData.find(
          (d) => d.displayTime === e.activeLabel || d.displayDate === e.activeLabel
        );
        if (point) {
          setRefAreaLeft(point.time);
          setIsDragging(true);
        }
      }
    },
    [displayData]
  );

  const handleMouseMove = useCallback(
    (e) => {
      if (isDragging && e && e.activeLabel) {
        const point = displayData.find(
          (d) => d.displayTime === e.activeLabel || d.displayDate === e.activeLabel
        );
        if (point) setRefAreaRight(point.time);
      }
    },
    [isDragging, displayData]
  );

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
    const pointsInRange = chartData.filter((d) => d.time >= left && d.time <= right);
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
  }, []);

  // Find the event marker point for the reference line
  const eventMarkerX = useMemo(() => {
    if (!eventTimeMs || displayData.length === 0) return null;
    // Find the closest point to the event time
    const closest = displayData.reduce((prev, curr) =>
      Math.abs(curr.time - eventTimeMs) < Math.abs(prev.time - eventTimeMs) ? curr : prev
    );
    return closest[xDataKey];
  }, [eventTimeMs, displayData, xDataKey]);

  // Overlapping events markers (other events in the same glucose window)
  const overlappingMarkers = useMemo(() => {
    if (!event?.overlappingEvents?.length || displayData.length === 0) return [];
    return event.overlappingEvents.map((oe) => {
      const oeTimeMs = new Date(oe.eventTimestamp).getTime();
      const closest = displayData.reduce((prev, curr) =>
        Math.abs(curr.time - oeTimeMs) < Math.abs(prev.time - oeTimeMs) ? curr : prev
      );
      return {
        ...oe,
        markerX: closest[xDataKey],
        timeLabel: format(parseISO(oe.eventTimestamp), 'HH:mm'),
      };
    });
  }, [event, displayData, xDataKey]);

  const CustomTooltip = ({ active, payload }) => {
    if (!active || !payload || !payload.length) return null;
    const d = payload[0].payload;
    const isBeforeEvent = eventTimeMs && d.time < eventTimeMs;
    return (
      <div
        style={{
          background: '#1a1a2e',
          border: '1px solid #333',
          borderRadius: 8,
          padding: '10px 14px',
          fontSize: '0.85rem',
        }}
      >
        <div style={{ color: '#888', marginBottom: 4 }}>{d.displayDate}</div>
        <div
          style={{
            fontWeight: 700,
            fontSize: '1.1rem',
            color: getColor(d.value),
          }}
        >
          {d.value} mg/dL
        </div>
        <div style={{ color: '#666', fontSize: '0.75rem', marginTop: 2 }}>
          {isBeforeEvent ? '◀ Before event' : '▶ After event'}
        </div>
      </div>
    );
  };

  // Simple markdown-like renderer for AI analysis
  const renderAnalysis = (text) => {
    if (!text) return null;
    // Basic markdown: **bold**, bullet points, paragraphs
    return text.split('\n').map((line, i) => {
      let content = line
        .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
        .replace(/\*(.+?)\*/g, '<em>$1</em>');

      if (line.startsWith('- ') || line.startsWith('• ')) {
        return (
          <li
            key={i}
            dangerouslySetInnerHTML={{ __html: content.substring(2) }}
            style={{ marginBottom: 4, marginLeft: 16 }}
          />
        );
      }
      if (line.trim() === '') return <br key={i} />;
      return (
        <p
          key={i}
          dangerouslySetInnerHTML={{ __html: content }}
          style={{ marginBottom: 6 }}
        />
      );
    });
  };

  return (
    <div className="event-modal-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="event-modal">
        {/* Header */}
        <div className="event-modal-header">
          <div>
            <h2 className="event-modal-title">
              {loading ? 'Loading...' : event?.noteTitle || 'Untitled Event'}
            </h2>
            {event && (
              <div className="event-modal-meta">
                <span className="note-date">
                  {format(parseISO(event.eventTimestamp), 'EEEE, MMM dd yyyy · HH:mm')}
                </span>
                {event.aiClassification && (
                  <span className={`event-tag classification-tag classification-tag-${event.aiClassification}`}>
                    {event.aiClassification === 'green' ? '🟢 Good' : event.aiClassification === 'yellow' ? '🟡 Concerning' : '🔴 Bad'}
                  </span>
                )}
                {event.isProcessed && (
                  <span className="event-tag ai">🤖 Analyzed</span>
                )}
                {!event.isProcessed && (
                  <span className="event-tag pending">⏳ Pending Analysis</span>
                )}
              </div>
            )}
          </div>
          <button className="note-modal-close" onClick={onClose}>
            ✕
          </button>
        </div>

        {/* Body */}
        <div className="event-modal-body">
          {loading ? (
            <div className="loading">
              <div className="spinner" />
              <p>Loading event data...</p>
            </div>
          ) : event ? (
            <>
              {/* Note Content Section */}
              {event.noteContent && (
                <div className="event-section">
                  <h3 className="event-section-title">📝 Note</h3>
                  <div className="event-note-content">{event.noteContent}</div>
                </div>
              )}

              {/* Glucose Stats */}
              <div className="event-section">
                <h3 className="event-section-title">📊 Glucose Summary</h3>
                <div className="event-stats-grid">
                  <div className="event-stat">
                    <span className="event-stat-label">At Event</span>
                    <span className="event-stat-value">
                      {event.glucoseAtEvent != null
                        ? `${Math.round(event.glucoseAtEvent)} mg/dL`
                        : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Min</span>
                    <span className="event-stat-value">
                      {event.glucoseMin != null
                        ? `${Math.round(event.glucoseMin)} mg/dL`
                        : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Max</span>
                    <span className="event-stat-value">
                      {event.glucoseMax != null
                        ? `${Math.round(event.glucoseMax)} mg/dL`
                        : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Average</span>
                    <span className="event-stat-value">
                      {event.glucoseAvg != null ? `${event.glucoseAvg} mg/dL` : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Spike</span>
                    <span
                      className={`event-stat-value ${
                        event.glucoseSpike != null
                          ? event.glucoseSpike > 60
                            ? 'text-very-high'
                            : event.glucoseSpike > 30
                            ? 'text-high'
                            : 'text-normal'
                          : ''
                      }`}
                    >
                      {event.glucoseSpike != null
                        ? `+${Math.round(event.glucoseSpike)} mg/dL`
                        : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Peak Time</span>
                    <span className="event-stat-value">
                      {event.peakTime
                        ? format(parseISO(event.peakTime), 'HH:mm')
                        : 'N/A'}
                    </span>
                  </div>
                </div>
              </div>

              {/* Interactive Glucose Chart */}
              {chartData.length > 0 && (
                <div className="event-section">
                  <h3 className="event-section-title">📈 Glucose Timeline</h3>
                  <div className="event-chart-container">
                    <div className="chart-toolbar">
                      <span className="chart-hint">
                        {isZoomed ? 'Zoomed — ' : 'Click & drag on chart to zoom'}
                      </span>
                      {isZoomed && (
                        <button className="btn-reset-zoom" onClick={handleResetZoom}>
                          ↩ Show All
                        </button>
                      )}
                    </div>
                    <ResponsiveContainer width="100%" height={320}>
                      <LineChart
                        data={displayData}
                        margin={{ top: 10, right: 20, left: 0, bottom: 0 }}
                        onMouseDown={handleMouseDown}
                        onMouseMove={handleMouseMove}
                        onMouseUp={handleMouseUp}
                        onMouseLeave={handleMouseUp}
                      >
                        <CartesianGrid strokeDasharray="3 3" stroke="#1f1f35" />
                        <XAxis
                          dataKey={xDataKey}
                          stroke="#555"
                          tick={{ fontSize: 11 }}
                          interval="preserveStartEnd"
                        />
                        <YAxis
                          stroke="#555"
                          tick={{ fontSize: 12 }}
                          domain={yDomain}
                        />
                        <Tooltip content={<CustomTooltip />} />

                        {/* Target range */}
                        <ReferenceArea
                          y1={70}
                          y2={180}
                          fill="#4ade80"
                          fillOpacity={0.05}
                        />
                        <ReferenceLine
                          y={70}
                          stroke="#4ade80"
                          strokeDasharray="4 4"
                          strokeOpacity={0.4}
                        />
                        <ReferenceLine
                          y={180}
                          stroke="#fbbf24"
                          strokeDasharray="4 4"
                          strokeOpacity={0.4}
                        />

                        {/* Event time vertical marker */}
                        {eventMarkerX && (
                          <ReferenceLine
                            x={eventMarkerX}
                            stroke="#ff6b6b"
                            strokeDasharray="6 3"
                            strokeWidth={2}
                            label={{
                              value: `Event ${eventTimeLabel}`,
                              position: 'top',
                              fill: '#ff6b6b',
                              fontSize: 11,
                              fontWeight: 600,
                            }}
                          />
                        )}

                        {/* Overlapping events — muted markers */}
                        {overlappingMarkers.map((oe) => (
                          <ReferenceLine
                            key={oe.id}
                            x={oe.markerX}
                            stroke="#888"
                            strokeDasharray="4 4"
                            strokeWidth={1}
                            strokeOpacity={0.6}
                            label={{
                              value: `${oe.noteTitle} ${oe.timeLabel}`,
                              position: 'insideTopRight',
                              fill: '#888',
                              fontSize: 9,
                              fontWeight: 400,
                            }}
                          />
                        ))}

                        <Line
                          type="monotone"
                          dataKey="value"
                          stroke="#4facfe"
                          strokeWidth={2}
                          dot={false}
                          activeDot={{ r: 5, fill: '#4facfe' }}
                          isAnimationActive={false}
                        />

                        {/* Zoom selection highlight */}
                        {isDragging && refAreaLeft && refAreaRight && (
                          <ReferenceArea
                            x1={
                              displayData.find((d) => d.time === refAreaLeft)?.[
                                xDataKey
                              ]
                            }
                            x2={
                              displayData.find((d) => d.time === refAreaRight)?.[
                                xDataKey
                              ]
                            }
                            strokeOpacity={0.3}
                            fill="#4facfe"
                            fillOpacity={0.15}
                          />
                        )}

                        {!isZoomed && chartData.length > 20 && (
                          <Brush
                            dataKey="displayTime"
                            height={25}
                            stroke="#4facfe"
                            fill="#12121f"
                            travellerWidth={10}
                            tickFormatter={() => ''}
                          />
                        )}
                      </LineChart>
                    </ResponsiveContainer>
                    <div className="event-chart-legend">
                      <span className="legend-item">
                        <span
                          className="legend-line"
                          style={{ background: '#ff6b6b' }}
                        />
                        Event time
                      </span>
                      {overlappingMarkers.length > 0 && (
                        <span className="legend-item">
                          <span
                            className="legend-line"
                            style={{ background: '#888' }}
                          />
                          Other events in window
                        </span>
                      )}
                      <span className="legend-item">
                        <span
                          className="legend-line"
                          style={{ background: '#4ade80' }}
                        />
                        Target range (70–180)
                      </span>
                    </div>
                  </div>
                </div>
              )}

              {chartData.length === 0 && (
                <div className="event-section">
                  <h3 className="event-section-title">📈 Glucose Timeline</h3>
                  <div className="event-no-data">
                    No glucose readings available for this time period.
                  </div>
                </div>
              )}

              {/* Overlapping Events Info */}
              {event.overlappingEvents && event.overlappingEvents.length > 0 && (
                <div className="event-section">
                  <h3 className="event-section-title">🔗 Other Events in Window</h3>
                  <div className="overlapping-events-list">
                    {event.overlappingEvents.map((oe) => {
                      const offsetMs = new Date(oe.eventTimestamp).getTime() - new Date(event.eventTimestamp).getTime();
                      const absMin = Math.round(Math.abs(offsetMs) / 60000);
                      const direction = offsetMs >= 0 ? 'after' : 'before';
                      const classColor = oe.aiClassification === 'green' ? '🟢' : oe.aiClassification === 'yellow' ? '🟡' : oe.aiClassification === 'red' ? '🔴' : '⬜';
                      return (
                        <div
                          key={oe.id}
                          className="overlapping-event-item overlapping-event-clickable"
                          onClick={() => setCurrentEventId(oe.id)}
                          title={`View "${oe.noteTitle}" event`}
                        >
                          <span className="overlapping-event-class">{classColor}</span>
                          <span className="overlapping-event-title">{oe.noteTitle}</span>
                          <span className="overlapping-event-time">
                            {format(parseISO(oe.eventTimestamp), 'HH:mm')} ({absMin} min {direction})
                          </span>
                          {oe.glucoseAtEvent != null && (
                            <span className="overlapping-event-glucose">
                              {Math.round(oe.glucoseAtEvent)} mg/dL
                            </span>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* AI Analysis — latest + full history */}
              <div className="event-section">
                <h3 className="event-section-title">
                  🤖 AI Analysis
                  <span className="reanalyze-controls">
                    <select
                      className="model-override-select"
                      value={reanalyzeModel}
                      onChange={(e) => setReanalyzeModel(e.target.value)}
                      disabled={reprocessing}
                      title="Pick a model for this reanalysis (or leave as default)"
                    >
                      {MODEL_OPTIONS.map((m) => (
                        <option key={m.value} value={m.value}>{m.label}</option>
                      ))}
                    </select>
                    <button
                      className="btn-reprocess"
                      onClick={handleReprocess}
                      disabled={reprocessing}
                      title="Re-run AI analysis immediately"
                    >
                      {reprocessing ? '⏳ Analyzing...' : '🔄 Reanalyze'}
                    </button>
                  </span>
                </h3>
                {event.aiAnalysis ? (
                  <div className="event-ai-analysis">
                    {renderAnalysis(event.aiAnalysis)}
                  </div>
                ) : (
                  <div className="event-no-data">
                    {event.isProcessed
                      ? 'Analysis was completed but no text was generated.'
                      : 'AI analysis is pending. Configure your GPT API key in Settings and wait for the next analysis cycle.'}
                  </div>
                )}
                {event.processedAt && (
                  <div className="event-processed-at">
                    Latest analysis: {format(parseISO(event.processedAt), 'MMM dd, yyyy HH:mm')}
                    {event.aiModel && <span className="ai-model-badge">{event.aiModel}</span>}
                  </div>
                )}
              </div>

              {/* Full Analysis History with glucose stats for each run */}
              {event.analysisHistory && event.analysisHistory.length > 0 && (
                <AnalysisHistorySection
                  history={event.analysisHistory}
                  renderAnalysis={renderAnalysis}
                />
              )}
            </>
          ) : (
            <div className="event-no-data">Event not found.</div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Glucose stats mini-grid for history entries ──────────────────
function HistoryStatsGrid({ entry }) {
  if (entry.glucoseAtEvent == null && entry.glucoseMin == null) return null;
  return (
    <div className="history-stats-grid">
      {entry.glucoseAtEvent != null && (
        <span className="history-stat">
          <span className="history-stat-label">At Event</span>
          <span className="history-stat-value">{Math.round(entry.glucoseAtEvent)}</span>
        </span>
      )}
      {entry.glucoseMin != null && (
        <span className="history-stat">
          <span className="history-stat-label">Min</span>
          <span className="history-stat-value">{Math.round(entry.glucoseMin)}</span>
        </span>
      )}
      {entry.glucoseMax != null && (
        <span className="history-stat">
          <span className="history-stat-label">Max</span>
          <span className="history-stat-value">{Math.round(entry.glucoseMax)}</span>
        </span>
      )}
      {entry.glucoseAvg != null && (
        <span className="history-stat">
          <span className="history-stat-label">Avg</span>
          <span className="history-stat-value">{entry.glucoseAvg}</span>
        </span>
      )}
      {entry.glucoseSpike != null && (
        <span className="history-stat">
          <span className="history-stat-label">Spike</span>
          <span className={`history-stat-value ${entry.glucoseSpike > 60 ? 'text-very-high' : entry.glucoseSpike > 30 ? 'text-high' : 'text-normal'}`}>
            +{Math.round(entry.glucoseSpike)}
          </span>
        </span>
      )}
      {entry.peakTime && (
        <span className="history-stat">
          <span className="history-stat-label">Peak</span>
          <span className="history-stat-value">{format(parseISO(entry.peakTime), 'HH:mm')}</span>
        </span>
      )}
    </div>
  );
}

// ── Analysis History sub-component ──────────────────────────────
function AnalysisHistorySection({ history, renderAnalysis }) {
  const [expandedId, setExpandedId] = useState(null);

  // history is sorted most-recent-first from the API — show ALL entries
  return (
    <div className="event-section">
      <h3 className="event-section-title">
        📜 Analysis History
        <span className="history-count">{history.length} run{history.length !== 1 ? 's' : ''}</span>
      </h3>
      <div className="analysis-history-list">
        {history.map((entry, index) => (
          <div key={entry.id} className={`analysis-history-item ${index === 0 ? 'latest' : ''}`}>
            <div
              className="analysis-history-header"
              onClick={() =>
                setExpandedId(expandedId === entry.id ? null : entry.id)
              }
            >
              <div className="analysis-history-meta">
                <span className="analysis-history-date">
                  {index === 0 && <span className="analysis-latest-badge">Latest</span>}
                  {format(parseISO(entry.analyzedAt), 'MMM dd, yyyy HH:mm')}
                </span>
                {entry.aiClassification && (
                  <span className={`event-tag classification-tag classification-tag-${entry.aiClassification}`} style={{ fontSize: '0.7rem', padding: '1px 6px' }}>
                    {entry.aiClassification === 'green' ? '🟢' : entry.aiClassification === 'yellow' ? '🟡' : '🔴'}
                  </span>
                )}
                {entry.reason && (
                  <span className="analysis-history-reason">{entry.reason}</span>
                )}
                {entry.aiModel && (
                  <span className="ai-model-badge">{entry.aiModel}</span>
                )}
                <span className="analysis-history-quick-stats">
                  {entry.readingCount} readings
                  {entry.glucoseAtEvent != null && ` · At event: ${Math.round(entry.glucoseAtEvent)}`}
                  {entry.glucoseSpike != null && ` · Spike: +${Math.round(entry.glucoseSpike)}`}
                  {entry.glucoseMin != null && entry.glucoseMax != null && ` · Range: ${Math.round(entry.glucoseMin)}–${Math.round(entry.glucoseMax)}`}
                  {(entry.glucoseAtEvent != null || entry.glucoseMin != null) && ' mg/dL'}
                </span>
              </div>
              <div className="analysis-history-stats">
                <span className="analysis-history-toggle">
                  {expandedId === entry.id ? '▲' : '▼'}
                </span>
              </div>
            </div>
            {expandedId === entry.id && (
              <div className="analysis-history-body">
                <HistoryStatsGrid entry={entry} />
                {entry.aiAnalysis && renderAnalysis(entry.aiAnalysis)}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function getColor(value) {
  if (value < 70) return '#f87171';
  if (value <= 180) return '#4ade80';
  if (value <= 250) return '#fbbf24';
  return '#f87171';
}

export default EventDetailModal;
