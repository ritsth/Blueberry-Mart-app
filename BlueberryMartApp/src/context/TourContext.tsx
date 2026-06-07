import React, { createContext, useCallback, useContext, useState } from 'react';

export interface Rect { x: number; y: number; w: number; h: number; }

interface TourContextValue {
  rects: Record<string, Rect>;
  register: (key: string, r: Rect) => void;
}

const TourContext = createContext<TourContextValue | null>(null);

/**
 * Holds measured on-screen rectangles (in window coords) that UI elements report via
 * `register`, so the onboarding tour can spotlight the real header / tab buttons.
 * Wraps the customer area; `useTour()` is null elsewhere (registration becomes a no-op).
 */
export function TourProvider({ children }: { children: React.ReactNode }) {
  const [rects, setRects] = useState<Record<string, Rect>>({});

  const register = useCallback((key: string, r: Rect) => {
    if (!r || (r.w === 0 && r.h === 0)) return;
    setRects(prev => {
      const c = prev[key];
      if (c && Math.abs(c.x - r.x) < 1 && Math.abs(c.y - r.y) < 1 && Math.abs(c.w - r.w) < 1 && Math.abs(c.h - r.h) < 1)
        return prev;
      return { ...prev, [key]: r };
    });
  }, []);

  return <TourContext.Provider value={{ rects, register }}>{children}</TourContext.Provider>;
}

export function useTour() {
  return useContext(TourContext);
}
