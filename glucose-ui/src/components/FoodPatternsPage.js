import React, { useState, useEffect, useCallback, useRef } from 'react';
import { format } from 'date-fns';
import MODEL_OPTIONS from './modelOptions';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

function FoodPatternsPage() {
  const [foods, setFoods] = useState([]);
  const [stats, setStats] = useState(null);
  const [loading, setLoading] = useState(true);
  const [scanning, setScanning] = useState(false);
  const [selectedFood, setSelectedFood] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [search, setSearch] = useState('');
  const [sortBy, setSortBy] = useState('count');
  const [sortDesc, setSortDesc] = useState(true);
  const [selectedEventId, setSelectedEventId] = useState(null);

  // AI Chat state
  const [chatSessionId, setChatSessionId] = useState(null);
  const [chatDetail, setChatDetail] = useState(null);
  const [chatMsg, setChatMsg] = useState('');
  const [chatModel, setChatModel] = useState('');
  const [chatSending, setChatSending] = useState(false);
  const [chatOpen, setChatOpen] = useState(false);
  const chatEndRef = useRef(null);

  const fetchFoods = useCallback(async () => {
    try {
      const params = new URLSearchParams();
      if (search) params.set('search', search);
      if (sortBy) params.set('sortBy', sortBy);
      params.set('desc', sortDesc);
      const res = await fetch(`${API_BASE}/food?${params}`);
      const data = await res.json();
      setFoods(data);
    } catch (err) {
      console.error('Failed to fetch foods:', err);
    }
  }, [search, sortBy, sortDesc]);

  const fetchStats = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/food/stats`);
      const data = await res.json();
      setStats(data);
    } catch (err) {
      console.error('Failed to fetch food stats:', err);
    }
  }, []);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      await Promise.all([fetchFoods(), fetchStats()]);
      setLoading(false);
    };
    load();
  }, [fetchFoods, fetchStats]);

  useEffect(() => {
    const handler = () => { fetchFoods(); fetchStats(); };
    window.addEventListener('foodPatternsUpdated', handler);
    return () => window.removeEventListener('foodPatternsUpdated', handler);
  }, [fetchFoods, fetchStats]);

  const handleScan = async () => {
    setScanning(true);
    try {
      await fetch(`${API_BASE}/food/scan`, { method: 'POST' });
      setTimeout(() => { fetchFoods(); fetchStats(); }, 3000);
    } catch (err) {
      console.error('Failed to trigger scan:', err);
    } finally {
      setTimeout(() => setScanning(false), 5000);
    }
  };

  const closeModal = () => {
    setSelectedFood(null);
    setChatOpen(false);
    setChatSessionId(null);
    setChatDetail(null);
  };

  const handleSelectFood = async (id) => {
    if (selectedFood?.id === id) { closeModal(); return; }
    setDetailLoading(true);
    setChatOpen(false);
    setChatSessionId(null);
    setChatDetail(null);
    try {
      const res = await fetch(`${API_BASE}/food/${id}`);
      const data = await res.json();
      setSelectedFood(data);
    } catch (err) {
      console.error('Failed to fetch food detail:', err);
    } finally {
      setDetailLoading(false);
    }
  };

  const handleDelete = async (id) => {
    if (!window.confirm('Delete this food item and all its links?')) return;
    try {
      await fetch(`${API_BASE}/food/${id}`, { method: 'DELETE' });
      if (selectedFood?.id === id) closeModal();
      fetchFoods();
      fetchStats();
    } catch (err) {
      console.error('Failed to delete food:', err);
    }
  };

  const handleSort = (col) => {
    if (sortBy === col) {
      setSortDesc(!sortDesc);
    } else {
      setSortBy(col);
      setSortDesc(true);
    }
  };

  // ── AI Chat ────────────────────────────────────────────

  const buildFoodContext = (food) => {
    const lines = [];
    const nameDisplay = food.nameEn && food.nameEn.toLowerCase() !== food.name.toLowerCase()
      ? `${food.name} (${food.nameEn})`
      : food.name;
    lines.push(`Food: ${nameDisplay}${food.category ? ` (${food.category})` : ''}`);
    lines.push(`Occurrences: ${food.occurrenceCount}`);
    lines.push(`Average spike: ${food.avgSpike != null ? `+${Math.round(food.avgSpike)} mg/dL` : 'N/A'}`);
    lines.push(`Worst spike: ${food.worstSpike != null ? `+${Math.round(food.worstSpike)} mg/dL` : 'N/A'}`);
    lines.push(`Best spike: ${food.bestSpike != null ? `+${Math.round(food.bestSpike)} mg/dL` : 'N/A'}`);
    lines.push(`Avg glucose at event: ${food.avgGlucoseAtEvent != null ? `${Math.round(food.avgGlucoseAtEvent)} mg/dL` : 'N/A'}`);
    lines.push(`Avg peak glucose: ${food.avgGlucoseMax != null ? `${Math.round(food.avgGlucoseMax)} mg/dL` : 'N/A'}`);
    lines.push(`Classifications: ${food.greenCount} good, ${food.yellowCount} concerning, ${food.redCount} problematic`);
    lines.push(`Period: ${format(new Date(food.firstSeen), 'MMM d, yyyy')} – ${format(new Date(food.lastSeen), 'MMM d, yyyy')}`);
    lines.push('');
    lines.push('Event history:');

    food.events?.forEach((evt, i) => {
      const titleDisplay = evt.noteTitleEn && evt.noteTitleEn !== evt.noteTitle
        ? `${evt.noteTitle} (${evt.noteTitleEn})`
        : evt.noteTitle;
      lines.push(`\n--- Event ${i + 1}: ${titleDisplay} (${format(new Date(evt.eventTimestamp), 'MMM d, yyyy HH:mm')}) ---`);
      if (evt.noteContent) {
        lines.push(`Notes: ${evt.noteContent}`);
        if (evt.noteContentEn && evt.noteContentEn !== evt.noteContent)
          lines.push(`Notes (EN): ${evt.noteContentEn}`);
      }
      lines.push(`Spike: ${evt.spike != null ? `+${Math.round(evt.spike)} mg/dL` : 'N/A'}`);
      lines.push(`Glucose at event: ${evt.glucoseAtEvent != null ? `${Math.round(evt.glucoseAtEvent)} mg/dL` : 'N/A'}`);
      if (evt.glucoseMax != null) lines.push(`Peak glucose: ${Math.round(evt.glucoseMax)} mg/dL`);
      if (evt.glucoseMin != null) lines.push(`Lowest glucose: ${Math.round(evt.glucoseMin)} mg/dL`);
      if (evt.glucoseAvg != null) lines.push(`Average glucose: ${Math.round(evt.glucoseAvg)} mg/dL`);
      if (evt.recoveryMinutes != null) lines.push(`Recovery time: ${Math.round(evt.recoveryMinutes)} min`);
      lines.push(`Classification: ${evt.aiClassification || 'N/A'}`);
      if (evt.aiAnalysis) lines.push(`AI Analysis: ${evt.aiAnalysis}`);
    });

    return lines.join('\n');
  };

  const handleStartChat = async (customPrompt) => {
    if (!selectedFood) return;
    setChatSending(true);
    setChatOpen(true);

    const context = buildFoodContext(selectedFood);
    const message = customPrompt || `Here is all the data about "${selectedFood.name}" and my glucose responses to it:\n\n${context}\n\nPlease analyze this food's impact on my glucose. What patterns do you see? Is this food generally safe, risky, or does it depend on context? Any recommendations?`;

    const periods = selectedFood.events
      ?.filter(e => e.periodStart && e.periodEnd)
      .map((e, i) => ({
        name: `${selectedFood.name} - ${format(new Date(e.eventTimestamp), 'MMM d HH:mm')}`,
        start: e.periodStart,
        end: e.periodEnd,
        color: ['#6366f1', '#f59e0b', '#10b981', '#ef4444', '#8b5cf6', '#ec4899', '#06b6d4', '#f97316'][i % 8],
      }));

    try {
      const res = await fetch(`${API_BASE}/chat/sessions`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title: `Food Analysis: ${selectedFood.name}`,
          initialMessage: message,
          model: chatModel || null,
          periods: periods?.length > 0 ? periods : null,
        }),
      });
      if (res.ok) {
        const data = await res.json();
        setChatSessionId(data.sessionId);
        fetchChatDetail(data.sessionId);
      }
    } catch (err) {
      console.error('Failed to start food chat:', err);
    } finally {
      setChatSending(false);
    }
  };

  const fetchChatDetail = async (id) => {
    try {
      const res = await fetch(`${API_BASE}/chat/sessions/${id}`);
      if (res.ok) {
        const data = await res.json();
        setChatDetail(data);
      }
    } catch (err) {
      console.error('Failed to fetch chat detail:', err);
    }
  };

  const handleSendChatMsg = async (e) => {
    e.preventDefault();
    if (!chatMsg.trim() || !chatSessionId) return;
    setChatSending(true);
    try {
      const res = await fetch(`${API_BASE}/chat/sessions/${chatSessionId}/messages`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: chatMsg, modelOverride: chatModel || null }),
      });
      if (res.ok) {
        setChatMsg('');
        fetchChatDetail(chatSessionId);
      }
    } catch (err) {
      console.error('Failed to send chat message:', err);
    } finally {
      setChatSending(false);
    }
  };

  useEffect(() => {
    if (!chatSessionId) return;
    const handler = (e) => {
      if (e.detail?.sessionId === chatSessionId) fetchChatDetail(chatSessionId);
    };
    window.addEventListener('chatMessageCompleted', handler);
    return () => window.removeEventListener('chatMessageCompleted', handler);
  }, [chatSessionId]);

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [chatDetail?.messages]);

  useEffect(() => {
    if (!chatSessionId || !chatDetail) return;
    const lastMsg = chatDetail.messages?.[chatDetail.messages.length - 1];
    if (lastMsg?.role === 'user') {
      const interval = setInterval(() => fetchChatDetail(chatSessionId), 2000);
      return () => clearInterval(interval);
    }
  }, [chatSessionId, chatDetail]);

  // Close modal on Escape
  useEffect(() => {
    if (!selectedFood) return;
    const handleKey = (e) => { if (e.key === 'Escape') closeModal(); };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  });

  const classBar = (green, yellow, red) => {
    const total = green + yellow + red;
    if (total === 0) return null;
    return (
      <div className="food-class-bar">
        {green > 0 && <span className="food-class-green" style={{ width: `${(green / total) * 100}%` }} title={`${green} good`} />}
        {yellow > 0 && <span className="food-class-yellow" style={{ width: `${(yellow / total) * 100}%` }} title={`${yellow} concerning`} />}
        {red > 0 && <span className="food-class-red" style={{ width: `${(red / total) * 100}%` }} title={`${red} problematic`} />}
      </div>
    );
  };

  const spikeColor = (spike) => {
    if (spike == null) return '';
    if (spike <= 30) return 'food-spike-good';
    if (spike <= 60) return 'food-spike-warn';
    return 'food-spike-bad';
  };

  if (loading) return <div className="food-page"><div className="food-loading">Loading food patterns...</div></div>;

  return (
    <div className="food-page">
      <div className="food-header">
        <h2>Food Patterns</h2>
        <p className="food-subtitle">
          How specific foods affect your glucose across multiple events
        </p>
      </div>

      {stats && (
        <div className="food-stats-row">
          <div className="food-stat-card">
            <div className="food-stat-value">{stats.totalFoods}</div>
            <div className="food-stat-label">Foods tracked</div>
          </div>
          <div className="food-stat-card">
            <div className="food-stat-value">{stats.totalLinks}</div>
            <div className="food-stat-label">Food-event links</div>
          </div>
          <div className="food-stat-card">
            <div className="food-stat-value">{stats.foodsWithMultipleOccurrences}</div>
            <div className="food-stat-label">With 2+ occurrences</div>
          </div>
          {stats.mostProblematicFood && (
            <div className="food-stat-card food-stat-danger">
              <div className="food-stat-value">{stats.mostProblematicFood}</div>
              <div className="food-stat-label">Highest avg spike ({stats.highestAvgSpike != null ? `+${Math.round(stats.highestAvgSpike)}` : '—'} mg/dL)</div>
            </div>
          )}
          {stats.safestFood && (
            <div className="food-stat-card food-stat-safe">
              <div className="food-stat-value">{stats.safestFood}</div>
              <div className="food-stat-label">Lowest avg spike ({stats.lowestAvgSpike != null ? `+${Math.round(stats.lowestAvgSpike)}` : '—'} mg/dL)</div>
            </div>
          )}
        </div>
      )}

      <div className="food-toolbar">
        <input
          type="text"
          className="food-search"
          placeholder="Search foods..."
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
        <button className="food-scan-btn" onClick={handleScan} disabled={scanning}>
          {scanning ? 'Scanning...' : 'Scan All Events'}
        </button>
      </div>

      {foods.length === 0 ? (
        <div className="food-empty">
          <p>No food patterns found yet.</p>
          <p>Click "Scan All Events" to extract food items from your meal events using AI.</p>
        </div>
      ) : (
        <div className="food-table-container">
          <table className="food-table">
            <thead>
              <tr>
                <th className="food-th-sortable" onClick={() => handleSort('name')}>
                  Food {sortBy === 'name' && (sortDesc ? '↓' : '↑')}
                </th>
                <th className="food-th-sortable" onClick={() => handleSort('count')}>
                  Times {sortBy === 'count' && (sortDesc ? '↓' : '↑')}
                </th>
                <th className="food-th-sortable" onClick={() => handleSort('spike')}>
                  Avg Spike {sortBy === 'spike' && (sortDesc ? '↓' : '↑')}
                </th>
                <th>Worst</th>
                <th>Best</th>
                <th>Classification</th>
                <th className="food-th-sortable" onClick={() => handleSort('lastseen')}>
                  Last Seen {sortBy === 'lastseen' && (sortDesc ? '↓' : '↑')}
                </th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {foods.map(f => (
                <tr
                  key={f.id}
                  className={`food-row${selectedFood?.id === f.id ? ' active' : ''}`}
                  onClick={() => handleSelectFood(f.id)}
                >
                  <td className="food-name-cell">
                    <span className="food-name">{f.name}</span>
                    {f.nameEn && f.nameEn.toLowerCase() !== f.name.toLowerCase() && (
                      <span className="food-name-en">{f.nameEn}</span>
                    )}
                    {f.category && <span className="food-category">{f.category}</span>}
                  </td>
                  <td className="food-count-cell">{f.occurrenceCount}</td>
                  <td className={`food-spike-cell ${spikeColor(f.avgSpike)}`}>
                    {f.avgSpike != null ? `+${Math.round(f.avgSpike)}` : '—'}
                  </td>
                  <td className={`food-spike-cell ${spikeColor(f.worstSpike)}`}>
                    {f.worstSpike != null ? `+${Math.round(f.worstSpike)}` : '—'}
                  </td>
                  <td className={`food-spike-cell ${spikeColor(f.bestSpike)}`}>
                    {f.bestSpike != null ? `+${Math.round(f.bestSpike)}` : '—'}
                  </td>
                  <td>{classBar(f.greenCount, f.yellowCount, f.redCount)}</td>
                  <td className="food-date-cell">
                    {format(new Date(f.lastSeen), 'MMM d')}
                  </td>
                  <td>
                    <button className="food-delete-btn"
                      onClick={(e) => { e.stopPropagation(); handleDelete(f.id); }}
                      title="Delete">&times;</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {detailLoading && (
        <div className="food-detail-loading">Loading food details...</div>
      )}

      {/* ── Food Detail Modal ────────────────────────────── */}
      {selectedFood && !detailLoading && (
        <div className="food-modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) closeModal(); }}>
          <div className="food-modal">
            <div className="food-modal-header">
              <div className="food-detail-title-group">
                <h3>{selectedFood.name}</h3>
                {selectedFood.nameEn && selectedFood.nameEn.toLowerCase() !== selectedFood.name.toLowerCase() && (
                  <span className="food-detail-name-en">{selectedFood.nameEn}</span>
                )}
              </div>
              <div className="food-detail-header-actions">
                <button
                  className="food-ai-btn"
                  onClick={() => handleStartChat()}
                  disabled={chatSending}
                >
                  {chatSending ? 'Starting...' : 'Analyze with AI'}
                </button>
                <button className="food-detail-close" onClick={closeModal}>&times;</button>
              </div>
            </div>

            <div className="food-modal-body">
              <div className="food-detail-stats">
                <div className="food-detail-stat">
                  <span className="food-detail-stat-label">Occurrences</span>
                  <span className="food-detail-stat-value">{selectedFood.occurrenceCount}</span>
                </div>
                <div className="food-detail-stat">
                  <span className="food-detail-stat-label">Avg Spike</span>
                  <span className={`food-detail-stat-value ${spikeColor(selectedFood.avgSpike)}`}>
                    {selectedFood.avgSpike != null ? `+${Math.round(selectedFood.avgSpike)} mg/dL` : '—'}
                  </span>
                </div>
                <div className="food-detail-stat">
                  <span className="food-detail-stat-label">Worst / Best</span>
                  <span className="food-detail-stat-value">
                    {selectedFood.worstSpike != null ? `+${Math.round(selectedFood.worstSpike)}` : '—'}
                    {' / '}
                    {selectedFood.bestSpike != null ? `+${Math.round(selectedFood.bestSpike)}` : '—'}
                  </span>
                </div>
                <div className="food-detail-stat">
                  <span className="food-detail-stat-label">Avg Glucose at Event</span>
                  <span className="food-detail-stat-value">
                    {selectedFood.avgGlucoseAtEvent != null ? `${Math.round(selectedFood.avgGlucoseAtEvent)} mg/dL` : '—'}
                  </span>
                </div>
                <div className="food-detail-stat">
                  <span className="food-detail-stat-label">Avg Peak</span>
                  <span className="food-detail-stat-value">
                    {selectedFood.avgGlucoseMax != null ? `${Math.round(selectedFood.avgGlucoseMax)} mg/dL` : '—'}
                  </span>
                </div>
                <div className="food-detail-stat">
                  <span className="food-detail-stat-label">Classification</span>
                  <span className="food-detail-stat-value">
                    {selectedFood.greenCount > 0 && <span className="food-badge food-badge-green">{selectedFood.greenCount} good</span>}
                    {selectedFood.yellowCount > 0 && <span className="food-badge food-badge-yellow">{selectedFood.yellowCount} mixed</span>}
                    {selectedFood.redCount > 0 && <span className="food-badge food-badge-red">{selectedFood.redCount} bad</span>}
                  </span>
                </div>
                <div className="food-detail-stat">
                  <span className="food-detail-stat-label">First / Last Seen</span>
                  <span className="food-detail-stat-value">
                    {format(new Date(selectedFood.firstSeen), 'MMM d, yyyy')} — {format(new Date(selectedFood.lastSeen), 'MMM d, yyyy')}
                  </span>
                </div>
              </div>

              <h4>Event History ({selectedFood.events?.length || 0} events)</h4>
              <div className="food-detail-events">
                {selectedFood.events?.map((evt, i) => (
                  <div key={evt.eventId} className="food-event-card">
                    <div className="food-event-card-header" onClick={() => setSelectedEventId(evt.eventId)}>
                      <span className={`food-event-class food-event-class-${evt.aiClassification || 'none'}`} />
                      <div className="food-event-info">
                        <span className="food-event-title">
                          {evt.noteTitle}
                          {evt.noteTitleEn && evt.noteTitleEn !== evt.noteTitle && (
                            <span className="food-event-title-en"> ({evt.noteTitleEn})</span>
                          )}
                        </span>
                        <span className="food-event-date">
                          {format(new Date(evt.eventTimestamp), 'MMM d, yyyy HH:mm')}
                        </span>
                      </div>
                      <div className="food-event-metrics">
                        <span className={`food-event-spike ${spikeColor(evt.spike)}`}>
                          {evt.spike != null ? `+${Math.round(evt.spike)}` : '—'} mg/dL
                        </span>
                        <span className="food-event-baseline">
                          @ {evt.glucoseAtEvent != null ? Math.round(evt.glucoseAtEvent) : '?'}
                        </span>
                      </div>
                      <span className="food-event-arrow">›</span>
                    </div>

                    <div className="food-event-card-body">
                      {evt.noteContent && (
                        <div className="food-event-note">
                          <span className="food-event-note-label">Notes:</span> {evt.noteContent}
                          {evt.noteContentEn && evt.noteContentEn !== evt.noteContent && (
                            <div className="food-event-note-en">
                              <span className="food-event-note-label">EN:</span> {evt.noteContentEn}
                            </div>
                          )}
                        </div>
                      )}
                      <div className="food-event-glucose-row">
                        {evt.glucoseMin != null && (
                          <span className="food-event-glucose-item">
                            <span className="food-event-glucose-label">Min</span>
                            <span className="food-event-glucose-val">{Math.round(evt.glucoseMin)}</span>
                          </span>
                        )}
                        {evt.glucoseAvg != null && (
                          <span className="food-event-glucose-item">
                            <span className="food-event-glucose-label">Avg</span>
                            <span className="food-event-glucose-val">{Math.round(evt.glucoseAvg)}</span>
                          </span>
                        )}
                        {evt.glucoseMax != null && (
                          <span className="food-event-glucose-item">
                            <span className="food-event-glucose-label">Max</span>
                            <span className="food-event-glucose-val">{Math.round(evt.glucoseMax)}</span>
                          </span>
                        )}
                        {evt.recoveryMinutes != null && (
                          <span className="food-event-glucose-item">
                            <span className="food-event-glucose-label">Recovery</span>
                            <span className="food-event-glucose-val">{Math.round(evt.recoveryMinutes)}m</span>
                          </span>
                        )}
                        {evt.readingCount > 0 && (
                          <span className="food-event-glucose-item">
                            <span className="food-event-glucose-label">Readings</span>
                            <span className="food-event-glucose-val">{evt.readingCount}</span>
                          </span>
                        )}
                      </div>
                      {evt.aiAnalysis && (
                        <div className="food-event-analysis">
                          <span className="food-event-note-label">AI Analysis:</span> {evt.aiAnalysis}
                        </div>
                      )}
                    </div>
                  </div>
                ))}
              </div>

              {/* ── AI Chat Panel ──────────────────────────────── */}
              {chatOpen && (
                <div className="food-chat-panel">
                  <div className="food-chat-header">
                    <h4>AI Chat — {selectedFood.name}</h4>
                    <button className="food-chat-close" onClick={() => setChatOpen(false)}>&times;</button>
                  </div>

                  <div className="food-chat-messages">
                    {chatDetail?.messages?.map(msg => (
                      <div key={msg.id} className={`food-chat-msg food-chat-msg-${msg.role}`}>
                        <div className="food-chat-msg-role">
                          {msg.role === 'user' ? 'You' : 'AI Assistant'}
                          {msg.model && <span className="food-chat-msg-model">{msg.model}</span>}
                        </div>
                        <div className="food-chat-msg-content">{msg.content}</div>
                      </div>
                    ))}
                    {chatSending && (
                      <div className="food-chat-msg food-chat-msg-assistant">
                        <div className="food-chat-msg-role">AI Assistant</div>
                        <div className="food-chat-msg-content food-chat-thinking">Thinking...</div>
                      </div>
                    )}
                    <div ref={chatEndRef} />
                  </div>

                  <form className="food-chat-input-row" onSubmit={handleSendChatMsg}>
                    <input
                      type="text"
                      className="food-chat-input"
                      placeholder="Ask about this food..."
                      value={chatMsg}
                      onChange={e => setChatMsg(e.target.value)}
                      disabled={chatSending}
                    />
                    <select
                      className="food-chat-model-select"
                      value={chatModel}
                      onChange={e => setChatModel(e.target.value)}
                    >
                      {MODEL_OPTIONS.map(m => (
                        <option key={m.value} value={m.value}>{m.label}</option>
                      ))}
                    </select>
                    <button className="food-chat-send" type="submit" disabled={chatSending || !chatMsg.trim()}>
                      Send
                    </button>
                  </form>

                  <div className="food-chat-quick-prompts">
                    <button onClick={() => handleStartChat(`Based on the data for "${selectedFood.name}", what time of day or meal context produces the best glucose response? What factors might explain the variation?`)}>
                      Best timing?
                    </button>
                    <button onClick={() => handleStartChat(`For "${selectedFood.name}", compare my best and worst glucose responses. What was different? What can I learn from the good responses?`)}>
                      Best vs worst
                    </button>
                    <button onClick={() => handleStartChat(`Should I keep eating "${selectedFood.name}"? Give me a clear recommendation based on all ${selectedFood.occurrenceCount} events and glucose data.`)}>
                      Should I eat this?
                    </button>
                    <button onClick={() => handleStartChat(`What healthier alternatives or modifications would you suggest for "${selectedFood.name}" to reduce my glucose spikes? Consider my typical response pattern.`)}>
                      Alternatives?
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {selectedEventId && (
        <EventDetailModalWrapper
          eventId={selectedEventId}
          onClose={() => setSelectedEventId(null)}
        />
      )}
    </div>
  );
}

function EventDetailModalWrapper({ eventId, onClose }) {
  const EventDetailModal = require('./EventDetailModal').default;
  return <EventDetailModal eventId={eventId} onClose={onClose} />;
}

export default FoodPatternsPage;
