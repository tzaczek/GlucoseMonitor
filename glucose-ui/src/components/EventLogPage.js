import React, { useState, useEffect, useCallback, useMemo } from 'react';
import EventDetailModal from './EventDetailModal';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

const LEVEL_ICONS = { info: 'ℹ️', warning: '⚠️', error: '❌' };
const LEVEL_LABELS = { info: 'Info', warning: 'Warning', error: 'Error' };

const CATEGORIES = [
  'glucose', 'notes', 'events', 'analysis', 'daily',
  'comparison', 'summary', 'backup', 'settings', 'system', 'sync',
];

function toLocal(utcStr) {
  const d = new Date(utcStr.endsWith('Z') ? utcStr : utcStr + 'Z');
  return d.toLocaleString();
}

function relativeTime(utcStr) {
  const d = new Date(utcStr.endsWith('Z') ? utcStr : utcStr + 'Z');
  const diffMs = Date.now() - d.getTime();
  if (diffMs < 60000) return 'just now';
  if (diffMs < 3600000) return `${Math.floor(diffMs / 60000)}m ago`;
  if (diffMs < 86400000) return `${Math.floor(diffMs / 3600000)}h ago`;
  return `${Math.floor(diffMs / 86400000)}d ago`;
}

/**
 * Resolves a navigation target for a log entry based on its category,
 * related entity type/ID, and message content.
 * Returns { label, page, entityId, entityType } or null if no link is available.
 */
function resolveLink(log) {
  const { category, relatedEntityType, relatedEntityId } = log;

  // Entries linked to a specific GlucoseEvent → open event detail modal
  if (relatedEntityType === 'GlucoseEvent' && relatedEntityId) {
    return { label: 'View Event', entityType: 'event', entityId: relatedEntityId };
  }

  // Entries linked to a DailySummary → navigate to daily summaries page
  if (relatedEntityType === 'DailySummary' && relatedEntityId) {
    return { label: 'View Daily Summary', page: 'dailysummaries' };
  }

  // Entries linked to a GlucoseComparison → navigate to compare page
  if (relatedEntityType === 'GlucoseComparison' && relatedEntityId) {
    return { label: 'View Comparison', page: 'compare' };
  }

  // Entries linked to a PeriodSummary → navigate to period summary page
  if (relatedEntityType === 'PeriodSummary' && relatedEntityId) {
    return { label: 'View Summary', page: 'periodsummary' };
  }

  // Category-based fallbacks (no specific entity)
  switch (category) {
    case 'glucose':
      return { label: 'View Dashboard', page: 'dashboard' };
    case 'events':
      return { label: 'View Events', page: 'events' };
    case 'analysis':
      return { label: 'View Events', page: 'events' };
    case 'notes':
      return { label: 'View Events', page: 'events' };
    case 'daily':
      return { label: 'View Daily Summaries', page: 'dailysummaries' };
    case 'comparison':
      return { label: 'View Comparisons', page: 'compare' };
    case 'summary':
      return { label: 'View Summaries', page: 'periodsummary' };
    case 'backup':
    case 'settings':
      return { label: 'View Settings', page: 'settings' };
    default:
      return null;
  }
}

export default function EventLogPage({ onNavigate }) {
  const [logs, setLogs] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);

  // Filters
  const [levelFilter, setLevelFilter] = useState('');
  const [categoryFilter, setCategoryFilter] = useState('');
  const [searchFilter, setSearchFilter] = useState('');
  const [timeFilter, setTimeFilter] = useState('24h');
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo] = useState('');

  // Pagination
  const [limit] = useState(100);
  const [offset, setOffset] = useState(0);

  // Expanded detail
  const [expandedId, setExpandedId] = useState(null);

  // Event detail modal
  const [selectedEventId, setSelectedEventId] = useState(null);

  // Auto-refresh tracker
  const [refreshKey, setRefreshKey] = useState(0);

  // Convert time filter to Date range
  const timeRange = useMemo(() => {
    const now = new Date();
    if (timeFilter === '1h') return { from: new Date(now - 3600000), to: null };
    if (timeFilter === '6h') return { from: new Date(now - 6 * 3600000), to: null };
    if (timeFilter === '24h') return { from: new Date(now - 24 * 3600000), to: null };
    if (timeFilter === '7d') return { from: new Date(now - 7 * 86400000), to: null };
    if (timeFilter === '30d') return { from: new Date(now - 30 * 86400000), to: null };
    if (timeFilter === 'custom') {
      return {
        from: customFrom ? new Date(customFrom) : null,
        to: customTo ? new Date(customTo) : null,
      };
    }
    return { from: null, to: null }; // all time
  }, [timeFilter, customFrom, customTo]);

  const fetchLogs = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams();
      params.set('limit', limit);
      if (offset > 0) params.set('offset', offset);
      if (levelFilter) params.set('level', levelFilter);
      if (categoryFilter) params.set('category', categoryFilter);
      if (searchFilter.trim()) params.set('search', searchFilter.trim());
      if (timeRange.from) params.set('from', timeRange.from.toISOString());
      if (timeRange.to) params.set('to', timeRange.to.toISOString());

      const res = await fetch(`${API_BASE}/eventlog?${params.toString()}`);
      if (res.ok) {
        const data = await res.json();
        setLogs(data.logs || []);
        setTotalCount(data.totalCount || 0);
      }
    } catch (err) {
      console.error('Failed to fetch event logs:', err);
    } finally {
      setLoading(false);
    }
  }, [limit, offset, levelFilter, categoryFilter, searchFilter, timeRange]);

  useEffect(() => {
    fetchLogs();
  }, [fetchLogs, refreshKey]);

  // Listen for SignalR updates
  useEffect(() => {
    const handler = () => {
      setRefreshKey(k => k + 1);
    };
    window.addEventListener('eventLogsUpdated', handler);
    return () => window.removeEventListener('eventLogsUpdated', handler);
  }, []);

  // Reset offset when filters change
  useEffect(() => {
    setOffset(0);
  }, [levelFilter, categoryFilter, searchFilter, timeFilter, customFrom, customTo]);

  // Stats summary
  const stats = useMemo(() => {
    const infoCount = logs.filter(l => l.level === 'info').length;
    const warnCount = logs.filter(l => l.level === 'warning').length;
    const errCount = logs.filter(l => l.level === 'error').length;
    return { infoCount, warnCount, errCount };
  }, [logs]);

  const handleLinkClick = useCallback((e, log) => {
    e.stopPropagation();
    const link = resolveLink(log);
    if (!link) return;

    if (link.entityType === 'event' && link.entityId) {
      // Open event detail modal
      setSelectedEventId(link.entityId);
    } else if (link.page && onNavigate) {
      // Navigate to another page
      onNavigate(link.page);
    }
  }, [onNavigate]);

  const totalPages = Math.ceil(totalCount / limit);
  const currentPage = Math.floor(offset / limit) + 1;

  return (
    <div className="event-log-page">
      <h2>Event Log</h2>

      {/* Stats bar */}
      <div className="event-log-stats">
        <span className="stat-badge info">{LEVEL_ICONS.info} {stats.infoCount} Info</span>
        <span className="stat-badge warning">{LEVEL_ICONS.warning} {stats.warnCount} Warnings</span>
        <span className="stat-badge error">{LEVEL_ICONS.error} {stats.errCount} Errors</span>
        <span className="stat-badge total">Total: {totalCount}</span>
        <button className="btn-refresh" onClick={() => setRefreshKey(k => k + 1)} title="Refresh">
          Refresh
        </button>
      </div>

      {/* Filters */}
      <div className="event-log-filters">
        <div className="filter-group">
          <label>Time</label>
          <select value={timeFilter} onChange={e => setTimeFilter(e.target.value)}>
            <option value="1h">Last hour</option>
            <option value="6h">Last 6 hours</option>
            <option value="24h">Last 24 hours</option>
            <option value="7d">Last 7 days</option>
            <option value="30d">Last 30 days</option>
            <option value="all">All time</option>
            <option value="custom">Custom range</option>
          </select>
        </div>

        {timeFilter === 'custom' && (
          <>
            <div className="filter-group">
              <label>From</label>
              <input type="datetime-local" value={customFrom} onChange={e => setCustomFrom(e.target.value)} />
            </div>
            <div className="filter-group">
              <label>To</label>
              <input type="datetime-local" value={customTo} onChange={e => setCustomTo(e.target.value)} />
            </div>
          </>
        )}

        <div className="filter-group">
          <label>Level</label>
          <select value={levelFilter} onChange={e => setLevelFilter(e.target.value)}>
            <option value="">All levels</option>
            <option value="info">Info</option>
            <option value="warning">Warning</option>
            <option value="error">Error</option>
          </select>
        </div>

        <div className="filter-group">
          <label>Category</label>
          <select value={categoryFilter} onChange={e => setCategoryFilter(e.target.value)}>
            <option value="">All categories</option>
            {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        <div className="filter-group search">
          <label>Search</label>
          <input
            type="text"
            placeholder="Search messages..."
            value={searchFilter}
            onChange={e => setSearchFilter(e.target.value)}
          />
        </div>
      </div>

      {/* Table */}
      <div className="event-log-table-wrap">
        {loading ? (
          <div className="loading-indicator">Loading...</div>
        ) : logs.length === 0 ? (
          <div className="empty-state">No events match the current filters.</div>
        ) : (
          <table className="event-log-table">
            <thead>
              <tr>
                <th className="col-time">Time</th>
                <th className="col-level">Level</th>
                <th className="col-category">Category</th>
                <th className="col-message">Message</th>
                <th className="col-source">Source</th>
                <th className="col-link">Link</th>
              </tr>
            </thead>
            <tbody>
              {logs.map(log => {
                const link = resolveLink(log);
                return (
                  <React.Fragment key={log.id}>
                    <tr
                      className={`log-row level-${log.level}${expandedId === log.id ? ' expanded' : ''}${log.detail ? ' clickable' : ''}`}
                      onClick={() => log.detail && setExpandedId(expandedId === log.id ? null : log.id)}
                    >
                      <td className="col-time" title={toLocal(log.timestamp)}>
                        {relativeTime(log.timestamp)}
                      </td>
                      <td className="col-level">
                        <span className={`level-badge ${log.level}`}>
                          {LEVEL_ICONS[log.level]} {LEVEL_LABELS[log.level]}
                        </span>
                      </td>
                      <td className="col-category">
                        <span className="category-tag">{log.category}</span>
                      </td>
                      <td className="col-message">{log.message}</td>
                      <td className="col-source">{log.source || '\u2014'}</td>
                      <td className="col-link">
                        {link ? (
                          <button
                            className="log-link-btn"
                            onClick={(e) => handleLinkClick(e, log)}
                            title={link.label}
                          >
                            {link.label} &rarr;
                          </button>
                        ) : '\u2014'}
                      </td>
                    </tr>
                    {expandedId === log.id && log.detail && (
                      <tr className="log-detail-row">
                        <td colSpan="6">
                          <div className="log-detail-content">
                            <div className="detail-header">
                              <strong>Timestamp:</strong> {toLocal(log.timestamp)}
                              {log.durationMs != null && <> &nbsp;|&nbsp; <strong>Duration:</strong> {log.durationMs}ms</>}
                              {log.relatedEntityType && (
                                <> &nbsp;|&nbsp; <strong>Related:</strong> {log.relatedEntityType} #{log.relatedEntityId}</>
                              )}
                            </div>
                            <pre className="detail-text">{log.detail}</pre>
                          </div>
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="event-log-pagination">
          <button
            disabled={currentPage <= 1}
            onClick={() => setOffset(Math.max(0, offset - limit))}
          >
            Previous
          </button>
          <span>Page {currentPage} of {totalPages}</span>
          <button
            disabled={currentPage >= totalPages}
            onClick={() => setOffset(offset + limit)}
          >
            Next
          </button>
        </div>
      )}

      {/* Event Detail Modal */}
      {selectedEventId && (
        <EventDetailModal
          eventId={selectedEventId}
          onClose={() => setSelectedEventId(null)}
          onEventClick={(id) => setSelectedEventId(id)}
        />
      )}
    </div>
  );
}
