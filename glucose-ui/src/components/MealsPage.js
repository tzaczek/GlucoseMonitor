import React, { useState, useEffect, useCallback, useRef } from 'react';
import { format } from 'date-fns';
import MODEL_OPTIONS from './modelOptions';
import useInfiniteScroll from '../hooks/useInfiniteScroll';
import PAGE_SIZES from '../config/pageSize';

const API_BASE = process.env.REACT_APP_API_URL || '/api';
const PAGE_SIZE = PAGE_SIZES.meals;

function MealsPage() {
  const [meals, setMeals] = useState([]);
  const [stats, setStats] = useState(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [totalCount, setTotalCount] = useState(0);
  const [search, setSearch] = useState('');
  const [sortBy, setSortBy] = useState('date');
  const [sortDesc, setSortDesc] = useState(true);
  const [classFilter, setClassFilter] = useState('');
  const mealsOffsetRef = useRef(0);

  // Detail modal
  const [selectedMeal, setSelectedMeal] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);

  // Selection for comparison
  const [selectedIds, setSelectedIds] = useState(new Set());
  const [compareData, setCompareData] = useState(null);
  const [compareOpen, setCompareOpen] = useState(false);
  const [compareLoading, setCompareLoading] = useState(false);

  // AI Chat state (shared between detail and compare modals)
  const [chatSessionId, setChatSessionId] = useState(null);
  const [chatDetail, setChatDetail] = useState(null);
  const [chatMsg, setChatMsg] = useState('');
  const [chatModel, setChatModel] = useState('');
  const [chatSending, setChatSending] = useState(false);
  const [chatOpen, setChatOpen] = useState(false);
  const chatEndRef = useRef(null);

  // Event detail modal for drilling down
  const [selectedEventId, setSelectedEventId] = useState(null);

  const fetchMeals = useCallback(async (append = false) => {
    if (append) setLoadingMore(true);
    try {
      const offset = append ? mealsOffsetRef.current : 0;
      const params = new URLSearchParams();
      if (search) params.set('search', search);
      if (sortBy) params.set('sortBy', sortBy);
      params.set('desc', sortDesc);
      if (classFilter) params.set('classification', classFilter);
      params.set('limit', PAGE_SIZE);
      params.set('offset', offset);
      const res = await fetch(`${API_BASE}/meals?${params}`);
      const data = await res.json();
      if (append) {
        setMeals(prev => [...prev, ...data.items]);
      } else {
        setMeals(data.items);
      }
      setTotalCount(data.totalCount);
      mealsOffsetRef.current = (append ? mealsOffsetRef.current : 0) + data.items.length;
    } catch (err) {
      console.error('Failed to fetch meals:', err);
    } finally {
      setLoadingMore(false);
    }
  }, [search, sortBy, sortDesc, classFilter]);

  const fetchStats = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/meals/stats`);
      const data = await res.json();
      setStats(data);
    } catch (err) {
      console.error('Failed to fetch meal stats:', err);
    }
  }, []);

  useEffect(() => {
    const load = async () => {
      setLoading(true);
      mealsOffsetRef.current = 0;
      await Promise.all([fetchMeals(), fetchStats()]);
      setLoading(false);
    };
    load();
  }, [fetchMeals, fetchStats]);

  useEffect(() => {
    const handler = () => { mealsOffsetRef.current = 0; fetchMeals(); fetchStats(); };
    window.addEventListener('eventsUpdated', handler);
    return () => window.removeEventListener('eventsUpdated', handler);
  }, [fetchMeals, fetchStats]);

  const mealsHasMore = meals.length < totalCount;
  const loadMoreMeals = useCallback(() => fetchMeals(true), [fetchMeals]);
  useInfiniteScroll(loadMoreMeals, { hasMore: mealsHasMore, loading: loadingMore });

  // ── Detail Modal ───────────────────────────────────────────

  const closeDetailModal = () => {
    setSelectedMeal(null);
    setChatOpen(false);
    setChatSessionId(null);
    setChatDetail(null);
  };

  const handleSelectMeal = async (id) => {
    if (selectedMeal?.id === id) { closeDetailModal(); return; }
    setDetailLoading(true);
    setChatOpen(false);
    setChatSessionId(null);
    setChatDetail(null);
    try {
      const res = await fetch(`${API_BASE}/meals/${id}`);
      const data = await res.json();
      setSelectedMeal(data);
    } catch (err) {
      console.error('Failed to fetch meal detail:', err);
    } finally {
      setDetailLoading(false);
    }
  };

  // ── Selection & Compare ────────────────────────────────────

  const toggleSelect = (id, e) => {
    e.stopPropagation();
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const clearSelection = () => setSelectedIds(new Set());

  const handleCompare = async () => {
    if (selectedIds.size < 2) return;
    setCompareLoading(true);
    setCompareOpen(true);
    setChatOpen(false);
    setChatSessionId(null);
    setChatDetail(null);
    try {
      const res = await fetch(`${API_BASE}/meals/compare`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ids: [...selectedIds] }),
      });
      if (res.ok) {
        const data = await res.json();
        setCompareData(data);
      }
    } catch (err) {
      console.error('Failed to compare meals:', err);
    } finally {
      setCompareLoading(false);
    }
  };

  const closeCompareModal = () => {
    setCompareOpen(false);
    setCompareData(null);
    setChatOpen(false);
    setChatSessionId(null);
    setChatDetail(null);
  };

  // ── AI Chat ────────────────────────────────────────────────

  const buildMealContext = (meal) => {
    const lines = [];
    const title = meal.noteTitleEn && meal.noteTitleEn !== meal.noteTitle
      ? `${meal.noteTitle} (${meal.noteTitleEn})` : meal.noteTitle;
    lines.push(`Meal: ${title}`);
    lines.push(`Date: ${format(new Date(meal.eventTimestamp), 'EEEE, MMM d, yyyy HH:mm')}`);

    if (meal.noteContent) {
      lines.push(`Notes: ${meal.noteContent}`);
      if (meal.noteContentEn && meal.noteContentEn !== meal.noteContent)
        lines.push(`Notes (EN): ${meal.noteContentEn}`);
    }

    if (meal.foods?.length > 0) {
      const foodNames = meal.foods.map(f =>
        f.nameEn && f.nameEn.toLowerCase() !== f.name.toLowerCase()
          ? `${f.name} (${f.nameEn})` : f.name
      ).join(', ');
      lines.push(`Foods: ${foodNames}`);
    }

    lines.push(`Glucose at event: ${meal.glucoseAtEvent != null ? `${Math.round(meal.glucoseAtEvent)} mg/dL` : 'N/A'}`);
    lines.push(`Spike: ${meal.glucoseSpike != null ? `+${Math.round(meal.glucoseSpike)} mg/dL` : 'N/A'}`);
    lines.push(`Min: ${meal.glucoseMin != null ? `${Math.round(meal.glucoseMin)} mg/dL` : 'N/A'}`);
    lines.push(`Max: ${meal.glucoseMax != null ? `${Math.round(meal.glucoseMax)} mg/dL` : 'N/A'}`);
    lines.push(`Average: ${meal.glucoseAvg != null ? `${Math.round(meal.glucoseAvg)} mg/dL` : 'N/A'}`);
    lines.push(`Readings: ${meal.readingCount}`);
    lines.push(`Classification: ${meal.aiClassification || 'N/A'}`);
    if (meal.aiAnalysis) lines.push(`AI Analysis: ${meal.aiAnalysis}`);
    return lines.join('\n');
  };

  const buildCompareContext = (mealsArr) => {
    const lines = ['MEAL COMPARISON DATA:'];
    mealsArr.forEach((m, i) => {
      lines.push(`\n=== Meal ${i + 1} ===`);
      lines.push(buildMealContext(m));
    });
    return lines.join('\n');
  };

  const handleStartChat = async (contextData, customPrompt, isCompare = false) => {
    setChatSending(true);
    setChatOpen(true);

    let message, periods;

    if (isCompare && compareData?.meals) {
      const context = buildCompareContext(compareData.meals);
      message = customPrompt || `I want to compare these ${compareData.meals.length} meals and their glucose impact:\n\n${context}\n\nPlease compare these meals. Which had the best and worst glucose response? What patterns or differences do you notice? Any recommendations?`;
      periods = compareData.meals
        .filter(m => m.periodStart && m.periodEnd)
        .map((m, i) => ({
          name: m.noteTitle,
          start: m.periodStart,
          end: m.periodEnd,
          color: ['#6366f1', '#f59e0b', '#10b981', '#ef4444', '#8b5cf6', '#ec4899', '#06b6d4', '#f97316'][i % 8],
        }));
    } else if (contextData) {
      const context = buildMealContext(contextData);
      message = customPrompt || `Here is the data about this meal and glucose response:\n\n${context}\n\nPlease analyze this meal's impact on my glucose. What do you observe? Is the glucose response good, concerning, or bad? Any recommendations?`;
      periods = contextData.periodStart && contextData.periodEnd ? [{
        name: contextData.noteTitle,
        start: contextData.periodStart,
        end: contextData.periodEnd,
        color: '#6366f1',
      }] : null;
    }

    const title = isCompare
      ? `Meal Comparison (${compareData?.meals?.length || 0} meals)`
      : `Meal Analysis: ${contextData?.noteTitle || 'Unknown'}`;

    try {
      const res = await fetch(`${API_BASE}/chat/sessions`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title,
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
      console.error('Failed to start meal chat:', err);
    } finally {
      setChatSending(false);
    }
  };

  const fetchChatDetail = async (id) => {
    try {
      const res = await fetch(`${API_BASE}/chat/sessions/${id}`);
      if (res.ok) setChatDetail(await res.json());
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

  // ── Keyboard shortcuts ─────────────────────────────────────

  useEffect(() => {
    const handleKey = (e) => {
      if (e.key === 'Escape') {
        if (selectedMeal) closeDetailModal();
        else if (compareOpen) closeCompareModal();
      }
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  });

  // ── Helpers ────────────────────────────────────────────────

  const spikeColor = (spike) => {
    if (spike == null) return '';
    if (spike <= 30) return 'meal-spike-good';
    if (spike <= 60) return 'meal-spike-warn';
    return 'meal-spike-bad';
  };

  const classIcon = (c) => {
    if (c === 'green') return '🟢';
    if (c === 'yellow') return '🟡';
    if (c === 'red') return '🔴';
    return '⬜';
  };

  const renderChatPanel = (contextData, isCompare = false) => (
    <div className="meal-chat-panel">
      <div className="meal-chat-header">
        <h4>AI Chat {isCompare ? '— Comparison' : `— ${contextData?.noteTitle || ''}`}</h4>
        <button className="meal-chat-close" onClick={() => setChatOpen(false)}>&times;</button>
      </div>
      <div className="meal-chat-messages">
        {chatDetail?.messages?.map(msg => (
          <div key={msg.id} className={`meal-chat-msg meal-chat-msg-${msg.role}`}>
            <div className="meal-chat-msg-role">
              {msg.role === 'user' ? 'You' : 'AI Assistant'}
              {msg.model && <span className="meal-chat-msg-model">{msg.model}</span>}
            </div>
            <div className="meal-chat-msg-content">{msg.content}</div>
          </div>
        ))}
        {chatSending && (
          <div className="meal-chat-msg meal-chat-msg-assistant">
            <div className="meal-chat-msg-role">AI Assistant</div>
            <div className="meal-chat-msg-content meal-chat-thinking">Thinking...</div>
          </div>
        )}
        <div ref={chatEndRef} />
      </div>
      <form className="meal-chat-input-row" onSubmit={handleSendChatMsg}>
        <input
          type="text"
          className="meal-chat-input"
          placeholder="Ask about this meal..."
          value={chatMsg}
          onChange={e => setChatMsg(e.target.value)}
          disabled={chatSending}
        />
        <select className="meal-chat-model-select" value={chatModel} onChange={e => setChatModel(e.target.value)}>
          {MODEL_OPTIONS.map(m => <option key={m.value} value={m.value}>{m.label}</option>)}
        </select>
        <button className="meal-chat-send" type="submit" disabled={chatSending || !chatMsg.trim()}>Send</button>
      </form>
      <div className="meal-chat-quick-prompts">
        {isCompare ? (
          <>
            <button onClick={() => handleStartChat(null, `Which of these ${compareData?.meals?.length} meals had the best glucose response and why? What made it different?\n\n${buildCompareContext(compareData?.meals || [])}`, true)}>
              Best meal?
            </button>
            <button onClick={() => handleStartChat(null, `What common foods or patterns appear in the meals with good glucose responses vs bad ones?\n\n${buildCompareContext(compareData?.meals || [])}`, true)}>
              Common patterns
            </button>
            <button onClick={() => handleStartChat(null, `Based on these ${compareData?.meals?.length} meals, what specific dietary changes would you recommend to improve my glucose responses?\n\n${buildCompareContext(compareData?.meals || [])}`, true)}>
              Recommendations
            </button>
          </>
        ) : (
          <>
            <button onClick={() => handleStartChat(contextData, `What specific foods in this meal likely caused the glucose spike? How could I modify this meal?\n\n${buildMealContext(contextData)}`)}>
              What caused the spike?
            </button>
            <button onClick={() => handleStartChat(contextData, `Is this meal safe for me to eat regularly based on the glucose response? Give a clear yes/no recommendation.\n\n${buildMealContext(contextData)}`)}>
              Safe to eat?
            </button>
            <button onClick={() => handleStartChat(contextData, `Suggest healthier alternatives or modifications for this meal to reduce the glucose spike.\n\n${buildMealContext(contextData)}`)}>
              Healthier alternatives
            </button>
          </>
        )}
      </div>
    </div>
  );

  if (loading) return <div className="meals-page"><div className="meals-loading">Loading meals...</div></div>;

  return (
    <div className="meals-page">
      <div className="meals-header">
        <h2>Meals</h2>
        <p className="meals-subtitle">Browse, analyze, and compare your meals and their glucose impact</p>
      </div>

      {/* Stats */}
      {stats && (
        <div className="meals-stats-row">
          <div className="meals-stat-card">
            <div className="meals-stat-value">{stats.totalMeals}</div>
            <div className="meals-stat-label">Total meals</div>
          </div>
          <div className="meals-stat-card">
            <div className="meals-stat-value">{stats.analyzedMeals}</div>
            <div className="meals-stat-label">Analyzed</div>
          </div>
          <div className="meals-stat-card meals-stat-green">
            <div className="meals-stat-value">{stats.greenMeals}</div>
            <div className="meals-stat-label">Good</div>
          </div>
          <div className="meals-stat-card meals-stat-yellow">
            <div className="meals-stat-value">{stats.yellowMeals}</div>
            <div className="meals-stat-label">Concerning</div>
          </div>
          <div className="meals-stat-card meals-stat-red">
            <div className="meals-stat-value">{stats.redMeals}</div>
            <div className="meals-stat-label">Problematic</div>
          </div>
          {stats.avgSpike != null && (
            <div className="meals-stat-card">
              <div className="meals-stat-value">+{Math.round(stats.avgSpike)}</div>
              <div className="meals-stat-label">Avg spike (mg/dL)</div>
            </div>
          )}
        </div>
      )}

      {/* Toolbar */}
      <div className="meals-toolbar">
        <input
          type="text"
          className="meals-search"
          placeholder="Search meals..."
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
        <select className="meals-sort-select" value={sortBy} onChange={e => setSortBy(e.target.value)}>
          <option value="date">Date</option>
          <option value="spike">Spike</option>
          <option value="glucose">Glucose at event</option>
          <option value="name">Name</option>
        </select>
        <button className="meals-sort-dir" onClick={() => setSortDesc(!sortDesc)} title="Toggle sort direction">
          {sortDesc ? '↓' : '↑'}
        </button>
        <select className="meals-class-filter" value={classFilter} onChange={e => setClassFilter(e.target.value)}>
          <option value="">All classifications</option>
          <option value="green">🟢 Good</option>
          <option value="yellow">🟡 Concerning</option>
          <option value="red">🔴 Problematic</option>
        </select>
        {selectedIds.size > 0 && (
          <div className="meals-selection-actions">
            <span className="meals-selected-count">{selectedIds.size} selected</span>
            <button
              className="meals-compare-btn"
              onClick={handleCompare}
              disabled={selectedIds.size < 2}
            >
              Compare ({selectedIds.size})
            </button>
            <button className="meals-clear-btn" onClick={clearSelection}>Clear</button>
          </div>
        )}
      </div>

      {/* Meal list */}
      {meals.length === 0 ? (
        <div className="meals-empty">
          <p>No meals found.</p>
          <p>Meals will appear here once events are processed.</p>
        </div>
      ) : (
        <div className="meals-list">
          {meals.map(meal => (
            <div
              key={meal.id}
              className={`meal-card${selectedMeal?.id === meal.id ? ' active' : ''}${selectedIds.has(meal.id) ? ' selected' : ''}`}
              onClick={() => handleSelectMeal(meal.id)}
            >
              <div className="meal-card-select" onClick={(e) => toggleSelect(meal.id, e)}>
                <input
                  type="checkbox"
                  checked={selectedIds.has(meal.id)}
                  onChange={() => {}}
                  className="meal-checkbox"
                />
              </div>
              <div className="meal-card-class">{classIcon(meal.aiClassification)}</div>
              <div className="meal-card-body">
                <div className="meal-card-title">
                  {meal.noteTitle}
                  {meal.noteTitleEn && meal.noteTitleEn !== meal.noteTitle && (
                    <span className="meal-card-title-en"> ({meal.noteTitleEn})</span>
                  )}
                </div>
                <div className="meal-card-date">
                  {format(new Date(meal.eventTimestamp), 'EEEE, MMM d, yyyy · HH:mm')}
                </div>
                {meal.foods?.length > 0 && (
                  <div className="meal-card-foods">
                    {meal.foods.map(f => (
                      <span key={f.foodId} className={`meal-food-tag meal-food-tag-${f.aiClassification || 'none'}`}>
                        {f.nameEn && f.nameEn.toLowerCase() !== f.name.toLowerCase() ? f.nameEn : f.name}
                      </span>
                    ))}
                  </div>
                )}
                {meal.noteContentPreview && (
                  <div className="meal-card-preview">{meal.noteContentPreview}</div>
                )}
              </div>
              <div className="meal-card-metrics">
                <div className={`meal-card-spike ${spikeColor(meal.glucoseSpike)}`}>
                  {meal.glucoseSpike != null ? `+${Math.round(meal.glucoseSpike)}` : '—'}
                  <span className="meal-card-unit">mg/dL</span>
                </div>
                <div className="meal-card-baseline">
                  @ {meal.glucoseAtEvent != null ? Math.round(meal.glucoseAtEvent) : '?'}
                </div>
                <div className="meal-card-range">
                  {meal.glucoseMin != null && meal.glucoseMax != null
                    ? `${Math.round(meal.glucoseMin)}–${Math.round(meal.glucoseMax)}`
                    : ''}
                </div>
              </div>
            </div>
          ))}
          <div className="scroll-sentinel" />
          {loadingMore && (
            <div className="loading-more">
              <div className="spinner spinner-sm" />
              <span>Loading more meals...</span>
            </div>
          )}
          {!mealsHasMore && meals.length > PAGE_SIZE && (
            <div className="list-end-message">All {totalCount} meals loaded</div>
          )}
        </div>
      )}

      {detailLoading && <div className="meals-loading">Loading meal details...</div>}

      {/* ── Meal Detail Modal ──────────────────────────── */}
      {selectedMeal && !detailLoading && (
        <div className="meal-modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) closeDetailModal(); }}>
          <div className="meal-modal">
            <div className="meal-modal-header">
              <div className="meal-modal-title-group">
                <h3>{selectedMeal.noteTitle}</h3>
                {selectedMeal.noteTitleEn && selectedMeal.noteTitleEn !== selectedMeal.noteTitle && (
                  <span className="meal-modal-title-en">{selectedMeal.noteTitleEn}</span>
                )}
                <span className="meal-modal-date">
                  {format(new Date(selectedMeal.eventTimestamp), 'EEEE, MMM d, yyyy · HH:mm')}
                </span>
              </div>
              <div className="meal-modal-actions">
                {selectedMeal.aiClassification && (
                  <span className={`meal-class-badge meal-class-badge-${selectedMeal.aiClassification}`}>
                    {classIcon(selectedMeal.aiClassification)} {selectedMeal.aiClassification}
                  </span>
                )}
                <button className="meal-ai-btn" onClick={() => handleStartChat(selectedMeal)} disabled={chatSending}>
                  {chatSending ? 'Starting...' : 'Analyze with AI'}
                </button>
                <button className="meal-modal-close" onClick={closeDetailModal}>&times;</button>
              </div>
            </div>

            <div className="meal-modal-body">
              {/* Note content */}
              {selectedMeal.noteContent && (
                <div className="meal-section">
                  <div className="meal-note-content">{selectedMeal.noteContent}</div>
                  {selectedMeal.noteContentEn && selectedMeal.noteContentEn !== selectedMeal.noteContent && (
                    <div className="meal-note-content meal-note-content-en">{selectedMeal.noteContentEn}</div>
                  )}
                </div>
              )}

              {/* Glucose stats */}
              <div className="meal-detail-stats">
                <div className="meal-detail-stat">
                  <span className="meal-detail-stat-label">At Event</span>
                  <span className="meal-detail-stat-value">
                    {selectedMeal.glucoseAtEvent != null ? `${Math.round(selectedMeal.glucoseAtEvent)} mg/dL` : '—'}
                  </span>
                </div>
                <div className="meal-detail-stat">
                  <span className="meal-detail-stat-label">Spike</span>
                  <span className={`meal-detail-stat-value ${spikeColor(selectedMeal.glucoseSpike)}`}>
                    {selectedMeal.glucoseSpike != null ? `+${Math.round(selectedMeal.glucoseSpike)} mg/dL` : '—'}
                  </span>
                </div>
                <div className="meal-detail-stat">
                  <span className="meal-detail-stat-label">Min</span>
                  <span className="meal-detail-stat-value">
                    {selectedMeal.glucoseMin != null ? `${Math.round(selectedMeal.glucoseMin)} mg/dL` : '—'}
                  </span>
                </div>
                <div className="meal-detail-stat">
                  <span className="meal-detail-stat-label">Max</span>
                  <span className="meal-detail-stat-value">
                    {selectedMeal.glucoseMax != null ? `${Math.round(selectedMeal.glucoseMax)} mg/dL` : '—'}
                  </span>
                </div>
                <div className="meal-detail-stat">
                  <span className="meal-detail-stat-label">Average</span>
                  <span className="meal-detail-stat-value">
                    {selectedMeal.glucoseAvg != null ? `${Math.round(selectedMeal.glucoseAvg)} mg/dL` : '—'}
                  </span>
                </div>
                <div className="meal-detail-stat">
                  <span className="meal-detail-stat-label">Peak Time</span>
                  <span className="meal-detail-stat-value">
                    {selectedMeal.peakTime ? format(new Date(selectedMeal.peakTime), 'HH:mm') : '—'}
                  </span>
                </div>
                <div className="meal-detail-stat">
                  <span className="meal-detail-stat-label">Readings</span>
                  <span className="meal-detail-stat-value">{selectedMeal.readingCount}</span>
                </div>
                <div className="meal-detail-stat">
                  <span className="meal-detail-stat-label">Period</span>
                  <span className="meal-detail-stat-value">
                    {format(new Date(selectedMeal.periodStart), 'HH:mm')} – {format(new Date(selectedMeal.periodEnd), 'HH:mm')}
                  </span>
                </div>
              </div>

              {/* Foods */}
              {selectedMeal.foods?.length > 0 && (
                <div className="meal-section">
                  <h4>Foods in this meal</h4>
                  <div className="meal-foods-grid">
                    {selectedMeal.foods.map(f => (
                      <div key={f.foodId} className={`meal-food-card meal-food-card-${f.aiClassification || 'none'}`}>
                        <span className="meal-food-name">{f.name}</span>
                        {f.nameEn && f.nameEn.toLowerCase() !== f.name.toLowerCase() && (
                          <span className="meal-food-name-en">{f.nameEn}</span>
                        )}
                        {f.spike != null && (
                          <span className={`meal-food-spike ${spikeColor(f.spike)}`}>+{Math.round(f.spike)}</span>
                        )}
                        {f.category && <span className="meal-food-category">{f.category}</span>}
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* AI Analysis */}
              {selectedMeal.aiAnalysis && (
                <div className="meal-section">
                  <h4>AI Analysis {selectedMeal.aiModel && <span className="meal-ai-model">{selectedMeal.aiModel}</span>}</h4>
                  <div className="meal-ai-analysis">{selectedMeal.aiAnalysis}</div>
                </div>
              )}

              {/* View full event detail link */}
              <div className="meal-section meal-section-actions">
                <button
                  className="meal-view-event-btn"
                  onClick={() => setSelectedEventId(selectedMeal.id)}
                >
                  View full event detail with glucose chart
                </button>
              </div>

              {/* AI Chat */}
              {chatOpen && renderChatPanel(selectedMeal, false)}
            </div>
          </div>
        </div>
      )}

      {/* ── Compare Modal ─────────────────────────────── */}
      {compareOpen && (
        <div className="meal-modal-overlay" onClick={(e) => { if (e.target === e.currentTarget) closeCompareModal(); }}>
          <div className="meal-modal meal-modal-wide">
            <div className="meal-modal-header">
              <div className="meal-modal-title-group">
                <h3>Compare Meals ({compareData?.meals?.length || selectedIds.size})</h3>
              </div>
              <div className="meal-modal-actions">
                <button
                  className="meal-ai-btn"
                  onClick={() => handleStartChat(null, null, true)}
                  disabled={chatSending || !compareData}
                >
                  {chatSending ? 'Starting...' : 'Compare with AI'}
                </button>
                <button className="meal-modal-close" onClick={closeCompareModal}>&times;</button>
              </div>
            </div>

            <div className="meal-modal-body">
              {compareLoading ? (
                <div className="meals-loading">Loading comparison data...</div>
              ) : compareData?.meals?.length > 0 ? (
                <>
                  <div className="meal-compare-grid">
                    {compareData.meals.map(m => (
                      <div key={m.id} className={`meal-compare-card meal-compare-card-${m.aiClassification || 'none'}`}>
                        <div className="meal-compare-card-header">
                          <span className="meal-compare-class">{classIcon(m.aiClassification)}</span>
                          <div className="meal-compare-title">{m.noteTitle}</div>
                          {m.noteTitleEn && m.noteTitleEn !== m.noteTitle && (
                            <div className="meal-compare-title-en">{m.noteTitleEn}</div>
                          )}
                          <div className="meal-compare-date">
                            {format(new Date(m.eventTimestamp), 'MMM d, yyyy HH:mm')}
                          </div>
                        </div>
                        <div className="meal-compare-stats">
                          <div className="meal-compare-stat">
                            <span className="meal-compare-stat-label">Spike</span>
                            <span className={`meal-compare-stat-value ${spikeColor(m.glucoseSpike)}`}>
                              {m.glucoseSpike != null ? `+${Math.round(m.glucoseSpike)}` : '—'}
                            </span>
                          </div>
                          <div className="meal-compare-stat">
                            <span className="meal-compare-stat-label">At Event</span>
                            <span className="meal-compare-stat-value">
                              {m.glucoseAtEvent != null ? Math.round(m.glucoseAtEvent) : '—'}
                            </span>
                          </div>
                          <div className="meal-compare-stat">
                            <span className="meal-compare-stat-label">Max</span>
                            <span className="meal-compare-stat-value">
                              {m.glucoseMax != null ? Math.round(m.glucoseMax) : '—'}
                            </span>
                          </div>
                          <div className="meal-compare-stat">
                            <span className="meal-compare-stat-label">Avg</span>
                            <span className="meal-compare-stat-value">
                              {m.glucoseAvg != null ? Math.round(m.glucoseAvg) : '—'}
                            </span>
                          </div>
                        </div>
                        {m.foods?.length > 0 && (
                          <div className="meal-compare-foods">
                            {m.foods.map(f => (
                              <span key={f.foodId} className={`meal-food-tag meal-food-tag-${f.aiClassification || 'none'}`}>
                                {f.nameEn && f.nameEn.toLowerCase() !== f.name.toLowerCase() ? f.nameEn : f.name}
                              </span>
                            ))}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>

                  {chatOpen && renderChatPanel(null, true)}
                </>
              ) : (
                <div className="meals-empty">No comparison data available.</div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Event Detail Modal (drill-down) */}
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

export default MealsPage;
