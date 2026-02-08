import React from 'react';

function getGlucoseColor(value) {
  if (value < 70) return 'text-low';
  if (value <= 180) return 'text-normal';
  if (value <= 250) return 'text-high';
  return 'text-very-high';
}

function getTrendArrowSymbol(trend) {
  switch (trend) {
    case 1: return '↓↓';  // Falling fast
    case 2: return '↓';   // Falling
    case 3: return '→';   // Stable
    case 4: return '↑';   // Rising
    case 5: return '↑↑';  // Rising fast
    default: return '?';
  }
}

function CurrentReading({ stats }) {
  const { latestReading, average, min, max, totalReadings, timeInRange } = stats;

  return (
    <div className="stats-grid">
      {latestReading && (
        <div className="stat-card current">
          <div className="label">Current</div>
          <div className={`value ${getGlucoseColor(latestReading.value)}`}>
            {latestReading.value}
            <span className="unit">mg/dL</span>
          </div>
          <div className="trend-arrow" title={`Trend: ${latestReading.trendArrow}`}>
            {getTrendArrowSymbol(latestReading.trendArrow)}
          </div>
        </div>
      )}

      <div className="stat-card">
        <div className="label">Average</div>
        <div className={`value ${getGlucoseColor(average)}`}>
          {average}
          <span className="unit">mg/dL</span>
        </div>
      </div>

      <div className="stat-card">
        <div className="label">Min / Max</div>
        <div className="value text-blue">
          {min}<span className="unit"> – </span>{max}
          <span className="unit">mg/dL</span>
        </div>
      </div>

      <div className="stat-card">
        <div className="label">Time in Range</div>
        <div className={`value ${timeInRange >= 70 ? 'text-normal' : 'text-high'}`}>
          {timeInRange}
          <span className="unit">%</span>
        </div>
      </div>

      <div className="stat-card">
        <div className="label">Readings</div>
        <div className="value text-blue">
          {totalReadings}
        </div>
      </div>
    </div>
  );
}

export default CurrentReading;
