import React, { useState, useEffect, useCallback, useRef } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import GlucoseChart from './components/GlucoseChart';
import GlucoseTable from './components/GlucoseTable';
import CurrentReading from './components/CurrentReading';
import SettingsPage from './components/SettingsPage';
import EventsPage from './components/EventsPage';
import EventDetailModal from './components/EventDetailModal';
import AiUsagePage from './components/AiUsagePage';
import DailySummariesPage from './components/DailySummariesPage';
import ReportsPage from './components/ReportsPage';
import ComparePage from './components/ComparePage';
import PeriodSummaryPage from './components/PeriodSummaryPage';
import EventLogPage from './components/EventLogPage';
import ChatPage from './components/ChatPage';
import FoodPatternsPage from './components/FoodPatternsPage';
import './App.css';

const API_BASE = process.env.REACT_APP_API_URL || '/api';
const SIGNALR_URL = process.env.REACT_APP_SIGNALR_URL || '/glucosehub';

function formatLocalTimestamp(date) {
  const pad = (n) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

function downloadCsv(data, label) {
  const header = 'Timestamp (local),Value (mg/dL),Trend Arrow,Is High,Is Low\n';
  const rows = [...data]
    .sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
    .map(d => {
      const ts = formatLocalTimestamp(new Date(d.timestamp));
      const trend = ['', '↓↓', '↓', '→', '↑', '↑↑'][d.trendArrow] || d.trendArrow;
      return `${ts},${d.value},${trend},${d.isHigh},${d.isLow}`;
    })
    .join('\n');

  const csv = header + rows;
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `glucose_data_${label}_${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

function App() {
  const [page, setPage] = useState('dashboard');
  const [history, setHistory] = useState([]);
  const [stats, setStats] = useState(null);
  const [hours, setHours] = useState(24);
  const [customDays, setCustomDays] = useState('');
  const [showCustom, setShowCustom] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [isConfigured, setIsConfigured] = useState(true);
  const [showExportMenu, setShowExportMenu] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [events, setEvents] = useState([]);
  const [selectedEventId, setSelectedEventId] = useState(null);
  const [aiUsageVersion, setAiUsageVersion] = useState(0);
  const [syncing, setSyncing] = useState(false);
  const [syncResult, setSyncResult] = useState(null);
  const exportRef = useRef(null);

  // Close export menu when clicking outside
  useEffect(() => {
    const handleClickOutside = (e) => {
      if (exportRef.current && !exportRef.current.contains(e.target)) {
        setShowExportMenu(false);
      }
    };
    if (showExportMenu) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => document.removeEventListener('mousedown', handleClickOutside);
    }
  }, [showExportMenu]);

  const fetchData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      // Check if settings are configured
      const settingsRes = await fetch(`${API_BASE}/settings`);
      if (settingsRes.ok) {
        const settingsData = await settingsRes.json();
        setIsConfigured(settingsData.isConfigured);
        if (!settingsData.isConfigured) {
          setLoading(false);
          return;
        }
      }

      const [historyRes, statsRes, eventsRes] = await Promise.all([
        fetch(`${API_BASE}/glucose/history?hours=${hours}`),
        fetch(`${API_BASE}/glucose/stats?hours=${hours}`),
        fetch(`${API_BASE}/events`)
      ]);

      if (historyRes.ok) {
        const historyData = await historyRes.json();
        setHistory(historyData);
      }

      if (statsRes.ok) {
        const statsData = await statsRes.json();
        setStats(statsData);
      }

      if (eventsRes.ok) {
        const eventsData = await eventsRes.json();
        setEvents(eventsData);
      }

      if (!historyRes.ok && !statsRes.ok) {
        setError('No glucose data available yet. Waiting for data from LibreLink...');
      }
    } catch (err) {
      setError('Failed to connect to API. Make sure the backend is running.');
    } finally {
      setLoading(false);
    }
  }, [hours]);

  // Fetch data on page load and set up fallback polling
  useEffect(() => {
    if (page === 'dashboard') {
      fetchData();
      // Fallback polling every 5 minutes in case SignalR disconnects
      const interval = setInterval(fetchData, 300000);
      return () => clearInterval(interval);
    }
  }, [fetchData, page]);

  // SignalR: connect and listen for real-time updates
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(SIGNALR_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Information)
      .build();

    connection.on('NewGlucoseData', (count) => {
      console.log(`[SignalR] ${count} new reading(s) received — refreshing data...`);
      fetchData();
    });

    connection.on('NotesUpdated', (count) => {
      console.log(`[SignalR] ${count} note(s) updated — will refresh notes if viewing.`);
      window.dispatchEvent(new CustomEvent('notesUpdated'));
    });

    connection.on('EventsUpdated', (count) => {
      console.log(`[SignalR] ${count} event(s) updated — will refresh events if viewing.`);
      window.dispatchEvent(new CustomEvent('eventsUpdated'));
    });

    connection.on('DailySummariesUpdated', (count) => {
      console.log(`[SignalR] ${count} daily summary(s) updated — will refresh if viewing.`);
      window.dispatchEvent(new CustomEvent('dailySummariesUpdated'));
    });

    connection.on('AiUsageUpdated', (count) => {
      console.log(`[SignalR] AI usage updated — refreshing AI Usage page.`);
      setAiUsageVersion(v => v + 1);
    });

    connection.on('ComparisonsUpdated', (count) => {
      console.log(`[SignalR] ${count} comparison(s) updated — will refresh if viewing.`);
      window.dispatchEvent(new CustomEvent('comparisonsUpdated'));
    });

    connection.on('PeriodSummariesUpdated', (count) => {
      console.log(`[SignalR] ${count} period summary(s) updated — will refresh if viewing.`);
      window.dispatchEvent(new CustomEvent('periodSummariesUpdated'));
    });

    connection.on('EventLogsUpdated', (count) => {
      console.log(`[SignalR] ${count} event log(s) — will refresh if viewing.`);
      window.dispatchEvent(new CustomEvent('eventLogsUpdated'));
    });

    connection.on('ChatMessageCompleted', (data) => {
      console.log(`[SignalR] Chat message completed:`, data);
      window.dispatchEvent(new CustomEvent('chatMessageCompleted', { detail: data }));
    });

    connection.on('ChatSessionsUpdated', (count) => {
      console.log(`[SignalR] ${count} chat session(s) updated.`);
      window.dispatchEvent(new CustomEvent('chatSessionsUpdated'));
    });

    connection.on('FoodPatternsUpdated', (count) => {
      console.log(`[SignalR] ${count} food pattern(s) updated.`);
      window.dispatchEvent(new CustomEvent('foodPatternsUpdated'));
    });

    connection.on('ChatPeriodResolved', (data) => {
      console.log(`[SignalR] Chat period resolved:`, data);
      window.dispatchEvent(new CustomEvent('chatPeriodResolved', { detail: data }));
    });

    connection.onreconnecting(() => {
      console.log('[SignalR] Reconnecting...');
    });

    connection.onreconnected(() => {
      console.log('[SignalR] Reconnected — refreshing data...');
      fetchData();
    });

    connection.start()
      .then(() => console.log('[SignalR] Connected to glucose hub'))
      .catch(err => console.error('[SignalR] Connection failed:', err));

    return () => {
      connection.stop();
    };
  }, [fetchData]);

  const timeRanges = [
    { label: '6h', value: 6 },
    { label: '12h', value: 12 },
    { label: '24h', value: 24 },
    { label: '48h', value: 48 },
    { label: '7d', value: 168 },
    { label: '2w', value: 336 },
    { label: '30d', value: 720 },
    { label: '90d', value: 2160 },
  ];

  const handlePresetClick = (value) => {
    setHours(value);
    setShowCustom(false);
  };

  const handleCustomApply = () => {
    const days = parseFloat(customDays);
    if (!isNaN(days) && days > 0) {
      setHours(Math.round(days * 24));
      setShowCustom(false);
    }
  };

  const exportOptions = [
    { label: 'Current view', value: null },
    { label: '24 hours', value: 24 },
    { label: '48 hours', value: 48 },
    { label: '7 days', value: 168 },
    { label: '2 weeks', value: 336 },
    { label: '30 days', value: 720 },
    { label: '90 days', value: 2160 },
    { label: 'All data', value: 87600 },
  ];

  const handleExport = async (exportHours) => {
    setExporting(true);
    setShowExportMenu(false);
    try {
      const h = exportHours ?? hours;
      const label = h >= 8760 ? 'all' : h >= 24 ? `${Math.round(h / 24)}d` : `${h}h`;

      if (exportHours === null) {
        // Export current view data
        downloadCsv(history, label);
      } else {
        // Fetch data for the requested period
        const res = await fetch(`${API_BASE}/glucose/history?hours=${h}`);
        if (res.ok) {
          const data = await res.json();
          if (data.length === 0) {
            alert('No data available for the selected period.');
          } else {
            downloadCsv(data, label);
          }
        } else {
          alert('Failed to fetch data for export.');
        }
      }
    } catch (err) {
      alert('Export failed: ' + err.message);
    } finally {
      setExporting(false);
    }
  };

  const handleSync = async () => {
    setSyncing(true);
    setSyncResult(null);
    try {
      const res = await fetch(`${API_BASE}/sync/trigger`, { method: 'POST' });
      const data = await res.json();
      setSyncResult({ success: res.ok, message: data.message });
      // Refresh dashboard data after sync
      if (page === 'dashboard') fetchData();
      // Auto-clear the result after 5 seconds
      setTimeout(() => setSyncResult(null), 5000);
    } catch (err) {
      setSyncResult({ success: false, message: 'Sync failed: ' + err.message });
      setTimeout(() => setSyncResult(null), 5000);
    } finally {
      setSyncing(false);
    }
  };

  return (
    <div className="app">
      <header className="header">
        <div className="header-brand">
          <div className="header-logo">💧</div>
          <h1>Glucose Monitor</h1>
          <button
            className={`btn-sync${syncing ? ' syncing' : ''}`}
            onClick={handleSync}
            disabled={syncing}
            title="Sync glucose data & events now"
          >
            <span className="sync-icon">⟳</span>
            {syncing ? 'Syncing...' : 'Sync'}
          </button>
          {syncResult && (
            <span className={`sync-result ${syncResult.success ? 'success' : 'error'}`}>
              {syncResult.success ? '✓' : '✗'} {syncResult.message}
            </span>
          )}
        </div>
        <p>Continuous Glucose Monitoring Dashboard</p>
        <nav className="nav-tabs">
          <button
            className={page === 'dashboard' ? 'active' : ''}
            onClick={() => setPage('dashboard')}
          >
            Dashboard
          </button>
          <button
            className={page === 'events' ? 'active' : ''}
            onClick={() => setPage('events')}
          >
            Events
          </button>
          <button
            className={page === 'dailysummaries' ? 'active' : ''}
            onClick={() => setPage('dailysummaries')}
          >
            Daily
          </button>
          <button
            className={page === 'periodsummary' ? 'active' : ''}
            onClick={() => setPage('periodsummary')}
          >
            Summaries
          </button>
          <button
            className={page === 'compare' ? 'active' : ''}
            onClick={() => setPage('compare')}
          >
            Compare
          </button>
          <button
            className={page === 'chat' ? 'active' : ''}
            onClick={() => setPage('chat')}
          >
            Chat
          </button>
          <button
            className={page === 'food' ? 'active' : ''}
            onClick={() => setPage('food')}
          >
            Food
          </button>
          <button
            className={page === 'aiusage' ? 'active' : ''}
            onClick={() => setPage('aiusage')}
          >
            AI Usage
          </button>
          <button
            className={page === 'reports' ? 'active' : ''}
            onClick={() => setPage('reports')}
          >
            Reports
          </button>
          <button
            className={page === 'eventlog' ? 'active' : ''}
            onClick={() => setPage('eventlog')}
          >
            Event Log
          </button>
          <button
            className={page === 'settings' ? 'active' : ''}
            onClick={() => setPage('settings')}
          >
            Settings
          </button>
        </nav>
      </header>

      {page === 'periodsummary' && <PeriodSummaryPage />}
      {page === 'compare' && <ComparePage />}
      {page === 'chat' && <ChatPage />}
      {page === 'food' && <FoodPatternsPage />}
      {page === 'events' && <EventsPage />}
      {page === 'dailysummaries' && <DailySummariesPage />}
      {page === 'eventlog' && <EventLogPage onNavigate={setPage} />}
      {page === 'aiusage' && <AiUsagePage key={aiUsageVersion} />}
      {page === 'reports' && <ReportsPage />}
      {page === 'settings' && <SettingsPage />}

      {page === 'dashboard' && (
        <>
          {!isConfigured && (
            <div className="setup-banner">
              <div className="setup-icon">⚙</div>
              <h2>Welcome! Let's get started.</h2>
              <p>Configure your LibreLink Up credentials to start monitoring your glucose levels.</p>
              <button className="btn-setup" onClick={() => setPage('settings')}>
                Open Settings
              </button>
            </div>
          )}

          {isConfigured && (
            <>
              <div className="controls">
                {timeRanges.map(r => (
                  <button
                    key={r.value}
                    className={hours === r.value && !showCustom ? 'active' : ''}
                    onClick={() => handlePresetClick(r.value)}
                  >
                    {r.label}
                  </button>
                ))}
                <button
                  className={showCustom ? 'active' : ''}
                  onClick={() => setShowCustom(!showCustom)}
                >
                  Custom
                </button>
              </div>
              {showCustom && (
                <div className="custom-range">
                  <input
                    type="number"
                    min="1"
                    step="1"
                    placeholder="Enter days"
                    value={customDays}
                    onChange={(e) => setCustomDays(e.target.value)}
                    onKeyDown={(e) => e.key === 'Enter' && handleCustomApply()}
                  />
                  <button onClick={handleCustomApply}>Apply</button>
                  {hours > 0 && !timeRanges.some(r => r.value === hours) && (
                    <span className="custom-label">Showing {hours >= 24 ? `${(hours / 24).toFixed(1).replace(/\.0$/, '')}d` : `${hours}h`}</span>
                  )}
                </div>
              )}

              {loading && history.length === 0 && (
                <div className="loading">
                  <div className="spinner" />
                  <p>Loading glucose data...</p>
                </div>
              )}

              {error && (
                <div className="error">
                  <p>{error}</p>
                  <button onClick={fetchData}>Retry</button>
                </div>
              )}

              {stats && <CurrentReading stats={stats} />}

              {history.length > 0 && (
                <>
                  <div className="chart-card">
                    <div className="chart-header">
                      <h2>Glucose Trend</h2>
                      <div className="export-wrapper" ref={exportRef}>
                        <button
                          className="btn-export"
                          onClick={() => setShowExportMenu(!showExportMenu)}
                          disabled={exporting}
                        >
                          {exporting ? '⏳ Exporting...' : '⬇ Export CSV'}
                        </button>
                        {showExportMenu && (
                          <div className="export-menu">
                            {exportOptions.map((opt, i) => (
                              <button
                                key={i}
                                className="export-menu-item"
                                onClick={() => handleExport(opt.value)}
                              >
                                {opt.label}
                              </button>
                            ))}
                          </div>
                        )}
                      </div>
                    </div>
                    <GlucoseChart
                      data={history}
                      events={events}
                      onEventClick={(eventId) => setSelectedEventId(eventId)}
                    />
                  </div>

                  <div className="table-card">
                    <h2>Recent Readings</h2>
                    <GlucoseTable data={history.slice(0, 50)} />
                  </div>
                </>
              )}
            </>
          )}

          {/* Event analysis popup */}
          {selectedEventId && (
            <EventDetailModal
              eventId={selectedEventId}
              onClose={() => setSelectedEventId(null)}
              onReprocess={() => fetchData()}
            />
          )}
        </>
      )}
    </div>
  );
}

export default App;
