import React, { useState, useEffect, useCallback, useRef } from 'react';
import { format, parseISO } from 'date-fns';
import EventDetailModal from './EventDetailModal';
import useInfiniteScroll from '../hooks/useInfiniteScroll';
import PAGE_SIZES from '../config/pageSize';

const API_BASE = process.env.REACT_APP_API_URL || '/api';
const PAGE_SIZE = PAGE_SIZES.events;

function EventsPage() {
  const [events, setEvents] = useState([]);
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [totalCount, setTotalCount] = useState(0);
  const [selectedEventId, setSelectedEventId] = useState(null);
  const offsetRef = useRef(0);

  const fetchEvents = useCallback(async (append = false) => {
    if (append) setLoadingMore(true); else setLoading(true);
    try {
      const offset = append ? offsetRef.current : 0;
      const res = await fetch(`${API_BASE}/events?limit=${PAGE_SIZE}&offset=${offset}`);
      if (res.ok) {
        const data = await res.json();
        if (append) {
          setEvents(prev => [...prev, ...data.items]);
        } else {
          setEvents(data.items);
        }
        setTotalCount(data.totalCount);
        offsetRef.current = (append ? offsetRef.current : 0) + data.items.length;
      }
    } catch (err) {
      console.error('Failed to fetch events:', err);
    } finally {
      setLoading(false);
      setLoadingMore(false);
    }
  }, []);

  const fetchStatus = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/events/status`);
      if (res.ok) {
        const data = await res.json();
        setStatus(data);
      }
    } catch (err) {
      console.error('Failed to fetch events status:', err);
    }
  }, []);

  useEffect(() => {
    fetchEvents();
    fetchStatus();

    const handleEventsUpdated = () => {
      offsetRef.current = 0;
      fetchEvents();
      fetchStatus();
    };

    window.addEventListener('eventsUpdated', handleEventsUpdated);
    return () => window.removeEventListener('eventsUpdated', handleEventsUpdated);
  }, [fetchEvents, fetchStatus]);

  const hasMore = events.length < totalCount;
  const loadMore = useCallback(() => fetchEvents(true), [fetchEvents]);
  useInfiniteScroll(loadMore, { hasMore, loading: loadingMore });

  const getSpikeClass = (spike) => {
    if (spike == null) return '';
    if (spike <= 30) return 'spike-low';
    if (spike <= 60) return 'spike-moderate';
    return 'spike-high';
  };

  const getSpikeLabel = (spike) => {
    if (spike == null) return 'N/A';
    if (spike <= 30) return 'Mild';
    if (spike <= 60) return 'Moderate';
    return 'Significant';
  };

  return (
    <div className="events-page">
      {/* Status bar */}
      {status && (
        <div className="events-status-bar">
          <div className="events-status-item">
            <span className="events-status-value">{status.totalEvents}</span>
            <span className="events-status-label">Total Events</span>
          </div>
          <div className="events-status-item">
            <span className="events-status-value text-normal">{status.processedEvents}</span>
            <span className="events-status-label">Analyzed</span>
          </div>
          {status.pendingEvents > 0 && (
            <div className="events-status-item">
              <span className="events-status-value text-high">{status.pendingEvents}</span>
              <span className="events-status-label">Pending</span>
            </div>
          )}
        </div>
      )}

      {/* Empty state */}
      {!loading && events.length === 0 && (
        <div className="events-empty">
          <div className="events-empty-icon">📊</div>
          <h3>No Glucose Events Yet</h3>
          <p>
            Events are automatically created by correlating your notes (from the configured
            folder) with glucose readings. Make sure you have:
          </p>
          <ol className="events-checklist">
            <li>Samsung Notes synced with notes in your tracking folder</li>
            <li>Glucose data being fetched from LibreLink</li>
            <li>GPT API key configured in Settings for AI analysis</li>
          </ol>
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="loading">
          <div className="spinner" />
          <p>Loading events...</p>
        </div>
      )}

      {/* Events list */}
      {!loading && events.length > 0 && (
        <div className="events-list">
          {events.map((evt) => (
            <div
              key={evt.id}
              className={`event-card ${evt.isProcessed ? 'processed' : 'pending'} ${evt.aiClassification ? `classification-${evt.aiClassification}` : ''}`}
              onClick={() => setSelectedEventId(evt.id)}
            >
              <div className="event-card-left">
                <div className={`event-date-badge ${evt.aiClassification ? `badge-${evt.aiClassification}` : ''}`}>
                  <span className="event-day">
                    {format(parseISO(evt.eventTimestamp), 'dd')}
                  </span>
                  <span className="event-month">
                    {format(parseISO(evt.eventTimestamp), 'MMM')}
                  </span>
                  <span className="event-time-small">
                    {format(parseISO(evt.eventTimestamp), 'HH:mm')}
                  </span>
                </div>
              </div>

              <div className="event-card-center">
                <h3 className="event-title">
                  {evt.noteTitle || 'Untitled'}
                  {evt.noteTitleEn && evt.noteTitleEn !== evt.noteTitle && (
                    <span className="event-title-en"> ({evt.noteTitleEn})</span>
                  )}
                </h3>
                {evt.noteContentPreview && (
                  <p className="event-content-preview">
                    {evt.noteContentPreview}
                    {evt.noteContentPreviewEn && evt.noteContentPreviewEn !== evt.noteContentPreview && (
                      <span className="event-content-en"><br/>{evt.noteContentPreviewEn}</span>
                    )}
                  </p>
                )}
                <div className="event-tags">
                  {evt.aiClassification && (
                    <span className={`event-tag classification-tag classification-tag-${evt.aiClassification}`}>
                      {evt.aiClassification === 'green' ? '🟢 Good' : evt.aiClassification === 'yellow' ? '🟡 Concerning' : '🔴 Bad'}
                    </span>
                  )}
                  {evt.hasAnalysis && (
                    <span className="event-tag ai">
                      🤖 AI Analysis
                      {evt.analysisCount > 1 && (
                        <span className="analysis-count-badge"> ×{evt.analysisCount}</span>
                      )}
                    </span>
                  )}
                  {!evt.isProcessed && (
                    <span className="event-tag pending">⏳ Processing...</span>
                  )}
                  <span className="event-tag readings">
                    {evt.readingCount} reading{evt.readingCount !== 1 ? 's' : ''}
                  </span>
                </div>
              </div>

              <div className="event-card-right">
                {evt.glucoseAtEvent != null && (
                  <div className="event-glucose-badge">
                    <span className="event-glucose-value">{Math.round(evt.glucoseAtEvent)}</span>
                    <span className="event-glucose-unit">mg/dL</span>
                  </div>
                )}
                {evt.glucoseSpike != null && (
                  <div className={`event-spike-badge ${getSpikeClass(evt.glucoseSpike)}`}>
                    <span className="spike-arrow">↑</span>
                    <span className="spike-value">+{Math.round(evt.glucoseSpike)}</span>
                    <span className="spike-label">{getSpikeLabel(evt.glucoseSpike)}</span>
                  </div>
                )}
                {evt.glucoseMin != null && evt.glucoseMax != null && (
                  <div className="event-range">
                    {Math.round(evt.glucoseMin)}–{Math.round(evt.glucoseMax)}
                  </div>
                )}
              </div>
            </div>
          ))}
          <div className="scroll-sentinel" />
          {loadingMore && (
            <div className="loading-more">
              <div className="spinner spinner-sm" />
              <span>Loading more events...</span>
            </div>
          )}
          {!hasMore && events.length > PAGE_SIZE && (
            <div className="list-end-message">All {totalCount} events loaded</div>
          )}
        </div>
      )}

      {/* Detail modal */}
      {selectedEventId && (
        <EventDetailModal
          eventId={selectedEventId}
          onClose={() => setSelectedEventId(null)}
          onReprocess={() => {
            fetchEvents();
            fetchStatus();
          }}
        />
      )}
    </div>
  );
}

export default EventsPage;
