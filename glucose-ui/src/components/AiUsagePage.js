import React, { useState, useEffect, useCallback, useMemo } from 'react';
import {
  ResponsiveContainer,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  PieChart,
  Pie,
  Cell,
  Area,
  AreaChart,
} from 'recharts';
import { format, parseISO, subDays, subMonths, startOfDay, endOfDay } from 'date-fns';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

const MODEL_COLORS = [
  '#4facfe', '#00f2fe', '#43e97b', '#fa709a',
  '#fee140', '#a18cd1', '#fbc2eb', '#f093fb',
];

// Period presets
const PERIOD_PRESETS = [
  { label: '7 days', value: '7d', getRange: () => ({ from: subDays(new Date(), 7), to: new Date() }) },
  { label: '2 weeks', value: '2w', getRange: () => ({ from: subDays(new Date(), 14), to: new Date() }) },
  { label: '1 month', value: '1m', getRange: () => ({ from: subMonths(new Date(), 1), to: new Date() }) },
  { label: '3 months', value: '3m', getRange: () => ({ from: subMonths(new Date(), 3), to: new Date() }) },
  { label: '6 months', value: '6m', getRange: () => ({ from: subMonths(new Date(), 6), to: new Date() }) },
  { label: '1 year', value: '1y', getRange: () => ({ from: subMonths(new Date(), 12), to: new Date() }) },
  { label: 'All time', value: 'all', getRange: () => ({ from: null, to: null }) },
];

function AiUsagePage() {
  const [summary, setSummary] = useState(null);
  const [logs, setLogs] = useState([]);
  const [loading, setLoading] = useState(true);
  const [activeTab, setActiveTab] = useState('overview'); // overview | logs

  // Period state — default to last month
  const [selectedPreset, setSelectedPreset] = useState('1m');
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo] = useState('');

  // Compute the active date range
  const dateRange = useMemo(() => {
    if (selectedPreset === 'custom') {
      return {
        from: customFrom ? startOfDay(new Date(customFrom)) : null,
        to: customTo ? endOfDay(new Date(customTo)) : null,
      };
    }
    const preset = PERIOD_PRESETS.find((p) => p.value === selectedPreset);
    if (!preset) return { from: null, to: null };
    return preset.getRange();
  }, [selectedPreset, customFrom, customTo]);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams();
      if (dateRange.from) params.set('from', dateRange.from.toISOString());
      if (dateRange.to) params.set('to', dateRange.to.toISOString());

      const qs = params.toString() ? `?${params.toString()}` : '';
      const logsQs = params.toString()
        ? `?limit=1000&${params.toString()}`
        : '?limit=1000';

      const [summaryRes, logsRes] = await Promise.all([
        fetch(`${API_BASE}/AiUsage/summary${qs}`, { cache: 'no-store' }),
        fetch(`${API_BASE}/AiUsage/logs${logsQs}`, { cache: 'no-store' }),
      ]);
      if (summaryRes.ok) setSummary(await summaryRes.json());
      if (logsRes.ok) setLogs(await logsRes.json());
    } catch (err) {
      console.error('Failed to load AI usage data:', err);
    } finally {
      setLoading(false);
    }
  }, [dateRange]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Derive period label for display
  const periodLabel = useMemo(() => {
    const preset = PERIOD_PRESETS.find((p) => p.value === selectedPreset);
    if (preset) return preset.label;
    if (selectedPreset === 'custom') {
      const f = customFrom ? format(new Date(customFrom), 'MMM dd, yyyy') : '...';
      const t = customTo ? format(new Date(customTo), 'MMM dd, yyyy') : '...';
      return `${f} — ${t}`;
    }
    return '';
  }, [selectedPreset, customFrom, customTo]);

  return (
    <div className="ai-usage-page">
      <h2 className="section-title">🤖 AI Usage</h2>

      {/* ── Period Picker ──────────────────────────────── */}
      <div className="ai-period-picker">
        <div className="ai-period-presets">
          {PERIOD_PRESETS.map((p) => (
            <button
              key={p.value}
              className={selectedPreset === p.value ? 'active' : ''}
              onClick={() => setSelectedPreset(p.value)}
            >
              {p.label}
            </button>
          ))}
          <button
            className={selectedPreset === 'custom' ? 'active' : ''}
            onClick={() => setSelectedPreset('custom')}
          >
            Custom
          </button>
        </div>
        {selectedPreset === 'custom' && (
          <div className="ai-period-custom">
            <label>
              From
              <input
                type="date"
                value={customFrom}
                onChange={(e) => setCustomFrom(e.target.value)}
              />
            </label>
            <label>
              To
              <input
                type="date"
                value={customTo}
                onChange={(e) => setCustomTo(e.target.value)}
              />
            </label>
          </div>
        )}
        <span className="ai-period-label">Showing: {periodLabel}</span>
      </div>

      {/* ── Tabs ───────────────────────────────────────── */}
      <div className="ai-tabs">
        <button
          className={activeTab === 'overview' ? 'active' : ''}
          onClick={() => setActiveTab('overview')}
        >
          📊 Overview
        </button>
        <button
          className={activeTab === 'logs' ? 'active' : ''}
          onClick={() => setActiveTab('logs')}
        >
          📋 Call Log ({logs.length})
        </button>
      </div>

      {loading && (
        <div className="ai-loading">
          <div className="spinner" />
          <p>Loading usage data...</p>
        </div>
      )}

      {!loading && (!summary || summary.totalCalls === 0) && (
        <div className="ai-empty">
          <p>No AI API calls recorded for this period.</p>
          <p className="ai-empty-hint">
            Usage data will appear here after the system performs glucose event
            analyses using the GPT API.
          </p>
        </div>
      )}

      {!loading && summary && summary.totalCalls > 0 && (
        <>
          {activeTab === 'overview' && <OverviewTab summary={summary} />}
          {activeTab === 'logs' && <LogsTab logs={logs} />}
        </>
      )}
    </div>
  );
}

// ── Overview Tab ────────────────────────────────────────────

function OverviewTab({ summary }) {
  const dailyData = useMemo(() => {
    return (summary.dailyUsage || []).map((d) => ({
      ...d,
      displayDate: format(parseISO(d.date), 'MMM dd'),
    }));
  }, [summary.dailyUsage]);

  const pieData = useMemo(() => {
    return (summary.modelBreakdown || []).map((m, i) => ({
      name: m.model || 'unknown',
      value: m.totalTokens,
      calls: m.calls,
      cost: m.cost,
      color: MODEL_COLORS[i % MODEL_COLORS.length],
    }));
  }, [summary.modelBreakdown]);

  return (
    <div className="ai-overview">
      {/* Summary Cards */}
      <div className="ai-stats-grid">
        <StatCard
          label="Total Cost"
          value={formatCost(summary.totalCost)}
          icon="💰"
          className="text-cost"
        />
        <StatCard
          label="Avg Cost / Call"
          value={formatCost(summary.avgCostPerCall)}
          icon="💵"
        />
        <StatCard
          label="Total API Calls"
          value={summary.totalCalls}
          icon="📞"
        />
        <StatCard
          label="Successful"
          value={summary.successfulCalls}
          icon="✅"
          className="text-normal"
        />
        <StatCard
          label="Failed"
          value={summary.failedCalls}
          icon="❌"
          className={summary.failedCalls > 0 ? 'text-very-high' : ''}
        />
        <StatCard
          label="Total Input Tokens"
          value={formatNumber(summary.totalInputTokens)}
          icon="📥"
        />
        <StatCard
          label="Total Output Tokens"
          value={formatNumber(summary.totalOutputTokens)}
          icon="📤"
        />
        <StatCard
          label="Total Tokens"
          value={formatNumber(summary.totalTokens)}
          icon="🔢"
        />
        <StatCard
          label="Avg Duration"
          value={`${Math.round(summary.avgDurationMs)} ms`}
          icon="⏱️"
        />
      </div>

      {/* Model Breakdown */}
      {summary.modelBreakdown && summary.modelBreakdown.length > 0 && (
        <div className="ai-section">
          <h3 className="ai-section-title">Models Used</h3>
          <div className="ai-model-breakdown">
            <div className="ai-model-table-wrap">
              <table className="ai-model-table">
                <thead>
                  <tr>
                    <th>Model</th>
                    <th>Calls</th>
                    <th>Success</th>
                    <th>Failed</th>
                    <th>Input Tokens</th>
                    <th>Output Tokens</th>
                    <th>Total Tokens</th>
                    <th>Cost</th>
                    <th>Avg Cost/Call</th>
                    <th>Avg Duration</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.modelBreakdown.map((m, i) => (
                    <tr key={m.model}>
                      <td>
                        <span
                          className="model-dot"
                          style={{ background: MODEL_COLORS[i % MODEL_COLORS.length] }}
                        />
                        {m.model || 'unknown'}
                      </td>
                      <td>{m.calls}</td>
                      <td className="text-normal">{m.successfulCalls}</td>
                      <td className={m.failedCalls > 0 ? 'text-very-high' : ''}>
                        {m.failedCalls}
                      </td>
                      <td>{formatNumber(m.inputTokens)}</td>
                      <td>{formatNumber(m.outputTokens)}</td>
                      <td>{formatNumber(m.totalTokens)}</td>
                      <td className="text-cost">{formatCost(m.cost)}</td>
                      <td>{formatCost(m.avgCostPerCall)}</td>
                      <td>{Math.round(m.avgDurationMs)} ms</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {pieData.length > 1 && (
              <div className="ai-pie-chart">
                <ResponsiveContainer width="100%" height={220}>
                  <PieChart>
                    <Pie
                      data={pieData}
                      dataKey="value"
                      nameKey="name"
                      cx="50%"
                      cy="50%"
                      outerRadius={80}
                      label={({ name, percent }) =>
                        `${name} (${(percent * 100).toFixed(0)}%)`
                      }
                      labelLine
                    >
                      {pieData.map((entry, index) => (
                        <Cell key={`cell-${index}`} fill={entry.color} />
                      ))}
                    </Pie>
                    <Tooltip
                      formatter={(value) => formatNumber(value) + ' tokens'}
                      contentStyle={{
                        background: '#1a1a2e',
                        border: '1px solid #333',
                        borderRadius: 8,
                      }}
                    />
                  </PieChart>
                </ResponsiveContainer>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Daily Cost Chart */}
      {dailyData.length > 0 && (
        <div className="ai-section">
          <h3 className="ai-section-title">Daily Cost</h3>
          <ResponsiveContainer width="100%" height={220}>
            <AreaChart
              data={dailyData}
              margin={{ top: 10, right: 20, left: 0, bottom: 0 }}
            >
              <defs>
                <linearGradient id="costGradient" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#43e97b" stopOpacity={0.3} />
                  <stop offset="95%" stopColor="#43e97b" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="#1f1f35" />
              <XAxis dataKey="displayDate" stroke="#555" tick={{ fontSize: 12 }} />
              <YAxis
                stroke="#555"
                tick={{ fontSize: 12 }}
                tickFormatter={(v) => `$${v.toFixed(4)}`}
              />
              <Tooltip
                contentStyle={{
                  background: '#1a1a2e',
                  border: '1px solid #333',
                  borderRadius: 8,
                }}
                formatter={(value) => [`$${Number(value).toFixed(6)}`, 'Cost']}
              />
              <Area
                type="monotone"
                dataKey="cost"
                stroke="#43e97b"
                fill="url(#costGradient)"
                strokeWidth={2}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      )}

      {/* Daily Token Usage Chart */}
      {dailyData.length > 0 && (
        <div className="ai-section">
          <h3 className="ai-section-title">Daily Token Usage</h3>
          <ResponsiveContainer width="100%" height={300}>
            <BarChart
              data={dailyData}
              margin={{ top: 10, right: 20, left: 0, bottom: 0 }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="#1f1f35" />
              <XAxis dataKey="displayDate" stroke="#555" tick={{ fontSize: 12 }} />
              <YAxis stroke="#555" tick={{ fontSize: 12 }} tickFormatter={formatNumber} />
              <Tooltip
                contentStyle={{
                  background: '#1a1a2e',
                  border: '1px solid #333',
                  borderRadius: 8,
                }}
                formatter={(value, name) => [formatNumber(value), name]}
              />
              <Legend />
              <Bar
                dataKey="inputTokens"
                name="Input Tokens"
                fill="#4facfe"
                radius={[4, 4, 0, 0]}
                stackId="tokens"
              />
              <Bar
                dataKey="outputTokens"
                name="Output Tokens"
                fill="#43e97b"
                radius={[4, 4, 0, 0]}
                stackId="tokens"
              />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}

      {/* Daily Calls Chart */}
      {dailyData.length > 0 && (
        <div className="ai-section">
          <h3 className="ai-section-title">Daily API Calls</h3>
          <ResponsiveContainer width="100%" height={220}>
            <BarChart
              data={dailyData}
              margin={{ top: 10, right: 20, left: 0, bottom: 0 }}
            >
              <CartesianGrid strokeDasharray="3 3" stroke="#1f1f35" />
              <XAxis dataKey="displayDate" stroke="#555" tick={{ fontSize: 12 }} />
              <YAxis stroke="#555" tick={{ fontSize: 12 }} allowDecimals={false} />
              <Tooltip
                contentStyle={{
                  background: '#1a1a2e',
                  border: '1px solid #333',
                  borderRadius: 8,
                }}
              />
              <Legend />
              <Bar
                dataKey="successfulCalls"
                name="Successful"
                fill="#4ade80"
                radius={[4, 4, 0, 0]}
                stackId="calls"
              />
              <Bar
                dataKey="calls"
                name="Total"
                fill="#4facfe44"
                radius={[4, 4, 0, 0]}
              />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}

// ── Call Logs Tab ────────────────────────────────────────────

function LogsTab({ logs }) {
  const [filter, setFilter] = useState('all'); // all | success | failed

  const filteredLogs = useMemo(() => {
    if (filter === 'success') return logs.filter((l) => l.success);
    if (filter === 'failed') return logs.filter((l) => !l.success);
    return logs;
  }, [logs, filter]);

  const totalCost = useMemo(() => {
    return filteredLogs.reduce((sum, l) => sum + (l.cost || 0), 0);
  }, [filteredLogs]);

  return (
    <div className="ai-logs">
      <div className="ai-logs-toolbar">
        <span className="ai-logs-count">
          {filteredLogs.length} calls · Total: {formatCost(totalCost)}
        </span>
        <div className="ai-logs-filters">
          <button
            className={filter === 'all' ? 'active' : ''}
            onClick={() => setFilter('all')}
          >
            All
          </button>
          <button
            className={filter === 'success' ? 'active' : ''}
            onClick={() => setFilter('success')}
          >
            ✅ Success
          </button>
          <button
            className={filter === 'failed' ? 'active' : ''}
            onClick={() => setFilter('failed')}
          >
            ❌ Failed
          </button>
        </div>
      </div>

      <div className="ai-logs-table-wrap">
        <table className="ai-logs-table">
          <thead>
            <tr>
              <th>Time</th>
              <th>Model</th>
              <th>Status</th>
              <th>Input</th>
              <th>Output</th>
              <th>Total</th>
              <th>Cost</th>
              <th>Duration</th>
              <th>Finish</th>
              <th>Reason</th>
            </tr>
          </thead>
          <tbody>
            {filteredLogs.map((log) => (
              <tr key={log.id} className={log.success ? '' : 'log-failed'}>
                <td className="log-time">
                  {format(parseISO(log.calledAt), 'MMM dd HH:mm:ss')}
                </td>
                <td className="log-model">{log.model}</td>
                <td>
                  {log.success ? (
                    <span className="badge badge-success">OK</span>
                  ) : (
                    <span className="badge badge-error">
                      {log.httpStatusCode || 'ERR'}
                    </span>
                  )}
                </td>
                <td className="log-tokens">{formatNumber(log.inputTokens)}</td>
                <td className="log-tokens">{formatNumber(log.outputTokens)}</td>
                <td className="log-tokens">{formatNumber(log.totalTokens)}</td>
                <td className="log-cost">{formatCost(log.cost)}</td>
                <td className="log-duration">
                  {log.durationMs != null ? `${log.durationMs} ms` : '-'}
                </td>
                <td className="log-finish">
                  {log.finishReason ? (
                    <span
                      className={`badge ${
                        log.finishReason === 'stop'
                          ? 'badge-success'
                          : 'badge-warn'
                      }`}
                    >
                      {log.finishReason}
                    </span>
                  ) : (
                    '-'
                  )}
                </td>
                <td className="log-reason">{log.reason || '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Helper Components ────────────────────────────────────────

function StatCard({ label, value, icon, className }) {
  return (
    <div className="ai-stat-card">
      <span className="ai-stat-icon">{icon}</span>
      <div className="ai-stat-info">
        <span className="ai-stat-label">{label}</span>
        <span className={`ai-stat-value ${className || ''}`}>{value}</span>
      </div>
    </div>
  );
}

function formatNumber(n) {
  if (n == null) return '0';
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K';
  return String(n);
}

function formatCost(cost) {
  if (cost == null || cost === 0) return '$0.00';
  if (cost < 0.01) return `$${cost.toFixed(4)}`;
  if (cost < 1) return `$${cost.toFixed(3)}`;
  return `$${cost.toFixed(2)}`;
}

export default AiUsagePage;
