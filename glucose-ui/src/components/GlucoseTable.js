import React from 'react';
import { format, parseISO } from 'date-fns';

function getTrendArrow(trend) {
  switch (trend) {
    case 1: return { symbol: '↓↓', label: 'Falling fast', color: '#f87171' };
    case 2: return { symbol: '↓',  label: 'Falling',      color: '#fbbf24' };
    case 3: return { symbol: '→',  label: 'Stable',       color: '#4ade80' };
    case 4: return { symbol: '↑',  label: 'Rising',       color: '#fbbf24' };
    case 5: return { symbol: '↑↑', label: 'Rising fast',  color: '#f87171' };
    default: return { symbol: '?', label: 'Unknown',      color: '#888' };
  }
}

function getValueColor(value) {
  if (value < 70) return '#f87171';
  if (value <= 180) return '#4ade80';
  if (value <= 250) return '#fbbf24';
  return '#f87171';
}

function GlucoseTable({ data }) {
  return (
    <table className="readings-table">
      <thead>
        <tr>
          <th>Time</th>
          <th>Value</th>
          <th>Trend</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        {data.map((reading) => {
          const trend = getTrendArrow(reading.trendArrow);
          return (
            <tr key={reading.id}>
              <td>{format(parseISO(reading.timestamp), 'MMM dd, HH:mm')}</td>
              <td style={{ color: getValueColor(reading.value), fontWeight: 600 }}>
                {reading.value} mg/dL
              </td>
              <td>
                <span className="trend-arrow" style={{ color: trend.color }} title={trend.label}>
                  {trend.symbol}
                </span>
              </td>
              <td>
                {reading.isHigh && <span style={{ color: '#fbbf24' }}>⚠ High</span>}
                {reading.isLow && <span style={{ color: '#f87171' }}>⚠ Low</span>}
                {!reading.isHigh && !reading.isLow && <span style={{ color: '#4ade80' }}>Normal</span>}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

export default GlucoseTable;
