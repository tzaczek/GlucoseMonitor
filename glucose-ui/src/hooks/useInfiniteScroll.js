import { useEffect, useRef, useCallback } from 'react';

/**
 * Triggers `onLoadMore` when the user scrolls near the bottom of the page
 * (or a specific scroll container). Also auto-loads if the current content
 * doesn't fill the viewport, so the user never sees a half-empty page.
 *
 * @param {Function} onLoadMore
 * @param {{ hasMore: boolean, loading: boolean }} opts
 * @param {Element|null} [scrollContainer] - pass a DOM element for panels with overflow-y:auto
 */
export default function useInfiniteScroll(onLoadMore, { hasMore, loading }, scrollContainer) {
  const callbackRef = useRef(onLoadMore);
  const hasMoreRef = useRef(hasMore);
  const loadingRef = useRef(loading);

  callbackRef.current = onLoadMore;
  hasMoreRef.current = hasMore;
  loadingRef.current = loading;

  const check = useCallback(() => {
    if (!hasMoreRef.current || loadingRef.current) return;

    let nearBottom = false;
    if (scrollContainer) {
      const { scrollTop, scrollHeight, clientHeight } = scrollContainer;
      nearBottom = scrollHeight - scrollTop - clientHeight < 400;
    } else {
      const scrollTop = window.scrollY || document.documentElement.scrollTop;
      const scrollHeight = document.documentElement.scrollHeight;
      const clientHeight = document.documentElement.clientHeight;
      nearBottom = scrollHeight - scrollTop - clientHeight < 400;
    }

    if (nearBottom) callbackRef.current();
  }, [scrollContainer]);

  // Scroll listener
  useEffect(() => {
    const target = scrollContainer || window;
    let ticking = false;
    const onScroll = () => {
      if (!ticking) {
        ticking = true;
        requestAnimationFrame(() => {
          check();
          ticking = false;
        });
      }
    };
    target.addEventListener('scroll', onScroll, { passive: true });
    return () => target.removeEventListener('scroll', onScroll);
  }, [check, scrollContainer]);

  // Auto-check after loading finishes (content may not fill viewport)
  useEffect(() => {
    if (!loading && hasMore) {
      const id = requestAnimationFrame(() => check());
      return () => cancelAnimationFrame(id);
    }
  }, [loading, hasMore, check]);

  // Also check on resize (viewport may grow)
  useEffect(() => {
    const onResize = () => check();
    window.addEventListener('resize', onResize, { passive: true });
    return () => window.removeEventListener('resize', onResize);
  }, [check]);
}
