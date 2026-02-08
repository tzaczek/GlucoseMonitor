import React, { useState, useEffect } from 'react';
import { format, subDays } from 'date-fns';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

const PRESETS = [
  { label: '7 days', days: 7 },
  { label: '2 weeks', days: 14 },
  { label: '30 days', days: 30 },
  { label: '60 days', days: 60 },
  { label: '90 days', days: 90 },
];

export default function ReportsPage() {
  const [fromDate, setFromDate] = useState(format(subDays(new Date(), 7), 'yyyy-MM-dd'));
  const [toDate, setToDate] = useState(format(new Date(), 'yyyy-MM-dd'));
  const [activePreset, setActivePreset] = useState(7);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState(null);
  const [availableDates, setAvailableDates] = useState([]);

  // Load available date range
  useEffect(() => {
    fetch(`${API_BASE}/glucose/dates`)
      .then(r => r.ok ? r.json() : [])
      .then(dates => setAvailableDates(dates))
      .catch(() => {});
  }, []);

  const handlePreset = (days) => {
    const to = new Date();
    const from = subDays(to, days);
    setFromDate(format(from, 'yyyy-MM-dd'));
    setToDate(format(to, 'yyyy-MM-dd'));
    setActivePreset(days);
  };

  const handleCustomDate = (field, value) => {
    if (field === 'from') setFromDate(value);
    else setToDate(value);
    setActivePreset(null);
  };

  const handleGenerate = async () => {
    setGenerating(true);
    setError(null);
    try {
      const res = await fetch(`${API_BASE}/reports/pdf?from=${fromDate}&to=${toDate}`);
      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.message || `Server error: ${res.status}`);
      }

      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `glucose_report_${fromDate}_${toDate}.pdf`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err.message);
    } finally {
      setGenerating(false);
    }
  };

  const dayCount = Math.max(1, Math.round((new Date(toDate) - new Date(fromDate)) / (1000 * 60 * 60 * 24)) + 1);

  return (
    <div className="reports-page">
      <div className="reports-header-card">
        <div className="reports-header-icon">📋</div>
        <h2>PDF Reports for Doctors</h2>
        <p>
          Generate a comprehensive PDF report with glucose statistics, daily breakdowns,
          event analysis, and AI insights — ready to share with your healthcare provider.
        </p>
      </div>

      <div className="reports-config-card">
        <h3>📅 Select Report Period</h3>

        <div className="reports-presets">
          {PRESETS.map(p => (
            <button
              key={p.days}
              className={activePreset === p.days ? 'active' : ''}
              onClick={() => handlePreset(p.days)}
            >
              {p.label}
            </button>
          ))}
        </div>

        <div className="reports-dates">
          <div className="reports-date-field">
            <label>From</label>
            <input
              type="date"
              value={fromDate}
              onChange={(e) => handleCustomDate('from', e.target.value)}
            />
          </div>
          <div className="reports-date-separator">→</div>
          <div className="reports-date-field">
            <label>To</label>
            <input
              type="date"
              value={toDate}
              max={format(new Date(), 'yyyy-MM-dd')}
              onChange={(e) => handleCustomDate('to', e.target.value)}
            />
          </div>
        </div>

        <div className="reports-period-info">
          <span className="reports-day-count">{dayCount} day{dayCount !== 1 ? 's' : ''}</span>
          {dayCount > 90 && (
            <span className="reports-limit-warning">⚠ Maximum 90 days per report</span>
          )}
        </div>
      </div>

      <div className="reports-preview-card">
        <h3>📄 Report Contents</h3>
        <div className="reports-contents-list">
          <div className="reports-content-item">
            <span className="reports-content-icon">📊</span>
            <div>
              <strong>Summary Statistics</strong>
              <p>Average glucose, estimated A1C, time in range, variability (CV), min/max, and more.</p>
            </div>
          </div>
          <div className="reports-content-item">
            <span className="reports-content-icon">📈</span>
            <div>
              <strong>Time in Range Bar</strong>
              <p>Visual distribution of time below, in, and above target range (70–180 mg/dL).</p>
            </div>
          </div>
          <div className="reports-content-item">
            <span className="reports-content-icon">📈</span>
            <div>
              <strong>Glucose Trend Chart</strong>
              <p>Full-period glucose line graph with target range shading, high/low coloring, and event markers.</p>
            </div>
          </div>
          <div className="reports-content-item">
            <span className="reports-content-icon">📅</span>
            <div>
              <strong>Daily Breakdown Table</strong>
              <p>Day-by-day statistics with average, min, max, time in range, and AI classification.</p>
            </div>
          </div>
          <div className="reports-content-item">
            <span className="reports-content-icon">🍽️</span>
            <div>
              <strong>Meal & Activity Events</strong>
              <p>Every logged event with glucose at event, spike, range, and AI classification.</p>
            </div>
          </div>
          <div className="reports-content-item">
            <span className="reports-content-icon">📉</span>
            <div>
              <strong>Glucose Distribution</strong>
              <p>Histogram showing time spent in each glucose range bracket.</p>
            </div>
          </div>
          <div className="reports-content-item">
            <span className="reports-content-icon">🤖</span>
            <div>
              <strong>AI Analysis Highlights</strong>
              <p>Key AI-generated insights from daily summaries — patterns, trends, and recommendations.</p>
            </div>
          </div>
        </div>
      </div>

      {error && (
        <div className="reports-error">
          <span>❌</span> {error}
        </div>
      )}

      <button
        className="reports-generate-btn"
        onClick={handleGenerate}
        disabled={generating || dayCount > 90}
      >
        {generating ? (
          <>
            <span className="spinner-inline" />
            Generating Report...
          </>
        ) : (
          <>
            📥 Generate & Download PDF
          </>
        )}
      </button>

      {availableDates.length > 0 && (
        <div className="reports-data-hint">
          Data available from <strong>{availableDates[availableDates.length - 1]}</strong> to{' '}
          <strong>{availableDates[0]}</strong> ({availableDates.length} days with readings)
        </div>
      )}
    </div>
  );
}
