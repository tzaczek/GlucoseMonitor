import React, { useState, useCallback } from 'react';
import { format, parseISO } from 'date-fns';
import useInfiniteScroll from '../hooks/useInfiniteScroll';

const PAGE_SIZE = 50;

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
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE);

  const hasMore = visibleCount < data.length;
  const loadMore = useCallback(() => {
    setVisibleCount(prev => Math.min(prev + PAGE_SIZE, data.length));
  }, [data.length]);

  useInfiniteScroll(loadMore, { hasMore, loading: false });

  const visibleData = data.slice(0, visibleCount);

  return (
    <>
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
          {visibleData.map((reading) => {
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
      {hasMore && (
        <div className="loading-more">
          <span>Showing {visibleCount} of {data.length} readings — scroll for more</span>
        </div>
      )}
      {!hasMore && data.length > PAGE_SIZE && (
        <div className="list-end-message">All {data.length} readings shown</div>
      )}
    </>
  );
}

export default GlucoseTable;
