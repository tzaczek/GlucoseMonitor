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
import { format, parseISO } from 'date-fns';
import MODEL_OPTIONS from './modelOptions';
import useInfiniteScroll from '../hooks/useInfiniteScroll';
import PAGE_SIZES from '../config/pageSize';

const API_BASE = process.env.REACT_APP_API_URL || '/api';
const PAGE_SIZE = PAGE_SIZES.dailySummaries;

function DailySummariesPage() {
  const [summaries, setSummaries] = useState([]);
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [totalCount, setTotalCount] = useState(0);
  const [selectedSummaryId, setSelectedSummaryId] = useState(null);
  const [triggering, setTriggering] = useState(false);
  const [triggerResult, setTriggerResult] = useState(null);
  const [triggerModel, setTriggerModel] = useState('');
  const summariesOffsetRef = useRef(0);

  const fetchSummaries = useCallback(async (append = false) => {
    if (append) setLoadingMore(true); else setLoading(true);
    try {
      const offset = append ? summariesOffsetRef.current : 0;
      const res = await fetch(`${API_BASE}/dailysummaries?limit=${PAGE_SIZE}&offset=${offset}`);
      if (res.ok) {
        const data = await res.json();
        if (append) {
          setSummaries(prev => [...prev, ...data.items]);
        } else {
          setSummaries(data.items);
        }
        setTotalCount(data.totalCount);
        summariesOffsetRef.current = (append ? summariesOffsetRef.current : 0) + data.items.length;
      }
    } catch (err) {
      console.error('Failed to fetch daily summaries:', err);
    } finally {
      setLoading(false);
      setLoadingMore(false);
    }
  }, []);

  const summariesHasMore = summaries.length < totalCount;
  const loadMoreSummaries = useCallback(() => fetchSummaries(true), [fetchSummaries]);
  useInfiniteScroll(loadMoreSummaries, { hasMore: summariesHasMore, loading: loadingMore });

  const fetchStatus = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/dailysummaries/status`);
      if (res.ok) {
        const data = await res.json();
        setStatus(data);
      }
    } catch (err) {
      console.error('Failed to fetch daily summaries status:', err);
    }
  }, []);

  const handleTrigger = useCallback(async () => {
    setTriggering(true);
    setTriggerResult(null);
    try {
      const body = triggerModel ? { modelOverride: triggerModel } : {};
      const res = await fetch(`${API_BASE}/dailysummaries/trigger`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      const data = await res.json();
      if (res.ok) {
        setTriggerResult({ success: true, message: data.message });
        summariesOffsetRef.current = 0;
        fetchSummaries();
        fetchStatus();
      } else {
        setTriggerResult({ success: false, message: data.message || 'Failed to trigger generation.' });
      }
    } catch (err) {
      console.error('Failed to trigger daily summary generation:', err);
      setTriggerResult({ success: false, message: 'Network error — could not reach the server.' });
    } finally {
      setTriggering(false);
      // Auto-hide result after 8 seconds
      setTimeout(() => setTriggerResult(null), 8000);
    }
  }, [fetchSummaries, fetchStatus, triggerModel]);

  useEffect(() => {
    summariesOffsetRef.current = 0;
    fetchSummaries();
    fetchStatus();

    const handleUpdated = () => {
      summariesOffsetRef.current = 0;
      fetchSummaries();
      fetchStatus();
    };

    window.addEventListener('dailySummariesUpdated', handleUpdated);
    return () => window.removeEventListener('dailySummariesUpdated', handleUpdated);
  }, [fetchSummaries, fetchStatus]);

  const getTimeInRangeClass = (tir) => {
    if (tir == null) return '';
    if (tir >= 70) return 'tir-good';
    if (tir >= 50) return 'tir-moderate';
    return 'tir-poor';
  };

  const getTimeInRangeLabel = (tir) => {
    if (tir == null) return 'N/A';
    if (tir >= 70) return 'Good';
    if (tir >= 50) return 'Moderate';
    return 'Needs Work';
  };

  return (
    <div className="daily-summaries-page">
      {/* Status bar */}
      {status && (
        <div className="events-status-bar">
          <div className="events-status-item">
            <span className="events-status-value">{status.totalSummaries}</span>
            <span className="events-status-label">Days Tracked</span>
          </div>
          <div className="events-status-item">
            <span className="events-status-value text-normal">{status.processedSummaries}</span>
            <span className="events-status-label">Analyzed</span>
          </div>
          {status.pendingSummaries > 0 && (
            <div className="events-status-item">
              <span className="events-status-value text-high">{status.pendingSummaries}</span>
              <span className="events-status-label">Pending</span>
            </div>
          )}
          <select
            className="model-override-select"
            value={triggerModel}
            onChange={(e) => setTriggerModel(e.target.value)}
            disabled={triggering}
            title="Pick a model for this generation (or leave as default)"
          >
            {MODEL_OPTIONS.map((m) => (
              <option key={m.value} value={m.value}>{m.label}</option>
            ))}
          </select>
          <button
            className="daily-trigger-btn"
            onClick={handleTrigger}
            disabled={triggering}
            title="Manually trigger daily summary generation for all missing days"
          >
            {triggering ? (
              <>
                <span className="spinner-inline" /> Generating...
              </>
            ) : (
              <>▶ Generate Summaries</>
            )}
          </button>
        </div>
      )}

      {/* Trigger result toast */}
      {triggerResult && (
        <div className={`daily-trigger-toast ${triggerResult.success ? 'success' : 'error'}`}>
          {triggerResult.success ? '✅' : '❌'} {triggerResult.message}
        </div>
      )}

      {/* Empty state */}
      {!loading && summaries.length === 0 && (
        <div className="events-empty">
          <div className="events-empty-icon">📅</div>
          <h3>No Daily Summaries Yet</h3>
          <p>
            Daily summaries aggregate all events and glucose readings for a day,
            then run an AI analysis. Use the <strong>Generate Summaries</strong> button
            to create them on demand — including for today's partial data.
          </p>
          <ol className="events-checklist">
            <li>At least some glucose data is required</li>
            <li>Manual trigger includes today (partial day)</li>
            <li>Each generation is saved as a snapshot for history</li>
            <li>GPT API key must be configured in Settings</li>
          </ol>
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="loading">
          <div className="spinner" />
          <p>Loading daily summaries...</p>
        </div>
      )}

      {/* Summaries list */}
      {!loading && summaries.length > 0 && (
        <div className="daily-summaries-list">
          {summaries.map((s) => (
            <div
              key={s.id}
              className={`daily-summary-card ${s.isProcessed ? 'processed' : 'pending'} ${s.aiClassification ? `classification-${s.aiClassification}` : ''}`}
              onClick={() => setSelectedSummaryId(s.id)}
            >
              <div className="daily-summary-left">
                <div className={`daily-summary-date-badge ${s.aiClassification ? `badge-${s.aiClassification}` : ''}`}>
                  <span className="event-day">
                    {format(parseISO(s.date), 'dd')}
                  </span>
                  <span className="event-month">
                    {format(parseISO(s.date), 'MMM')}
                  </span>
                  <span className="daily-summary-year">
                    {format(parseISO(s.date), 'yyyy')}
                  </span>
                  <span className="daily-summary-dow">
                    {format(parseISO(s.date), 'EEE')}
                  </span>
                </div>
              </div>

              <div className="daily-summary-center">
                <div className="daily-summary-title-row">
                  <h3 className="daily-summary-title">
                    {format(parseISO(s.date), 'EEEE, MMMM d')}
                  </h3>
                  {s.aiClassification && (
                    <span className={`event-tag classification-tag classification-tag-${s.aiClassification}`}>
                      {s.aiClassification === 'green' ? '🟢 Good Day' : s.aiClassification === 'yellow' ? '🟡 Concerning' : '🔴 Difficult Day'}
                    </span>
                  )}
                  {s.hasAnalysis && (
                    <span className="event-tag ai">🤖 AI Summary</span>
                  )}
                  {!s.isProcessed && (
                    <span className="event-tag pending">⏳ Processing...</span>
                  )}
                </div>
                {s.eventTitles && (
                  <p className="daily-summary-events-preview">
                    {s.eventCount} event{s.eventCount !== 1 ? 's' : ''}: {s.eventTitles}
                  </p>
                )}
                {!s.eventTitles && s.eventCount === 0 && (
                  <p className="daily-summary-events-preview">No events logged</p>
                )}
                <div className="event-tags">
                  <span className="event-tag readings">
                    {s.readingCount} reading{s.readingCount !== 1 ? 's' : ''}
                  </span>
                  {s.eventCount > 0 && (
                    <span className="event-tag readings">
                      {s.eventCount} event{s.eventCount !== 1 ? 's' : ''}
                    </span>
                  )}
                  {s.snapshotCount > 0 && (
                    <span className="event-tag snapshots">
                      📸 {s.snapshotCount} snapshot{s.snapshotCount !== 1 ? 's' : ''}
                    </span>
                  )}
                </div>
              </div>

              <div className="daily-summary-right">
                {s.glucoseAvg != null && (
                  <div className="event-glucose-badge">
                    <span className="event-glucose-value">{Math.round(s.glucoseAvg)}</span>
                    <span className="event-glucose-unit">avg mg/dL</span>
                  </div>
                )}
                {s.timeInRange != null && (
                  <div className={`daily-tir-badge ${getTimeInRangeClass(s.timeInRange)}`}>
                    <span className="tir-value">{Math.round(s.timeInRange)}%</span>
                    <span className="tir-label">{getTimeInRangeLabel(s.timeInRange)}</span>
                  </div>
                )}
                {s.glucoseMin != null && s.glucoseMax != null && (
                  <div className="event-range">
                    {Math.round(s.glucoseMin)}–{Math.round(s.glucoseMax)}
                  </div>
                )}
              </div>
            </div>
          ))}
          <div className="scroll-sentinel" />
          {loadingMore && (
            <div className="loading-more">
              <div className="spinner spinner-sm" />
              <span>Loading more summaries...</span>
            </div>
          )}
          {!summariesHasMore && summaries.length > PAGE_SIZE && (
            <div className="list-end-message">All {totalCount} summaries loaded</div>
          )}
        </div>
      )}

      {/* Detail modal */}
      {selectedSummaryId && (
        <DailySummaryDetailModal
          summaryId={selectedSummaryId}
          onClose={() => setSelectedSummaryId(null)}
        />
      )}
    </div>
  );
}

// ── Detail Modal ──────────────────────────────────────────────

function DailySummaryDetailModal({ summaryId, onClose }) {
  const [summary, setSummary] = useState(null);
  const [loading, setLoading] = useState(true);
  const [selectedSnapshotId, setSelectedSnapshotId] = useState(null);
  const [snapshotDetail, setSnapshotDetail] = useState(null);
  const [snapshotLoading, setSnapshotLoading] = useState(false);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      try {
        const res = await fetch(`${API_BASE}/dailysummaries/${summaryId}`);
        if (res.ok) {
          const data = await res.json();
          setSummary(data);
        }
      } catch (err) {
        console.error('Failed to fetch daily summary detail:', err);
      } finally {
        setLoading(false);
      }
    };
    load();
  }, [summaryId]);

  useEffect(() => {
    const handleKeyDown = (e) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  // Fetch snapshot detail when one is selected
  useEffect(() => {
    if (!selectedSnapshotId) {
      setSnapshotDetail(null);
      return;
    }
    const loadSnapshot = async () => {
      setSnapshotLoading(true);
      try {
        const res = await fetch(`${API_BASE}/dailysummaries/snapshots/${selectedSnapshotId}`);
        if (res.ok) {
          const data = await res.json();
          setSnapshotDetail(data);
        }
      } catch (err) {
        console.error('Failed to fetch snapshot detail:', err);
      } finally {
        setSnapshotLoading(false);
      }
    };
    loadSnapshot();
  }, [selectedSnapshotId]);

  // Chart data
  const chartData = useMemo(() => {
    if (!summary?.readings) return [];
    return summary.readings
      .sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
      .map((d) => ({
        ...d,
        time: new Date(d.timestamp).getTime(),
        displayTime: format(parseISO(d.timestamp), 'HH:mm'),
      }));
  }, [summary]);

  const yDomain = useMemo(() => {
    if (chartData.length === 0) return [40, 300];
    const values = chartData.map((d) => d.value);
    const min = Math.min(...values);
    const max = Math.max(...values);
    return [
      Math.max(30, Math.floor(min / 10) * 10 - 10),
      Math.min(400, Math.ceil(max / 10) * 10 + 10),
    ];
  }, [chartData]);

  // Event time markers
  const eventMarkers = useMemo(() => {
    if (!summary?.events || chartData.length === 0) return [];
    return summary.events.map((evt) => {
      const evtTimeMs = new Date(evt.eventTimestamp).getTime();
      const closest = chartData.reduce((prev, curr) =>
        Math.abs(curr.time - evtTimeMs) < Math.abs(prev.time - evtTimeMs) ? curr : prev
      );
      return {
        x: closest.displayTime,
        label: evt.noteTitle?.length > 20 ? evt.noteTitle.slice(0, 18) + '…' : evt.noteTitle,
        time: format(parseISO(evt.eventTimestamp), 'HH:mm'),
      };
    });
  }, [summary, chartData]);

  const CustomTooltip = ({ active, payload }) => {
    if (!active || !payload || !payload.length) return null;
    const d = payload[0].payload;
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
        <div style={{ color: '#888', marginBottom: 4 }}>{d.displayTime}</div>
        <div
          style={{
            fontWeight: 700,
            fontSize: '1.1rem',
            color: getColor(d.value),
          }}
        >
          {d.value} mg/dL
        </div>
      </div>
    );
  };

  const renderAnalysis = (text) => {
    if (!text) return null;
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
              {loading
                ? 'Loading...'
                : summary
                ? `Daily Summary — ${format(parseISO(summary.date), 'EEEE, MMMM d, yyyy')}`
                : 'Not Found'}
            </h2>
            {summary && (
              <div className="event-modal-meta">
                {summary.aiClassification && (
                  <span className={`event-tag classification-tag classification-tag-${summary.aiClassification}`}>
                    {summary.aiClassification === 'green' ? '🟢 Good Day' : summary.aiClassification === 'yellow' ? '🟡 Concerning' : '🔴 Difficult Day'}
                  </span>
                )}
                {summary.isProcessed && (
                  <span className="event-tag ai">🤖 AI Analyzed</span>
                )}
                {!summary.isProcessed && (
                  <span className="event-tag pending">⏳ Pending Analysis</span>
                )}
                <span className="note-date">
                  {summary.eventCount} event{summary.eventCount !== 1 ? 's' : ''} · {summary.readingCount} readings
                </span>
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
              <p>Loading daily summary...</p>
            </div>
          ) : summary ? (
            <>
              {/* Day Stats */}
              <div className="event-section">
                <h3 className="event-section-title">📊 Day Statistics</h3>
                <div className="daily-stats-grid">
                  <div className="event-stat">
                    <span className="event-stat-label">Average</span>
                    <span className="event-stat-value">
                      {summary.glucoseAvg != null ? `${summary.glucoseAvg} mg/dL` : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Min</span>
                    <span className="event-stat-value">
                      {summary.glucoseMin != null ? `${Math.round(summary.glucoseMin)} mg/dL` : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Max</span>
                    <span className="event-stat-value">
                      {summary.glucoseMax != null ? `${Math.round(summary.glucoseMax)} mg/dL` : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Std Dev</span>
                    <span className="event-stat-value">
                      {summary.glucoseStdDev != null ? `${summary.glucoseStdDev}` : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Time in Range</span>
                    <span className={`event-stat-value ${
                      summary.timeInRange != null
                        ? summary.timeInRange >= 70
                          ? 'text-normal'
                          : summary.timeInRange >= 50
                          ? 'text-high'
                          : 'text-very-high'
                        : ''
                    }`}>
                      {summary.timeInRange != null ? `${summary.timeInRange}%` : 'N/A'}
                    </span>
                  </div>
                  <div className="event-stat">
                    <span className="event-stat-label">Readings</span>
                    <span className="event-stat-value">{summary.readingCount}</span>
                  </div>
                </div>

                {/* Time distribution bar */}
                {summary.timeInRange != null && (
                  <div className="tir-bar-container">
                    <div className="tir-bar">
                      {summary.timeBelowRange > 0 && (
                        <div
                          className="tir-segment tir-below"
                          style={{ width: `${summary.timeBelowRange}%` }}
                          title={`Below range: ${summary.timeBelowRange}%`}
                        />
                      )}
                      <div
                        className="tir-segment tir-in"
                        style={{ width: `${summary.timeInRange}%` }}
                        title={`In range: ${summary.timeInRange}%`}
                      />
                      {summary.timeAboveRange > 0 && (
                        <div
                          className="tir-segment tir-above"
                          style={{ width: `${summary.timeAboveRange}%` }}
                          title={`Above range: ${summary.timeAboveRange}%`}
                        />
                      )}
                    </div>
                    <div className="tir-labels">
                      {summary.timeBelowRange > 0 && (
                        <span className="tir-label-item tir-below-label">
                          ↓ {summary.timeBelowRange}% Low
                        </span>
                      )}
                      <span className="tir-label-item tir-in-label">
                        ✓ {summary.timeInRange}% In Range
                      </span>
                      {summary.timeAboveRange > 0 && (
                        <span className="tir-label-item tir-above-label">
                          ↑ {summary.timeAboveRange}% High
                        </span>
                      )}
                    </div>
                  </div>
                )}
              </div>

              {/* Glucose Chart */}
              {chartData.length > 0 && (
                <div className="event-section">
                  <h3 className="event-section-title">📈 Full Day Glucose</h3>
                  <div className="event-chart-container">
                    <ResponsiveContainer width="100%" height={320}>
                      <LineChart
                        data={chartData}
                        margin={{ top: 10, right: 20, left: 0, bottom: 0 }}
                      >
                        <CartesianGrid strokeDasharray="3 3" stroke="#1f1f35" />
                        <XAxis
                          dataKey="displayTime"
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
                        <ReferenceArea y1={70} y2={180} fill="#4ade80" fillOpacity={0.05} />
                        <ReferenceLine y={70} stroke="#4ade80" strokeDasharray="4 4" strokeOpacity={0.4} />
                        <ReferenceLine y={180} stroke="#fbbf24" strokeDasharray="4 4" strokeOpacity={0.4} />

                        {/* Event markers */}
                        {eventMarkers.map((marker, i) => (
                          <ReferenceLine
                            key={i}
                            x={marker.x}
                            stroke="#ff6b6b"
                            strokeDasharray="6 3"
                            strokeWidth={1.5}
                            label={{
                              value: marker.time,
                              position: 'top',
                              fill: '#ff6b6b',
                              fontSize: 10,
                              fontWeight: 600,
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
                      </LineChart>
                    </ResponsiveContainer>
                    <div className="event-chart-legend">
                      <span className="legend-item">
                        <span className="legend-line" style={{ background: '#4facfe' }} />
                        Glucose
                      </span>
                      <span className="legend-item">
                        <span className="legend-line" style={{ background: '#ff6b6b' }} />
                        Events
                      </span>
                      <span className="legend-item">
                        <span className="legend-line" style={{ background: '#4ade80' }} />
                        Target range (70–180)
                      </span>
                    </div>
                  </div>
                </div>
              )}

              {/* Events for the day */}
              {summary.events && summary.events.length > 0 && (
                <div className="event-section">
                  <h3 className="event-section-title">
                    🍽️ Events ({summary.events.length})
                  </h3>
                  <div className="daily-events-list">
                    {summary.events.map((evt) => (
                      <div key={evt.id} className={`daily-event-item ${evt.aiClassification ? `classification-${evt.aiClassification}` : ''}`}>
                        {evt.aiClassification && (
                          <div className={`daily-event-classification classification-dot-${evt.aiClassification}`} title={
                            evt.aiClassification === 'green' ? 'Good' : evt.aiClassification === 'yellow' ? 'Concerning' : 'Bad'
                          } />
                        )}
                        <div className="daily-event-time">
                          {format(parseISO(evt.eventTimestamp), 'HH:mm')}
                        </div>
                        <div className="daily-event-info">
                          <span className="daily-event-title">{evt.noteTitle}</span>
                          {evt.noteContentPreview && (
                            <span className="daily-event-preview">{evt.noteContentPreview}</span>
                          )}
                        </div>
                        <div className="daily-event-stats">
                          {evt.glucoseAtEvent != null && (
                            <span className="daily-event-glucose">
                              {Math.round(evt.glucoseAtEvent)} mg/dL
                            </span>
                          )}
                          {evt.glucoseSpike != null && (
                            <span className={`daily-event-spike ${
                              evt.glucoseSpike > 60 ? 'text-very-high' :
                              evt.glucoseSpike > 30 ? 'text-high' : 'text-normal'
                            }`}>
                              +{Math.round(evt.glucoseSpike)}
                            </span>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* AI Analysis */}
              <div className="event-section">
                <h3 className="event-section-title">🤖 Daily AI Analysis</h3>
                {summary.aiAnalysis ? (
                  <div className="event-ai-analysis">
                    {renderAnalysis(summary.aiAnalysis)}
                  </div>
                ) : (
                  <div className="event-no-data">
                    {summary.isProcessed
                      ? 'Analysis completed but no text was generated.'
                      : 'AI analysis is pending. Configure your GPT API key in Settings and wait for the next analysis cycle.'}
                  </div>
                )}
                {summary.processedAt && (
                  <div className="event-processed-at">
                    Analyzed: {format(parseISO(summary.processedAt), 'MMM dd, yyyy HH:mm')}
                    {summary.aiModel && <span className="ai-model-badge">{summary.aiModel}</span>}
                  </div>
                )}
              </div>

              {/* Snapshot History */}
              {summary.snapshots && summary.snapshots.length > 0 && (
                <div className="event-section">
                  <h3 className="event-section-title">
                    📸 Generation History ({summary.snapshots.length})
                  </h3>
                  <div className="snapshot-history-list">
                    {summary.snapshots.map((snap) => (
                      <div
                        key={snap.id}
                        className={`snapshot-item ${selectedSnapshotId === snap.id ? 'selected' : ''}`}
                        onClick={() =>
                          setSelectedSnapshotId(
                            selectedSnapshotId === snap.id ? null : snap.id
                          )
                        }
                      >
                        <div className="snapshot-item-header">
                          <span className="snapshot-time">
                            {format(parseISO(snap.generatedAt), 'MMM dd, yyyy HH:mm')}
                          </span>
                          <span className={`event-tag ${snap.trigger === 'manual' ? 'pending' : 'ai'}`}>
                            {snap.trigger === 'manual' ? '🖐 Manual' : '⚙️ Auto'}
                          </span>
                        </div>
                        <div className="snapshot-item-stats">
                          <span>{snap.readingCount} readings</span>
                          <span>{snap.eventCount} events</span>
                          {snap.glucoseAvg != null && (
                            <span>avg {Math.round(snap.glucoseAvg)} mg/dL</span>
                          )}
                          {snap.timeInRange != null && (
                            <span>TIR {Math.round(snap.timeInRange)}%</span>
                          )}
                          {snap.firstReadingUtc && snap.lastReadingUtc && (
                            <span className="snapshot-data-range">
                              {format(parseISO(snap.firstReadingUtc), 'HH:mm')}–
                              {format(parseISO(snap.lastReadingUtc), 'HH:mm')}
                            </span>
                          )}
                        </div>

                        {/* Expanded snapshot detail */}
                        {selectedSnapshotId === snap.id && (
                          <div className="snapshot-expanded">
                            {snapshotLoading ? (
                              <div className="loading" style={{ padding: '12px 0' }}>
                                <div className="spinner" />
                                <p>Loading snapshot...</p>
                              </div>
                            ) : snapshotDetail ? (
                              <div className="snapshot-analysis">
                                <div className="snapshot-detail-stats">
                                  <span>Min: {snapshotDetail.glucoseMin != null ? `${Math.round(snapshotDetail.glucoseMin)}` : 'N/A'}</span>
                                  <span>Max: {snapshotDetail.glucoseMax != null ? `${Math.round(snapshotDetail.glucoseMax)}` : 'N/A'}</span>
                                  <span>Avg: {snapshotDetail.glucoseAvg != null ? `${snapshotDetail.glucoseAvg}` : 'N/A'}</span>
                                  <span>StdDev: {snapshotDetail.glucoseStdDev != null ? `${snapshotDetail.glucoseStdDev}` : 'N/A'}</span>
                                </div>
                                {snapshotDetail.aiAnalysis ? (
                                  <div className="event-ai-analysis" style={{ marginTop: 8 }}>
                                    {renderAnalysis(snapshotDetail.aiAnalysis)}
                                    {snapshotDetail.aiModel && (
                                      <div className="event-processed-at" style={{ marginTop: 4 }}>
                                        <span className="ai-model-badge">{snapshotDetail.aiModel}</span>
                                      </div>
                                    )}
                                  </div>
                                ) : (
                                  <div className="event-no-data">No AI analysis in this snapshot.</div>
                                )}
                              </div>
                            ) : null}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </>
          ) : (
            <div className="event-no-data">Summary not found.</div>
          )}
        </div>
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

export default DailySummariesPage;
