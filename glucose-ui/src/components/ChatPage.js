import React, { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { subDays, format } from 'date-fns';
import {
  ResponsiveContainer, ComposedChart, Line, Scatter, XAxis, YAxis,
  CartesianGrid, Tooltip, ReferenceLine, ReferenceArea, Cell,
} from 'recharts';
import MODEL_OPTIONS from './modelOptions';
import EventDetailModal from './EventDetailModal';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

const PERIOD_COLORS = [
  '#6366f1', '#f59e0b', '#10b981', '#ef4444', '#8b5cf6',
  '#ec4899', '#06b6d4', '#f97316',
];

const GRAPH_RANGE_OPTIONS = [
  { label: '3 days', days: 3 },
  { label: '7 days', days: 7 },
  { label: '14 days', days: 14 },
  { label: '30 days', days: 30 },
];

function generatePeriodName(start, end, existingNames) {
  const s = new Date(start);
  const e = new Date(end);
  const sameDay = s.toDateString() === e.toDateString();
  const h = s.getHours();

  let timeOfDay = '';
  if (h >= 22 || h < 6) timeOfDay = 'Night';
  else if (h >= 6 && h < 12) timeOfDay = 'Morning';
  else if (h >= 12 && h < 17) timeOfDay = 'Afternoon';
  else timeOfDay = 'Evening';

  let base = sameDay
    ? `${format(s, 'MMM d')} ${timeOfDay}`
    : `${format(s, 'MMM d')} – ${format(e, 'MMM d')}`;

  let name = base;
  let counter = 2;
  while (existingNames.includes(name)) {
    name = `${base} (${counter})`;
    counter++;
  }
  return name;
}

// ─── Main ChatPage ─────────────────────────────────────────

function ChatPage() {
  const [sessions, setSessions] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const [detail, setDetail] = useState(null);
  const [templates, setTemplates] = useState([]);
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [showNewChat, setShowNewChat] = useState(false);
  const [selectedEventId, setSelectedEventId] = useState(null);
  const messagesEndRef = useRef(null);

  // New chat form state
  const [newTitle, setNewTitle] = useState('');
  const [newMessage, setNewMessage] = useState('');
  const [newModel, setNewModel] = useState('');
  const [newTemplate, setNewTemplate] = useState('');
  const [selectedPeriods, setSelectedPeriods] = useState([]);
  const [promptPreview, setPromptPreview] = useState('');
  const [isPromptEdited, setIsPromptEdited] = useState(false);

  // Follow-up state
  const [followUpMsg, setFollowUpMsg] = useState('');
  const [followUpModel, setFollowUpModel] = useState('');

  // Thread chart data
  const [chartData, setChartData] = useState(null);
  const [chartLoading, setChartLoading] = useState(false);

  const fetchSessions = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/chat/sessions`);
      if (res.ok) setSessions(await res.json());
    } catch (err) {
      console.error('Failed to fetch chat sessions:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  const fetchDetail = useCallback(async (id) => {
    if (!id) { setDetail(null); return; }
    try {
      const res = await fetch(`${API_BASE}/chat/sessions/${id}`);
      if (res.ok) setDetail(await res.json());
    } catch (err) {
      console.error('Failed to fetch session detail:', err);
    }
  }, []);

  const fetchTemplates = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/chat/templates`);
      if (res.ok) setTemplates(await res.json());
    } catch (err) {
      console.error('Failed to fetch templates:', err);
    }
  }, []);

  const fetchChartData = useCallback(async (start, end) => {
    if (!start || !end) { setChartData(null); return; }
    setChartLoading(true);
    try {
      const s = new Date(start).toISOString();
      const e = new Date(end).toISOString();
      const res = await fetch(`${API_BASE}/glucose/range?start=${encodeURIComponent(s)}&end=${encodeURIComponent(e)}`);
      if (res.ok) setChartData(await res.json());
    } catch (err) {
      console.error('Failed to fetch chart data:', err);
    } finally {
      setChartLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchSessions();
    fetchTemplates();
  }, [fetchSessions, fetchTemplates]);

  useEffect(() => {
    if (selectedId) fetchDetail(selectedId);
  }, [selectedId, fetchDetail]);

  useEffect(() => {
    if (detail?.periodStart && detail?.periodEnd) {
      fetchChartData(detail.periodStart, detail.periodEnd);
    } else {
      setChartData(null);
    }
  }, [detail?.periodStart, detail?.periodEnd, fetchChartData]);

  // SignalR listeners
  useEffect(() => {
    const handleMessageCompleted = (e) => {
      const data = e.detail;
      if (data && data.sessionId === selectedId) fetchDetail(selectedId);
      fetchSessions();
    };
    const handleSessionsUpdated = () => fetchSessions();
    const handlePeriodResolved = (e) => {
      const data = e.detail;
      if (data && data.sessionId === selectedId) fetchDetail(selectedId);
    };

    window.addEventListener('chatMessageCompleted', handleMessageCompleted);
    window.addEventListener('chatSessionsUpdated', handleSessionsUpdated);
    window.addEventListener('chatPeriodResolved', handlePeriodResolved);
    return () => {
      window.removeEventListener('chatMessageCompleted', handleMessageCompleted);
      window.removeEventListener('chatSessionsUpdated', handleSessionsUpdated);
      window.removeEventListener('chatPeriodResolved', handlePeriodResolved);
    };
  }, [selectedId, fetchDetail, fetchSessions]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [detail?.messages]);

  // When template changes, populate prompt preview
  useEffect(() => {
    if (!newTemplate) {
      setPromptPreview('');
      setIsPromptEdited(false);
      return;
    }
    const tpl = templates.find(t => t.name === newTemplate);
    if (tpl && tpl.userPromptTemplate) {
      let prompt = tpl.userPromptTemplate;
      if (selectedPeriods.length > 0) {
        const label = selectedPeriods.map(p => `"${p.name}"`).join(', ');
        prompt = prompt.replace(/\{\{?period_label\}?\}/g, label);
      }
      prompt = prompt.replace(/\{\{?user_message\}?\}/g, '');
      setPromptPreview(prompt);
      if (!isPromptEdited) setNewMessage(prompt);
    }
  }, [newTemplate, templates, selectedPeriods, isPromptEdited]);

  const handleCreateSession = async (e) => {
    e.preventDefault();
    if (!newMessage.trim()) return;
    setSending(true);
    try {
      const periods = selectedPeriods.map(p => ({
        name: p.name,
        start: new Date(p.start).toISOString(),
        end: new Date(p.end).toISOString(),
        color: p.color,
      }));

      const body = {
        title: newTitle || null,
        periodStart: periods.length ? null : null,
        periodEnd: periods.length ? null : null,
        periodDescription: null,
        templateName: newTemplate || null,
        initialMessage: newMessage,
        model: newModel || null,
        periods: periods.length > 0 ? periods : null,
      };
      const res = await fetch(`${API_BASE}/chat/sessions`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (res.ok) {
        const data = await res.json();
        setSelectedId(data.sessionId);
        setShowNewChat(false);
        setNewTitle('');
        setNewMessage('');
        setNewModel('');
        setNewTemplate('');
        setSelectedPeriods([]);
        setPromptPreview('');
        setIsPromptEdited(false);
        fetchSessions();
        fetchDetail(data.sessionId);
      }
    } catch (err) {
      console.error('Failed to create chat session:', err);
    } finally {
      setSending(false);
    }
  };

  const handleSendFollowUp = async (e) => {
    e.preventDefault();
    if (!followUpMsg.trim() || !selectedId) return;
    setSending(true);
    try {
      const res = await fetch(`${API_BASE}/chat/sessions/${selectedId}/messages`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          content: followUpMsg,
          modelOverride: followUpModel || null,
        }),
      });
      if (res.ok) {
        setFollowUpMsg('');
        fetchDetail(selectedId);
      } else {
        const data = await res.json();
        alert(data || 'Failed to send message.');
      }
    } catch (err) {
      console.error('Failed to send message:', err);
    } finally {
      setSending(false);
    }
  };

  const handleDeleteSession = async (id) => {
    if (!window.confirm('Permanently delete this chat session and all its messages?')) return;
    try {
      await fetch(`${API_BASE}/chat/sessions/${id}`, { method: 'DELETE' });
      if (selectedId === id) { setSelectedId(null); setDetail(null); }
      fetchSessions();
    } catch (err) {
      console.error('Failed to delete session:', err);
    }
  };

  const handleDeleteAllSessions = async () => {
    if (!window.confirm(`Permanently delete all ${sessions.length} chat session(s)? This cannot be undone.`)) return;
    try {
      await fetch(`${API_BASE}/chat/sessions`, { method: 'DELETE' });
      setSelectedId(null);
      setDetail(null);
      fetchSessions();
    } catch (err) {
      console.error('Failed to delete all sessions:', err);
    }
  };

  const handleMessageEdit = (value) => {
    setNewMessage(value);
    if (promptPreview && value !== promptPreview) setIsPromptEdited(true);
  };

  const handleResetPrompt = () => {
    if (promptPreview) { setNewMessage(promptPreview); setIsPromptEdited(false); }
  };

  const handleAddPeriod = (start, end) => {
    const names = selectedPeriods.map(p => p.name);
    const color = PERIOD_COLORS[selectedPeriods.length % PERIOD_COLORS.length];
    const name = generatePeriodName(start, end, names);
    setSelectedPeriods(prev => [...prev, { name, start, end, color }]);
  };

  const handleRemovePeriod = (index) => {
    setSelectedPeriods(prev => prev.filter((_, i) => i !== index));
  };

  const handleRenamePeriod = (index, newName) => {
    setSelectedPeriods(prev => prev.map((p, i) => i === index ? { ...p, name: newName } : p));
  };

  const hasProcessing = detail?.messages?.some(m => m.status === 'processing');

  const renderMarkdownWithEventLinks = (text) => {
    if (!text) return null;
    const parts = text.split(/(event\s*#\d+)/gi);
    return parts.map((part, i) => {
      const match = part.match(/event\s*#(\d+)/i);
      if (match) {
        const eventId = parseInt(match[1]);
        return (
          <a key={i} href="#!" className="chat-event-link"
            onClick={(e) => { e.preventDefault(); setSelectedEventId(eventId); }}
          >{part}</a>
        );
      }
      return <span key={i} dangerouslySetInnerHTML={{ __html: simpleMarkdown(part) }} />;
    });
  };

  const simpleMarkdown = (text) => {
    return text
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/\*(.+?)\*/g, '<em>$1</em>')
      .replace(/`(.+?)`/g, '<code>$1</code>')
      .replace(/^### (.+)$/gm, '<h4>$1</h4>')
      .replace(/^## (.+)$/gm, '<h3>$1</h3>')
      .replace(/^# (.+)$/gm, '<h2>$1</h2>')
      .replace(/^- (.+)$/gm, '<li>$1</li>')
      .replace(/\n/g, '<br/>');
  };

  if (loading) {
    return (
      <div className="chat-page"><div className="loading"><div className="spinner" /><p>Loading chat...</p></div></div>
    );
  }

  return (
    <div className="chat-page">
      {/* Sidebar */}
      <div className="chat-sidebar">
        <button className="chat-new-btn" onClick={() => { setShowNewChat(true); setSelectedId(null); setDetail(null); }}>
          + New Chat
        </button>
        {sessions.length > 0 && (
          <button className="chat-delete-all-btn" onClick={handleDeleteAllSessions}>
            Delete All Chats
          </button>
        )}
        <div className="chat-session-list">
          {sessions.map(s => (
            <div key={s.id}
              className={`chat-session-item${selectedId === s.id ? ' active' : ''}`}
              onClick={() => { setSelectedId(s.id); setShowNewChat(false); }}
            >
              <div className="chat-session-title">{s.title}</div>
              <div className="chat-session-meta">
                {s.messageCount} messages &middot; {new Date(s.updatedAt).toLocaleDateString()}
                {s.periods?.length > 0 && ` · ${s.periods.length} period${s.periods.length > 1 ? 's' : ''}`}
              </div>
              <button className="chat-session-delete"
                onClick={(e) => { e.stopPropagation(); handleDeleteSession(s.id); }}
                title="Delete session"
              >&times;</button>
            </div>
          ))}
          {sessions.length === 0 && <div className="chat-empty-sidebar">No conversations yet</div>}
        </div>
      </div>

      {/* Main Area */}
      <div className="chat-main">
        {showNewChat || (!selectedId && sessions.length === 0) ? (
          <div className="chat-new-form-container">
            <h2>New Chat</h2>
            <form onSubmit={handleCreateSession} className="chat-new-form">
              <div className="chat-form-row">
                <div className="chat-form-group">
                  <label>Template</label>
                  <select value={newTemplate} onChange={e => { setNewTemplate(e.target.value); setIsPromptEdited(false); }}>
                    <option value="">Free Chat</option>
                    {templates.map(t => (
                      <option key={t.id} value={t.name}>{t.name} ({t.category})</option>
                    ))}
                  </select>
                </div>
                <div className="chat-form-group">
                  <label>AI Model</label>
                  <select value={newModel} onChange={e => setNewModel(e.target.value)}>
                    {MODEL_OPTIONS.map(o => (
                      <option key={o.value} value={o.value}>{o.label}</option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="chat-form-group">
                <label>Select Data Periods</label>
                <PeriodSelectorGraph
                  periods={selectedPeriods}
                  onAddPeriod={handleAddPeriod}
                  onRemovePeriod={handleRemovePeriod}
                  onRenamePeriod={handleRenamePeriod}
                  onEventClick={setSelectedEventId}
                />
              </div>

              <div className="chat-form-group">
                <label>Title (optional)</label>
                <input type="text" value={newTitle} onChange={e => setNewTitle(e.target.value)}
                  placeholder="Auto-generated from message if empty" />
              </div>

              <div className="chat-form-group">
                <div className="chat-message-label-row">
                  <label>Message</label>
                  {newTemplate && promptPreview && (
                    <div className="chat-prompt-actions">
                      {isPromptEdited && (
                        <button type="button" className="chat-reset-prompt-btn" onClick={handleResetPrompt}>
                          Reset to template
                        </button>
                      )}
                      <span className="chat-prompt-source">
                        {isPromptEdited ? 'Edited from template' : `From: ${newTemplate}`}
                      </span>
                    </div>
                  )}
                </div>
                <textarea
                  value={newMessage}
                  onChange={e => handleMessageEdit(e.target.value)}
                  placeholder={newTemplate ? 'Template prompt will appear here...' : 'Ask about your glucose data...'}
                  rows={6}
                  required
                  className={newTemplate && promptPreview && !isPromptEdited ? 'chat-textarea-template' : ''}
                />
                {selectedPeriods.length > 0 && (
                  <div className="chat-prompt-hint">
                    {selectedPeriods.length} period{selectedPeriods.length > 1 ? 's' : ''} selected:
                    {' '}{selectedPeriods.map(p => `"${p.name}"`).join(', ')}.
                    Use these names in your prompt to reference specific periods.
                  </div>
                )}
              </div>

              <button type="submit" className="chat-submit-btn" disabled={sending || !newMessage.trim()}>
                {sending ? 'Starting...' : 'Start Chat'}
              </button>
            </form>
          </div>
        ) : selectedId && detail ? (
          <div className="chat-thread-container">
            <div className="chat-thread-header">
              <div className="chat-thread-title-row">
                <h2>{detail.title}</h2>
                <button className="chat-thread-delete-btn" onClick={() => handleDeleteSession(selectedId)}
                  title="Permanently delete this chat">Delete</button>
              </div>
              <div className="chat-thread-badges">
                {detail.periods?.length > 0 ? (
                  detail.periods.map((p, idx) => (
                    <span key={idx} className="chat-period-badge" style={{ borderLeftColor: p.color }}>
                      <span className="chat-period-badge-dot" style={{ background: p.color }} />
                      {p.name}: {format(new Date(p.start), 'MMM d HH:mm')} – {format(new Date(p.end), 'MMM d HH:mm')}
                    </span>
                  ))
                ) : (
                  <>
                    {detail.periodStart && detail.periodEnd && (
                      <span className="chat-period-badge">
                        {new Date(detail.periodStart).toLocaleDateString()} {new Date(detail.periodStart).toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'})} &ndash; {new Date(detail.periodEnd).toLocaleDateString()} {new Date(detail.periodEnd).toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'})}
                      </span>
                    )}
                  </>
                )}
                {detail.templateName && (
                  <span className="chat-template-badge">{detail.templateName}</span>
                )}
              </div>
            </div>

            {/* Glucose Chart */}
            {detail.periodStart && detail.periodEnd && (
              <ChatGlucoseChart
                chartData={chartData}
                chartLoading={chartLoading}
                periods={detail.periods || []}
                onEventClick={setSelectedEventId}
              />
            )}

            <div className="chat-messages">
              {detail.messages.map(msg => (
                <div key={msg.id} className={`chat-message chat-message-${msg.role}`}>
                  <div className="chat-message-role">
                    {msg.role === 'user' ? 'You' : 'AI Assistant'}
                  </div>
                  <div className="chat-message-content">
                    {msg.status === 'processing' ? (
                      <div className="chat-processing">
                        <div className="spinner" />
                        <span>Thinking...</span>
                      </div>
                    ) : (
                      renderMarkdownWithEventLinks(msg.content)
                    )}
                  </div>
                  {msg.role === 'assistant' && msg.status === 'completed' && (
                    <div className="chat-message-meta">
                      {msg.aiModel && <span className="ai-model-badge">{msg.aiModel}</span>}
                      {msg.inputTokens != null && (
                        <span className="chat-token-info">
                          {msg.inputTokens + msg.outputTokens} tokens
                        </span>
                      )}
                      {msg.costUsd != null && (
                        <span className="chat-cost-info">${msg.costUsd.toFixed(4)}</span>
                      )}
                      {msg.durationMs != null && (
                        <span className="chat-duration-info">{(msg.durationMs / 1000).toFixed(1)}s</span>
                      )}
                    </div>
                  )}
                  {msg.status === 'failed' && (
                    <div className="chat-message-error">
                      Failed: {msg.errorMessage || 'Unknown error'}
                    </div>
                  )}
                </div>
              ))}
              <div ref={messagesEndRef} />
            </div>

            <form onSubmit={handleSendFollowUp} className="chat-followup-form">
              <div className="chat-followup-row">
                <textarea
                  value={followUpMsg}
                  onChange={e => setFollowUpMsg(e.target.value)}
                  placeholder="Ask a follow-up question..."
                  rows={2}
                  disabled={sending || hasProcessing}
                  onKeyDown={e => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault();
                      handleSendFollowUp(e);
                    }
                  }}
                />
                <div className="chat-followup-controls">
                  <select value={followUpModel} onChange={e => setFollowUpModel(e.target.value)}
                    className="chat-followup-model" disabled={sending || hasProcessing}>
                    {MODEL_OPTIONS.map(o => (
                      <option key={o.value} value={o.value}>{o.label}</option>
                    ))}
                  </select>
                  <button type="submit" className="chat-send-btn"
                    disabled={sending || hasProcessing || !followUpMsg.trim()}>
                    {sending ? '...' : 'Send'}
                  </button>
                </div>
              </div>
            </form>
          </div>
        ) : (
          <div className="chat-empty-main">
            <h2>AI Chat</h2>
            <p>Select a conversation from the sidebar or start a new one.</p>
            <p>Ask questions about your glucose data, analyze patterns, compare periods, or get personalized insights.</p>
            <button className="chat-submit-btn" onClick={() => setShowNewChat(true)}>
              + Start New Chat
            </button>
          </div>
        )}
      </div>

      {selectedEventId && (
        <EventDetailModal
          eventId={selectedEventId}
          onClose={() => setSelectedEventId(null)}
        />
      )}
    </div>
  );
}

// ─── Pixel ↔ Time conversion helper ───────────────────────

const CHART_MARGIN = { top: 10, right: 10, left: 0, bottom: 5 };
const YAXIS_WIDTH = 35;

function pixelToTime(clientX, containerEl, domainStart, domainEnd) {
  if (!containerEl) return null;
  const rect = containerEl.getBoundingClientRect();
  const plotLeft = YAXIS_WIDTH + CHART_MARGIN.left;
  const plotRight = rect.width - CHART_MARGIN.right;
  const plotWidth = plotRight - plotLeft;
  const relX = clientX - rect.left - plotLeft;
  const ratio = Math.max(0, Math.min(1, relX / plotWidth));
  return domainStart + ratio * (domainEnd - domainStart);
}

// ─── Shared zoom hook ──────────────────────────────────────

function useChartZoom(dataMin, dataMax) {
  const [zoomDomain, setZoomDomain] = useState(null);
  const domain = zoomDomain || [dataMin, dataMax];

  const handleWheel = useCallback((e, containerEl) => {
    e.preventDefault();
    const [dStart, dEnd] = zoomDomain || [dataMin, dataMax];
    const range = dEnd - dStart;
    const zoomFactor = e.deltaY > 0 ? 1.3 : 0.7;

    const mouseTime = pixelToTime(e.clientX, containerEl, dStart, dEnd);
    if (mouseTime == null) return;

    const leftRatio = (mouseTime - dStart) / range;
    const newRange = Math.min(range * zoomFactor, dataMax - dataMin);
    const newStart = Math.max(dataMin, mouseTime - newRange * leftRatio);
    const newEnd = Math.min(dataMax, newStart + newRange);

    if (newEnd - newStart < 30 * 60 * 1000) return;
    setZoomDomain([newStart, newEnd]);
  }, [zoomDomain, dataMin, dataMax]);

  const zoomIn = useCallback(() => {
    const [s, e] = zoomDomain || [dataMin, dataMax];
    const mid = (s + e) / 2;
    const half = (e - s) / 2 * 0.5;
    if (half < 15 * 60 * 1000) return;
    setZoomDomain([mid - half, mid + half]);
  }, [zoomDomain, dataMin, dataMax]);

  const zoomOut = useCallback(() => {
    const [s, e] = zoomDomain || [dataMin, dataMax];
    const mid = (s + e) / 2;
    const half = Math.min((e - s) / 2 * 2, (dataMax - dataMin) / 2);
    setZoomDomain([Math.max(dataMin, mid - half), Math.min(dataMax, mid + half)]);
  }, [zoomDomain, dataMin, dataMax]);

  const resetZoom = useCallback(() => setZoomDomain(null), []);

  const isZoomed = zoomDomain != null;

  return { domain, handleWheel, zoomIn, zoomOut, resetZoom, isZoomed };
}

// ─── Period Selector Graph ─────────────────────────────────

function PeriodSelectorGraph({ periods, onAddPeriod, onRemovePeriod, onRenamePeriod, onEventClick }) {
  const [graphRange, setGraphRange] = useState(7);
  const [graphData, setGraphData] = useState(null);
  const [graphLoading, setGraphLoading] = useState(false);
  const [dragging, setDragging] = useState(false);
  const [dragStartTime, setDragStartTime] = useState(null);
  const [dragEndTime, setDragEndTime] = useState(null);
  const [editingIdx, setEditingIdx] = useState(null);
  const [editName, setEditName] = useState('');
  const chartContainerRef = useRef(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      setGraphLoading(true);
      try {
        const end = new Date();
        const start = subDays(end, graphRange);
        const res = await fetch(
          `${API_BASE}/glucose/range?start=${encodeURIComponent(start.toISOString())}&end=${encodeURIComponent(end.toISOString())}`
        );
        if (res.ok && !cancelled) setGraphData(await res.json());
      } catch (err) {
        console.error('Failed to fetch graph data:', err);
      } finally {
        if (!cancelled) setGraphLoading(false);
      }
    };
    load();
    return () => { cancelled = true; };
  }, [graphRange]);

  const sortedReadings = useMemo(() => {
    if (!graphData?.readings?.length) return [];
    return graphData.readings
      .map(r => ({ time: new Date(r.timestamp).getTime(), value: r.value }))
      .sort((a, b) => a.time - b.time);
  }, [graphData]);

  const eventMarkers = useMemo(() => {
    if (!graphData?.events?.length || !sortedReadings.length) return [];
    return graphData.events.map(evt => {
      const evtTime = new Date(evt.timestamp).getTime();
      const closest = sortedReadings.reduce((prev, curr) =>
        Math.abs(curr.time - evtTime) < Math.abs(prev.time - evtTime) ? curr : prev
      );
      return { ...evt, time: evtTime, eventValue: closest.value };
    });
  }, [graphData, sortedReadings]);

  const dataMin = sortedReadings.length ? sortedReadings[0].time : Date.now() - 7 * 86400000;
  const dataMax = sortedReadings.length ? sortedReadings[sortedReadings.length - 1].time : Date.now();
  const { domain, handleWheel, zoomIn, zoomOut, resetZoom, isZoomed } = useChartZoom(dataMin, dataMax);

  // Drag selection via pixel-to-time (works anywhere, not just on data points)
  const handleOverlayMouseDown = useCallback((e) => {
    if (e.button !== 0) return;
    const t = pixelToTime(e.clientX, chartContainerRef.current, domain[0], domain[1]);
    if (t != null) {
      setDragging(true);
      setDragStartTime(t);
      setDragEndTime(t);
    }
  }, [domain]);

  const handleOverlayMouseMove = useCallback((e) => {
    if (!dragging) return;
    const t = pixelToTime(e.clientX, chartContainerRef.current, domain[0], domain[1]);
    if (t != null) setDragEndTime(t);
  }, [dragging, domain]);

  const handleOverlayMouseUp = useCallback(() => {
    if (dragging && dragStartTime != null && dragEndTime != null) {
      const s = Math.min(dragStartTime, dragEndTime);
      const e = Math.max(dragStartTime, dragEndTime);
      if (e - s > 5 * 60 * 1000) {
        onAddPeriod(s, e);
      }
    }
    setDragging(false);
    setDragStartTime(null);
    setDragEndTime(null);
  }, [dragging, dragStartTime, dragEndTime, onAddPeriod]);

  // Attach global mousemove/mouseup so drag works even outside the chart
  useEffect(() => {
    if (!dragging) return;
    const moveHandler = (e) => handleOverlayMouseMove(e);
    const upHandler = () => handleOverlayMouseUp();
    window.addEventListener('mousemove', moveHandler);
    window.addEventListener('mouseup', upHandler);
    return () => {
      window.removeEventListener('mousemove', moveHandler);
      window.removeEventListener('mouseup', upHandler);
    };
  }, [dragging, handleOverlayMouseMove, handleOverlayMouseUp]);

  const onWheelChart = useCallback((e) => {
    handleWheel(e, chartContainerRef.current);
  }, [handleWheel]);

  const startEditName = (idx) => { setEditingIdx(idx); setEditName(periods[idx].name); };
  const commitEditName = () => {
    if (editingIdx != null && editName.trim()) onRenamePeriod(editingIdx, editName.trim());
    setEditingIdx(null);
    setEditName('');
  };

  if (graphLoading && !graphData) {
    return (
      <div className="period-selector">
        <div className="period-selector-loading"><div className="spinner" /> Loading glucose data...</div>
      </div>
    );
  }

  const values = sortedReadings.map(d => d.value);
  const minVal = values.length ? Math.max(40, Math.min(...values) - 10) : 40;
  const maxVal = values.length ? Math.min(400, Math.max(...values) + 10) : 300;

  const selLeft = dragStartTime != null && dragEndTime != null ? Math.min(dragStartTime, dragEndTime) : null;
  const selRight = dragStartTime != null && dragEndTime != null ? Math.max(dragStartTime, dragEndTime) : null;

  return (
    <div className="period-selector">
      <div className="period-selector-toolbar">
        <span className="period-selector-hint">Drag to select · Scroll to zoom</span>
        <div className="period-selector-zoom-btns">
          <button type="button" className="zoom-btn" onClick={zoomIn} title="Zoom in">+</button>
          <button type="button" className="zoom-btn" onClick={zoomOut} title="Zoom out">−</button>
          {isZoomed && (
            <button type="button" className="zoom-btn zoom-reset" onClick={resetZoom} title="Reset zoom">Reset</button>
          )}
        </div>
        <div className="period-selector-range-btns">
          {GRAPH_RANGE_OPTIONS.map(opt => (
            <button key={opt.days} type="button"
              className={`period-range-btn${graphRange === opt.days ? ' active' : ''}`}
              onClick={() => { setGraphRange(opt.days); resetZoom(); }}
            >{opt.label}</button>
          ))}
        </div>
      </div>

      {isZoomed && (
        <div className="period-selector-zoom-info">
          Viewing: {format(new Date(domain[0]), 'MMM d, HH:mm')} – {format(new Date(domain[1]), 'MMM d, HH:mm')}
        </div>
      )}

      <div className="period-selector-chart" ref={chartContainerRef} style={{ position: 'relative' }}>
        {sortedReadings.length > 0 ? (
          <>
            <ResponsiveContainer width="100%" height={220}>
              <ComposedChart data={sortedReadings} margin={CHART_MARGIN}>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.06)" />
                <XAxis
                  dataKey="time" type="number" scale="time"
                  domain={domain}
                  tickFormatter={t => format(new Date(t), 'MMM d HH:mm')}
                  stroke="rgba(255,255,255,0.3)"
                  tick={{ fontSize: 10, fill: '#94a3b8' }}
                  minTickGap={50}
                  allowDataOverflow
                />
                <YAxis domain={[minVal, maxVal]} stroke="rgba(255,255,255,0.3)"
                  tick={{ fontSize: 10, fill: '#94a3b8' }} width={YAXIS_WIDTH} />
                <Tooltip
                  contentStyle={{ background: '#1e293b', border: '1px solid rgba(255,255,255,0.1)', borderRadius: 8, fontSize: '0.8rem' }}
                  labelFormatter={t => format(new Date(t), 'MMM d, HH:mm')}
                  formatter={(val) => [`${val} mg/dL`, 'Glucose']}
                />
                <ReferenceArea y1={70} y2={180} fill="rgba(34,197,94,0.06)" />
                <ReferenceLine y={70} stroke="rgba(234,179,8,0.3)" strokeDasharray="3 3" />
                <ReferenceLine y={180} stroke="rgba(234,179,8,0.3)" strokeDasharray="3 3" />

                {periods.map((p, idx) => (
                  <ReferenceArea key={`period-${idx}`}
                    x1={new Date(p.start).getTime()} x2={new Date(p.end).getTime()}
                    fill={p.color} fillOpacity={0.15} stroke={p.color} strokeOpacity={0.6} strokeWidth={1} />
                ))}

                {selLeft != null && selRight != null && (
                  <ReferenceArea x1={selLeft} x2={selRight}
                    fill={PERIOD_COLORS[periods.length % PERIOD_COLORS.length]} fillOpacity={0.25}
                    stroke={PERIOD_COLORS[periods.length % PERIOD_COLORS.length]}
                    strokeOpacity={0.8} strokeWidth={2} strokeDasharray="4 2" />
                )}

                <Line type="monotone" dataKey="value" stroke="#6366f1" strokeWidth={1.5}
                  dot={false} connectNulls isAnimationActive={false} />

                {eventMarkers.length > 0 && (
                  <Scatter data={eventMarkers} dataKey="eventValue" fill="#f59e0b"
                    isAnimationActive={false} cursor="pointer"
                    onClick={(data) => onEventClick && onEventClick(data.id)}>
                    {eventMarkers.map((evt, idx) => (
                      <Cell key={idx} fill="#f59e0b" r={3} />
                    ))}
                  </Scatter>
                )}
              </ComposedChart>
            </ResponsiveContainer>
            {/* Transparent overlay for free-form drag selection & scroll zoom */}
            <div
              className="chart-interaction-overlay"
              onMouseDown={handleOverlayMouseDown}
              onWheel={onWheelChart}
            />
          </>
        ) : (
          <div className="period-selector-empty">No glucose data for this range</div>
        )}
      </div>

      {periods.length > 0 && (
        <div className="period-selector-list">
          {periods.map((p, idx) => (
            <div key={idx} className="period-selector-item">
              <span className="period-selector-color" style={{ background: p.color }} />
              <div className="period-selector-item-info">
                {editingIdx === idx ? (
                  <input type="text" className="period-name-input" value={editName}
                    onChange={e => setEditName(e.target.value)} onBlur={commitEditName}
                    onKeyDown={e => { if (e.key === 'Enter') commitEditName(); if (e.key === 'Escape') setEditingIdx(null); }}
                    autoFocus />
                ) : (
                  <span className="period-selector-name" onClick={() => startEditName(idx)} title="Click to rename">
                    {p.name}
                  </span>
                )}
                <span className="period-selector-range">
                  {format(new Date(p.start), 'MMM d, HH:mm')} – {format(new Date(p.end), 'MMM d, HH:mm')}
                </span>
              </div>
              <button type="button" className="period-selector-remove"
                onClick={() => onRemovePeriod(idx)} title="Remove period">&times;</button>
            </div>
          ))}
        </div>
      )}

      {eventMarkers.length > 0 && (
        <div className="period-selector-events">
          <span className="period-selector-events-label">Events in view:</span>
          {eventMarkers.slice(0, 20).map(evt => (
            <span key={evt.id} className="period-selector-event-tag"
              onClick={() => onEventClick && onEventClick(evt.id)}
              title={`${evt.title} — ${format(new Date(evt.time), 'MMM d, HH:mm')}`}>
              #{evt.id} {evt.title?.substring(0, 20)}{evt.title?.length > 20 ? '…' : ''}
            </span>
          ))}
          {eventMarkers.length > 20 && (
            <span className="period-selector-event-more">+{eventMarkers.length - 20} more</span>
          )}
        </div>
      )}
    </div>
  );
}

// ─── Thread Glucose Chart ──────────────────────────────────

function ChatGlucoseChart({ chartData, chartLoading, periods, onEventClick }) {
  const [collapsed, setCollapsed] = useState(false);
  const threadChartRef = useRef(null);

  const sortedReadings = useMemo(() => {
    if (!chartData?.readings?.length) return [];
    return chartData.readings
      .map(r => ({ time: new Date(r.timestamp).getTime(), value: r.value, timestamp: r.timestamp }))
      .sort((a, b) => a.time - b.time);
  }, [chartData]);

  const hasMultiplePeriods = periods.length > 1;

  const dataMin = sortedReadings.length ? sortedReadings[0].time : 0;
  const dataMax = sortedReadings.length ? sortedReadings[sortedReadings.length - 1].time : 1;
  const { domain, handleWheel, zoomIn, zoomOut, resetZoom, isZoomed } = useChartZoom(dataMin, dataMax);

  const onWheelChart = useCallback((e) => {
    handleWheel(e, threadChartRef.current);
  }, [handleWheel]);

  const eventMarkers = useMemo(() => {
    if (!chartData?.events?.length || !sortedReadings.length) return [];
    return chartData.events.map(evt => {
      const evtTime = new Date(evt.timestamp).getTime();
      const closest = sortedReadings.reduce((prev, curr) =>
        Math.abs(curr.time - evtTime) < Math.abs(prev.time - evtTime) ? curr : prev
      );
      let periodIdx = 0;
      if (hasMultiplePeriods) {
        periodIdx = periods.findIndex(p =>
          evtTime >= new Date(p.start).getTime() && evtTime <= new Date(p.end).getTime()
        );
        if (periodIdx < 0) periodIdx = 0;
      }
      return { ...evt, time: evtTime, eventValue: closest.value, periodIndex: periodIdx };
    });
  }, [chartData, sortedReadings, periods, hasMultiplePeriods]);

  if (chartLoading) {
    return (
      <div className="chat-chart-section">
        <div className="chat-chart-header" onClick={() => setCollapsed(!collapsed)}>
          <span className="chat-chart-toggle">{collapsed ? '+' : '−'}</span>
          <h4>Glucose Graph</h4>
          <span className="chat-chart-loading">Loading...</span>
        </div>
      </div>
    );
  }

  if (!sortedReadings.length) return null;

  const values = sortedReadings.map(d => d.value);
  const minVal = Math.max(40, Math.min(...values) - 10);
  const maxVal = Math.min(400, Math.max(...values) + 10);

  const renderEventLabel = (props) => {
    const { x, y, index } = props;
    const evt = eventMarkers[index];
    if (!evt) return null;
    const color = hasMultiplePeriods ? (periods[evt.periodIndex]?.color || '#f59e0b') : '#f59e0b';
    return (
      <g>
        <circle cx={x} cy={y} r={6} fill={color} stroke="#0f172a" strokeWidth={1.5} />
        <text x={x} y={y - 12} textAnchor="middle" fill="#fbbf24" fontSize={9} fontWeight={600}>
          {index + 1}
        </text>
      </g>
    );
  };

  return (
    <div className="chat-chart-section">
      <div className="chat-chart-header" onClick={() => setCollapsed(!collapsed)}>
        <span className="chat-chart-toggle">{collapsed ? '+' : '−'}</span>
        <h4>Glucose Graph</h4>
        <span className="chat-chart-stats">
          {sortedReadings.length} readings, {eventMarkers.length} events
          {hasMultiplePeriods && ` · ${periods.length} periods`}
        </span>
        {!collapsed && (
          <div className="chat-chart-zoom-btns" onClick={e => e.stopPropagation()}>
            <button className="zoom-btn" onClick={zoomIn} title="Zoom in">+</button>
            <button className="zoom-btn" onClick={zoomOut} title="Zoom out">−</button>
            {isZoomed && <button className="zoom-btn zoom-reset" onClick={resetZoom} title="Reset">Reset</button>}
          </div>
        )}
      </div>
      {!collapsed && (
        <div className="chat-chart-body">
          {hasMultiplePeriods && (
            <div className="chat-chart-legend">
              {periods.map((p, idx) => (
                <span key={idx} className="chat-chart-legend-item">
                  <span className="chat-chart-legend-dot" style={{ background: p.color }} />
                  {p.name}
                </span>
              ))}
            </div>
          )}
          {isZoomed && (
            <div className="period-selector-zoom-info" style={{ padding: '2px 12px' }}>
              Viewing: {format(new Date(domain[0]), 'MMM d, HH:mm')} – {format(new Date(domain[1]), 'MMM d, HH:mm')}
            </div>
          )}
          <div className="chat-chart-row">
            <div className="chat-chart-graph" ref={threadChartRef} style={{ position: 'relative' }}>
              <ResponsiveContainer width="100%" height={hasMultiplePeriods ? 250 : 220}>
                <ComposedChart data={sortedReadings} margin={{ top: 15, right: 10, left: 0, bottom: 5 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="rgba(255,255,255,0.06)" />
                  <XAxis
                    dataKey="time" type="number" scale="time"
                    domain={domain}
                    tickFormatter={t => format(new Date(t), 'MMM d HH:mm')}
                    stroke="rgba(255,255,255,0.3)"
                    tick={{ fontSize: 10, fill: '#94a3b8' }}
                    minTickGap={40} allowDataOverflow
                  />
                  <YAxis domain={[minVal, maxVal]} stroke="rgba(255,255,255,0.3)"
                    tick={{ fontSize: 10, fill: '#94a3b8' }} width={YAXIS_WIDTH} />
                  <Tooltip
                    contentStyle={{ background: '#1e293b', border: '1px solid rgba(255,255,255,0.1)', borderRadius: 8, fontSize: '0.8rem' }}
                    labelFormatter={t => format(new Date(t), 'MMM d, HH:mm')}
                    formatter={(val, name) => {
                      if (name === 'eventValue') return [`${val} mg/dL`, 'Event'];
                      return [`${val} mg/dL`, 'Glucose'];
                    }}
                  />
                  <ReferenceArea y1={70} y2={180} fill="rgba(34,197,94,0.06)" />
                  <ReferenceLine y={70} stroke="rgba(234,179,8,0.3)" strokeDasharray="3 3" />
                  <ReferenceLine y={180} stroke="rgba(234,179,8,0.3)" strokeDasharray="3 3" />

                  {periods.map((p, idx) => (
                    <ReferenceArea key={`p-${idx}`}
                      x1={new Date(p.start).getTime()} x2={new Date(p.end).getTime()}
                      fill={p.color} fillOpacity={0.12} stroke={p.color} strokeOpacity={0.5} strokeWidth={1} />
                  ))}

                  <Line type="monotone" dataKey="value" stroke="#6366f1"
                    strokeWidth={1.5} dot={false} connectNulls isAnimationActive={false} />

                  {eventMarkers.length > 0 && (
                    <Scatter data={eventMarkers} dataKey="eventValue" name="eventValue"
                      fill="#f59e0b" onClick={(data) => onEventClick && onEventClick(data.id)}
                      cursor="pointer" isAnimationActive={false} shape={renderEventLabel} />
                  )}
                </ComposedChart>
              </ResponsiveContainer>
              <div className="chart-interaction-overlay" onWheel={onWheelChart} />
            </div>

            {eventMarkers.length > 0 && (
              <div className="chat-chart-event-panel">
                <div className="chat-chart-event-panel-title">Events</div>
                <div className="chat-chart-event-list">
                  {eventMarkers.map((evt, idx) => (
                    <div key={evt.id} className="chat-chart-event-row"
                      onClick={() => onEventClick && onEventClick(evt.id)}>
                      <span className="chat-chart-event-num"
                        style={{ background: hasMultiplePeriods ? (periods[evt.periodIndex]?.color || '#f59e0b') : '#f59e0b' }}>
                        {idx + 1}
                      </span>
                      <div className="chat-chart-event-info">
                        <span className="chat-chart-event-name">{evt.title || `Event #${evt.id}`}</span>
                        <span className="chat-chart-event-time">
                          {format(new Date(evt.time), 'MMM d, HH:mm')}
                          {evt.eventValue != null && ` · ${evt.eventValue} mg/dL`}
                        </span>
                      </div>
                      <span className="chat-chart-event-arrow">›</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

export default ChatPage;
